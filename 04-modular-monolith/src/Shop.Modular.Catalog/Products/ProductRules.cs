using System.Globalization;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.Catalog.Data;

namespace Shop.Modular.Catalog.Products;

/// <summary>
/// The catalog's rules, as plain functions over plain data: the CRUD style (Guide §7.2). Compare with
/// version 02, where the same rules live inside a <c>Product</c> aggregate and value objects. For a module
/// whose data has a few field checks and no lifecycle, this is enough and easier to read.
/// </summary>
internal static class ProductRules
{
    public const decimal MaxPrice = 1_000_000.00m;
    public const int MaxStock = 1_000_000;
    public const int MaxAdjustment = 1_000_000;
    public const int MaxNameLength = 200;
    public const int MaxSkuLength = 50;

    /// <summary>A valid new product, or a <see cref="ValidationException"/> listing every invalid field.</summary>
    public static Product NewProduct(string? name, string? sku, decimal price, int initialStock)
    {
        var errors = new Dictionary<string, string[]>();
        var trimmedName = name?.Trim() ?? "";
        var normalizedSku = NormalizeSku(sku);
        if (trimmedName.Length is < 1 or > MaxNameLength)
        {
            errors["name"] = [$"Name must have between 1 and {MaxNameLength} characters."];
        }

        if (normalizedSku.Length is < 1 or > MaxSkuLength)
        {
            errors["sku"] = [$"SKU must have between 1 and {MaxSkuLength} characters."];
        }

        if (PriceError(price) is { } priceError)
        {
            errors["price"] = [priceError];
        }

        if (initialStock is < 0 or > MaxStock)
        {
            errors["initialStock"] = [$"Initial stock must be between 0 and {MaxStock}."];
        }

        return errors.Count > 0
            ? throw new ValidationException(errors)
            : new Product { Id = Guid.CreateVersion7(), Name = trimmedName, Sku = normalizedSku, Price = price, Stock = initialStock };
    }

    /// <summary>SKUs are stored upper-case, so "abc-1" and "ABC-1" are the same SKU.</summary>
    public static string NormalizeSku(string? sku) => sku?.Trim().ToUpperInvariant() ?? "";

    public static void CheckPrice(decimal price)
    {
        if (PriceError(price) is { } error)
        {
            throw new ValidationException("price", error);
        }
    }

    public static void CheckAdjustment(int quantity)
    {
        // long: Math.Abs(int.MinValue) does not fit in an int.
        if (quantity == 0 || Math.Abs((long)quantity) > MaxAdjustment)
        {
            throw new ValidationException("quantity", $"Quantity must not be zero and at most {MaxAdjustment} units either way.");
        }
    }

    private static string? PriceError(decimal price) =>
        price is <= 0 or > MaxPrice ? string.Create(CultureInfo.InvariantCulture, $"Price must be greater than zero and at most {MaxPrice:0.00}.")
        : decimal.Round(price, 2) != price ? "Price can have at most two decimals."
        : null;
}
