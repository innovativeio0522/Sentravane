using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Sentravane.Sensor.Service.Services;
using Sentravane.Shared.IPC;
using Xunit;

namespace Sentravane.Tests;

public class SensorServicesTests
{
    [Fact]
    public void UserProfileLocator_EnumeratesWithoutThrowing()
    {
        var folders = UserProfileLocator.GetInteractiveUserFolders();
        Assert.NotNull(folders);
    }

    [Fact]
    public void CanaryManager_InitializesAndCleansUp()
    {
        var logger = NullLogger<CanaryManager>.Instance;
        using var canaryManager = new CanaryManager(logger);

        canaryManager.InitializeCanaries();
        var paths = canaryManager.RegisteredPaths;
        Assert.NotNull(paths);

        foreach (var path in paths)
        {
            Assert.True(canaryManager.IsCanaryPath(path));
        }

        canaryManager.CleanupCanaries();
        Assert.Empty(canaryManager.RegisteredPaths);
    }

    [Fact]
    public void RapidEncryptionDetector_DetectsBurstAnomaly()
    {
        var logger = NullLogger<RapidEncryptionDetector>.Instance;
        var detector = new RapidEncryptionDetector(logger)
        {
            ThresholdModificationCount = 10,
            WindowDuration = TimeSpan.FromSeconds(3)
        };

        bool anomalyTriggered = false;
        detector.EncryptionAnomalyDetected += (_, alert) =>
        {
            anomalyTriggered = true;
            Assert.Equal(1234, alert.ProcessId);
            Assert.Equal("malware.exe", alert.ProcessName);
            Assert.True(alert.ModificationCount >= 10);
        };

        for (int i = 0; i < 12; i++)
        {
            detector.RecordFileModification(1234, "malware.exe", $@"C:\Users\victim\file_{i}.docx");
        }

        Assert.True(anomalyTriggered, "Rapid encryption anomaly detector should have fired on 10+ rapid file touches.");
    }

    [Fact]
    public void ForensicDumpService_InstantiatesWithoutThrowing()
    {
        var logger = NullLogger<ForensicDumpService>.Instance;
        var dumpService = new ForensicDumpService(logger);
        Assert.NotNull(dumpService);
    }
}
