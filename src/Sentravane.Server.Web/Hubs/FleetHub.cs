using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Services;
using Sentravane.Shared.Contracts;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Hubs;

public class FleetHub(
    AppDbContext dbContext,
    WebhookDispatcher webhookDispatcher,
    ILogger<FleetHub> logger) : Hub<IFleetHubClient>, IFleetHubServer
{
    // Thread-safe mapping of ConnectionId -> EndpointId
    private static readonly ConcurrentDictionary<string, string> ConnectionMap = new();

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var endpointId = httpContext?.Request.Headers["X-Endpoint-ID"].FirstOrDefault();

        if (!string.IsNullOrEmpty(endpointId))
        {
            ConnectionMap[Context.ConnectionId] = endpointId;
            var endpoint = await dbContext.Endpoints.FindAsync(endpointId);
            if (endpoint != null)
            {
                endpoint.CurrentConnectionId = Context.ConnectionId;
                endpoint.Status = EndpointStatus.Online;
                endpoint.LastHeartbeatUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                logger.LogInformation("Endpoint {EndpointId} connected with socket {ConnectionId}", endpointId, Context.ConnectionId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionMap.TryRemove(Context.ConnectionId, out var endpointId))
        {
            var endpoint = await dbContext.Endpoints.FindAsync(endpointId);
            if (endpoint != null)
            {
                endpoint.Status = EndpointStatus.Offline;
                endpoint.CurrentConnectionId = null;
                await dbContext.SaveChangesAsync();
                logger.LogInformation("Endpoint {EndpointId} disconnected", endpointId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendHeartbeat(HeartbeatPayload payload)
    {
        var endpoint = await dbContext.Endpoints.FindAsync(payload.EndpointId);
        if (endpoint != null)
        {
            endpoint.LastHeartbeatUtc = DateTime.UtcNow;
            endpoint.Status = payload.ContainmentStatus == ContainmentStatus.Quarantined 
                ? EndpointStatus.Quarantined 
                : EndpointStatus.Online;
            await dbContext.SaveChangesAsync();
        }

        await Clients.Caller.AcknowledgeHeartbeat(DateTime.UtcNow);
    }

    public async Task ReportIncident(ForensicSnapshotPayload snapshot)
    {
        logger.LogWarning("Incident reported by {EndpointId}: {Reason} (PID {Pid})",
            snapshot.EndpointId, snapshot.TriggerReason, snapshot.OffendingProcess.Pid);

        var incident = new Incident
        {
            EndpointId = snapshot.EndpointId,
            Reason = snapshot.TriggerReason,
            CanaryPath = snapshot.CanaryFilePath,
            OffendingPid = snapshot.OffendingProcess.Pid,
            ProcessName = snapshot.OffendingProcess.ProcessName,
            CommandLine = snapshot.OffendingProcess.CommandLine,
            ForensicSnapshotJson = JsonSerializer.Serialize(snapshot),
            OccurredAtUtc = snapshot.TimestampUtc
        };

        dbContext.Incidents.Add(incident);

        var endpoint = await dbContext.Endpoints.FindAsync(snapshot.EndpointId);
        if (endpoint != null)
        {
            endpoint.Status = EndpointStatus.Quarantined;
        }

        await dbContext.SaveChangesAsync();

        // Dispatch background webhook alert
        _ = webhookDispatcher.BroadcastIncidentAsync("https://hooks.slack.com/services/sentravane-demo-alert", snapshot);
    }

    public Task ReportUsbEvent(string serialNumber, bool isAuthorized, string actionTaken)
    {
        logger.LogInformation("USB Event: Serial={Serial}, Authorized={Authorized}, Action={Action}",
            serialNumber, isAuthorized, actionTaken);
        return Task.CompletedTask;
    }
}

