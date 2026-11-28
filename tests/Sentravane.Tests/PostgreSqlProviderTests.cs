using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentravane.Server.Web.Data;
using Xunit;

namespace Sentravane.Tests;

public class PostgreSqlProviderTests
{
    private static IServiceProvider BuildServiceProvider(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        var dbProvider = configuration["DatabaseProvider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=sentravane_fleet.db";

        bool isPostgres = string.Equals(dbProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(dbProvider, "Npgsql", StringComparison.OrdinalIgnoreCase) ||
                          connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
                          connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
                          connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                          connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        services.AddDbContext<AppDbContext>(options =>
        {
            if (isPostgres)
            {
                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AppDbContext_SelectsPostgreSqlProvider_WhenDatabaseProviderIsPostgres()
    {
        var sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["DatabaseProvider"] = "PostgreSql",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=sentravane;Username=postgres;Password=pass"
        });

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
    }

    [Fact]
    public void AppDbContext_AutodetectsPostgreSql_WhenConnectionStringStartsWithHost()
    {
        var sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.internal;Port=5432;Database=sentravane;Username=user;Password=pass"
        });

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
    }

    [Fact]
    public void AppDbContext_SelectsSqliteProvider_ByDefault()
    {
        var sp = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=test.db"
        });

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
    }
}
