using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Hubs;
using Sentravane.Server.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

// SQLite for local developer velocity and fleet storage
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=sentravane_fleet.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<LicenseManager>();
builder.Services.AddSingleton<WebhookDispatcher>();

builder.Services.AddSignalR();

var app = builder.Build();

// Ensure DB is created and seed a default test tenant
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    if (!db.Tenants.Any())
    {
        db.Tenants.Add(new Tenant
        {
            Id = "tenant_default",
            Name = "Acme Security Corp",
            EnrollmentToken = "sentra_enroll_demo_token_12345",
            MaxSeats = 50,
            UsedSeats = 0
        });
        db.SaveChanges();
    }
}

app.UseRouting();
app.MapControllers();
app.MapHub<FleetHub>("/hubs/fleet");

app.Run();

// Partial class for WebApplicationFactory in integration tests
public partial class Program { }

