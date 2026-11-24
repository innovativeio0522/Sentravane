using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Shared.Models;
using Xunit;

namespace Sentravane.Tests;

public class TestServerFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sentravane_test_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite($"Data Source={_dbPath}");
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // Ignored if file handle is still released asynchronously by OS
            }
        }
    }
}

public class ServerIntegrationTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _factory;
    private readonly HttpClient _client;

    public ServerIntegrationTests(TestServerFixture factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task EndToEnd_ServerIntegration_FullLifecycleFlow()
    {
        // -------------------------------------------------------------
        // STEP 1: Verify Initial Bootstrap Status
        // -------------------------------------------------------------
        var statusResp = await _client.GetAsync("/api/v1/auth/status");
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);

        var statusJson = await statusResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(statusJson.GetProperty("requiresBootstrap").GetBoolean());
        Assert.False(statusJson.GetProperty("isAuthenticated").GetBoolean());

        // -------------------------------------------------------------
        // STEP 2: SuperAdmin Bootstrap
        // -------------------------------------------------------------
        var bootstrapPayload = new
        {
            email = "admin@sentravane.internal",
            password = "EnterpriseSecurePassword123!"
        };

        var bootstrapResp = await _client.PostAsJsonAsync("/api/v1/auth/bootstrap-superadmin", bootstrapPayload);
        Assert.Equal(HttpStatusCode.OK, bootstrapResp.StatusCode);

        // Verify Set-Cookie header contains authentication cookie
        Assert.True(bootstrapResp.Headers.Contains("Set-Cookie"), "Bootstrap must issue authentication cookie");

        // Verify authenticated status
        var authStatusResp = await _client.GetAsync("/api/v1/auth/status");
        var authStatusJson = await authStatusResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(authStatusJson.GetProperty("requiresBootstrap").GetBoolean());
        Assert.True(authStatusJson.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("admin@sentravane.internal", authStatusJson.GetProperty("email").GetString());
        Assert.Equal("SuperAdmin", authStatusJson.GetProperty("role").GetString());

        // Attempting a second bootstrap must be rejected with 409 Conflict
        var secondBootstrap = await _client.PostAsJsonAsync("/api/v1/auth/bootstrap-superadmin", bootstrapPayload);
        Assert.Equal(HttpStatusCode.Conflict, secondBootstrap.StatusCode);

        // -------------------------------------------------------------
        // STEP 3: SuperAdmin Provisions a New Customer Organization
        // -------------------------------------------------------------
        var createTenantPayload = new
        {
            name = "Wayne Cyber Defense",
            maxSeats = 2,
            tokenValidityDays = 30,
            licenseDays = 365,
            webhookUrl = "https://hooks.slack.com/services/wayne-alerts"
        };

        var createTenantResp = await _client.PostAsJsonAsync("/api/v1/admin/tenants", createTenantPayload);
        Assert.Equal(HttpStatusCode.OK, createTenantResp.StatusCode);

        var tenantData = await createTenantResp.Content.ReadFromJsonAsync<JsonElement>();
        string tenantId = tenantData.GetProperty("id").GetString()!;
        string enrollmentToken = tenantData.GetProperty("enrollmentToken").GetString()!;
        Assert.NotEmpty(tenantId);
        Assert.NotEmpty(enrollmentToken);
        Assert.StartsWith("sentra_etk_", enrollmentToken);

        // Verify tenant is enumerated in list
        var listTenantsResp = await _client.GetAsync("/api/v1/admin/tenants");
        Assert.Equal(HttpStatusCode.OK, listTenantsResp.StatusCode);
        var tenantsList = await listTenantsResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(tenantsList.GetArrayLength() >= 1);

        // -------------------------------------------------------------
        // STEP 4: Sensor Enrollment & Secret Hash Verification
        // -------------------------------------------------------------
        var unauthenticatedClient = _factory.CreateClient();

        // 4.1 First endpoint enrolls successfully
        var endpoint1Req = new EnrollmentRequest
        {
            EnrollmentToken = enrollmentToken,
            HardwareUuid = "HW-INTEGRATION-001",
            MacAddress = "00:50:56:C0:00:01",
            Hostname = "FIN-LAPTOP-01",
            OsVersion = "Windows 11 Enterprise",
            SensorVersion = "1.0.0"
        };

        var enroll1Resp = await unauthenticatedClient.PostAsJsonAsync("/api/v1/fleet/enroll", endpoint1Req);
        Assert.Equal(HttpStatusCode.OK, enroll1Resp.StatusCode);
        var enroll1 = (await enroll1Resp.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        Assert.NotEmpty(enroll1.EndpointId);
        Assert.NotEmpty(enroll1.AgentSecret);
        Assert.Contains("/hubs/fleet", enroll1.WebSocketUrl);

        // Verify hash integrity in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ep = await db.Endpoints.FindAsync(enroll1.EndpointId);
            Assert.NotNull(ep);
            var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enroll1.AgentSecret)));
            Assert.Equal(expectedHash, ep.AgentSecretHash, ignoreCase: true);
        }

        // 4.2 Second endpoint enrolls successfully (reaching 2/2 seat capacity)
        var endpoint2Req = new EnrollmentRequest
        {
            EnrollmentToken = enrollmentToken,
            HardwareUuid = "HW-INTEGRATION-002",
            MacAddress = "00:50:56:C0:00:02",
            Hostname = "FIN-LAPTOP-02",
            OsVersion = "Windows 11 Enterprise",
            SensorVersion = "1.0.0"
        };
        var enroll2Resp = await unauthenticatedClient.PostAsJsonAsync("/api/v1/fleet/enroll", endpoint2Req);
        Assert.Equal(HttpStatusCode.OK, enroll2Resp.StatusCode);

        // 4.3 Third endpoint attempts to enroll -> MUST be rejected (quota exceeded)
        var endpoint3Req = new EnrollmentRequest
        {
            EnrollmentToken = enrollmentToken,
            HardwareUuid = "HW-INTEGRATION-003",
            MacAddress = "00:50:56:C0:00:03",
            Hostname = "FIN-LAPTOP-03",
            OsVersion = "Windows 11 Enterprise",
            SensorVersion = "1.0.0"
        };
        var enroll3Resp = await unauthenticatedClient.PostAsJsonAsync("/api/v1/fleet/enroll", endpoint3Req);
        Assert.Equal(HttpStatusCode.BadRequest, enroll3Resp.StatusCode);

        // 4.4 Machine 1 re-enrolls (e.g. reboot/service restart) -> Allowed without taking new seat
        var reEnrollResp = await unauthenticatedClient.PostAsJsonAsync("/api/v1/fleet/enroll", endpoint1Req);
        Assert.Equal(HttpStatusCode.OK, reEnrollResp.StatusCode);

        // -------------------------------------------------------------
        // STEP 5: Dual Route Verification (/api/v1/enrollment/enroll)
        // -------------------------------------------------------------
        var dualRouteResp = await unauthenticatedClient.PostAsJsonAsync("/api/v1/enrollment/enroll", endpoint1Req);
        Assert.Equal(HttpStatusCode.OK, dualRouteResp.StatusCode);

        // -------------------------------------------------------------
        // STEP 6: Endpoint Telemetry Query
        // -------------------------------------------------------------
        var endpointsQueryResp = await _client.GetAsync("/api/v1/endpoints");
        Assert.Equal(HttpStatusCode.OK, endpointsQueryResp.StatusCode);
        var endpointsList = await endpointsQueryResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(endpointsList.GetArrayLength() >= 2);

        // -------------------------------------------------------------
        // STEP 7: Tenant Suspension Enforces Quarantine on New Enrollments
        // -------------------------------------------------------------
        var toggleResp = await _client.PostAsync($"/api/v1/admin/tenants/{tenantId}/toggle-status", null);
        Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);

        var suspendedEnrollResp = await unauthenticatedClient.PostAsJsonAsync("/api/v1/fleet/enroll", endpoint1Req);
        Assert.Equal(HttpStatusCode.BadRequest, suspendedEnrollResp.StatusCode);
    }

    [Fact]
    public async Task SuperAdminEndpoints_RequireAuthentication()
    {
        var anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Unauthenticated access to SuperAdmin routes must yield 401 Unauthorized
        var listResp = await anonymousClient.GetAsync("/api/v1/admin/tenants");
        Assert.Equal(HttpStatusCode.Unauthorized, listResp.StatusCode);

        var createResp = await anonymousClient.PostAsJsonAsync("/api/v1/admin/tenants", new { name = "Unauthorized Corp" });
        Assert.Equal(HttpStatusCode.Unauthorized, createResp.StatusCode);
    }
}
