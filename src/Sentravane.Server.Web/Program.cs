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

// Ensure DB schema is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseRouting();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<FleetHub>("/hubs/fleet");

app.Run();

// Partial class for WebApplicationFactory in integration tests
public partial class Program { }

