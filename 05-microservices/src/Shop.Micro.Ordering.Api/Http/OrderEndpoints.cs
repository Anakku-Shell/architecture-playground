using Shop.Micro.Ordering.Application.UseCases;
using Shop.Micro.Ordering.Domain;

namespace Shop.Micro.Ordering.Api.Http;

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
/// The Ordering service's driving adapter for HTTP, back in its own Api project as in 02. Placing and paying
/// answer <c>202 Accepted</c>: the request was taken, the outcome comes later (poll the order). The <c>Location</c>
/// header says where. This is the one intended difference in the public API. Guide: §8.6.
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
            return TypedResults.Accepted($"/api/orders/{order.Id}", OrderResponse.From(order));
        });

        orders.MapGet("/{id:guid}", async (Guid id, GetOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));

        orders.MapPost("/{id:guid}/pay", async (Guid id, PayOrder useCase, CancellationToken ct) =>
        {
            var order = await useCase.ExecuteAsync(id, ct);
            return TypedResults.Accepted($"/api/orders/{order.Id}", OrderResponse.From(order));
        });

        orders.MapPost("/{id:guid}/cancel", async (Guid id, CancelOrder useCase, CancellationToken ct) =>
            TypedResults.Ok(OrderResponse.From(await useCase.ExecuteAsync(id, ct))));
    }
}
