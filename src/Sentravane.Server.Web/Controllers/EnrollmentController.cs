using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using FleetEndpoint = Sentravane.Server.Web.Data.Entities.Endpoint;
using Sentravane.Server.Web.Services;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/fleet")]
public class EnrollmentController(
    AppDbContext dbContext,
    LicenseManager licenseManager,
    ILogger<EnrollmentController> logger) : ControllerBase
{
    [HttpPost("enroll")]
    public async Task<ActionResult<EnrollmentResponse>> Enroll([FromBody] EnrollmentRequest request)
    {
        var (canEnroll, errorMessage, tenant) = await licenseManager.CanEnrollAsync(request.EnrollmentToken);
        if (!canEnroll || tenant == null)
        {
            return BadRequest(new { error = errorMessage });
        }

        // Check if endpoint already exists for this tenant + hardware
        var existingEndpoint = await dbContext.Endpoints
            .FirstOrDefaultAsync(e => e.TenantId == tenant.Id && e.HardwareUuid == request.HardwareUuid);

        string endpointId;
        string agentSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        if (existingEndpoint != null)
        {
            existingEndpoint.Hostname = request.Hostname;
            existingEndpoint.MacAddress = request.MacAddress;
            existingEndpoint.OsVersion = request.OsVersion;
            existingEndpoint.SensorVersion = request.SensorVersion;
            existingEndpoint.Status = EndpointStatus.Online;
            existingEndpoint.AgentSecretHash = HashSecret(agentSecret);
            endpointId = existingEndpoint.Id;
        }
        else
        {
            var newEndpoint = new FleetEndpoint
            {
                TenantId = tenant.Id,
                HardwareUuid = request.HardwareUuid,
                MacAddress = request.MacAddress,
                Hostname = request.Hostname,
                OsVersion = request.OsVersion,
                SensorVersion = request.SensorVersion,
                AgentSecretHash = HashSecret(agentSecret),
                Status = EndpointStatus.Online
            };

            dbContext.Endpoints.Add(newEndpoint);
            endpointId = newEndpoint.Id;
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Enrolled endpoint {EndpointId} for tenant {TenantId}", endpointId, tenant.Id);

        var response = new EnrollmentResponse
        {
            EndpointId = endpointId,
            TenantId = tenant.Id,
            AgentSecret = agentSecret,
            WebSocketUrl = $"{Request.Scheme}://{Request.Host}/hubs/fleet",
            HeartbeatIntervalSeconds = 30,
            Policies = new EndpointPolicy
            {
                UsbEnforcementEnabled = true,
                OverrideTimeoutSeconds = 20
            }
        };

        return Ok(response);
    }

    private static string HashSecret(string secret)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
