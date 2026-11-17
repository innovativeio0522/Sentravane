using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;

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
            return Ok(new
            {
                Registered = false,
                Name = "No Corporation Registered",
                MaxSeats = 0,
                UsedSeats = 0,
                EndpointsCount = 0
            });
        }

        return Ok(new
        {
            Registered = true,
            tenant.Id,
            tenant.Name,
            tenant.MaxSeats,
            tenant.UsedSeats,
            tenant.EnrollmentToken,
            EndpointsCount = await dbContext.Endpoints.CountAsync(e => e.TenantId == tenant.Id)
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "Corporation name cannot be empty." });
        }

        var existing = await dbContext.Tenants.FirstOrDefaultAsync();
        if (existing != null)
        {
            existing.Name = dto.Name;
            existing.MaxSeats = dto.MaxSeats > 0 ? dto.MaxSeats : 50;
            await dbContext.SaveChangesAsync();
            return Ok(existing);
        }

        var newTenant = new Tenant
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = dto.Name,
            EnrollmentToken = $"sentra_{Guid.NewGuid():N}",
            MaxSeats = dto.MaxSeats > 0 ? dto.MaxSeats : 50,
            UsedSeats = 0
        };

        dbContext.Tenants.Add(newTenant);
        await dbContext.SaveChangesAsync();
        return Ok(newTenant);
    }
}

public record RegisterTenantDto(string Name, int MaxSeats);
