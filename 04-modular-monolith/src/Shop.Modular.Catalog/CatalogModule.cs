using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Modules;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Catalog.Data;
using Shop.Modular.Catalog.Integration;
using Shop.Modular.Catalog.Products;
using Shop.Modular.Ordering.Contracts;

namespace Shop.Modular.Catalog;

/// <summary>
/// The Catalog module's entry point, and its only public type. Everything it registers is internal: other
/// modules reach Catalog through <see cref="ICatalogQueries"/> and events, never through these classes.
/// Guide: §7.2.
/// </summary>
public sealed class CatalogModule : IModule
{
    public string Name => CatalogDbContext.Schema;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddModuleDbContext<CatalogDbContext>(CatalogDbContext.Schema);
        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddScoped<IIntegrationEventConsumer<OrderPlaced>, OrderPlacedConsumer>();
        services.AddScoped<IIntegrationEventConsumer<OrderCancelled>, OrderCancelledConsumer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => ProductEndpoints.Map(app);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
