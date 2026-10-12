using Shop.Layered.Business.Catalog;
using Xunit;

namespace Shop.Layered.UnitTests;

// Only the small pure pieces of the Business layer can be unit-tested here. OrderService and
// ProductService take ShopDbContext in their constructor and run SQL (conditional UPDATEs,
// transactions), so testing them needs a real database: that is what the contract tests do.
// Version 02 moves the rules into a domain model that tests can reach without a database. Guide: §4.5.
public sealed class PriceRulesTests
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("10")]
    [InlineData("10.5")]
    [InlineData("10.99")]
    public void TwoDecimalsOrFewer_IsValid(string price) =>
        Assert.True(PriceRules.HasAtMostTwoDecimals(Parse(price)));

    [Theory]
    [InlineData("10.999")]
    [InlineData("0.001")]
    public void MoreThanTwoDecimals_IsInvalid(string price) =>
        Assert.False(PriceRules.HasAtMostTwoDecimals(Parse(price)));

    [Fact]
    public void TrailingZeros_DoNotCount()
    {
        // 10.500 is the same amount as 10.50: decimal keeps the scale, the rule must not care.
        Assert.True(PriceRules.HasAtMostTwoDecimals(10.500m));
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
