using Shop.Layered.Data.Entities;

namespace Shop.Layered.Api.Models;

// Request bodies are plain records: nullable where the client may leave a field out, because the
// Business layer, not the JSON binder, decides what is valid.

public sealed record CreateProductRequest(string? Name, string? Sku, decimal Price, int InitialStock);

public sealed record ChangePriceRequest(decimal Price);

public sealed record AdjustStockRequest(int Quantity);

/// <summary>
/// The product as the API shows it. Built from the EF entity at the last moment, in the Api layer. Note
/// the <c>using Shop.Layered.Data.Entities</c> above: the Api has no project reference to Data, yet it
/// compiles against Data's types through Business. Guide: §4.6, "The honest limit".
/// </summary>
public sealed record ProductResponse(Guid Id, string Name, string Sku, decimal Price, int Stock)
{
    public static ProductResponse From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new(product.Id, product.Name, product.Sku, product.Price, product.Stock);
    }
}
