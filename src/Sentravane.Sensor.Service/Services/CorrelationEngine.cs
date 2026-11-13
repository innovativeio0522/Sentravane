using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentravane.Sensor.Service.Native;
using Sentravane.Shared.Models;

namespace Sentravane.Sensor.Service.Services;

public interface ICorrelationEngine
{
    void Start();
    void Stop();
    event EventHandler<ForensicSnapshotPayload>? IncidentDetected;
}

public class CorrelationEngine : ICorrelationEngine, IDisposable
{
    private readonly ILogger<CorrelationEngine> _logger;
    private readonly ICanaryManager _canaryManager;
    private readonly IEtwCollectorService _etwCollector;
    private readonly IContainmentService _containmentService;
    private readonly ILocalStoreService _localStore;
    private readonly ConcurrentDictionary<string, DateTime> _recentCanaryAlerts = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ForensicSnapshotPayload>? IncidentDetected;

    public CorrelationEngine(
        ILogger<CorrelationEngine> logger,
        ICanaryManager canaryManager,
        IEtwCollectorService etwCollector,
        IContainmentService containmentService,
        ILocalStoreService localStore)
    {
        _logger = logger;
        _canaryManager = canaryManager;
        _etwCollector = etwCollector;
        _containmentService = containmentService;
        _localStore = localStore;
    }

    public void Start()
    {
        _canaryManager.InitializeCanaries();
        _etwCollector.FileAccessed += OnEtwFileAccessed;
        _etwCollector.Start();
        _logger.LogInformation("Correlation Engine armed and listening to Canary and ETW telemetry");
    }

    public void Stop()
    {
        _etwCollector.FileAccessed -= OnEtwFileAccessed;
        _etwCollector.Stop();
        _canaryManager.CleanupCanaries();
        _logger.LogInformation("Correlation Engine disarmed");
    }

    private void OnEtwFileAccessed(object? sender, FileAccessEvent e)
    {
        if (e.ProcessId <= 4) return; // Ignore System Idle and System

        if (_canaryManager.IsCanaryPath(e.FilePath))
        {
            HandleCanaryTouch(e.ProcessId, e.ProcessName, e.FilePath, e.Operation);
        }
    }

    private void HandleCanaryTouch(int pid, string processName, string filePath, string operation)
    {
        var key = $"{pid}:{filePath}";
        if (_recentCanaryAlerts.TryGetValue(key, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 5)
        {
            return;
        }
        _recentCanaryAlerts[key] = DateTime.UtcNow;

        _logger.LogCritical("CANARY TRIPWIRE HIT! PID {Pid} ({ProcessName}) executed {Op} on decoy {Path}", pid, processName, operation, filePath);

        // 1. Atomic containment: Suspend process tree immediately
        var containmentResult = _containmentService.SuspendProcessTree(pid, $"Canary violation on {filePath}");

        // 2. Dynamic network isolation
        _containmentService.IsolateEndpointNetwork();

        // 3. Capture process metadata
        string processPath = "Unknown";
        string? cmdLine = null;
        try
        {
            using var proc = Process.GetProcessById(pid);
            processPath = proc.MainModule?.FileName ?? "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read main module for PID {Pid}", pid);
        }

        // 4. Capture active network sockets
        var sockets = new List<SocketEntry>();
        try
        {
            var activeConns = IpHelperNative.GetActiveTcpConnections();
            foreach (var conn in activeConns)
            {
                if (conn.ProcessId == pid)
                {
                    sockets.Add(new SocketEntry
                    {
                        LocalIp = conn.LocalAddress.ToString(),
                        LocalPort = conn.LocalPort,
                        RemoteIp = conn.RemoteAddress.ToString(),
                        RemotePort = conn.RemotePort,
                        State = conn.State.ToString()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to capture TCP sockets for PID {Pid}", pid);
        }

        var snapshot = new ForensicSnapshotPayload
        {
            EndpointId = Environment.MachineName, // Replaced or supplemented by Fleet client
            IncidentId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            TriggerReason = IncidentReason.CanaryModification,
            CanaryFilePath = filePath,
            OffendingProcess = new ProcessEntry
            {
                Pid = pid,
                ProcessName = processName,
                ImagePath = processPath,
                CommandLine = cmdLine
            },
            ActiveNetworkSockets = sockets,
            RecentModifiedFiles = new List<string> { filePath },
            ContainmentActionsTaken = new List<string>
            {
                $"NtSuspendProcess on {containmentResult.SuspendedPids.Count} processes",
                "WFP Ephemeral Network Quarantine applied"
            }
        };

        // Record locally in encrypted store
        _ = _localStore.RecordIncidentAsync(snapshot);

        IncidentDetected?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        Stop();
    }
}
