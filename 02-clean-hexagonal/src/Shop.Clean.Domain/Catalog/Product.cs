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

    public static Product Create(Guid id, ProductName name, Sku sku, Money price, int initialStock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(price);
        return initialStock < 0
            ? throw new DomainValidationException("Initial stock cannot be negative.")
            : new Product(id, name, sku, price, initialStock);
    }

    public void ChangePrice(Money price) => Price = price ?? throw new ArgumentNullException(nameof(price));

    /// <summary>Adds (positive) or removes (negative) units, for example after a stock count.</summary>
    public void AdjustStock(int quantity)
    {
        if (quantity == 0)
        {
            throw new DomainValidationException("Quantity must not be zero.");
        }

        if (Stock + quantity < 0)
        {
            throw new BusinessRuleViolationException($"Stock of product {Id} cannot go below zero.");
        }

        Stock += quantity;
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
