using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sentravane.Sensor.Service.Services;
using Sentravane.Shared.Models;
using Xunit;

namespace Sentravane.Tests;

public class SensorUpdaterTests
{
    private class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private static IConfiguration BuildConfig(string currentVersion = "1.0.0") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sentravane:ServerUrl"] = "http://localhost:5000",
                ["Sentravane:SensorVersion"] = currentVersion
            })
            .Build();

    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.1.0", false)]
    [InlineData("1.0.0", "invalid", false)]
    public void IsNewerVersionAvailable_EvaluatesSemVerCorrectly(string current, string latest, bool expected)
    {
        var updater = new SensorUpdaterService(NullLogger<SensorUpdaterService>.Instance, BuildConfig(current));
        Assert.Equal(expected, updater.IsNewerVersionAvailable(current, latest));
    }

    [Fact]
    public async Task CheckAndApplyUpdateAsync_DownloadsVerifiesSha256AndInvokesInstaller()
    {
        byte[] fakeMsiBytes = Encoding.UTF8.GetBytes("FAKE_SIGNED_MSI_BINARY_PAYLOAD_V1_2_0");
        string validHash = Convert.ToHexString(SHA256.HashData(fakeMsiBytes));
        string tempUpdatesDir = Path.Combine(Path.GetTempPath(), $"sentravane_upd_{Guid.NewGuid():N}");

        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v1/fleet/version")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SensorVersionManifest
                    {
                        LatestVersion = "1.2.0",
                        DownloadUrl = "https://cdn.sentravane.internal/updates/SentravaneSensor-1.2.0.msi",
                        Sha256Checksum = validHash,
                        IsMandatory = true
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fakeMsiBytes)
            };
        });

        string? invokedPath = null;
        var updater = new SensorUpdaterService(
            NullLogger<SensorUpdaterService>.Instance,
            BuildConfig("1.0.0"),
            new HttpClient(handler),
            path => { invokedPath = path; return true; },
            tempUpdatesDir);

        try
        {
            bool result = await updater.CheckAndApplyUpdateAsync();
            Assert.True(result);
            Assert.NotNull(invokedPath);
            Assert.True(File.Exists(invokedPath));
            Assert.EndsWith("SentravaneSensor-1.2.0.msi", invokedPath);
        }
        finally
        {
            if (Directory.Exists(tempUpdatesDir)) Directory.Delete(tempUpdatesDir, true);
        }
    }

    [Fact]
    public async Task CheckAndApplyUpdateAsync_RejectsAndDeletesOnSha256Mismatch()
    {
        byte[] tamperedBytes = Encoding.UTF8.GetBytes("TAMPERED_MALICIOUS_PAYLOAD");
        string tempUpdatesDir = Path.Combine(Path.GetTempPath(), $"sentravane_upd_{Guid.NewGuid():N}");

        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v1/fleet/version")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SensorVersionManifest
                    {
                        LatestVersion = "1.2.0",
                        DownloadUrl = "https://cdn.sentravane.internal/updates/SentravaneSensor-1.2.0.msi",
                        Sha256Checksum = "0000000000000000000000000000000000000000000000000000000000000000"
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(tamperedBytes)
            };
        });

        bool installerCalled = false;
        var updater = new SensorUpdaterService(
            NullLogger<SensorUpdaterService>.Instance,
            BuildConfig("1.0.0"),
            new HttpClient(handler),
            _ => { installerCalled = true; return true; },
            tempUpdatesDir);

        try
        {
            bool result = await updater.CheckAndApplyUpdateAsync();
            Assert.False(result);
            Assert.False(installerCalled, "Installer must NEVER be invoked when SHA-256 verification fails.");
            Assert.Empty(Directory.GetFiles(tempUpdatesDir));
        }
        finally
        {
            if (Directory.Exists(tempUpdatesDir)) Directory.Delete(tempUpdatesDir, true);
        }
    }
}
