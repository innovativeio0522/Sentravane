using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sentravane.Server.Web.Controllers;
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

    [Fact]
    public async Task SuperAdmin_CanConfigureAndRetrieveTenantWebhook()
    {
        await using var db = CreateInMemoryDbContext();
        var hasher = new PasswordHasherService();
        var controller = new SuperAdminController(db, hasher);

        // 1. Create tenant with webhook URL
        var dto = new CreateTenantRequestDto(
            Name: "Acme Cyber",
            MaxSeats: 10,
            WebhookUrl: "https://hooks.slack.com/services/T00/B00/X123");

        var createResult = await controller.CreateTenant(dto);
        var okResult = Assert.IsType<OkObjectResult>(createResult);
        Assert.NotNull(okResult.Value);

        var tenant = await db.Tenants.FirstAsync(t => t.Name == "Acme Cyber");
        Assert.Equal("https://hooks.slack.com/services/T00/B00/X123", tenant.WebhookUrl);

        // 2. Update webhook URL
        var updateResult = await controller.UpdateWebhook(tenant.Id, new UpdateWebhookDto("https://siem.corp.internal/api/alerts"));
        Assert.IsType<OkObjectResult>(updateResult);

        var refreshed = await db.Tenants.FindAsync(tenant.Id);
        Assert.Equal("https://siem.corp.internal/api/alerts", refreshed!.WebhookUrl);
    }

    [Fact]
    public async Task EnrollmentController_IssuesSecretAndStoresHash()
    {
        await using var db = CreateInMemoryDbContext();
        var licenseManager = new LicenseManager(db);
        var logger = NullLogger<EnrollmentController>.Instance;
        var controller = new EnrollmentController(db, licenseManager, logger);

        var tenant = new Tenant
        {
            Id = "tenant_enroll_test",
            Name = "Enrollment Org",
            EnrollmentToken = "valid_token_xyz",
            MaxSeats = 5,
            IsActive = true
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var request = new EnrollmentRequest
        {
            EnrollmentToken = "valid_token_xyz",
            HardwareUuid = "HW-TEST-UUID",
            Hostname = "FIN-WORKSTATION",
            MacAddress = "00:11:22:33:44:55",
            OsVersion = "Windows 11 Enterprise",
            SensorVersion = "1.0.0"
        };

        var response = await controller.Enroll(request);
        var actionResult = Assert.IsType<ActionResult<EnrollmentResponse>>(response);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var enrollmentResp = Assert.IsType<EnrollmentResponse>(okResult.Value);

        Assert.NotEmpty(enrollmentResp.EndpointId);
        Assert.NotEmpty(enrollmentResp.AgentSecret);

        // Verify endpoint entity persisted in database has the matching SHA-256 hash
        var endpoint = await db.Endpoints.FindAsync(enrollmentResp.EndpointId);
        Assert.NotNull(endpoint);
        Assert.NotEmpty(endpoint.AgentSecretHash);

        var computedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enrollmentResp.AgentSecret)));
        Assert.Equal(computedHash, endpoint.AgentSecretHash, ignoreCase: true);
    }
}
