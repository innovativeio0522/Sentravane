using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Server.Web.Hubs;
using Sentravane.Server.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

// Database Provider configuration (PostgreSQL or SQLite)
var dbProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=sentravane_fleet.db";

bool isPostgres = string.Equals(dbProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(dbProvider, "Npgsql", StringComparison.OrdinalIgnoreCase) ||
                  connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
                  connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
                  connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                  connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (isPostgres)
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
        });
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// Register Security & Licensing services
builder.Services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<LicenseManager>();
builder.Services.AddSingleton<WebhookDispatcher>();

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Sentravane.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

var app = builder.Build();

// Ensure DB schema is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<FleetHub>("/hubs/fleet");

app.Run();

// Partial class for WebApplicationFactory in integration tests
public partial class Program { }
