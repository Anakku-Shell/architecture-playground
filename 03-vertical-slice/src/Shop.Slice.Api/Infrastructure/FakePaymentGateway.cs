using Shop.Slice.Api.Domain.Common;
using Shop.Slice.Api.Domain.Payments;

namespace Shop.Slice.Api.Infrastructure;

/// <summary>
/// Stands in for a real payment provider. A concrete class, used by the one slice that pays: with a single
/// consumer, an interface would be a port nobody else plugs into. Should a second slice or a test need to
/// replace it, extracting the interface is a five-minute refactoring. Guide: §6.8.
/// </summary>
internal sealed class FakePaymentGateway
{
    /// <summary>Amounts up to this value are approved, anything above is declined.</summary>
    public const decimal ApprovalLimit = 1000.00m;

    /// <param name="orderId">The idempotency key a real provider would receive.</param>
    /// <param name="amount">The amount to charge.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Stands in for a real gateway, which has instance state (an HTTP client, credentials).")]
    public Task<PaymentStatus> ChargeAsync(Guid orderId, Money amount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return Task.FromResult(amount.Amount <= ApprovalLimit ? PaymentStatus.Approved : PaymentStatus.Declined);
    }
}
