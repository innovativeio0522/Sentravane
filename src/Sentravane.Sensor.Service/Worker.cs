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

    public Worker(
        ILogger<Worker> logger,
        ICorrelationEngine correlationEngine,
        IFleetSignalRClient fleetClient)
    {
        _logger = logger;
        _correlationEngine = correlationEngine;
        _fleetClient = fleetClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sentravane Sensor Service starting up at: {time}", DateTimeOffset.Now);

        try
        {
            // Start Correlation and Detection Engine
            _correlationEngine.Start();

            // Connect to Fleet Hub
            await _fleetClient.StartAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10000, stoppingToken);
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
            await _fleetClient.StopAsync();
            _logger.LogInformation("Sentravane Sensor Service stopped gracefully");
        }
    }
}
