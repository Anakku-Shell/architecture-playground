using Microsoft.EntityFrameworkCore;
using Shop.Slice.Api.Infrastructure.Persistence;

namespace Shop.Slice.Api.Infrastructure;

internal static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Read the connection string when a context is created, so the contract tests can replace it.
        services.AddDbContext<ShopDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("Shop")
                ?? throw new InvalidOperationException("Connection string 'Shop' is missing.")));
        services.AddSingleton<FakePaymentGateway>();
        return services;
    }

    /// <summary>
    /// Applies pending migrations. Called at startup in Development only: in production a schema change is a
    /// deliberate deployment step, not a side effect of starting an instance.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.MigrateAsync();
    }
}
