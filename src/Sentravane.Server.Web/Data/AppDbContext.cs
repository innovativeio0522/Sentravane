using Microsoft.EntityFrameworkCore;
using FleetEndpoint = Sentravane.Server.Web.Data.Entities.Endpoint;
using Sentravane.Server.Web.Data.Entities;

namespace Sentravane.Server.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<FleetEndpoint> Endpoints => Set<FleetEndpoint>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Policy> Policies => Set<Policy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>()
            .HasIndex(t => t.EnrollmentToken)
            .IsUnique();

        modelBuilder.Entity<FleetEndpoint>()
            .HasIndex(e => new { e.TenantId, e.HardwareUuid })
            .IsUnique();
    }
}
