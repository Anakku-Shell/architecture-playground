using Microsoft.EntityFrameworkCore;
using Shop.Micro.Messaging;
using Xunit;

namespace Shop.Micro.IntegrationTests;

/// <summary>
/// The idempotent inbox: a broker delivers <b>at least once</b>, so a consumer must be safe to run twice on
/// the same message. Guide: §8.4, "The idempotent inbox".
/// </summary>
public sealed class InboxTests(Infrastructure infrastructure)
{
    [Fact]
    public async Task DuplicateMessage_IsHandledOnce()
    {
        await using var host = await MessagingTestHost.StartAsync(infrastructure, consume: true);
        var duplicated = new TestMessage(Guid.NewGuid());
        var messageId = Guid.NewGuid();
        var marker = new TestMessage(Guid.NewGuid());

        // The same message (same id) twice, as after a lost acknowledgement, then a marker. Deliveries are
        // handled one at a time, in order, so once the marker is handled both copies have been seen.
        await host.PublishRawAsync(duplicated, messageId);
        await host.PublishRawAsync(duplicated, messageId);
        await host.PublishRawAsync(marker, Guid.NewGuid());
        await MessagingTestHost.WaitUntil(
            () => host.InScope((db, _) => db.Handled.AnyAsync(h => h.TestMessageId == marker.Id)), "the marker");

        Assert.Equal(1, host.Attempts.Of(duplicated.Id));
        Assert.Equal(1, await host.InScope((db, _) => db.Handled.CountAsync(h => h.TestMessageId == duplicated.Id)));
        Assert.Equal(1, await host.InScope((db, _) => db.Set<InboxMessage>().CountAsync(m => m.MessageId == messageId)));
    }

    [Fact]
    public async Task FailedMessage_IsRetriedThenDeadLettered()
    {
        await using var host = await MessagingTestHost.StartAsync(infrastructure, consume: true);
        var failing = new TestMessage(Guid.NewGuid(), Fail: true);
        var messageId = Guid.NewGuid();

        await host.PublishRawAsync(failing, messageId);

        await using var channel = await host.Broker.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await MessagingTestHost.WaitUntil(
            async () => await channel.MessageCountAsync(Topology.DeadLetterQueue(host.Queue)) == 1, "the dead-letter queue");

        // Every attempt rolled back: no business row and no inbox row, so a fixed consumer could still take it.
        Assert.Equal(MessagingOptions.DefaultMaxAttempts, host.Attempts.Of(failing.Id));
        Assert.False(await host.InScope((db, _) => db.Handled.AnyAsync(h => h.TestMessageId == failing.Id)));
        Assert.False(await host.InScope((db, _) => db.Set<InboxMessage>().AnyAsync(m => m.MessageId == messageId)));
    }
}
