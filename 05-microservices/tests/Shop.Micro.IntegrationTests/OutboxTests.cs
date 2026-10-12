using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shop.Micro.Messaging;
using Xunit;

namespace Shop.Micro.IntegrationTests;

/// <summary>
/// The transactional outbox: a message is written in the same transaction as the business change and sent
/// by a background dispatcher only once that transaction has committed. Guide: §8.4, "The transactional outbox".
/// </summary>
public sealed class OutboxTests(Infrastructure infrastructure)
{
    [Fact]
    public async Task MessageIsPublishedOnlyAfterCommit()
    {
        await using var host = await MessagingTestHost.StartAsync(infrastructure, consume: false);
        await using var observer = await ObserveAsync(host);
        var message = new TestMessage(Guid.NewGuid());

        var outboxId = await host.InScope(async (db, outbox) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            outbox.Add(message);
            await db.SaveChangesAsync();

            // Saved but not committed: the dispatcher is woken up, yet cannot see the row.
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            Assert.Equal(0u, await observer.Channel.MessageCountAsync(observer.Queue, TestContext.Current.CancellationToken));

            await transaction.CommitAsync();
            return await db.Set<OutboxMessage>().Select(m => m.Id).SingleAsync();
        });

        var delivered = await observer.NextAsync();
        Assert.Equal(outboxId.ToString(), delivered.BasicProperties.MessageId);
        Assert.Equal(nameof(TestMessage), delivered.BasicProperties.Type);
        Assert.Equal(message, JsonSerializer.Deserialize<TestMessage>(delivered.Body.Span, JsonSerializerOptions.Web));
        await MessagingTestHost.WaitUntil(
            () => host.InScope((db, _) => db.Set<OutboxMessage>().AnyAsync(m => m.Id == outboxId && m.SentAt != null)), "the row to be marked sent");
    }

    [Fact]
    public async Task RolledBackMessage_IsNeverPublished()
    {
        await using var host = await MessagingTestHost.StartAsync(infrastructure, consume: false);
        await using var observer = await ObserveAsync(host);
        var rolledBack = new TestMessage(Guid.NewGuid());
        var committed = new TestMessage(Guid.NewGuid());

        await host.InScope(async (db, outbox) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            outbox.Add(rolledBack);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
            return 0;
        });
        await host.InScope(async (db, outbox) =>
        {
            outbox.Add(committed);
            await db.SaveChangesAsync();
            return 0;
        });

        var delivered = await observer.NextAsync();
        Assert.Equal(committed, JsonSerializer.Deserialize<TestMessage>(delivered.Body.Span, JsonSerializerOptions.Web));
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(0u, await observer.Channel.MessageCountAsync(observer.Queue, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A row the broker refuses (no queue is bound to its type) must not hold back the rows after it: it is
    /// retried with an attempt count and left for a person after too many tries.
    /// </summary>
    [Fact]
    public async Task UnroutableMessage_DoesNotBlockTheOthers()
    {
        await using var host = await MessagingTestHost.StartAsync(infrastructure, consume: false);
        await using var observer = await ObserveAsync(host);
        var after = new TestMessage(Guid.NewGuid());

        await host.InScope(async (db, outbox) =>
        {
            outbox.Add(new UnroutedMessage(Guid.NewGuid()));
            await db.SaveChangesAsync();
            outbox.Add(after);
            await db.SaveChangesAsync();
            return 0;
        });

        var delivered = await observer.NextAsync();
        Assert.Equal(after, JsonSerializer.Deserialize<TestMessage>(delivered.Body.Span, JsonSerializerOptions.Web));
        var unrouted = await host.InScope((db, _) => db.Set<OutboxMessage>().SingleAsync(m => m.Type == nameof(UnroutedMessage)));
        Assert.Null(unrouted.SentAt);
        Assert.True(unrouted.Attempts >= 1);
        Assert.NotNull(unrouted.LastError);
    }

    private static async Task<Observer> ObserveAsync(MessagingTestHost host)
    {
        var channel = await host.Broker.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(host.Exchange, ExchangeType.Topic, durable: true);
        var queue = (await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(queue, host.Exchange, nameof(TestMessage));
        return new Observer(channel, queue);
    }

    /// <summary>A throwaway queue bound to the test exchange: it sees what the dispatcher publishes.</summary>
    private sealed record Observer(IChannel Channel, string Queue) : IAsyncDisposable
    {
        public async Task<BasicGetResult> NextAsync()
        {
            BasicGetResult? result = null;
            await MessagingTestHost.WaitUntil(async () => (result = await Channel.BasicGetAsync(Queue, autoAck: true)) is not null, "a published message");
            return result!;
        }

        public async ValueTask DisposeAsync() => await Channel.DisposeAsync();
    }
}
