using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shop.Modular.Ordering.Application.UseCases;
using Shop.Modular.Ordering.Domain;

namespace Shop.Modular.Ordering.Infrastructure.Http;

internal sealed record PlaceOrderRequest(Guid CustomerId, IReadOnlyList<PlaceOrderLineRequest?>? Lines);

internal sealed record PlaceOrderLineRequest(Guid ProductId, int Quantity);

internal sealed record OrderLineResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

internal sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    CancellationReason? CancellationReason,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset PlacedAt)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status,
        order.CancellationReason,
        order.Total.Amount,
        [.. order.Lines.Select(l => new OrderLineResponse(l.ProductId, l.ProductName.Value, l.UnitPrice.Amount, l.Quantity.Value, l.LineTotal.Amount))],
        order.PlacedAt);
}

/// <summary>
/// The Ordering module's driving adapter for HTTP, as version 02's Api project: translate the request into a
/// command, call the use case, translate the aggregate into the response. Guide: §7.2.
/// </summary>
internal static class OrderEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders");

        orders.MapPost("/", async (PlaceOrderRequest request, PlaceOrder useCase, CancellationToken ct) =>
        {
            // A null element ("lines": [null]) becomes an empty line, which validation reports.
            var lines = request.Lines?.Select(l => new PlaceOrderLine(l?.ProductId ?? Guid.Empty, l?.Quantity ?? 0)).ToList();
            var order = await useCase.ExecuteAsync(new PlaceOrderCommand(request.CustomerId, lines), ct);
            return TypedResults.Created($"/api/orders/{order.Id}", OrderResponse.From(order));
        });

        orders.MapGet("/{id:guid}", async (Guid id, GetOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));

        orders.MapPost("/{id:guid}/pay", async (Guid id, PayOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));

        orders.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));
    }
}
