namespace Shop.ContractTests;

// The JSON shapes of the public API, as a client sees them.
// Statuses are strings on purpose: the suite checks the wire format, not a C# enum
// that one version might name differently.

public sealed record ProductResponse(Guid Id, string Name, string Sku, decimal Price, int Stock);

public sealed record OrderLineResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    string? CancellationReason,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset PlacedAt);

public sealed record PaymentResponse(Guid Id, Guid OrderId, decimal Amount, string Status, DateTimeOffset ProcessedAt);

/// <summary>Order statuses as they appear on the wire.</summary>
public static class OrderStatus
{
    public const string Pending = "Pending";
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Rejected = "Rejected";
    public const string PaymentPending = "PaymentPending";
    public const string Paid = "Paid";
    public const string Cancelled = "Cancelled";
}
