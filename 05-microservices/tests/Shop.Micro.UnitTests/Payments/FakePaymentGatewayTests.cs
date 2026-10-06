using Shop.Micro.Payments.Api;
using Xunit;

namespace Shop.Micro.UnitTests.Payments;

public sealed class FakePaymentGatewayTests
{
    [Theory]
    [InlineData("0.01", "Approved")]
    [InlineData("1000.00", "Approved")]
    [InlineData("1000.01", "Declined")]
    public async Task ApprovesUpTo1000(string amount, string expected) =>
        Assert.Equal(expected, (await new FakePaymentGateway().ChargeAsync(
            Guid.NewGuid(), decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), TestContext.Current.CancellationToken)).ToString());
}
