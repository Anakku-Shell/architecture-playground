using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Layered.Data;

/// <summary>Registers the Data layer. Each layer registers its own services; the layer above calls this.</summary>
public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddShopData(this IServiceCollection services)
    {
        // The connection string is read when a context is created, not here at registration, so the
        // contract tests can point the app at their own throwaway database through configuration.
        services.AddDbContext<ShopDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("Shop")
                ?? throw new InvalidOperationException("Connection string 'Shop' is missing.")));
        return services;
    }

    /// <summary>
    /// Applies pending migrations. Called at startup in Development only: in production a schema change
    /// is a deliberate deployment step (a migration script or bundle), not a side effect of starting an
    /// instance, and several instances starting at once must not race to migrate.
    /// </summary>
    public static async Task MigrateShopDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.MigrateAsync();
    }
}
