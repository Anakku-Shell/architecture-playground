using Shop.Layered.Data.Entities;

namespace Shop.Layered.Api.Models;

public sealed record PlaceOrderRequest(Guid CustomerId, IReadOnlyList<PlaceOrderLineRequest>? Lines);

public sealed record PlaceOrderLineRequest(Guid ProductId, int Quantity);

/// <summary>
/// The order as the API shows it. <see cref="Status"/> and <see cref="CancellationReason"/> are the Data
/// layer's enums: rename a member there and the database column values and this JSON change together.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    CancellationReason? CancellationReason,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset PlacedAt)
{
    public static OrderResponse From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(
            order.Id,
            order.CustomerId,
            order.Status,
            order.CancellationReason,
            order.Total,
            [.. order.Lines.Select(l => new OrderLineResponse(l.ProductId, l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal))],
            order.PlacedAt);
    }
}

public sealed record OrderLineResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
