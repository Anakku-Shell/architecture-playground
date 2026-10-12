using Shop.Layered.Api.Models;
using Shop.Layered.Business.Ordering;

namespace Shop.Layered.Api.Endpoints;

/// <summary>HTTP endpoints for orders. The journey of <c>POST /api/orders</c> starts here: Guide §4.5.</summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders");

        orders.MapPost("/", async (PlaceOrderRequest request, OrderService service, CancellationToken ct) =>
        {
            // A null element ("lines": [null]) becomes an empty line, which the Business layer rejects with 400.
            var lines = request.Lines?.Select(l => new OrderLineInput(l?.ProductId ?? Guid.Empty, l?.Quantity ?? 0)).ToList();
            var order = await service.PlaceAsync(request.CustomerId, lines, ct);
            return TypedResults.Created($"/api/orders/{order.Id}", OrderResponse.From(order));
        });

        orders.MapGet("/{id:guid}", async (Guid id, OrderService service, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await service.GetAsync(id, ct))));

        orders.MapPost("/{id:guid}/pay", async (Guid id, OrderService service, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await service.PayAsync(id, ct))));

        orders.MapPost("/{id:guid}/cancel", async (Guid id, OrderService service, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await service.CancelAsync(id, ct))));

        return app;
    }
}
