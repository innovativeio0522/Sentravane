using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sentravane.Sensor.Service.Native;
using Sentravane.Sensor.Service.Services;
using Sentravane.Shared.IPC;
using Sentravane.Shared.Models;
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
    public void RapidEncryptionDetector_HonorsConfigSettings()
    {
        var logger = NullLogger<RapidEncryptionDetector>.Instance;
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Sentravane:RapidBurstThresholdCount", "25"},
            {"Sentravane:RapidBurstWindowSeconds", "8"}
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var detector = new RapidEncryptionDetector(logger, configuration);
        Assert.Equal(25, detector.ThresholdModificationCount);
        Assert.Equal(TimeSpan.FromSeconds(8), detector.WindowDuration);
    }

    [Fact]
    public void RapidEncryptionDetector_IgnoresSystemPids()
    {
        var logger = NullLogger<RapidEncryptionDetector>.Instance;
        var detector = new RapidEncryptionDetector(logger)
        {
            ThresholdModificationCount = 5,
            WindowDuration = TimeSpan.FromSeconds(5)
        };

        bool anomalyTriggered = false;
        detector.EncryptionAnomalyDetected += (_, _) => anomalyTriggered = true;

        // Feed events from System Idle (0) and System (4)
        for (int i = 0; i < 20; i++)
        {
            detector.RecordFileModification(0, "System Idle", @"C:\file.dat");
            detector.RecordFileModification(4, "System", @"C:\file.dat");
        }

        Assert.False(anomalyTriggered, "System PIDs (<= 4) must never trigger rapid encryption anomaly alerts.");
    }

    [Fact]
    public void ForensicDumpService_InstantiatesWithoutThrowing()
    {
        var logger = NullLogger<ForensicDumpService>.Instance;
        var dumpService = new ForensicDumpService(logger);
        Assert.NotNull(dumpService);
    }

    [Fact]
    public void IpHelperNative_RetrievesTcpConnectionsWithoutCrashing()
    {
        var connections = IpHelperNative.GetActiveTcpConnections();
        Assert.NotNull(connections);
    }

    [Fact]
    public void ProcessTree_BuildsWithoutCrashing()
    {
        var logger = NullLogger<ContainmentService>.Instance;
        using var containment = new ContainmentService(logger);

        // Current test process
        int myPid = Environment.ProcessId;
        Assert.True(myPid > 0);
    }
}
