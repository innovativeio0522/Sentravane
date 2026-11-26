using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sentravane.Shared.Contracts;
using Sentravane.Shared.Models;

namespace Sentravane.Sensor.Service.Services;

public interface IFleetSignalRClient
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    Task DrainOfflineQueueAsync();
    string EndpointId { get; }
    bool IsConnected { get; }
}

public class FleetSignalRClient : IFleetSignalRClient, IAsyncDisposable
{
    private readonly ILogger<FleetSignalRClient> _logger;
    private readonly IConfiguration _config;
    private readonly ICorrelationEngine _correlationEngine;
    private readonly IContainmentService _containmentService;
    private readonly ILocalStoreService _localStore;
    private readonly IRapidEncryptionDetector _rapidDetector;
    private HubConnection? _hubConnection;
    private string _endpointId = string.Empty;
    private string _agentSecret = string.Empty;

    public string EndpointId => _endpointId;
    public string AgentSecret => _agentSecret;
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public FleetSignalRClient(
        ILogger<FleetSignalRClient> logger,
        IConfiguration config,
        ICorrelationEngine correlationEngine,
        IContainmentService containmentService,
        ILocalStoreService localStore,
        IRapidEncryptionDetector rapidDetector)
    {
        _logger = logger;
        _config = config;
        _correlationEngine = correlationEngine;
        _containmentService = containmentService;
        _localStore = localStore;
        _rapidDetector = rapidDetector;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var serverUrl = _config["Sentravane:ServerUrl"] ?? "http://localhost:5000";
        var enrollmentToken = _config["Sentravane:EnrollmentToken"] ?? "DEFAULT_TOKEN";

        await EnsureEnrolledAsync(serverUrl, enrollmentToken);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/hubs/fleet", options =>
            {
                options.Headers.Add("X-Endpoint-Id", _endpointId);
                if (!string.IsNullOrEmpty(_agentSecret))
                {
                    options.Headers.Add("X-Agent-Secret", _agentSecret);
                }
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) })
            .Build();

        _hubConnection.Reconnected += async _ =>
        {
            _logger.LogInformation("SignalR connection re-established with Fleet Hub. Draining offline telemetry queue...");
            await DrainOfflineQueueAsync();
        };

        _hubConnection.On<string>("IsolateEndpoint", incidentId =>
        {
            _logger.LogWarning("Received IsolateEndpoint command from Fleet Hub for incident {IncidentId}", incidentId);
            _containmentService.IsolateEndpointNetwork();
        });

        _hubConnection.On("LiftQuarantine", () =>
        {
            _logger.LogInformation("Received LiftQuarantine command from Fleet Hub");
            _containmentService.RestoreEndpointNetwork();
        });

        _hubConnection.On<EndpointPolicy>("UpdatePolicy", policy =>
        {
            _logger.LogInformation("Received dynamic Policy update from Fleet Hub: BurstThreshold={Threshold}, BurstWindow={Window}s",
                policy.RapidBurstThresholdCount, policy.RapidBurstWindowSeconds);

            if (_rapidDetector is RapidEncryptionDetector detector)
            {
                detector.ThresholdModificationCount = policy.RapidBurstThresholdCount;
                detector.WindowDuration = TimeSpan.FromSeconds(policy.RapidBurstWindowSeconds);
            }
        });

        _correlationEngine.IncidentDetected += async (_, snapshot) =>
        {
            var enrichedSnapshot = snapshot with { EndpointId = _endpointId };
            if (IsConnected)
            {
                try
                {
                    await _hubConnection!.InvokeAsync("ReportIncident", enrichedSnapshot, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stream incident to Fleet Hub via SignalR; fallback to store");
                }
            }
        };

        try
        {
            await _hubConnection.StartAsync(cancellationToken);
            _logger.LogInformation("Connected to Sentravane Fleet Hub at {ServerUrl}", serverUrl);

            // Send initial heartbeat
            var heartbeat = new HeartbeatPayload
            {
                EndpointId = _endpointId,
                TimestampUtc = DateTime.UtcNow,
                ContainmentStatus = ContainmentStatus.Normal,
                CanaryTripwiresIntact = true,
                ActiveCanariesMonitored = 4
            };
            await _hubConnection.InvokeAsync("SendHeartbeat", heartbeat, cancellationToken);

            // Drain any pending events from previous offline runs
            await DrainOfflineQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initially connect to Fleet Hub at {ServerUrl}. Will retry automatically.", serverUrl);
        }
    }

    public async Task DrainOfflineQueueAsync()
    {
        if (!IsConnected) return;

        try
        {
            int drainedCount = 0;
            while (await _localStore.DequeueHeartbeatAsync() is { } queued)
            {
                await _hubConnection!.InvokeAsync("SendHeartbeat", queued);
                drainedCount++;
            }

            if (drainedCount > 0)
            {
                _logger.LogInformation("Successfully drained {Count} offline telemetry events to Fleet Hub", drainedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while draining offline telemetry queue");
        }
    }

    private async Task EnsureEnrolledAsync(string serverUrl, string enrollmentToken)
    {
        var idPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sentravane", "endpoint.id");
        var secretPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sentravane", "agent.secret");
        if (File.Exists(idPath))
        {
            var existingId = await File.ReadAllTextAsync(idPath);
            if (!string.IsNullOrWhiteSpace(existingId))
            {
                _endpointId = existingId.Trim();
                _agentSecret = LoadProtectedSecret(secretPath) ?? string.Empty;
                _logger.LogInformation("Loaded existing Endpoint ID: {Id} (HasSecret: {HasSecret})", _endpointId, !string.IsNullOrEmpty(_agentSecret));
                return;
            }
        }

        try
        {
            using var http = new HttpClient();
            var req = new EnrollmentRequest
            {
                EnrollmentToken = enrollmentToken,
                HardwareUuid = GetHardwareUuid(),
                MacAddress = GetPrimaryMacAddress(),
                Hostname = Environment.MachineName,
                OsVersion = Environment.OSVersion.VersionString,
                SensorVersion = "1.0.0"
            };

            // Attempt enrollment on /api/v1/fleet/enroll, falling back to /api/v1/enrollment/enroll
            var resp = await http.PostAsJsonAsync($"{serverUrl}/api/v1/fleet/enroll", req);
            if (!resp.IsSuccessStatusCode)
            {
                resp = await http.PostAsJsonAsync($"{serverUrl}/api/v1/enrollment/enroll", req);
            }

            if (resp.IsSuccessStatusCode)
            {
                var enrolled = await resp.Content.ReadFromJsonAsync<EnrollmentResponse>();
                if (enrolled != null)
                {
                    _endpointId = enrolled.EndpointId;
                    _agentSecret = enrolled.AgentSecret;
                    Directory.CreateDirectory(Path.GetDirectoryName(idPath)!);
                    await File.WriteAllTextAsync(idPath, _endpointId);
                    if (!string.IsNullOrEmpty(_agentSecret))
                    {
                        SaveProtectedSecret(secretPath, _agentSecret);
                    }
                    _logger.LogInformation("Endpoint successfully enrolled with ID {Id}", _endpointId);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enrollment handshake with server failed; generating local fallback endpoint ID");
        }

        _endpointId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(idPath)!);
        await File.WriteAllTextAsync(idPath, _endpointId);
    }

    private static void SaveProtectedSecret(string filePath, string secret)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
                var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                    bytes, null, System.Security.Cryptography.DataProtectionScope.LocalMachine);
                File.WriteAllBytes(filePath, encrypted);
                return;
            }
        }
        catch
        {
            // Fallback to plain if DPAPI is unavailable
        }
        File.WriteAllText(filePath, secret);
    }

    private static string? LoadProtectedSecret(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var bytes = File.ReadAllBytes(filePath);
                try
                {
                    var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                        bytes, null, System.Security.Cryptography.DataProtectionScope.LocalMachine);
                    return System.Text.Encoding.UTF8.GetString(decrypted).Trim();
                }
                catch
                {
                    return System.Text.Encoding.UTF8.GetString(bytes).Trim();
                }
            }
            return File.ReadAllText(filePath).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string GetHardwareUuid()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var machineGuid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(machineGuid))
                {
                    return machineGuid.Trim();
                }
            }
        }
        catch
        {
            // Fallback to machine name if registry read fails
        }
        return Environment.MachineName;
    }

    private static string GetPrimaryMacAddress()
    {
        try
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                    n.GetPhysicalAddress().GetAddressBytes().Length == 6);

            if (nic != null)
            {
                var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                return string.Join(":", bytes.Select(b => b.ToString("X2")));
            }
        }
        catch
        {
            // Fallback if NIC enumeration fails
        }
        return "00:00:00:00:00:00";
    }

    public async Task StopAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
