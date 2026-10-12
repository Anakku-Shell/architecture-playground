using Shop.Modular.Payments.Data;

namespace Shop.Modular.Payments;

/// <summary>
/// Stands in for a real payment provider. A concrete class, as in version 03: the module has one slice that
/// pays, so an interface would be a port nobody else plugs into.
/// </summary>
internal sealed class FakePaymentGateway
{
    /// <summary>Amounts up to this value are approved, anything above is declined.</summary>
    public const decimal ApprovalLimit = 1000.00m;

    /// <param name="orderId">The idempotency key a real provider would receive: a repeated charge for one order is ignored.</param>
    /// <param name="amount">The amount to charge.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Stands in for a real gateway, which has instance state (an HTTP client, credentials).")]
    public Task<PaymentStatus> ChargeAsync(Guid orderId, decimal amount, CancellationToken cancellationToken) =>
        Task.FromResult(amount <= ApprovalLimit ? PaymentStatus.Approved : PaymentStatus.Declined);
}
