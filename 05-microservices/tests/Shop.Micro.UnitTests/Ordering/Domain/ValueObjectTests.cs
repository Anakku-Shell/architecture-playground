using Shop.Micro.Ordering.Domain.Common;
using Xunit;

namespace Shop.Micro.UnitTests.Ordering.Domain;

// Value objects check their own rules when they are created: an invalid Money, ProductName or
// Quantity cannot exist. Plain tests, no database, no mocks. Guide: §5.5 (version 02, where they come from).
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
        Assert.Equal("", ProductName.Rehydrate("").Value);
    }
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
