using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Services;
using Sentravane.Shared.Models;
using Xunit;

namespace Sentravane.Tests;

public class SuperAdminAndLicensingTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void PasswordHasher_HashesAndVerifiesCorrectly()
    {
        var hasher = new PasswordHasherService();
        string password = "EnterpriseSecurePassword123!";

        string hash = hasher.HashPassword(password);
        Assert.NotEmpty(hash);
        Assert.NotEqual(password, hash);

        Assert.True(hasher.VerifyPassword(hash, password));
        Assert.False(hasher.VerifyPassword(hash, "WrongPassword!"));
    }

    [Fact]
    public async Task LicenseManager_EnforcesSeatLimitsAndReEnrollment()
    {
        await using var db = CreateInMemoryDbContext();
        var licenseManager = new LicenseManager(db);

        var tenant = new Tenant
        {
            Id = "tenant_test",
            Name = "Test Corp",
            EnrollmentToken = "test_token_123",
            MaxSeats = 2,
            UsedSeats = 0,
            IsActive = true
        };
        db.Tenants.Add(tenant);

        // Add 2 endpoints (at capacity)
        db.Endpoints.Add(new Endpoint
        {
            TenantId = "tenant_test",
            HardwareUuid = "HW-001",
            Hostname = "PC-001",
            Status = EndpointStatus.Online
        });
        db.Endpoints.Add(new Endpoint
        {
            TenantId = "tenant_test",
            HardwareUuid = "HW-002",
            Hostname = "PC-002",
            Status = EndpointStatus.Online
        });
        await db.SaveChangesAsync();

        // 1. New machine attempting enrollment should be rejected (seat quota exceeded)
        var (canEnrollNew, errNew, _) = await licenseManager.CanEnrollAsync("test_token_123", "HW-003");
        Assert.False(canEnrollNew);
        Assert.Contains("Seat quota exceeded", errNew);

        // 2. Existing machine re-enrolling (e.g. reboot/reinstall) should be allowed without extra seat
        var (canReEnroll, errRe, _) = await licenseManager.CanEnrollAsync("test_token_123", "HW-001");
        Assert.True(canReEnroll);
        Assert.Null(errRe);
    }

    [Fact]
    public async Task LicenseManager_RejectsSuspendedTenant()
    {
        await using var db = CreateInMemoryDbContext();
        var licenseManager = new LicenseManager(db);

        var tenant = new Tenant
        {
            Id = "tenant_suspended",
            Name = "Suspended Corp",
            EnrollmentToken = "token_suspended",
            MaxSeats = 50,
            IsActive = false // Suspended!
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var (canEnroll, err, _) = await licenseManager.CanEnrollAsync("token_suspended", "HW-999");
        Assert.False(canEnroll);
        Assert.Contains("suspended", err?.ToLowerInvariant());
    }

    [Fact]
    public async Task LicenseManager_RejectsExpiredToken()
    {
        await using var db = CreateInMemoryDbContext();
        var licenseManager = new LicenseManager(db);

        var tenant = new Tenant
        {
            Id = "tenant_expired",
            Name = "Expired Corp",
            EnrollmentToken = "token_expired",
            EnrollmentTokenExpiresUtc = DateTime.UtcNow.AddDays(-1), // Expired!
            MaxSeats = 50,
            IsActive = true
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var (canEnroll, err, _) = await licenseManager.CanEnrollAsync("token_expired", "HW-999");
        Assert.False(canEnroll);
        Assert.Contains("expired", err?.ToLowerInvariant());
    }
}
