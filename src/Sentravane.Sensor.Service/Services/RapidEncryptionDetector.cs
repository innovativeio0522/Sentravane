using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Sentravane.Sensor.Service.Services;

public interface IRapidEncryptionDetector
{
    void RecordFileModification(int pid, string processName, string filePath);
    event EventHandler<RapidEncryptionAlert>? EncryptionAnomalyDetected;
}

public record RapidEncryptionAlert(int ProcessId, string ProcessName, int ModificationCount, TimeSpan TimeWindow, IReadOnlyList<string> SampleFiles);

public class RapidEncryptionDetector : IRapidEncryptionDetector
{
    private readonly ILogger<RapidEncryptionDetector> _logger;
    private readonly ConcurrentDictionary<int, SlidingWindowTracker> _processTrackers = new();

    public int ThresholdModificationCount { get; set; } = 40;
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromSeconds(5);

    public event EventHandler<RapidEncryptionAlert>? EncryptionAnomalyDetected;

    public RapidEncryptionDetector(ILogger<RapidEncryptionDetector> logger)
    {
        _logger = logger;
    }

    public void RecordFileModification(int pid, string processName, string filePath)
    {
        // Ignore low PID system tasks
        if (pid <= 4) return;

        var tracker = _processTrackers.GetOrAdd(pid, _ => new SlidingWindowTracker(WindowDuration));
        int count = tracker.AddEvent(filePath, out var sampleFiles);

        if (count >= ThresholdModificationCount)
        {
            if (tracker.TryTriggerAlert())
            {
                _logger.LogCritical("RAPID ENCRYPTION ANOMALY! PID {Pid} ({ProcessName}) modified {Count} files within {Seconds} seconds!",
                    pid, processName, count, WindowDuration.TotalSeconds);

                EncryptionAnomalyDetected?.Invoke(this, new RapidEncryptionAlert(pid, processName, count, WindowDuration, sampleFiles));
            }
        }
    }

    private class SlidingWindowTracker
    {
        private readonly TimeSpan _window;
        private readonly Queue<(DateTime Timestamp, string FilePath)> _history = new();
        private readonly object _lock = new();
        private bool _alerted = false;

        public SlidingWindowTracker(TimeSpan window)
        {
            _window = window;
        }

        public int AddEvent(string filePath, out List<string> sampleFiles)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _history.Enqueue((now, filePath));

                var cutoff = now - _window;
                while (_history.Count > 0 && _history.Peek().Timestamp < cutoff)
                {
                    _history.Dequeue();
                }

                sampleFiles = new List<string>();
                int sampleLimit = Math.Min(5, _history.Count);
                int skipped = 0;
                foreach (var item in _history)
                {
                    if (skipped++ < sampleLimit)
                    {
                        sampleFiles.Add(item.FilePath);
                    }
                }

                return _history.Count;
            }
        }

        public bool TryTriggerAlert()
        {
            lock (_lock)
            {
                if (!_alerted)
                {
                    _alerted = true;
                    return true;
                }
                return false;
            }
        }
    }
}
