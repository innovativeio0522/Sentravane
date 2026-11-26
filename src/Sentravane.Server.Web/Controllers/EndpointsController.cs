using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Hubs;
using Sentravane.Shared.Contracts;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/endpoints")]
[Authorize]
public class EndpointsController(
    AppDbContext dbContext,
    IHubContext<FleetHub, IFleetHubClient> hubContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListEndpoints([FromQuery] string? tenantId)
    {
        var query = dbContext.Endpoints.AsQueryable();

        if (!User.IsInRole("SuperAdmin"))
        {
            var callerTenantId = User.FindFirstValue("TenantId");
            if (string.IsNullOrEmpty(callerTenantId)) return Forbid();
            query = query.Where(e => e.TenantId == callerTenantId);
        }
        else if (!string.IsNullOrEmpty(tenantId))
        {
            query = query.Where(e => e.TenantId == tenantId);
        }

        var list = await query.OrderByDescending(e => e.LastHeartbeatUtc).ToListAsync();
        return Ok(list);
    }

    [HttpPost("{endpointId}/isolate")]
    public async Task<IActionResult> ForceIsolateEndpoint(string endpointId)
    {
        var ep = await dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == endpointId);
        if (ep == null) return NotFound(new { message = "Endpoint not found" });

        if (!User.IsInRole("SuperAdmin") && ep.TenantId != User.FindFirstValue("TenantId"))
        {
            return Forbid();
        }

        ep.Status = EndpointStatus.Quarantined;
        await dbContext.SaveChangesAsync();

        if (!string.IsNullOrEmpty(ep.CurrentConnectionId))
        {
            await hubContext.Clients.Client(ep.CurrentConnectionId).IsolateEndpoint("manual_admin_quarantine");
        }

        return Ok(new { message = $"Endpoint {ep.Hostname} isolated successfully." });
    }

    [HttpPost("{endpointId}/release")]
    public async Task<IActionResult> ForceReleaseEndpoint(string endpointId)
    {
        var ep = await dbContext.Endpoints.FirstOrDefaultAsync(e => e.Id == endpointId);
        if (ep == null) return NotFound(new { message = "Endpoint not found" });

        if (!User.IsInRole("SuperAdmin") && ep.TenantId != User.FindFirstValue("TenantId"))
        {
            return Forbid();
        }

        ep.Status = EndpointStatus.Online;
        await dbContext.SaveChangesAsync();

        if (!string.IsNullOrEmpty(ep.CurrentConnectionId))
        {
            await hubContext.Clients.Client(ep.CurrentConnectionId).LiftQuarantine();
        }

        return Ok(new { message = $"Endpoint {ep.Hostname} quarantine lifted." });
    }
}
