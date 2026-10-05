using Shop.Modular.Ordering.Domain;

namespace Shop.Modular.Ordering.Application.Ports;

// The ports of the Ordering hexagon, as in version 02 (Guide §5.4). Two other things the module needs
// from outside are NOT ports defined here: ICatalogQueries (Catalog's contract) and IEventBus (the
// building blocks). They are interfaces too, owned by their providers. Guide: §7.2.

/// <summary>Loads and stores <see cref="Order"/> aggregates (with their lines).</summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the order and <b>locks</b> it until the transaction ends: a concurrent pay or cancel of the same
    /// order waits, then sees the new state. Must be called inside <see cref="IUnitOfWork.InTransactionAsync"/>.
    /// </summary>
    Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    void Add(Order order);
}

/// <summary>
/// The transaction boundary. Ordering starts every flow that spans modules (place, pay, cancel), so it owns
/// the boundary: everything the other modules do while reacting to its events commits or rolls back with it.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in one transaction that every module joins, then commits.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>Writes the changes made to loaded orders (inside the current transaction, if any).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
