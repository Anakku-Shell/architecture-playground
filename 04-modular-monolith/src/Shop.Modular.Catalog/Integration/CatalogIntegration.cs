using Microsoft.EntityFrameworkCore;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.Catalog.Contracts;
using Shop.Modular.Catalog.Data;
using Shop.Modular.Ordering.Contracts;

namespace Shop.Modular.Catalog.Integration;

// How the Catalog module serves the others: one query (its contract's ICatalogQueries) and two consumers
// of Ordering's events. These classes are internal; other modules only see the interfaces and events.
// Guide: §7.3.

/// <summary>Implements Catalog's public query. Returns plain snapshots, never the <c>Product</c> rows.</summary>
internal sealed class CatalogQueries(CatalogDbContext db) : ICatalogQueries
{
    public async Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductSnapshot(p.Id, p.Name, p.Price))
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Reserves the stock of a new order: every line or none. Runs inside Ordering's transaction (the bus is
/// in-process), so the units it takes are committed together with the order, or not at all.
/// </summary>
internal sealed class OrderPlacedConsumer(CatalogDbContext db, IEventBus bus) : IIntegrationEventConsumer<OrderPlaced>
{
    public async Task ConsumeAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Outside a transaction each statement commits on its own: the lock below would be released at once
        // and two orders could take the same units. Fail loudly instead of overselling quietly.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException($"{nameof(OrderPlaced)} must be consumed inside the publisher's transaction.");
        }

        var ids = integrationEvent.Items.Select(i => i.ProductId).ToArray();

        // Lock the order's products (FOR UPDATE) until the transaction ends, always in id order so two
        // orders for the same products cannot deadlock. A concurrent order for the last units waits here,
        // then reads the stock this one left. Guide: §7.5, step 6.
        var products = await db.Products
            .FromSql($"""SELECT * FROM catalog.products WHERE "Id" = ANY({ids}) ORDER BY "Id" FOR UPDATE""")
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Units wanted per product, summed: Catalog does not rely on Ordering having merged repeated products.
        var wanted = integrationEvent.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var enough = wanted.All(w => products.TryGetValue(w.Key, out var p) && p.Stock >= w.Value);
        if (!enough)
        {
            await bus.PublishAsync(new StockReservationFailed(integrationEvent.OrderId), cancellationToken);
            return;
        }

        foreach (var (productId, quantity) in wanted)
        {
            products[productId].Stock -= quantity;
        }

        await db.SaveChangesAsync(cancellationToken);
        await bus.PublishAsync(new StockReserved(integrationEvent.OrderId), cancellationToken);
    }
}

/// <summary>
/// Gives back the units of a cancelled order. Never refused: returning units may end slightly above the
/// maximum stock, which caps what is added by hand, not what comes back.
/// </summary>
internal sealed class OrderCancelledConsumer(CatalogDbContext db) : IIntegrationEventConsumer<OrderCancelled>
{
    public async Task ConsumeAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        // Product id order, as in the reservation: every transaction locks products in the same order, so
        // none waits for a lock another holds while holding one it needs (.NET Guid order = PostgreSQL uuid order).
        foreach (var item in integrationEvent.Items.OrderBy(i => i.ProductId))
        {
            var quantity = item.Quantity;
            await db.Products
                .Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, p => p.Stock + quantity), cancellationToken);
        }
    }
}
