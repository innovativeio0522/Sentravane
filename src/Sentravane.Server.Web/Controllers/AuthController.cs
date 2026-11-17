using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Services;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    AppDbContext dbContext,
    IPasswordHasherService passwordHasher) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        bool hasSuperAdmin = await dbContext.Users.AnyAsync(u => u.Role == "SuperAdmin");
        if (!hasSuperAdmin)
        {
            return Ok(new
            {
                RequiresBootstrap = true,
                IsAuthenticated = false
            });
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return Ok(new
            {
                RequiresBootstrap = false,
                IsAuthenticated = true,
                Email = User.FindFirstValue(ClaimTypes.Email),
                Role = User.FindFirstValue(ClaimTypes.Role),
                TenantId = User.FindFirstValue("TenantId")
            });
        }

        return Ok(new
        {
            RequiresBootstrap = false,
            IsAuthenticated = false
        });
    }

    [HttpPost("bootstrap-superadmin")]
    public async Task<IActionResult> BootstrapSuperAdmin([FromBody] BootstrapSuperAdminDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
        {
            return BadRequest(new { message = "Valid email and a password of at least 8 characters are required." });
        }

        // Hard security gate: bootstrap can only be executed if no SuperAdmin exists
        bool superAdminExists = await dbContext.Users.AnyAsync(u => u.Role == "SuperAdmin");
        if (superAdminExists)
        {
            return Conflict(new { message = "Platform SuperAdmin account already initialized. Please log in." });
        }

        var superAdmin = new User
        {
            Email = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHasher.HashPassword(dto.Password),
            Role = "SuperAdmin",
            TenantId = null
        };

        dbContext.Users.Add(superAdmin);
        await dbContext.SaveChangesAsync();

        await SignInUserAsync(superAdmin);

        return Ok(new
        {
            message = "SuperAdmin account created successfully.",
            superAdmin.Email,
            superAdmin.Role
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null || !passwordHasher.VerifyPassword(user.PasswordHash, dto.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        await SignInUserAsync(user);

        return Ok(new
        {
            message = "Login successful.",
            user.Email,
            user.Role,
            user.TenantId
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out successfully." });
    }

    private async Task SignInUserAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        if (!string.IsNullOrEmpty(user.TenantId))
        {
            claims.Add(new Claim("TenantId", user.TenantId));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }
}

public record BootstrapSuperAdminDto(string Email, string Password);
public record LoginRequestDto(string Email, string Password);
