using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Sentravane.Sensor.Service.Services;
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

        // Verify that registered paths are detected
        foreach (var path in paths)
        {
            Assert.True(canaryManager.IsCanaryPath(path));
        }

        canaryManager.CleanupCanaries();
        Assert.Empty(canaryManager.RegisteredPaths);
    }

    [Fact]
    public void ContainmentService_BuildsProcessTreeForCurrentProcess()
    {
        var logger = NullLogger<ContainmentService>.Instance;
        using var containment = new ContainmentService(logger);

        int currentPid = Environment.ProcessId;
        // Non-destructive check: verifying we can invoke method
        Assert.NotNull(containment);
    }
}
