using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Shop.Micro.Messaging;

/// <summary>
/// Receives the service's messages and runs each through the <b>idempotent inbox</b>: one local transaction
/// records the message id, runs the consumer (its writes and the messages it sends) and commits; only then is
/// the delivery acknowledged. A redelivered message finds its id already recorded and is acknowledged without
/// running again. A failure rolls everything back and the message is tried again; after
/// <see cref="MessagingOptions.MaxAttempts"/> attempts it goes to the dead-letter queue.
/// Guide: §8.4, "The idempotent inbox".
/// </summary>
internal sealed class RabbitMqConsumerHost<TContext>(
    IConnection connection,
    IServiceScopeFactory scopes,
    MessageTypeRegistry registry,
    MessagingOptions options,
    ConsumerReadiness readiness,
    TimeProvider time,
    ILogger<RabbitMqConsumerHost<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private const ushort Prefetch = 10;
    private const string AttemptHeader = "x-attempt";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Publisher confirms, for the retry copies this channel publishes (see RetryOrDeadLetterAsync).
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
        try
        {
            await Topology.DeclareQueueAsync(channel, options, registry.Names, stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Prefetch, global: false, stoppingToken);

            // Deliveries on one channel are handled one at a time, in order (the client's default).
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);
            await channel.BasicConsumeAsync(options.QueueName, autoAck: false, consumer, stoppingToken);
            readiness.MarkReady();

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Unacknowledged deliveries go back to the queue when the channel closes.
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var type = delivery.BasicProperties.Type;
        var messageIdText = delivery.BasicProperties.MessageId;
        using var activity = MessagingTracing.StartProcess(type ?? "unknown", messageIdText, MessagingTracing.ReadTraceParent(delivery.BasicProperties.Headers), options.QueueName);

        if (!registry.TryGet(type, out var registration) || !Guid.TryParse(messageIdText, out var messageId))
        {
            // Not something this service understands: retrying will not help. Straight to the dead-letter queue.
            MessagingLog.Unreadable(logger, type, messageIdText);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        // A body that is not valid JSON for its type will never become valid: no point retrying it.
        object message;
        try
        {
            message = MessageSerializer.Deserialize(delivery.Body.Span, registration.Type);
        }
        catch (JsonException)
        {
            MessagingLog.Unreadable(logger, type, messageIdText);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        try
        {
            if (await ProcessOnceAsync(registration, type!, messageId, message, stoppingToken))
            {
                MessagingLog.Handled(logger, type!, messageId);
            }
            else
            {
                MessagingLog.Duplicate(logger, type!, messageId);
            }

            // After the commit. If the process dies before this line, the message comes back and the inbox
            // recognises it: that is the window the inbox exists for.
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            await RetryOrDeadLetterAsync(channel, delivery, ex, type!, messageId, stoppingToken);
        }
    }

    /// <summary>
    /// A failed message is tried again up to <see cref="MessagingOptions.MaxAttempts"/> times, then rejected to
    /// the dead-letter queue. The attempts are counted here, in a header, not left to the broker: RabbitMQ's own
    /// delivery count does not grow when a consumer returns a message (nack with requeue) in recent versions,
    /// which turned a poison message into thousands of retries a second. A retry is a copy published back to
    /// the queue with the next attempt number, after a short pause; the original is then acknowledged. If the
    /// process dies in between, both may arrive: the inbox handles that.
    /// </summary>
    private async Task RetryOrDeadLetterAsync(IChannel channel, BasicDeliverEventArgs delivery, Exception exception, string type, Guid messageId, CancellationToken stoppingToken)
    {
        var attempt = ReadAttempt(delivery.BasicProperties.Headers);
        if (attempt >= options.MaxAttempts)
        {
            MessagingLog.DeadLettered(logger, exception, type, messageId, attempt);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, stoppingToken);
            return;
        }

        MessagingLog.Failed(logger, exception, type, messageId, attempt);
        await Task.Delay(options.RetryDelay * attempt, time, stoppingToken);
        var properties = new BasicProperties(delivery.BasicProperties)
        {
            Headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [AttemptHeader] = attempt + 1,
            },
        };
        try
        {
            await channel.BasicPublishAsync(exchange: "", routingKey: options.QueueName, mandatory: true, properties, delivery.Body, stoppingToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The copy could not be published: give the original back to the broker instead of leaving it
            // unacknowledged. It is retried with the same attempt number, which is fine.
            MessagingLog.RetryFailed(logger, ex, type, messageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    /// <summary>Which attempt this delivery is (1 for the first). AMQP may hand the header back as int or long.</summary>
    private static int ReadAttempt(IDictionary<string, object?>? headers) =>
        headers?.TryGetValue(AttemptHeader, out var value) == true
            ? value switch { int i => i, long l => (int)l, _ => 1 }
            : 1;

    /// <returns><c>false</c> if the message had already been handled.</returns>
    private async Task<bool> ProcessOnceAsync(MessageTypeRegistry.Registration registration, string type, Guid messageId, object message, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (await db.Set<InboxMessage>().AnyAsync(m => m.MessageId == messageId, cancellationToken))
        {
            return false;
        }

        // Two copies handled at the same moment both pass the check above; the primary key stops the
        // second at commit, its work rolls back, and its redelivery is then recognised as a duplicate.
        db.Set<InboxMessage>().Add(new InboxMessage { MessageId = messageId, Type = type, ProcessedAt = time.GetUtcNow() });
        await registration.Dispatch(scope.ServiceProvider, message, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }
}

/// <summary>Whether the service's queue is declared and being consumed.</summary>
public sealed class ConsumerReadiness
{
    private volatile bool _ready;

    public bool IsReady => _ready;

    internal void MarkReady() => _ready = true;
}

/// <summary>
/// Reports the service unhealthy until its queue exists. Aspire starts the gateway only once every service
/// is healthy, so no message is published before the queue that should receive it is there.
/// </summary>
internal sealed class MessagingHealthCheck(ConsumerReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.IsReady
            ? HealthCheckResult.Healthy("Consuming.")
            : HealthCheckResult.Unhealthy("The queue is not being consumed yet."));
}
