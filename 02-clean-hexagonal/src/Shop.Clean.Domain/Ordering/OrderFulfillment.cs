using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Payments;

namespace Shop.Clean.Domain.Ordering;

/// <summary>
/// A <b>domain service</b> (Guide §3.7): business logic that needs two aggregates, <see cref="Order"/> and
/// <see cref="Product"/>, so it belongs to neither. It holds the rules "reserve every line or none" and
/// "every cancellation gives the stock back". It is pure: no database, no I/O, just objects in memory.
/// The use cases load the aggregates, call it, and save. Guide: §5.2.
/// </summary>
public static class OrderFulfillment
{
    public static Order Place(
        Guid orderId,
        Guid customerId,
        IReadOnlyList<(Product Product, Quantity Quantity)> lines,
        DateTimeOffset placedAt)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var orderLines = lines.Select(l => OrderLine.Snapshot(l.Product, l.Quantity)).ToList();

        // Check every line first, then reserve: all or none. (A product appears at most once in an
        // order, which Order itself enforces, so checking lines one by one is enough.)
        if (!lines.All(l => l.Product.CanReserve(l.Quantity)))
        {
            return Order.Reject(orderId, customerId, orderLines, placedAt);
        }

        var order = Order.Place(orderId, customerId, orderLines, placedAt);
        foreach (var (product, quantity) in lines)
        {
            product.Reserve(quantity);
        }

        return order;
    }

    public static void Cancel(Order order, IReadOnlyDictionary<Guid, Product> products)
    {
        ArgumentNullException.ThrowIfNull(order);
        order.Cancel();
        Release(order, products);
    }

    public static void RecordPayment(Order order, PaymentStatus status, IReadOnlyDictionary<Guid, Product> products)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (status == PaymentStatus.Approved)
        {
            order.MarkPaid();
            return;
        }

        order.DeclinePayment();
        Release(order, products);
    }

    private static void Release(Order order, IReadOnlyDictionary<Guid, Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        foreach (var line in order.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                throw new InvalidOperationException($"Product {line.ProductId} of order {order.Id} was not loaded.");
            }

            product.Release(line.Quantity);
        }
    }
}
