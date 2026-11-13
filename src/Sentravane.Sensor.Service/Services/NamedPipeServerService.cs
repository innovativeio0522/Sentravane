using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentravane.Shared.IPC;

namespace Sentravane.Sensor.Service.Services;

public interface INamedPipeServerService
{
    void Start();
    void Stop();
}

public class NamedPipeServerService : INamedPipeServerService, IDisposable
{
    private const string PipeName = "sentravane-sensor";
    private readonly ILogger<NamedPipeServerService> _logger;
    private readonly ICanaryManager _canaryManager;
    private readonly IContainmentService _containmentService;
    private readonly IFleetSignalRClient _fleetClient;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public NamedPipeServerService(
        ILogger<NamedPipeServerService> logger,
        ICanaryManager canaryManager,
        IContainmentService containmentService,
        IFleetSignalRClient fleetClient)
    {
        _logger = logger;
        _canaryManager = canaryManager;
        _containmentService = containmentService;
        _fleetClient = fleetClient;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _logger.LogInformation("Named Pipe Server listening on \\\\.\\pipe\\{PipeName}", PipeName);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Create pipe with LocalSystem & BuiltinAdministrators DACL
                var pipeSecurity = new PipeSecurity();
                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

                pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
                pipeSecurity.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));

                var pipeServer = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity);

                await pipeServer.WaitForConnectionAsync(cancellationToken);
                _ = HandleClientConnectionAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in Named Pipe Server listener loop");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) return;

                var request = JsonSerializer.Deserialize<IpcRequest>(line);
                if (request == null) return;

                var response = ProcessRequest(request);
                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling named pipe client connection");
            }
        }
    }

    private IpcResponse ProcessRequest(IpcRequest request)
    {
        return request.Command switch
        {
            IpcCommand.GetStatus => new IpcResponse
            {
                Success = true,
                Message = "Sentravane Sensor Active",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    EndpointId = _fleetClient.EndpointId,
                    CanariesArmed = _canaryManager.RegisteredPaths.Count,
                    Status = "Running",
                    TimestampUtc = DateTime.UtcNow
                })
            },
            IpcCommand.ListCanaries => new IpcResponse
            {
                Success = true,
                Message = $"{_canaryManager.RegisteredPaths.Count} canaries registered",
                PayloadJson = JsonSerializer.Serialize(_canaryManager.RegisteredPaths)
            },
            IpcCommand.QuarantineEndpoint => HandleQuarantineCommand(true),
            IpcCommand.UnquarantineEndpoint => HandleQuarantineCommand(false),
            _ => new IpcResponse { Success = false, Message = $"Unknown command: {request.Command}" }
        };
    }

    private IpcResponse HandleQuarantineCommand(bool isolate)
    {
        if (isolate)
        {
            bool ok = _containmentService.IsolateEndpointNetwork();
            return new IpcResponse { Success = ok, Message = ok ? "Dynamic WFP Quarantine Active" : "Failed to isolate network" };
        }
        else
        {
            bool ok = _containmentService.RestoreEndpointNetwork();
            return new IpcResponse { Success = ok, Message = ok ? "Quarantine Released" : "Failed to release quarantine" };
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
    }
}
