using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Modular.BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// The one public entry point of a module. The Host knows a module only through this interface: it lets
/// the module register its services, map its endpoints and migrate its schema, and never sees what is
/// inside. Guide: §7.2.
/// </summary>
public interface IModule
{
    /// <summary>Short name, also the module's database schema (<c>catalog</c>, <c>ordering</c>, <c>payments</c>).</summary>
    string Name { get; }

    void RegisterServices(IServiceCollection services);

    void MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>
    /// Applies the module's pending migrations to its own schema. Called at startup in Development only: in
    /// production a schema change is a deliberate deployment step, not a side effect of starting an instance.
    /// </summary>
    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);
}
