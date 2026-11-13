using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentravane.Sensor.Service;
using Sentravane.Sensor.Service.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SentravaneSensor";
});

// Register Sensor core services
builder.Services.AddSingleton<ILocalStoreService, LocalStoreService>();
builder.Services.AddSingleton<ICanaryManager, CanaryManager>();
builder.Services.AddSingleton<IContainmentService, ContainmentService>();
builder.Services.AddSingleton<IEtwCollectorService, EtwCollectorService>();
builder.Services.AddSingleton<IRapidEncryptionDetector, RapidEncryptionDetector>();
builder.Services.AddSingleton<IForensicDumpService, ForensicDumpService>();
builder.Services.AddSingleton<ICorrelationEngine, CorrelationEngine>();
builder.Services.AddSingleton<IFleetSignalRClient, FleetSignalRClient>();
builder.Services.AddSingleton<INamedPipeServerService, NamedPipeServerService>();

// Background Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
