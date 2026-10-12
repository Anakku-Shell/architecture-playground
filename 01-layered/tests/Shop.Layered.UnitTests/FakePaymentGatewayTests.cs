using Shop.Layered.Business.Payments;
using Shop.Layered.Data.Entities;
using Xunit;

namespace Shop.Layered.UnitTests;

public sealed class FakePaymentGatewayTests
{
    private readonly FakePaymentGateway _gateway = new();

    [Theory]
    [InlineData("0.01")]
    [InlineData("999.99")]
    [InlineData("1000.00")]
    public void Charge_UpTo1000_IsApproved(string amount) =>
        Assert.Equal(PaymentStatus.Approved, _gateway.Charge(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1000.01")]
    [InlineData("5000.00")]
    public void Charge_Above1000_IsDeclined(string amount) =>
        Assert.Equal(PaymentStatus.Declined, _gateway.Charge(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
}
