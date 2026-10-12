using Microsoft.EntityFrameworkCore;
using Shop.Modular.BuildingBlocks.Infrastructure.Persistence;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Domain;

namespace Shop.Modular.Ordering.Infrastructure.Persistence;

/// <summary>The driven adapter of <see cref="IOrderRepository"/>.</summary>
internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <summary>
    /// <b>Pessimistic locking</b>: <c>SELECT … FOR UPDATE</c> locks the row until the transaction ends, so a
    /// second pay or cancel of this order waits instead of racing. Versions 02 and 03 checked afterwards
    /// (optimistic <c>xmin</c>) and retried; here the database queues the requests. Guide: §7.5, step 3 of "Pay".
    /// </summary>
    public async Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A lock is only held inside a transaction: call this inside InTransactionAsync.");
        }

        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM ordering.orders WHERE "Id" = {id} FOR UPDATE""", cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public void Add(Order order) => db.Orders.Add(order);
}

/// <summary>
/// The driven adapter of <see cref="IUnitOfWork"/>. The transaction is the building blocks'
/// <see cref="SharedTransaction"/>, so the other modules' writes, made while they consume Ordering's events,
/// commit or roll back together with the order.
/// </summary>
internal sealed class EfUnitOfWork(OrderingDbContext db, SharedTransaction transaction) : IUnitOfWork
{
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(work, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
