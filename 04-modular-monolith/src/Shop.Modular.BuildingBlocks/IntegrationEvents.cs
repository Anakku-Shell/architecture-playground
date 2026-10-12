namespace Shop.Modular.BuildingBlocks;

// The way modules tell each other that something happened. Guide: §7.3.

/// <summary>
/// Marks a fact one module publishes for the others ("order placed", "stock reserved"). It is part of the
/// publisher's public contract, so it lives in its <c>*.Contracts</c> project and carries only ids and
/// plain values, never a module's internal types. An <b>integration event</b>, as opposed to a domain event
/// that stays inside one module (Guide §3.9).
/// </summary>
public interface IIntegrationEvent;

/// <summary>Reacts to one kind of integration event (a <b>consumer</b>). A module registers one per event it cares about.</summary>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task ConsumeAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Publishes an integration event to every module that handles it. The publisher does not know who
/// listens: that is the decoupling. In this version the bus is in-process and synchronous (see
/// <c>InProcessEventBus</c>); in 05 the same interface sits on top of RabbitMQ.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}
