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
        var endpointId = httpContext?.Request.Headers["X-Endpoint-Id"].FirstOrDefault()
            ?? httpContext?.Request.Headers["X-Endpoint-ID"].FirstOrDefault();
        var agentSecret = httpContext?.Request.Headers["X-Agent-Secret"].FirstOrDefault();

        if (!string.IsNullOrEmpty(endpointId))
        {
            var endpoint = await dbContext.Endpoints.FindAsync(endpointId);
            if (endpoint == null)
            {
                logger.LogWarning("Rejecting SignalR connection: Unknown endpoint {EndpointId}", endpointId);
                Context.Abort();
                return;
            }

            // Verify AgentSecretHash if configured on the endpoint
            if (!string.IsNullOrEmpty(endpoint.AgentSecretHash))
            {
                if (string.IsNullOrEmpty(agentSecret) || !VerifySecret(agentSecret, endpoint.AgentSecretHash))
                {
                    logger.LogWarning("Rejecting SignalR connection: Invalid or missing agent secret for endpoint {EndpointId}", endpointId);
                    Context.Abort();
                    return;
                }
            }

            ConnectionMap[Context.ConnectionId] = endpointId;
            endpoint.CurrentConnectionId = Context.ConnectionId;
            endpoint.Status = EndpointStatus.Online;
            endpoint.LastHeartbeatUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Endpoint {EndpointId} connected with socket {ConnectionId}", endpointId, Context.ConnectionId);

            await Clients.Groups("superadmins", $"tenant_{endpoint.TenantId}").FleetUpdated();
        }
        else if (Context.User?.Identity?.IsAuthenticated == true)
        {
            // Dashboard browser session: join scoped tenant or superadmin group
            if (Context.User.IsInRole("SuperAdmin"))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "superadmins");
            }
            else if (Context.User.FindFirst("TenantId")?.Value is { Length: > 0 } tenantId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
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

                await Clients.Groups("superadmins", $"tenant_{endpoint.TenantId}").FleetUpdated();
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

            await Clients.Groups("superadmins", $"tenant_{endpoint.TenantId}").FleetUpdated();
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

        // Notify authenticated dashboard browsers in real time
        if (endpoint != null)
        {
            await Clients.Groups("superadmins", $"tenant_{endpoint.TenantId}").ReportIncident(snapshot);
        }

        // Dispatch background webhook alert to tenant-configured webhook or fallback
        var tenant = endpoint != null ? await dbContext.Tenants.FindAsync(endpoint.TenantId) : null;
        var webhookUrl = tenant?.WebhookUrl;
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            _ = webhookDispatcher.BroadcastIncidentAsync(webhookUrl, snapshot);
        }
    }

    private static bool VerifySecret(string secret, string expectedHash)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var actualHash = Convert.ToHexString(hash);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public Task ReportUsbEvent(string serialNumber, bool isAuthorized, string actionTaken)
    {
        logger.LogInformation("USB Event: Serial={Serial}, Authorized={Authorized}, Action={Action}",
            serialNumber, isAuthorized, actionTaken);
        return Task.CompletedTask;
    }
}

