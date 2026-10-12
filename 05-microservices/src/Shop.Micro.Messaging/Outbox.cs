using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shop.Micro.Contracts;

namespace Shop.Micro.Messaging;

/// <summary>Stages a message as an outbox row in the service's DbContext. Guide: §8.4, "The transactional outbox".</summary>
internal sealed class EfMessageOutbox<TContext>(TContext db, TimeProvider time) : IMessageOutbox
    where TContext : DbContext
{
    public void Add(IIntegrationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = MessageTypeRegistry.NameOf(message.GetType()),
            Payload = MessageSerializer.Serialize(message),
            OccurredAt = time.GetUtcNow(),
            TraceParent = Activity.Current?.Id,
        });
    }
}

/// <summary>Wakes the dispatcher up. Several signals before it wakes count as one.</summary>
internal sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _semaphore.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>
/// Tells the dispatcher that new rows may have become visible: after a commit, or after a save outside an
/// explicit transaction (which commits on its own). Without it the dispatcher would only find new messages
/// on its next poll. Nothing is published here: a row saved inside a transaction is invisible to the
/// dispatcher until that transaction commits.
/// </summary>
internal sealed class OutboxSignalInterceptor(OutboxSignal signal) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => signal.Notify();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        signal.Notify();
        return Task.CompletedTask;
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyIfAutoCommitted(eventData);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        NotifyIfAutoCommitted(eventData);
        return ValueTask.FromResult(result);
    }

    private void NotifyIfAutoCommitted(SaveChangesCompletedEventData eventData)
    {
        if (eventData?.Context?.Database.CurrentTransaction is null)
        {
            signal.Notify();
        }
    }
}

/// <summary>
/// The outbox <b>dispatcher</b> (also called the relay): publishes committed outbox rows to RabbitMQ, oldest
/// first, and marks them sent. Publishing and marking are not atomic either: if the process dies between the
/// two, the row is published again later. That is why delivery is "at least once" and every consumer needs
/// an inbox. Guide: §8.4, "The transactional outbox".
/// </summary>
internal sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopes,
    IConnection connection,
    OutboxSignal signal,
    MessagingOptions options,
    TimeProvider time,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Publisher confirms: BasicPublishAsync returns only once the broker has taken the message, and
        // throws if it refused it or (mandatory) could not route it to any queue. Only then is the row marked.
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
        await Topology.DeclareExchangeAsync(channel, options.Exchange, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(channel, stoppingToken))
                {
                    // A full batch: there may be more waiting.
                }

                await signal.WaitAsync(options.DispatchInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The broker or the database is unavailable. The rows stay unsent and are retried.
                MessagingLog.DispatchFailed(logger, ex);
                await Task.Delay(options.DispatchInterval, time, stoppingToken);
            }
        }
    }

    /// <returns><c>true</c> when a whole batch was sent, so another one may be waiting.</returns>
    private async Task<bool> DispatchBatchAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // FOR UPDATE SKIP LOCKED: with several instances of the service, each dispatcher takes rows the
        // others are not already sending, instead of all sending the same ones.
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw(UnsentRowsSql(db))
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            return false;
        }

        Exception? failure = null;
        foreach (var message in batch)
        {
            try
            {
                await PublishAsync(channel, message, cancellationToken);
                message.SentAt = time.GetUtcNow();
            }
            catch (PublishException ex)
            {
                // The broker refused THIS message (no queue bound to its type, or a nack). Retrying it first
                // every time would hold back every message behind it (head-of-line blocking), so count the
                // attempt and go on; after MaxAttempts the row is left, unsent, for a person to look at.
                message.Attempts++;
                message.LastError = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
                MessagingLog.PublishRefused(logger, ex, message.Type, message.Id, message.Attempts);
            }
            catch (Exception ex)
            {
                // The broker or the connection is down (or the service is stopping): nothing can be sent now.
                // Record what was sent and retry the rest later, without counting this against the rows.
                failure = ex;
                break;
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        var sent = batch.Count(m => m.SentAt is not null);
        MessagingLog.Dispatched(logger, sent);
        return failure is null ? batch.Count == options.DispatchBatchSize : throw failure;
    }

    private async Task PublishAsync(IChannel channel, OutboxMessage message, CancellationToken cancellationToken)
    {
        using var activity = MessagingTracing.StartPublish(message, options.Exchange);
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            Persistent = true,
            Headers = new Dictionary<string, object?>(),
        };
        if ((activity?.Id ?? message.TraceParent) is { } traceParent)
        {
            properties.Headers[MessagingTracing.TraceParentHeader] = traceParent;
        }

        await channel.BasicPublishAsync(options.Exchange, message.Type, mandatory: true, properties, MessageSerializer.ToBytes(message.Payload), cancellationToken);
    }

    private string UnsentRowsSql(TContext db)
    {
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = entity.GetSchema() is { } schema ? $"\"{schema}\".\"{entity.GetTableName()}\"" : $"\"{entity.GetTableName()}\"";
        return $"""SELECT * FROM {table} WHERE "SentAt" IS NULL AND "Attempts" < {options.MaxAttempts} ORDER BY "OccurredAt", "Id" LIMIT {options.DispatchBatchSize} FOR UPDATE SKIP LOCKED""";
    }
}
