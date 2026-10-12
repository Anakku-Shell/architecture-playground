namespace Shop.Layered.Data.Entities;

/// <summary>
/// A product of the catalog, exactly as it is stored: one property per column, public setters,
/// no behaviour. In the layered version this class is also the business model and travels up
/// to the Api layer. Guide: §4.2.
/// </summary>
public sealed class Product
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Stock Keeping Unit, unique, stored upper-case.</summary>
    public string Sku { get; set; } = "";

    public decimal Price { get; set; }

    /// <summary>Units available to sell. Reserved units are already subtracted.</summary>
    public int Stock { get; set; }
}
