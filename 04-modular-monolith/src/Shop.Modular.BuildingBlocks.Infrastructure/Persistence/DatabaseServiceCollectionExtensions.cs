using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Shop.Modular.BuildingBlocks.Infrastructure.Persistence;

public static class DatabaseServiceCollectionExtensions
{
    /// <summary>The database every module lives in (one schema each), for <c>dotnet ef</c> at design time.</summary>
    public const string DesignTimeConnectionString = "Host=localhost;Port=5433;Database=shop_modular;Username=shop;Password=shop";

    /// <summary>
    /// One database for the whole monolith, one connection per request. Every module's DbContext is built
    /// on this connection, which is what lets <see cref="SharedTransaction"/> span modules. Guide: §7.3.
    /// </summary>
    public static IServiceCollection AddSharedDatabase(this IServiceCollection services)
    {
        // The connection string is read when a request needs it, so the contract tests can replace it. The
        // request scope disposes the connection at the end; EF Core does not, because it did not create it.
        services.AddScoped<DbConnection>(provider => new NpgsqlConnection(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("Shop")
            ?? throw new InvalidOperationException("Connection string 'Shop' is missing.")));
        services.AddScoped<SharedTransaction>();
        return services;
    }

    /// <summary>
    /// Registers a module's DbContext on the shared connection, with its migrations history table in its own
    /// schema (so each module migrates on its own), and enlists it in <see cref="SharedTransaction"/>.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<DbConnection>(),
            npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema)));
        services.AddScoped(provider => new ModuleDbContext(provider.GetRequiredService<TContext>()));
        return services;
    }

    /// <summary>The same options for a design-time factory (<c>dotnet ef</c> runs without the Host).</summary>
    public static DbContextOptions<TContext> DesignTimeOptions<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(DesignTimeConnectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema))
            .Options;
}
