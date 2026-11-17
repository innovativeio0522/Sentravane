using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/tenant")]
public class TenantController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentTenant()
    {
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync();
        if (tenant == null)
        {
            return NotFound(new { message = "No tenant registered" });
        }

        return Ok(new
        {
            tenant.Id,
            tenant.Name,
            tenant.MaxSeats,
            tenant.UsedSeats,
            tenant.EnrollmentToken,
            EndpointsCount = await dbContext.Endpoints.CountAsync(e => e.TenantId == tenant.Id)
        });
    }
}
