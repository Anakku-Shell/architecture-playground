using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Infrastructure.Payments;

/// <summary>
/// The driven adapter for <see cref="IPaymentGateway"/>: stands in for a real payment provider. Replacing it
/// with a real one is a new class here and one line in <c>AddInfrastructure</c>; no use case changes.
/// Compare with 01, where <c>PaymentService</c> depended on the concrete class.
/// </summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    /// <summary>Amounts up to this value are approved, anything above is declined.</summary>
    public const decimal ApprovalLimit = 1000.00m;

    public Task<PaymentStatus> ChargeAsync(Guid orderId, Money amount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return Task.FromResult(amount.Amount <= ApprovalLimit ? PaymentStatus.Approved : PaymentStatus.Declined);
    }
}
