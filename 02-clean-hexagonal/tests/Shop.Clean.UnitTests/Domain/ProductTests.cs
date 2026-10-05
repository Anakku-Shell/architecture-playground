using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Xunit;

namespace Shop.Clean.UnitTests.Domain;

public sealed class ProductTests
{
    [Fact]
    public void Create_SetsEveryValue()
    {
        var id = Guid.NewGuid();

        var product = Product.Create(id, ProductName.Of("Mug"), Sku.Of("mug-1"), Money.Of(10m), 5);

        Assert.Equal(id, product.Id);
        Assert.Equal("Mug", product.Name.Value);
        Assert.Equal("MUG-1", product.Sku.Value);
        Assert.Equal(Money.Of(10m), product.Price);
        Assert.Equal(5, product.Stock);
    }

    [Fact]
    public void Create_WithNegativeStock_Throws() =>
        Assert.Throws<DomainValidationException>(() => Product.Create(Guid.NewGuid(), ProductName.Of("Mug"), Sku.Of("m"), Money.Of(1m), -1));

    [Fact]
    public void AdjustStock_AddsAndRemoves()
    {
        var product = Products.Mug(stock: 10);

        product.AdjustStock(5);
        product.AdjustStock(-15);

        Assert.Equal(0, product.Stock);
    }

    [Fact]
    public void AdjustStock_BelowZero_IsABusinessRuleViolation_AndChangesNothing()
    {
        var product = Products.Mug(stock: 3);

        Assert.Throws<BusinessRuleViolationException>(() => product.AdjustStock(-4));
        Assert.Equal(3, product.Stock);
    }

    [Fact]
    public void AdjustStock_ByZero_IsInvalid() =>
        Assert.Throws<DomainValidationException>(() => Products.Mug().AdjustStock(0));

    [Fact]
    public void Limits_PriceAndInitialStock()
    {
        Assert.Equal(Money.Of(1_000_000m), Product.ValidPrice(1_000_000m));
        Assert.Throws<DomainValidationException>(() => Product.ValidPrice(1_000_000.01m));
        Assert.Throws<DomainValidationException>(() => Product.ValidPrice(0m));
        Assert.Throws<DomainValidationException>(() => Product.Create(Guid.NewGuid(), ProductName.Of("Mug"), Sku.Of("m"), Money.Of(1m), Product.MaxStock + 1));
        Assert.Throws<DomainValidationException>(() => Products.Mug().ChangePrice(Money.Of(1_000_000.01m)));
    }

    [Theory]
    [InlineData(Product.MaxAdjustment + 1)]
    [InlineData(-Product.MaxAdjustment - 1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void AdjustStock_ByMoreThanTheMaximumAdjustment_IsInvalid(int quantity) =>
        Assert.Throws<DomainValidationException>(() => Products.Mug(stock: 5).AdjustStock(quantity));

    [Fact]
    public void AdjustStock_AboveTheMaximumStock_IsABusinessRuleViolation_AndChangesNothing()
    {
        var product = Products.Mug(stock: Product.MaxStock - 1);

        Assert.Throws<BusinessRuleViolationException>(() => product.AdjustStock(2));
        Assert.Equal(Product.MaxStock - 1, product.Stock);
    }

    [Fact]
    public void Reserve_TakesUnits_Release_GivesThemBack()
    {
        var product = Products.Mug(stock: 5);

        product.Reserve(Quantity.Of(2));
        Assert.Equal(3, product.Stock);

        product.Release(Quantity.Of(2));
        Assert.Equal(5, product.Stock);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_Throws_AndChangesNothing()
    {
        var product = Products.Mug(stock: 1);

        Assert.False(product.CanReserve(Quantity.Of(2)));
        Assert.Throws<BusinessRuleViolationException>(() => product.Reserve(Quantity.Of(2)));
        Assert.Equal(1, product.Stock);
    }

    [Fact]
    public void ChangePrice_ChangesPrice()
    {
        var product = Products.Mug();

        product.ChangePrice(Money.Of(12.50m));

        Assert.Equal(Money.Of(12.50m), product.Price);
    }
}

/// <summary>Test data builders shared by the unit tests.</summary>
internal static class Products
{
    public static Product Mug(decimal price = 10m, int stock = 10) =>
        Product.Create(Guid.NewGuid(), ProductName.Of("Mug"), Sku.Of($"MUG-{Guid.NewGuid():N}"[..20]), Money.Of(price), stock);
}
