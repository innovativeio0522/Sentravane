using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Hubs;
using Sentravane.Shared.Contracts;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/policies")]
[Authorize]
public class PolicyController(
    AppDbContext dbContext,
    IHubContext<FleetHub, IFleetHubClient> hubContext) : ControllerBase
{
    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetPolicy(string tenantId)
    {
        if (!User.IsInRole("SuperAdmin") && tenantId != User.FindFirstValue("TenantId"))
        {
            return Forbid();
        }

        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.TenantId == tenantId);
        if (policy == null)
        {
            policy = new Policy { TenantId = tenantId };
            dbContext.Policies.Add(policy);
            await dbContext.SaveChangesAsync();
        }

        return Ok(policy);
    }

    [HttpPost("{tenantId}")]
    public async Task<IActionResult> UpdatePolicy(string tenantId, [FromBody] Policy updated)
    {
        if (!User.IsInRole("SuperAdmin") && tenantId != User.FindFirstValue("TenantId"))
        {
            return Forbid();
        }

        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.TenantId == tenantId);
        if (policy == null)
        {
            policy = new Policy { TenantId = tenantId };
            dbContext.Policies.Add(policy);
        }

        policy.CanaryTripwiresEnabled = updated.CanaryTripwiresEnabled;
        policy.WfpQuarantineEnabled = updated.WfpQuarantineEnabled;
        policy.RapidBurstThresholdCount = updated.RapidBurstThresholdCount > 0 ? updated.RapidBurstThresholdCount : 40;
        policy.RapidBurstWindowSeconds = updated.RapidBurstWindowSeconds > 0 ? updated.RapidBurstWindowSeconds : 5;
        policy.CaptureMinidumpOnIncident = updated.CaptureMinidumpOnIncident;
        policy.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        // Broadcast policy hot-reload to all online endpoints in this tenant
        var endpoints = await dbContext.Endpoints
            .Where(e => e.TenantId == tenantId && !string.IsNullOrEmpty(e.CurrentConnectionId))
            .ToListAsync();

        var contractPolicy = new EndpointPolicy
        {
            CanaryTripwiresEnabled = policy.CanaryTripwiresEnabled,
            WfpQuarantineEnabled = policy.WfpQuarantineEnabled,
            RapidBurstThresholdCount = policy.RapidBurstThresholdCount,
            RapidBurstWindowSeconds = policy.RapidBurstWindowSeconds,
            CaptureMinidumpOnIncident = policy.CaptureMinidumpOnIncident,
            CanaryRelativePaths = ["Documents\\!00_sentravane_canary.docx", "Desktop\\!00_sentravane_canary.xlsx"]
        };

        foreach (var ep in endpoints)
        {
            if (!string.IsNullOrEmpty(ep.CurrentConnectionId))
            {
                await hubContext.Clients.Client(ep.CurrentConnectionId).UpdatePolicy(contractPolicy);
            }
        }

        return Ok(policy);
    }
}
