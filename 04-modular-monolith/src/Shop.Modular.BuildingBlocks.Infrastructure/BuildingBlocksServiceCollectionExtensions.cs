using Microsoft.Extensions.DependencyInjection;
using Shop.Modular.BuildingBlocks.Infrastructure.Errors;
using Shop.Modular.BuildingBlocks.Infrastructure.Events;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;

namespace Shop.Modular.BuildingBlocks.Infrastructure;

public static class BuildingBlocksServiceCollectionExtensions
{
    /// <summary>What every module relies on: the shared database and transaction, the event bus, error handling.</summary>
    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.AddSharedDatabase();
        // Scoped: the bus resolves consumers from the request's services, so they share its DbContexts and transaction.
        services.AddScoped<IEventBus, InProcessEventBus>();
        services.AddProblemDetails();
        services.AddExceptionHandler<SharedErrorsExceptionHandler>();
        return services;
    }
}
