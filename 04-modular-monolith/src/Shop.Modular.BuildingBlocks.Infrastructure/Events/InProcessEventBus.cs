using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Shop.Modular.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// An event bus that is just a method call. <see cref="PublishAsync{TEvent}"/> finds every consumer registered
/// for the event in the current request's services and awaits them one by one, so:
/// <list type="bullet">
/// <item>consumers run in the publisher's request, scope and database transaction;</item>
/// <item>when <c>PublishAsync</c> returns, every module has reacted (a consumer may publish further events, which also run before it returns);</item>
/// <item>a consumer that throws fails the publisher, and the shared transaction rolls everything back.</item>
/// </list>
/// The modules are decoupled in <i>code</i> (the publisher does not know who listens) but not in <i>time</i>
/// or <i>failure</i>. Version 05 puts RabbitMQ behind the same idea and pays for the rest with an outbox, an
/// inbox and a saga. Guide: §7.3.
/// </summary>
public sealed partial class InProcessEventBus(IServiceProvider services, ILogger<InProcessEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var consumers = services.GetServices<IIntegrationEventConsumer<TEvent>>().ToList();
        LogPublishing(logger, typeof(TEvent).Name, consumers.Count);
        foreach (var consumer in consumers)
        {
            await consumer.ConsumeAsync(integrationEvent, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing {Event} to {ConsumerCount} consumer(s)")]
    private static partial void LogPublishing(ILogger logger, string @event, int consumerCount);
}
