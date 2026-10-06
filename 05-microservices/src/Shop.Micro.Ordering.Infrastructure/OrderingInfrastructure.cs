using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Messaging;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Application.Sagas;
using Shop.Micro.Ordering.Application.UseCases;
using Shop.Micro.Ordering.Infrastructure.Catalog;
using Shop.Micro.Ordering.Infrastructure.Messaging;
using Shop.Micro.Ordering.Infrastructure.Persistence;

namespace Shop.Micro.Ordering.Infrastructure;

/// <summary>
/// Plugs the Ordering adapters into its ports: database, outbox, broker consumers and the Catalog HTTP client.
/// Called once by the Api's Program.cs, the service's composition root. Guide: §8.2.
/// </summary>
public static class OrderingInfrastructure
{
    public static IHostApplicationBuilder AddOrderingInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Retries off: the messaging building block opens its own transactions (see the Catalog service).
        builder.AddNpgsqlDbContext<OrderingDbContext>("orderingdb", settings => settings.DisableRetry = true);
        builder.AddRabbitMQClient("messaging");
        builder.Services.AddMessaging<OrderingDbContext>("ordering")
            .Consume<StockReserved, StockReservedConsumer>()
            .Consume<StockReservationFailed, StockReservationFailedConsumer>()
            .Consume<PaymentSucceeded, PaymentSucceededConsumer>()
            .Consume<PaymentDeclined, PaymentDeclinedConsumer>();

        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IOutgoingMessages, OutboxOutgoingMessages>();
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        builder.Services.AddHttpClient<ICatalogClient, CatalogHttpClient>(client =>
        {
            client.BaseAddress = new Uri("http://catalog");
            client.Timeout = CatalogHttpClient.Timeout;
        });

        builder.Services.AddScoped<PlaceOrder>();
        builder.Services.AddScoped<GetOrder>();
        builder.Services.AddScoped<PayOrder>();
        builder.Services.AddScoped<CancelOrder>();
        builder.Services.AddScoped<OrderSaga>();
        return builder;
    }

    /// <summary>Development only: in production, migrations are a deployment step.</summary>
    public static async Task MigrateOrderingDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync();
    }
}
