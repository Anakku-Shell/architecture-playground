using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Modules;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Ordering.Application.IntegrationEvents;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Application.UseCases;
using Shop.Modular.Ordering.Infrastructure.Http;
using Shop.Modular.Ordering.Infrastructure.Persistence;
using Shop.Modular.Payments.Contracts;

namespace Shop.Modular.Ordering.Infrastructure;

/// <summary>
/// The Ordering module's entry point, and the only public type of its Infrastructure project. It is also
/// the module's composition root: it plugs its adapters into its ports, as version 02's Program.cs did for
/// the whole application. Guide: §7.2.
/// </summary>
public sealed class OrderingModule : IModule
{
    public string Name => OrderingDbContext.Schema;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddModuleDbContext<OrderingDbContext>(OrderingDbContext.Schema);
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddScoped<PlaceOrder>();
        services.AddScoped<GetOrder>();
        services.AddScoped<PayOrder>();
        services.AddScoped<CancelOrder>();

        services.AddScoped<IIntegrationEventConsumer<StockReserved>, StockReservedConsumer>();
        services.AddScoped<IIntegrationEventConsumer<StockReservationFailed>, StockReservationFailedConsumer>();
        services.AddScoped<IIntegrationEventConsumer<PaymentSucceeded>, PaymentSucceededConsumer>();
        services.AddScoped<IIntegrationEventConsumer<PaymentDeclined>, PaymentDeclinedConsumer>();

        services.AddExceptionHandler<OrderingExceptionHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => OrderEndpoints.Map(app);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
