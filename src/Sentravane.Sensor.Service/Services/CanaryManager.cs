using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Sentravane.Sensor.Service.Services;

public interface ICanaryManager
{
    void InitializeCanaries();
    bool IsCanaryPath(string fullPath);
    void CleanupCanaries();
    IReadOnlyCollection<string> RegisteredPaths { get; }
}

public class CanaryManager : ICanaryManager, IDisposable
{
    private readonly ILogger<CanaryManager> _logger;
    private readonly ConcurrentDictionary<string, byte[]> _canaryRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();

    private static readonly string[] CanaryNames =
    {
        "$!00_Important_Document.docx",
        "$!00_Financial_Report.xlsx",
        "~$_Password_Vault_Backup.kdbx",
        "A000_Executive_Summary.pdf"
    };

    public CanaryManager(ILogger<CanaryManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<string> RegisteredPaths => _canaryRegistry.Keys.ToList().AsReadOnly();

    public void InitializeCanaries()
    {
        var targetDirectories = UserProfileLocator.GetInteractiveUserFolders();
        if (targetDirectories.Count == 0)
        {
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "SentravaneCanaries");
            targetDirectories.Add(fallback);
        }

        foreach (var dir in targetDirectories)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                foreach (var fileName in CanaryNames)
                {
                    var filePath = Path.Combine(dir, fileName);
                    if (!File.Exists(filePath))
                    {
                        var content = GenerateCanaryContent();
                        File.WriteAllBytes(filePath, content);
                        File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.Archive);
                    }

                    var hash = SHA256.HashData(File.ReadAllBytes(filePath));
                    _canaryRegistry[filePath] = hash;
                }

                var watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                watcher.Changed += OnCanaryFileActivity;
                watcher.Deleted += OnCanaryFileActivity;
                watcher.Renamed += OnCanaryFileActivity;
                _watchers.Add(watcher);

                _logger.LogInformation("Canary tripwires armed in directory: {Directory}", dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to arm canaries in directory: {Directory}", dir);
            }
        }
    }

    private void OnCanaryFileActivity(object sender, FileSystemEventArgs e)
    {
        if (IsCanaryPath(e.FullPath))
        {
            _logger.LogWarning("Canary tripwire file event triggered: {ChangeType} on {FullPath}", e.ChangeType, e.FullPath);
            CanaryTriggered?.Invoke(this, e.FullPath);
        }
    }

    public event EventHandler<string>? CanaryTriggered;

    public bool IsCanaryPath(string fullPath)
    {
        return _canaryRegistry.ContainsKey(fullPath);
    }

    private static byte[] GenerateCanaryContent()
    {
        var data = new byte[1024];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);
        return data;
    }

    public void CleanupCanaries()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        foreach (var path in _canaryRegistry.Keys)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove canary {Path} on cleanup", path);
            }
        }
        _canaryRegistry.Clear();
    }

    public void Dispose()
    {
        CleanupCanaries();
    }
}
