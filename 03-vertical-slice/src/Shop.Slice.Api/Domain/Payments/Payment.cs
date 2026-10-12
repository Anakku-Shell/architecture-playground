using Shop.Slice.Api.Domain.Common;

namespace Shop.Slice.Api.Domain.Payments;

/// <summary>What the payment gateway answered.</summary>
public enum PaymentStatus
{
    Approved,
    Declined,
}

/// <summary>The record of one charge attempt for an order. Created once, never changed.</summary>
public sealed class Payment
{
    private Payment()
    {
        Amount = null!;
    }

    private Payment(Guid id, Guid orderId, Money amount, PaymentStatus status, DateTimeOffset processedAt)
    {
        Id = id;
        OrderId = orderId;
        Amount = amount;
        Status = status;
        ProcessedAt = processedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }

    public static Payment Record(Guid id, Guid orderId, Money amount, PaymentStatus status, DateTimeOffset processedAt) =>
        new(id, orderId, amount ?? throw new ArgumentNullException(nameof(amount)), status, processedAt);
}
