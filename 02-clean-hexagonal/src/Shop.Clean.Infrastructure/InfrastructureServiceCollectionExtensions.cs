using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Clean.Application.Ports;
using Shop.Clean.Infrastructure.Payments;
using Shop.Clean.Infrastructure.Persistence;

namespace Shop.Clean.Infrastructure;

/// <summary>
/// Plugs the adapters into the ports: the one place that decides "IProductRepository means EF Core". The
/// Api calls it at startup (the composition root) and never names an adapter itself. Guide: §5.2.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Read the connection string when a context is created, so the contract tests can replace it.
        services.AddDbContext<ShopDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("Shop")
                ?? throw new InvalidOperationException("Connection string 'Shop' is missing.")));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
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
