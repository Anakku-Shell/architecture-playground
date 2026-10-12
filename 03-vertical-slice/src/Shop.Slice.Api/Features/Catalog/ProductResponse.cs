using Shop.Slice.Api.Domain.Catalog;

namespace Shop.Slice.Api.Features.Catalog;

// The JSON shape of a product, shared by the catalog slices. It lives at the context level
// (Features/Catalog), not inside one slice, because it is part of the API contract that every catalog
// slice answers with: sharing a contract is fine, sharing logic between slices is not. Guide: §6.2.

public sealed record ProductResponse(Guid Id, string Name, string Sku, decimal Price, int Stock)
{
    /// <summary>For command slices, which hold the aggregate they just changed.</summary>
    public static ProductResponse From(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new(product.Id, product.Name.Value, product.Sku.Value, product.Price.Amount, product.Stock);
    }
}
