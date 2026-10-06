using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shop.Micro.Contracts;

namespace Shop.Micro.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Plugs a service into the broker: an outbox and a dispatcher on <typeparamref name="TContext"/>, and a
    /// consumer host for <paramref name="queueName"/>. The service registers the RabbitMQ <c>IConnection</c>
    /// itself (Aspire's <c>AddRabbitMQClient</c> in the services, a test container in the tests).
    /// </summary>
    public static MessagingBuilder AddMessaging<TContext>(this IServiceCollection services, string queueName, Action<MessagingOptions>? configure = null)
        where TContext : DbContext
    {
        var options = new MessagingOptions { QueueName = queueName };
        configure?.Invoke(options);
        var registry = new MessageTypeRegistry();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton(registry);
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<OutboxSignalInterceptor>();
        services.AddSingleton<ConsumerReadiness>();

        // Added to the service's own DbContext options, wherever they are configured.
        services.ConfigureDbContext<TContext>((provider, dbOptions) => dbOptions.AddInterceptors(provider.GetRequiredService<OutboxSignalInterceptor>()));

        services.AddScoped<IMessageOutbox, EfMessageOutbox<TContext>>();
        services.AddHostedService<OutboxDispatcher<TContext>>();
        services.AddHostedService<RabbitMqConsumerHost<TContext>>();
        services.AddHealthChecks().AddCheck<MessagingHealthCheck>("messaging");
        return new MessagingBuilder(services, registry);
    }
}

/// <summary>Declares which messages the service consumes, and with which consumer.</summary>
public sealed class MessagingBuilder
{
    private readonly IServiceCollection _services;
    private readonly MessageTypeRegistry _registry;

    internal MessagingBuilder(IServiceCollection services, MessageTypeRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    public MessagingBuilder Consume<TMessage, TConsumer>()
        where TMessage : class, IIntegrationMessage
        where TConsumer : class, IMessageConsumer<TMessage>
    {
        _registry.Add<TMessage>();
        _services.AddScoped<IMessageConsumer<TMessage>, TConsumer>();
        return this;
    }
}

internal static partial class MessagingLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Published {Count} outbox message(s)")]
    public static partial void Dispatched(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish the outbox; the messages stay unsent and will be retried")]
    public static partial void DispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The broker refused {Type} {MessageId} (attempt {Attempt}); the other outbox messages go on")]
    public static partial void PublishRefused(ILogger logger, Exception exception, string type, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Type} {MessageId}")]
    public static partial void Handled(ILogger logger, string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {Type} {MessageId}: already handled (duplicate delivery)")]
    public static partial void Duplicate(ILogger logger, string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling {Type} {MessageId} failed (attempt {Attempt}); it will be tried again")]
    public static partial void Failed(ILogger logger, Exception exception, string type, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {Type} {MessageId} failed {Attempt} times; moved to the dead-letter queue")]
    public static partial void DeadLettered(ILogger logger, Exception exception, string type, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not schedule the retry of {Type} {MessageId}; it goes back to the queue")]
    public static partial void RetryFailed(ILogger logger, Exception exception, string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rejected an unreadable message (type {Type}, id {MessageId}) to the dead-letter queue")]
    public static partial void Unreadable(ILogger logger, string? type, string? messageId);
}
