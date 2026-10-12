using Microsoft.Extensions.DependencyInjection;
using Shop.Clean.Application.UseCases.Catalog;
using Shop.Clean.Application.UseCases.Ordering;
using Shop.Clean.Application.UseCases.Payments;

namespace Shop.Clean.Application;

/// <summary>
/// Registers the use cases. What they need from outside is registered by the composition root: the ports by
/// Infrastructure (<c>AddInfrastructure</c>), the clock (<see cref="TimeProvider"/>) by <c>Program.cs</c>.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateProduct>();
        services.AddScoped<ListProducts>();
        services.AddScoped<GetProduct>();
        services.AddScoped<ChangeProductPrice>();
        services.AddScoped<AdjustStock>();
        services.AddScoped<PlaceOrder>();
        services.AddScoped<GetOrder>();
        services.AddScoped<PayOrder>();
        services.AddScoped<CancelOrder>();
        services.AddScoped<GetPayment>();
        return services;
    }
}
