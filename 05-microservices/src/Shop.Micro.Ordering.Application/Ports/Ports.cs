using Shop.Micro.Contracts;
using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Ordering.Domain;

namespace Shop.Micro.Ordering.Application.Ports;

// The ports of the Ordering hexagon. Two are new in 05 and show that "another service" is just one more
// thing outside the hexagon: ICatalogClient (a synchronous HTTP call) and IOutgoingMessages (the broker,
// through the outbox). The application decides WHAT to ask; the adapters decide HOW. Guide: §8.2.

/// <summary>Loads and stores <see cref="Order"/> aggregates (with their lines).</summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Add(Order order);
}

/// <summary>
/// Messages to other services. Sending only <b>stages</b> the message: it leaves with the next
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, in the same transaction as the orders, or not at all.
/// </summary>
public interface IOutgoingMessages
{
    void Send(IIntegrationMessage message);
}

/// <summary>Writes the changes to loaded orders and the staged messages, atomically.</summary>
public interface IUnitOfWork
{
    /// <exception cref="Common.ConcurrencyConflictException">An order changed since it was read.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Names and prices from the Catalog service, at the moment the order is placed.</summary>
public interface ICatalogClient
{
    /// <summary>The products that exist among <paramref name="ids"/>; unknown ids are simply missing.</summary>
    /// <exception cref="Common.CatalogUnavailableException">Catalog did not answer in time or failed.</exception>
    Task<IReadOnlyList<ProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
