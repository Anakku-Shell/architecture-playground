namespace Shop.Layered.Data.Entities;

/// <summary>The result of charging an order. One payment per order (unique index on <see cref="OrderId"/>).</summary>
public sealed class Payment
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
