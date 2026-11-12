using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Hubs;
using Sentravane.Shared.Contracts;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/incidents")]
public class IncidentController(
    AppDbContext dbContext,
    IHubContext<FleetHub, IFleetHubClient> hubContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListIncidents([FromQuery] string? endpointId)
    {
        var query = dbContext.Incidents.AsQueryable();
        if (!string.IsNullOrEmpty(endpointId))
        {
            query = query.Where(i => i.EndpointId == endpointId);
        }

        var incidents = await query.OrderByDescending(i => i.OccurredAtUtc).Take(50).ToListAsync();
        return Ok(incidents);
    }

    [HttpPost("{incidentId}/lift-quarantine")]
    public async Task<IActionResult> LiftQuarantine(string incidentId)
    {
        var incident = await dbContext.Incidents.Include(i => i.Endpoint).FirstOrDefaultAsync(i => i.Id == incidentId);
        if (incident == null) return NotFound();

        incident.IsResolved = true;

        if (incident.Endpoint != null)
        {
            incident.Endpoint.Status = EndpointStatus.Online;
            if (!string.IsNullOrEmpty(incident.Endpoint.CurrentConnectionId))
            {
                await hubContext.Clients.Client(incident.Endpoint.CurrentConnectionId).LiftQuarantine();
            }
        }

        await dbContext.SaveChangesAsync();
        return Ok(new { message = "Quarantine lifted." });
    }
}

