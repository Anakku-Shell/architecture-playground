using Shop.Layered.Data.Entities;

namespace Shop.Layered.Business.Payments;

/// <summary>
/// Stands in for a real payment provider. A concrete class with no interface: <see cref="PaymentService"/>
/// depends on it directly, which is normal in a layered design and is why replacing it (with a real
/// provider, or with a stub in a test) means editing the service. Version 02 puts a port in between.
/// </summary>
public sealed class FakePaymentGateway
{
    /// <summary>Amounts up to this value are approved, anything above is declined.</summary>
    public const decimal ApprovalLimit = 1000.00m;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Stands in for a real gateway, which has instance state (an HTTP client, credentials).")]
    public PaymentStatus Charge(decimal amount) => amount <= ApprovalLimit ? PaymentStatus.Approved : PaymentStatus.Declined;
}
