using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sentravane.Shared.Models;

namespace Sentravane.Sensor.Service.Services;

public interface ISensorUpdaterService
{
    string CurrentVersion { get; }
    bool IsNewerVersionAvailable(string currentVersion, string latestVersion);
    Task<bool> CheckAndApplyUpdateAsync(CancellationToken cancellationToken = default);
}

public class SensorUpdaterService : ISensorUpdaterService
{
    private readonly ILogger<SensorUpdaterService> _logger;
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly Func<string, bool> _msiInstallerRunner;
    private readonly string _updatesDirectory;

    public string CurrentVersion { get; }

    public SensorUpdaterService(
        ILogger<SensorUpdaterService> logger,
        IConfiguration config,
        HttpClient? httpClient = null,
        Func<string, bool>? msiInstallerRunner = null,
        string? updatesDirectory = null)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _msiInstallerRunner = msiInstallerRunner ?? DefaultRunMsiInstaller;
        _updatesDirectory = updatesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Sentravane",
            "Updates");

        CurrentVersion = _config["Sentravane:SensorVersion"] ?? "1.0.0";
    }

    public bool IsNewerVersionAvailable(string currentVersion, string latestVersion)
    {
        if (!Version.TryParse(currentVersion?.Trim(), out var current) ||
            !Version.TryParse(latestVersion?.Trim(), out var latest))
        {
            return false;
        }

        return latest > current;
    }

    public async Task<bool> CheckAndApplyUpdateAsync(CancellationToken cancellationToken = default)
    {
        var serverUrl = (_config["Sentravane:ServerUrl"] ?? "http://localhost:5000").TrimEnd('/');
        try
        {
            var manifest = await _httpClient.GetFromJsonAsync<SensorVersionManifest>(
                $"{serverUrl}/api/v1/fleet/version",
                cancellationToken);

            if (manifest == null)
            {
                _logger.LogDebug("Empty version manifest received from server.");
                return false;
            }

            if (!IsNewerVersionAvailable(CurrentVersion, manifest.LatestVersion))
            {
                _logger.LogDebug("Sensor is up to date (Current={Current}, Latest={Latest}).",
                    CurrentVersion, manifest.LatestVersion);
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl) ||
                !Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.LogWarning("Update available ({Latest}) but DownloadUrl is missing or invalid.", manifest.LatestVersion);
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.Sha256Checksum))
            {
                _logger.LogWarning("Refusing unsigned/unverified update {Latest}: Sha256Checksum is required.", manifest.LatestVersion);
                return false;
            }

            Directory.CreateDirectory(_updatesDirectory);
            var safeVersion = string.Concat(manifest.LatestVersion.Split(Path.GetInvalidFileNameChars()));
            var msiPath = Path.Combine(_updatesDirectory, $"SentravaneSensor-{safeVersion}.msi");

            _logger.LogInformation("Downloading Sentravane Sensor update v{Version} from {Url}...",
                manifest.LatestVersion, downloadUri);

            using (var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var fileStream = new FileStream(msiPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            // Verify SHA-256 integrity before executing installer
            var actualHash = await ComputeFileSha256Async(msiPath, cancellationToken);
            if (!string.Equals(actualHash, manifest.Sha256Checksum.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "SHA-256 checksum mismatch for update v{Version}! Expected={Expected}, Actual={Actual}. Deleting file.",
                    manifest.LatestVersion, manifest.Sha256Checksum, actualHash);

                TryDeleteFile(msiPath);
                return false;
            }

            _logger.LogInformation("SHA-256 verified for v{Version}. Launching silent MSI upgrade...", manifest.LatestVersion);
            return _msiInstallerRunner(msiPath);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sensor auto-update check failed.");
            return false;
        }
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    private bool DefaultRunMsiInstaller(string msiPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Silent MSI installation is only supported on Windows.");
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{msiPath}\" /qn /norestart",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            _logger.LogError("Failed to start msiexec.exe for {Path}", msiPath);
            return false;
        }

        _logger.LogInformation("Started msiexec.exe (PID {Pid}) for silent sensor upgrade.", process.Id);
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failure
        }
    }
}
