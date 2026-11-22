using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Services;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/admin/tenants")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController(
    AppDbContext dbContext,
    IPasswordHasherService passwordHasher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListTenants()
    {
        var tenants = await dbContext.Tenants
            .Include(t => t.Endpoints)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.EnrollmentToken,
                t.EnrollmentTokenExpiresUtc,
                t.MaxSeats,
                UsedSeats = t.Endpoints.Count,
                t.IsActive,
                t.LicenseExpiresAtUtc,
                t.WebhookUrl,
                t.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(tenants);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "Organization name cannot be empty." });
        }

        // Generate high-entropy 256-bit CSPRNG token with clean prefix
        var rawBytes = RandomNumberGenerator.GetBytes(24);
        var token = "sentra_etk_" + Convert.ToHexString(rawBytes).ToLowerInvariant();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = dto.Name.Trim(),
            EnrollmentToken = token,
            EnrollmentTokenExpiresUtc = DateTime.UtcNow.AddDays(dto.TokenValidityDays > 0 ? dto.TokenValidityDays : 30),
            MaxSeats = dto.MaxSeats > 0 ? dto.MaxSeats : 50,
            UsedSeats = 0,
            IsActive = true,
            LicenseExpiresAtUtc = dto.LicenseDays > 0 ? DateTime.UtcNow.AddDays(dto.LicenseDays) : null,
            WebhookUrl = string.IsNullOrWhiteSpace(dto.WebhookUrl) ? null : dto.WebhookUrl.Trim()
        };

        dbContext.Tenants.Add(tenant);

        // Optionally create initial TenantAdmin account
        if (!string.IsNullOrWhiteSpace(dto.AdminEmail) && !string.IsNullOrWhiteSpace(dto.AdminPassword))
        {
            var adminUser = new User
            {
                TenantId = tenant.Id,
                Email = dto.AdminEmail.Trim().ToLowerInvariant(),
                PasswordHash = passwordHasher.HashPassword(dto.AdminPassword),
                Role = "TenantAdmin"
            };
            dbContext.Users.Add(adminUser);
        }

        await dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Organization created successfully.",
            tenant.Id,
            tenant.Name,
            tenant.EnrollmentToken,
            tenant.MaxSeats,
            tenant.EnrollmentTokenExpiresUtc
        });
    }

    [HttpPut("{tenantId}/license")]
    public async Task<IActionResult> UpdateLicense(string tenantId, [FromBody] UpdateLicenseDto dto)
    {
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound(new { message = "Organization not found." });

        if (dto.MaxSeats > 0)
        {
            tenant.MaxSeats = dto.MaxSeats;
        }

        if (dto.ExtendDays.HasValue && dto.ExtendDays.Value > 0)
        {
            var baseDate = tenant.LicenseExpiresAtUtc ?? DateTime.UtcNow;
            if (baseDate < DateTime.UtcNow) baseDate = DateTime.UtcNow;
            tenant.LicenseExpiresAtUtc = baseDate.AddDays(dto.ExtendDays.Value);
        }

        await dbContext.SaveChangesAsync();
        return Ok(new
        {
            message = "License updated successfully.",
            tenant.Id,
            tenant.MaxSeats,
            tenant.LicenseExpiresAtUtc
        });
    }

    [HttpPost("{tenantId}/rotate-token")]
    public async Task<IActionResult> RotateToken(string tenantId)
    {
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound(new { message = "Organization not found." });

        var rawBytes = RandomNumberGenerator.GetBytes(24);
        tenant.EnrollmentToken = "sentra_etk_" + Convert.ToHexString(rawBytes).ToLowerInvariant();
        tenant.EnrollmentTokenExpiresUtc = DateTime.UtcNow.AddDays(30);

        await dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "New enrollment token generated.",
            tenant.EnrollmentToken,
            tenant.EnrollmentTokenExpiresUtc
        });
    }

    [HttpPost("{tenantId}/toggle-status")]
    public async Task<IActionResult> ToggleTenantStatus(string tenantId)
    {
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound(new { message = "Organization not found." });

        tenant.IsActive = !tenant.IsActive;
        await dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = $"Organization is now {(tenant.IsActive ? "Active" : "Suspended")}.",
            tenant.IsActive
        });
    }

    [HttpPut("{tenantId}/webhook")]
    public async Task<IActionResult> UpdateWebhook(string tenantId, [FromBody] UpdateWebhookDto dto)
    {
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound(new { message = "Organization not found." });

        tenant.WebhookUrl = string.IsNullOrWhiteSpace(dto.WebhookUrl) ? null : dto.WebhookUrl.Trim();
        await dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Webhook configuration updated successfully.",
            tenant.Id,
            tenant.WebhookUrl
        });
    }
}

public record CreateTenantRequestDto(
    string Name,
    int MaxSeats,
    int TokenValidityDays = 30,
    int LicenseDays = 365,
    string? AdminEmail = null,
    string? AdminPassword = null,
    string? WebhookUrl = null);

public record UpdateLicenseDto(int MaxSeats, int? ExtendDays);

public record UpdateWebhookDto(string? WebhookUrl);
