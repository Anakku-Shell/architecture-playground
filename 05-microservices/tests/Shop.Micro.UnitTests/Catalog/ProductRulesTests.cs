using Shop.Micro.Catalog.Api.Errors;
using Shop.Micro.Catalog.Api.Products;
using Xunit;

namespace Shop.Micro.UnitTests.Catalog;

// Catalog is CRUD: no domain model, but its few rules still deserve tests. They are pure functions, so
// these tests need nothing else (the service exposes its internals to this project only). Guide: §8.2.
public sealed class ProductRulesTests
{
    [Fact]
    public void NewProduct_TrimsTheName_AndUpperCasesTheSku()
    {
        var product = ProductRules.NewProduct(" Mug ", " abc-1 ", 10.50m, 3);

        Assert.Equal("Mug", product.Name);
        Assert.Equal("ABC-1", product.Sku);
        Assert.Equal(10.50m, product.Price);
        Assert.Equal(3, product.Stock);
        Assert.NotEqual(Guid.Empty, product.Id);
    }

    [Fact]
    public void NewProduct_ReportsEveryInvalidField()
    {
        var error = Assert.Throws<ValidationException>(() => ProductRules.NewProduct(" ", new string('s', 51), 0m, -1));

        Assert.Equal(["initialStock", "name", "price", "sku"], error.Errors.Keys.Order());
    }

    [Fact]
    public void NewProduct_AcceptsTheLimits() =>
        Assert.Equal(ProductRules.MaxStock, ProductRules.NewProduct(new string('n', 200), new string('s', 50), ProductRules.MaxPrice, ProductRules.MaxStock).Stock);

    [Fact]
    public void NewProduct_RejectsStockAboveTheMaximum() =>
        Assert.Throws<ValidationException>(() => ProductRules.NewProduct("Mug", "S-1", 1m, ProductRules.MaxStock + 1));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.999")]
    [InlineData("1000000.01")]
    public void CheckPrice_RejectsInvalidPrices(string price) =>
        Assert.Equal(["price"], Assert.Throws<ValidationException>(() => ProductRules.CheckPrice(Parse(price))).Errors.Keys);

    [Theory]
    [InlineData("0.01")]
    [InlineData("10.50")]
    [InlineData("1000000")]
    public void CheckPrice_AcceptsValidPrices(string price) => ProductRules.CheckPrice(Parse(price));

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    [InlineData(-1_000_001)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void CheckAdjustment_RejectsZeroAndMoreThanTheMaximum(int quantity) =>
        Assert.Equal(["quantity"], Assert.Throws<ValidationException>(() => ProductRules.CheckAdjustment(quantity)).Errors.Keys);

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(1_000_000)]
    [InlineData(-1_000_000)]
    public void CheckAdjustment_AcceptsUpToTheMaximumEitherWay(int quantity) => ProductRules.CheckAdjustment(quantity);

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
