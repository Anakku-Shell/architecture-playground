using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Api.Models;

// The JSON shapes of the API. Requests are turned into the use cases' commands in the endpoints; responses
// are built from the aggregates. The Api may know the Domain (dependencies point inwards), but it never
// sees EF Core: the database is on the other side of the hexagon. Guide: §5.2, data shapes.

public sealed record CreateProductRequest(string? Name, string? Sku, decimal Price, int InitialStock);

public sealed record ChangePriceRequest(decimal Price);

public sealed record AdjustStockRequest(int Quantity);

public sealed record PlaceOrderRequest(Guid CustomerId, IReadOnlyList<PlaceOrderLineRequest?>? Lines);

public sealed record PlaceOrderLineRequest(Guid ProductId, int Quantity);

public sealed record ProductResponse(Guid Id, string Name, string Sku, decimal Price, int Stock)
{
    public static ProductResponse From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new(product.Id, product.Name.Value, product.Sku.Value, product.Price.Amount, product.Stock);
    }
}

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
            order.Total.Amount,
            [.. order.Lines.Select(l => new OrderLineResponse(l.ProductId, l.ProductName.Value, l.UnitPrice.Amount, l.Quantity.Value, l.LineTotal.Amount))],
            order.PlacedAt);
    }
}

public sealed record OrderLineResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record PaymentResponse(Guid Id, Guid OrderId, decimal Amount, PaymentStatus Status, DateTimeOffset ProcessedAt)
{
    public static PaymentResponse From(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new(payment.Id, payment.OrderId, payment.Amount.Amount, payment.Status, payment.ProcessedAt);
    }
}
