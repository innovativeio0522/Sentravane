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
    private readonly IRapidEncryptionDetector _rapidEncryptionDetector;
    private readonly IForensicDumpService _dumpService;
    private readonly ConcurrentDictionary<string, DateTime> _recentAlerts = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ForensicSnapshotPayload>? IncidentDetected;

    public CorrelationEngine(
        ILogger<CorrelationEngine> logger,
        ICanaryManager canaryManager,
        IEtwCollectorService etwCollector,
        IContainmentService containmentService,
        ILocalStoreService localStore,
        IRapidEncryptionDetector rapidEncryptionDetector,
        IForensicDumpService dumpService)
    {
        _logger = logger;
        _canaryManager = canaryManager;
        _etwCollector = etwCollector;
        _containmentService = containmentService;
        _localStore = localStore;
        _rapidEncryptionDetector = rapidEncryptionDetector;
        _dumpService = dumpService;
    }

    public void Start()
    {
        _canaryManager.InitializeCanaries();
        _etwCollector.FileAccessed += OnEtwFileAccessed;
        _rapidEncryptionDetector.EncryptionAnomalyDetected += OnRapidEncryptionAnomaly;
        _etwCollector.Start();
        _logger.LogInformation("Correlation Engine armed with Canary + Rapid Encryption Anomaly detection");
    }

    public void Stop()
    {
        _etwCollector.FileAccessed -= OnEtwFileAccessed;
        _rapidEncryptionDetector.EncryptionAnomalyDetected -= OnRapidEncryptionAnomaly;
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
        else if (e.Operation.Contains("Write") || e.Operation.Contains("SetInfo") || e.Operation.Contains("Rename"))
        {
            _rapidEncryptionDetector.RecordFileModification(e.ProcessId, e.ProcessName, e.FilePath);
        }
    }

    private void OnRapidEncryptionAnomaly(object? sender, RapidEncryptionAlert alert)
    {
        TriggerIncident(
            alert.ProcessId,
            alert.ProcessName,
            IncidentReason.RapidEncryptionAnomaly,
            null,
            alert.SampleFiles,
            $"Rapid file encryption anomaly detected: {alert.ModificationCount} files modified in {alert.TimeWindow.TotalSeconds}s"
        );
    }

    private void HandleCanaryTouch(int pid, string processName, string filePath, string operation)
    {
        TriggerIncident(
            pid,
            processName,
            IncidentReason.CanaryModification,
            filePath,
            new List<string> { filePath },
            $"Canary tripwire hit: {operation} on decoy {filePath}"
        );
    }

    private void TriggerIncident(
        int pid,
        string processName,
        IncidentReason reason,
        string? triggerPath,
        IReadOnlyList<string> affectedFiles,
        string logMessage)
    {
        var key = $"{pid}:{reason}";
        if (_recentAlerts.TryGetValue(key, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 5)
        {
            return;
        }
        _recentAlerts[key] = DateTime.UtcNow;

        _logger.LogCritical("SECURITY INCIDENT: {Message}", logMessage);

        // 1. Atomic containment: Suspend process tree immediately
        var containmentResult = _containmentService.SuspendProcessTree(pid, logMessage);

        // 2. Dynamic network isolation
        _containmentService.IsolateEndpointNetwork();

        var incidentId = Guid.NewGuid().ToString("N");

        // 3. Capture process memory dump
        string? dumpPath = _dumpService.GenerateProcessMinidump(pid, incidentId);

        // 4. Capture process metadata
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

        // 5. Capture active network sockets
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

        var actions = new List<string>
        {
            $"NtSuspendProcess on {containmentResult.SuspendedPids.Count} processes",
            "WFP Ephemeral Network Quarantine applied"
        };
        if (!string.IsNullOrEmpty(dumpPath))
        {
            actions.Add($"Forensic dump captured at {dumpPath}");
        }

        var snapshot = new ForensicSnapshotPayload
        {
            EndpointId = Environment.MachineName,
            IncidentId = incidentId,
            TimestampUtc = DateTime.UtcNow,
            TriggerReason = reason,
            CanaryFilePath = triggerPath,
            OffendingProcess = new ProcessEntry
            {
                Pid = pid,
                ProcessName = processName,
                ImagePath = processPath,
                CommandLine = cmdLine
            },
            ActiveNetworkSockets = sockets,
            RecentModifiedFiles = new List<string>(affectedFiles),
            ContainmentActionsTaken = actions
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
