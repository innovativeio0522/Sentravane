using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace Sentravane.Sensor.Service.Services;

public interface IEtwCollectorService
{
    void Start();
    void Stop();
    event EventHandler<FileAccessEvent>? FileAccessed;
}

public record FileAccessEvent(int ProcessId, string ProcessName, string FilePath, string Operation);

public class EtwCollectorService : IEtwCollectorService, IDisposable
{
    private readonly ILogger<EtwCollectorService> _logger;
    private TraceEventSession? _kernelSession;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    public event EventHandler<FileAccessEvent>? FileAccessed;

    public EtwCollectorService(ILogger<EtwCollectorService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (TraceEventSession.IsElevated() != true)
        {
            _logger.LogWarning("Current process does not have administrator privileges to start Kernel ETW session");
            return;
        }

        _cts = new CancellationTokenSource();
        _processingTask = Task.Run(() => ProcessKernelEvents(_cts.Token));
    }

    private void ProcessKernelEvents(CancellationToken cancellationToken)
    {
        try
        {
            const string sessionName = "SentravaneKernelFileSession";
            _kernelSession = new TraceEventSession(sessionName);
            _kernelSession.StopOnDispose = true;

            _kernelSession.EnableKernelProvider(
                KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.Process
            );

            _kernelSession.Source.Kernel.FileIOCreate += data =>
            {
                if (!string.IsNullOrWhiteSpace(data.FileName))
                {
                    FileAccessed?.Invoke(this, new FileAccessEvent(data.ProcessID, data.ProcessName, data.FileName, "Create/Open"));
                }
            };

            _kernelSession.Source.Kernel.FileIOWrite += data =>
            {
                if (!string.IsNullOrWhiteSpace(data.FileName))
                {
                    FileAccessed?.Invoke(this, new FileAccessEvent(data.ProcessID, data.ProcessName, data.FileName, "Write"));
                }
            };

            _kernelSession.Source.Kernel.FileIOSetInfo += data =>
            {
                if (!string.IsNullOrWhiteSpace(data.FileName))
                {
                    FileAccessed?.Invoke(this, new FileAccessEvent(data.ProcessID, data.ProcessName, data.FileName, "SetInfo/Rename"));
                }
            };

            _logger.LogInformation("ETW Kernel File Session started successfully");
            _kernelSession.Source.Process();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running ETW Kernel session");
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _kernelSession?.Dispose();
            _kernelSession = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ETW session");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

