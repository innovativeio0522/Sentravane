using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Services;

public class LicenseManager(AppDbContext dbContext)
{
    public async Task<(bool CanEnroll, string? ErrorMessage, Tenant? Tenant)> CanEnrollAsync(string enrollmentToken, string? hardwareUuid = null)
    {
        var tenant = await dbContext.Tenants
            .Include(t => t.Endpoints)
            .FirstOrDefaultAsync(t => t.EnrollmentToken == enrollmentToken);

        if (tenant == null)
        {
            return (false, "Invalid enrollment token.", null);
        }

        if (!tenant.IsActive)
        {
            return (false, "Organization account is suspended. Contact your platform administrator.", tenant);
        }

        if (tenant.EnrollmentTokenExpiresUtc < DateTime.UtcNow)
        {
            return (false, "Enrollment token has expired.", tenant);
        }

        if (tenant.LicenseExpiresAtUtc.HasValue && tenant.LicenseExpiresAtUtc.Value < DateTime.UtcNow)
        {
            return (false, "Organization fleet license has expired.", tenant);
        }

        // Re-enrollment check: if the machine already possesses an active seat in this tenant, allow re-enrollment without consuming another seat
        if (!string.IsNullOrEmpty(hardwareUuid))
        {
            var existingEndpoint = tenant.Endpoints.FirstOrDefault(e => e.HardwareUuid == hardwareUuid);
            if (existingEndpoint != null && existingEndpoint.Status != EndpointStatus.Decommissioned)
            {
                return (true, null, tenant);
            }
        }

        var activeEndpointsCount = tenant.Endpoints.Count(e => e.Status != EndpointStatus.Decommissioned);
        if (activeEndpointsCount >= tenant.MaxSeats)
        {
            return (false, $"Seat quota exceeded ({activeEndpointsCount}/{tenant.MaxSeats} seats used).", tenant);
        }

        return (true, null, tenant);
    }

    public async Task<bool> DecommissionEndpointAsync(string endpointId)
    {
        var endpoint = await dbContext.Endpoints.FindAsync(endpointId);
        if (endpoint == null) return false;

        endpoint.Status = EndpointStatus.Decommissioned;
        await dbContext.SaveChangesAsync();
        return true;
    }
}
