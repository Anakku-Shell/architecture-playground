using Shop.Slice.Api.Domain.Ordering;

namespace Shop.Slice.Api.Features.Ordering;

// The JSON shape of an order, shared by the ordering slices (context level, like ProductResponse).

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    CancellationReason? CancellationReason,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset PlacedAt)
{
    /// <summary>For command slices, which hold the aggregate they just changed.</summary>
    public static OrderResponse From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(
            order.Id,
            order.CustomerId,
            order.Status,
            order.CancellationReason,
            order.Total.Amount,
            [.. order.Lines.Select(l => new OrderLineResponse(l.ProductId, l.ProductName.Value, l.UnitPrice.Amount, l.Quantity.Value, l.LineTotal.Amount))],
            order.PlacedAt);
    }
}

public sealed record OrderLineResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
