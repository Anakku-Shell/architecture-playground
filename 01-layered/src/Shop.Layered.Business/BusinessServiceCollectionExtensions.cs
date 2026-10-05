using Microsoft.Extensions.DependencyInjection;
using Shop.Layered.Business.Catalog;
using Shop.Layered.Business.Ordering;
using Shop.Layered.Business.Payments;
using Shop.Layered.Data;

namespace Shop.Layered.Business;

/// <summary>
/// Registers the Business layer and, through it, the Data layer. The Api calls only these two methods,
/// so it never names a Data type to start the application. Guide: §4.2.
/// </summary>
public static class BusinessServiceCollectionExtensions
{
    public static IServiceCollection AddShopBusiness(this IServiceCollection services)
    {
        services.AddShopData();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<FakePaymentGateway>();
        // Scoped: one instance per HTTP request, sharing that request's DbContext.
        services.AddScoped<ProductService>();
        services.AddScoped<OrderService>();
        services.AddScoped<PaymentService>();
        return services;
    }

    /// <inheritdoc cref="DataServiceCollectionExtensions.MigrateShopDatabaseAsync"/>
    public static Task MigrateDatabaseAsync(this IServiceProvider services) => services.MigrateShopDatabaseAsync();
}
