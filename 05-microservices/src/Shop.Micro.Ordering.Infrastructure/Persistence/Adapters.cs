using Microsoft.EntityFrameworkCore;
using Shop.Micro.Contracts;
using Shop.Micro.Messaging;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Domain;

namespace Shop.Micro.Ordering.Infrastructure.Persistence;

/// <summary>The driven adapter of <see cref="IOrderRepository"/>.</summary>
internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}

/// <summary>
/// The driven adapter of <see cref="IOutgoingMessages"/>: hands the message to the messaging building block's
/// outbox, which stages it in this same DbContext. The application never learns there is an outbox.
/// </summary>
internal sealed class OutboxOutgoingMessages(IMessageOutbox outbox) : IOutgoingMessages
{
    public void Send(IIntegrationMessage message) => outbox.Add(message);
}

/// <summary>
/// The driven adapter of <see cref="IUnitOfWork"/>: one <c>SaveChanges</c> writes the orders and the outbox rows
/// in one transaction. A changed row version becomes the application's <see cref="ConcurrencyConflictException"/>.
/// </summary>
internal sealed class EfUnitOfWork(OrderingDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The order was changed by another request; read it again and retry.", ex);
        }
    }
}
