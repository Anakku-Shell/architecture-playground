using Microsoft.EntityFrameworkCore;
using Shop.Micro.Catalog.Api.Data;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Messaging;

namespace Shop.Micro.Catalog.Api.Stock;

// How Catalog takes part in the saga: two commands from Ordering, each handled in one local transaction
// (opened by the messaging building block, together with the inbox row). The answer is staged in the
// outbox inside that same transaction, so "stock taken" and "StockReserved sent" cannot disagree.
// Compare with 04's OrderPlacedConsumer, which ran inside Ordering's transaction. Guide: §8.5.

/// <summary>Reserves the stock of an order, every line or none, and answers.</summary>
internal sealed class ReserveStockConsumer(CatalogDbContext db, IMessageOutbox outbox) : IMessageConsumer<ReserveStock>
{
    public async Task ConsumeAsync(ReserveStock message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var ids = message.Items.Select(i => i.ProductId).ToArray();

        // Lock the order's products (FOR UPDATE) until the transaction ends, in id order so two reservations
        // cannot deadlock. Each service instance handles one message at a time, but there may be several
        // instances, and HTTP stock adjustments run at the same time too.
        var products = await db.Products
            .FromSql($"""SELECT * FROM products WHERE "Id" = ANY({ids}) ORDER BY "Id" FOR UPDATE""")
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Units wanted per product, summed: Catalog does not rely on Ordering having merged repeated products.
        var wanted = message.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (!wanted.All(w => products.TryGetValue(w.Key, out var p) && p.Stock >= w.Value))
        {
            outbox.Add(new StockReservationFailed(message.OrderId));
            return;
        }

        foreach (var (productId, quantity) in wanted)
        {
            products[productId].Stock -= quantity;
        }

        outbox.Add(new StockReserved(message.OrderId));

        // No SaveChanges here: the messaging building block saves the stock, the outbox row and the inbox
        // row in one go, then commits.
    }
}

/// <summary>
/// The saga's <b>compensation</b>: gives back the units of an order that will not be paid. Never refused (the
/// stock may end slightly above the maximum, which caps what is added by hand, not what comes back).
/// Not answered either: nothing in the saga waits for it.
/// </summary>
internal sealed class ReleaseStockConsumer(CatalogDbContext db) : IMessageConsumer<ReleaseStock>
{
    public async Task ConsumeAsync(ReleaseStock message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Product id order, as in the reservation, so the two never wait for each other's locks.
        foreach (var item in message.Items.OrderBy(i => i.ProductId))
        {
            var quantity = item.Quantity;
            await db.Products
                .Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, p => p.Stock + quantity), cancellationToken);
        }
    }
}
