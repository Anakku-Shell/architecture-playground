using Shop.Layered.Data.Entities;

namespace Shop.Layered.Api.Models;

public sealed record PaymentResponse(Guid Id, Guid OrderId, decimal Amount, PaymentStatus Status, DateTimeOffset ProcessedAt)
{
    public static PaymentResponse From(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new(payment.Id, payment.OrderId, payment.Amount, payment.Status, payment.ProcessedAt);
    }
}
