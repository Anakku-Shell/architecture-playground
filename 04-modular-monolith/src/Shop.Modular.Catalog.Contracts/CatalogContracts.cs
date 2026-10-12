using Shop.Modular.BuildingBlocks;

namespace Shop.Modular.Catalog.Contracts;

// The public surface of the Catalog module: what other modules may know about it. Plain values and ids
// only; no Catalog type leaks out. Guide: §7.3.

/// <summary>Reads the catalog for other modules (Ordering copies names and prices into new orders).</summary>
public interface ICatalogQueries
{
    /// <summary>The products with these ids that exist. Unknown ids are simply missing from the result.</summary>
    Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>A product as other modules see it: enough to price an order line, nothing more.</summary>
public sealed record ProductSnapshot(Guid Id, string Name, decimal Price);

/// <summary>Published after an <c>OrderPlaced</c>: every line of the order got its units.</summary>
public sealed record StockReserved(Guid OrderId) : IIntegrationEvent;

/// <summary>Published after an <c>OrderPlaced</c>: at least one line could not get its units, so none were taken.</summary>
public sealed record StockReservationFailed(Guid OrderId) : IIntegrationEvent;
