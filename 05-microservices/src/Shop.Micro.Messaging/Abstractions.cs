using Shop.Micro.Contracts;

namespace Shop.Micro.Messaging;

/// <summary>
/// Sends a message <b>as part of the current unit of work</b>: it only stages a row in the service's own
/// database (the outbox). The row is written by the same <c>SaveChanges</c>, in the same transaction, as the
/// business change, and a background dispatcher publishes it once that transaction has committed. So a
/// message goes out if and only if the change it announces was saved. Guide: §8.4, "The transactional outbox".
/// </summary>
public interface IMessageOutbox
{
    void Add(IIntegrationMessage message);
}

/// <summary>
/// Handles one kind of message arriving from the broker. It runs inside one database transaction together
/// with the inbox row that marks the message as handled, so its writes and that mark commit or roll back
/// together. It may also send messages through <see cref="IMessageOutbox"/>: they join the same transaction.
/// Guide: §8.4, "The idempotent inbox".
/// </summary>
public interface IMessageConsumer<in TMessage>
    where TMessage : IIntegrationMessage
{
    Task ConsumeAsync(TMessage message, CancellationToken cancellationToken);
}
