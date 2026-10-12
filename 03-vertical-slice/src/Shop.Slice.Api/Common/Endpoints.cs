using System.Reflection;

namespace Shop.Slice.Api.Common;

/// <summary>
/// Implemented by every slice: it maps its own route. Adding a use case means adding one file under
/// <c>Features/</c>; no central list of endpoints has to be edited. Guide: §6.2.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder app);
}

/// <summary>
/// Finds every <see cref="IEndpoint"/> in the assembly at startup and maps it. Twenty lines of reflection
/// instead of a library: this is the part of a "mediator" a vertical-slice app really needs (ADR 0002).
/// </summary>
public static class EndpointDiscovery
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var endpoints = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpoint).IsAssignableFrom(t));
        foreach (var endpoint in endpoints)
        {
            services.AddSingleton(typeof(IEndpoint), endpoint);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        foreach (var endpoint in app.ServiceProvider.GetServices<IEndpoint>())
        {
            endpoint.Map(app);
        }

        return app;
    }
}
