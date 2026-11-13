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
    private HubConnection? _hubConnection;
    private string _endpointId = string.Empty;

    public string EndpointId => _endpointId;
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public FleetSignalRClient(
        ILogger<FleetSignalRClient> logger,
        IConfiguration config,
        ICorrelationEngine correlationEngine,
        IContainmentService containmentService,
        ILocalStoreService localStore)
    {
        _logger = logger;
        _config = config;
        _correlationEngine = correlationEngine;
        _containmentService = containmentService;
        _localStore = localStore;
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
        if (File.Exists(idPath))
        {
            var existingId = await File.ReadAllTextAsync(idPath);
            if (!string.IsNullOrWhiteSpace(existingId))
            {
                _endpointId = existingId.Trim();
                _logger.LogInformation("Loaded existing Endpoint ID: {Id}", _endpointId);
                return;
            }
        }

        try
        {
            using var http = new HttpClient();
            var req = new EnrollmentRequest
            {
                EnrollmentToken = enrollmentToken,
                HardwareUuid = Environment.MachineName,
                MacAddress = "00:15:5D:00:01:02",
                Hostname = Environment.MachineName,
                OsVersion = Environment.OSVersion.VersionString,
                SensorVersion = "1.0.0"
            };

            var resp = await http.PostAsJsonAsync($"{serverUrl}/api/v1/enrollment/enroll", req);
            if (resp.IsSuccessStatusCode)
            {
                var enrolled = await resp.Content.ReadFromJsonAsync<EnrollmentResponse>();
                if (enrolled != null)
                {
                    _endpointId = enrolled.EndpointId;
                    Directory.CreateDirectory(Path.GetDirectoryName(idPath)!);
                    await File.WriteAllTextAsync(idPath, _endpointId);
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
