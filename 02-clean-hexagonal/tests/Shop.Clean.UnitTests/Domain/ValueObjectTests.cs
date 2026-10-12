using Shop.Clean.Domain.Common;
using Xunit;

namespace Shop.Clean.UnitTests.Domain;

// Value objects check their own rules when they are created: an invalid Money, Sku, ProductName or
// Quantity cannot exist. Plain tests, no database, no mocks. Guide: §5.5.
public sealed class MoneyTests
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("10")]
    [InlineData("10.50")]
    [InlineData("10.500")]
    public void Of_AcceptsPositiveAmountsWithAtMostTwoDecimals(string amount) =>
        Assert.Equal(Parse(amount), Money.Of(Parse(amount)).Amount);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.999")]
    public void Of_RejectsZeroNegativeAndThreeDecimals(string amount) =>
        Assert.Throws<DomainValidationException>(() => Money.Of(Parse(amount)));

    [Fact]
    public void Add_And_MultiplyByQuantity()
    {
        Assert.Equal(Money.Of(15.75m), Money.Of(10.50m) + Money.Of(5.25m));
        Assert.Equal(Money.Of(31.50m), Money.Of(10.50m) * Quantity.Of(3));
    }

    [Fact]
    public void Equality_IsByValue() => Assert.Equal(Money.Of(10.5m), Money.Of(10.50m));

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class RehydrationTests
{
    // Values read back from storage were valid when they were written. If a rule tightens later (say, at
    // most 500 units per line), old rows must still load, so adapters rehydrate without re-checking.
    [Fact]
    public void Rehydrate_DoesNotApplyTodaysRules()
    {
        Assert.Equal(1500, Quantity.Rehydrate(1500).Value);
        Assert.Equal(10.999m, Money.Rehydrate(10.999m).Amount);
        Assert.Equal("old-lowercase", Sku.Rehydrate("old-lowercase").Value);
        Assert.Equal("", ProductName.Rehydrate("").Value);
    }
}

public sealed class SkuTests
{
    [Fact]
    public void Of_TrimsAndUpperCases() => Assert.Equal("ABC-1", Sku.Of("  abc-1 ").Value);

    [Fact]
    public void SameCodeInDifferentCase_IsTheSameSku() => Assert.Equal(Sku.Of("abc-1"), Sku.Of("ABC-1"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_RejectsEmpty(string? value) => Assert.Throws<DomainValidationException>(() => Sku.Of(value));

    [Fact]
    public void Of_Rejects51Characters() => Assert.Throws<DomainValidationException>(() => Sku.Of(new string('S', 51)));

    [Fact]
    public void Of_Accepts50Characters() => Assert.Equal(50, Sku.Of(new string('S', 50)).Value.Length);
}

public sealed class ProductNameTests
{
    [Fact]
    public void Of_Trims() => Assert.Equal("Mug", ProductName.Of(" Mug ").Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Of_RejectsEmpty(string? value) => Assert.Throws<DomainValidationException>(() => ProductName.Of(value));

    [Fact]
    public void Of_Rejects201Characters() => Assert.Throws<DomainValidationException>(() => ProductName.Of(new string('n', 201)));
}

public sealed class QuantityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void Of_AcceptsOneToOneThousand(int value) => Assert.Equal(value, Quantity.Of(value).Value);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Of_RejectsOutOfRange(int value) => Assert.Throws<DomainValidationException>(() => Quantity.Of(value));
}
