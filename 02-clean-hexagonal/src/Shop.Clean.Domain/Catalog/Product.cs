using Shop.Clean.Domain.Common;

namespace Shop.Clean.Domain.Catalog;

/// <summary>
/// A product and its stock: an <b>aggregate root</b> (Guide §3.7). Compare with 01's <c>Product</c>
/// entity: the setters are private, so the only way to change stock is through the methods below, and
/// each method protects the rule "stock never goes below zero". Guide: §5.2.
/// </summary>
public sealed class Product
{
    // For EF Core, which creates the object first and then fills the properties from the row. A small,
    // deliberate concession of the domain to persistence; the alternative is a separate persistence model
    // plus mapping (Guide §5.8).
    private Product()
    {
        Name = null!;
        Sku = null!;
        Price = null!;
    }

    private Product(Guid id, ProductName name, Sku sku, Money price, int stock)
    {
        Id = id;
        Name = name;
        Sku = sku;
        Price = price;
        Stock = stock;
    }

    public Guid Id { get; private set; }

    public ProductName Name { get; private set; }

    public Sku Sku { get; private set; }

    public Money Price { get; private set; }

    /// <summary>Units available to sell. Reserved units are already subtracted.</summary>
    public int Stock { get; private set; }

    // The catalog's limits (Guide §3.11, "Limits"). Business rules, and a guard for the storage: a price of
    // 10^17 would overflow its numeric(18,2) column, and stock near int.MaxValue would wrap to a negative.
    public const decimal MaxPrice = 1_000_000.00m;

    public const int MaxStock = 1_000_000;

    /// <summary>The largest number of units one stock adjustment may add or remove.</summary>
    public const int MaxAdjustment = 1_000_000;

    public static Product Create(Guid id, ProductName name, Sku sku, Money price, int initialStock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sku);
        EnsureValidPrice(price);
        return initialStock is < 0 or > MaxStock
            ? throw new DomainValidationException($"Initial stock must be between 0 and {MaxStock}.")
            : new Product(id, name, sku, price, initialStock);
    }

    /// <summary>A price for a product: a valid <see cref="Money"/> that is not above <see cref="MaxPrice"/>.</summary>
    public static Money ValidPrice(decimal amount)
    {
        var price = Money.Of(amount);
        EnsureValidPrice(price);
        return price;
    }

    public void ChangePrice(Money price)
    {
        EnsureValidPrice(price);
        Price = price;
    }

    /// <summary>Adds (positive) or removes (negative) units, for example after a stock count.</summary>
    public void AdjustStock(int quantity)
    {
        EnsureValidAdjustment(quantity);
        if (Stock + quantity is < 0 or > MaxStock)
        {
            throw new BusinessRuleViolationException($"Stock of product {Id} must stay between 0 and {MaxStock}.");
        }

        Stock += quantity;
    }

    /// <summary>Checks the size of an adjustment on its own, before any product is loaded.</summary>
    public static void EnsureValidAdjustment(int quantity)
    {
        // As a long, so int.MinValue cannot overflow in Math.Abs.
        if (quantity == 0 || Math.Abs((long)quantity) > MaxAdjustment)
        {
            throw new DomainValidationException($"Quantity must not be zero and at most {MaxAdjustment} units either way.");
        }
    }

    private static void EnsureValidPrice(Money price)
    {
        ArgumentNullException.ThrowIfNull(price);
        if (price.Amount > MaxPrice)
        {
            throw new DomainValidationException($"Price can be at most {MaxPrice:0.00}.");
        }
    }

    public bool CanReserve(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        return Stock >= quantity.Value;
    }

    public void Reserve(Quantity quantity)
    {
        if (!CanReserve(quantity))
        {
            throw new BusinessRuleViolationException($"Product {Id} has only {Stock} units, {quantity} requested.");
        }

        Stock -= quantity.Value;
    }

    public void Release(Quantity quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);
        Stock += quantity.Value;
    }
}
