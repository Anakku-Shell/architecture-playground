using RabbitMQ.Client;

namespace Shop.Micro.Messaging;

/// <summary>How one service plugs into the broker. Set when the service registers messaging.</summary>
public sealed class MessagingOptions
{
    public const int DefaultMaxAttempts = 5;

    /// <summary>The topic exchange every service publishes to.</summary>
    public string Exchange { get; set; } = Topology.DefaultExchange;

    /// <summary>The service's own queue (one per service, whatever the number of instances).</summary>
    public string QueueName { get; set; } = "";

    /// <summary>How many times a failing message is tried before it goes to the dead-letter queue.</summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>How often the dispatcher checks the outbox when no commit has woken it up.</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int DispatchBatchSize { get; set; } = 50;

    /// <summary>The pause before a failed message is tried again, multiplied by the attempt number.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// The broker layout, in one place. One <b>topic exchange</b> (<c>shop</c>) receives every message with its
/// type name as the routing key. Each service owns one durable queue, bound to the types it consumes: a
/// command has one binding (its receiver), an event any number. A message that keeps failing is moved to
/// the queue's <b>dead-letter queue</b> for a person to look at, instead of blocking the queue forever.
/// Guide: §8.4, "The broker".
/// </summary>
public static class Topology
{
    public const string DefaultExchange = "shop";

    public static string DeadLetterQueue(string queue) => $"{queue}.dead-letter";

    internal static Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken cancellationToken) =>
        channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

    /// <summary>Declares (idempotently) the exchange, the service's queue, its dead-letter queue and the bindings.</summary>
    internal static async Task DeclareQueueAsync(IChannel channel, MessagingOptions options, IEnumerable<string> routingKeys, CancellationToken cancellationToken)
    {
        await DeclareExchangeAsync(channel, options.Exchange, cancellationToken);
        var deadLetters = DeadLetterQueue(options.QueueName);
        await channel.QueueDeclareAsync(deadLetters, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);

        // A quorum queue: replicated, the recommended durable type. The consumer host counts attempts itself
        // and rejects a message to the dead-letter exchange after MaxAttempts. x-delivery-limit is only the
        // backstop for messages the broker takes back on its own (a consumer that crashes mid-message), which
        // it counts; RabbitMQ 4.3 does not count a nack with requeue, which is why the consumer counts.
        await channel.QueueDeclareAsync(
            options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = options.MaxAttempts,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = deadLetters,
            },
            cancellationToken: cancellationToken);
        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(options.QueueName, options.Exchange, routingKey, cancellationToken: cancellationToken);
        }
    }
}
