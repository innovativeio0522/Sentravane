using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentravane.Sensor.Service.Services;

namespace Sentravane.Sensor.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ICorrelationEngine _correlationEngine;
    private readonly IFleetSignalRClient _fleetClient;
    private readonly INamedPipeServerService _pipeServer;

    public Worker(
        ILogger<Worker> logger,
        ICorrelationEngine correlationEngine,
        IFleetSignalRClient fleetClient,
        INamedPipeServerService pipeServer)
    {
        _logger = logger;
        _correlationEngine = correlationEngine;
        _fleetClient = fleetClient;
        _pipeServer = pipeServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sentravane Sensor Service starting up at: {time}", DateTimeOffset.Now);

        try
        {
            // Start Local Named Pipe IPC listener
            _pipeServer.Start();

            // Start Correlation and Detection Engine (Canaries + ETW + Rapid Encryption Anomaly)
            _correlationEngine.Start();

            // Connect to Fleet Hub over SignalR
            await _fleetClient.StartAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Periodic maintenance: drain offline queue if connection restored
                if (_fleetClient.IsConnected)
                {
                    await _fleetClient.DrainOfflineQueueAsync();
                }

                await Task.Delay(15000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Sentravane Sensor background worker");
        }
        finally
        {
            _correlationEngine.Stop();
            _pipeServer.Stop();
            await _fleetClient.StopAsync();
            _logger.LogInformation("Sentravane Sensor Service stopped gracefully");
        }
    }
}
