using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Modules;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Ordering.Contracts;
using Shop.Modular.Payments.Data;
using Shop.Modular.Payments.Features;

namespace Shop.Modular.Payments;

/// <summary>The Payments module's entry point, and its only public type. Guide: §7.2.</summary>
public sealed class PaymentsModule : IModule
{
    public string Name => PaymentsDbContext.Schema;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddModuleDbContext<PaymentsDbContext>(PaymentsDbContext.Schema);
        services.AddSingleton<FakePaymentGateway>();
        services.AddScoped<IIntegrationEventConsumer<PaymentRequested>, ProcessPayment>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => GetPayment.Map(app);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
