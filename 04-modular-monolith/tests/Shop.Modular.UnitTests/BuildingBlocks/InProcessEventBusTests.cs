using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Events;
using Xunit;

namespace Shop.Modular.UnitTests.BuildingBlocks;

// The in-process bus is a loop over the consumers registered for an event. These tests pin the three
// properties the whole version relies on: every consumer runs, nested events run before PublishAsync
// returns, and a consumer's exception reaches the publisher. Guide: §7.3.
public sealed class InProcessEventBusTests
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task Publish_RunsEveryConsumer_InRegistrationOrder()
    {
        var bus = Bus(services => services
            .AddScoped<IIntegrationEventConsumer<Ping>>(_ => new Recorder(_log, "first"))
            .AddScoped<IIntegrationEventConsumer<Ping>>(_ => new Recorder(_log, "second")));

        await bus.PublishAsync(new Ping(), TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], _log);
    }

    [Fact]
    public async Task EventsPublishedByAConsumer_RunBeforePublishReturns()
    {
        var bus = Bus(services => services
            .AddScoped<IIntegrationEventConsumer<Ping>>(provider => new Forwarder(provider.GetRequiredService<IEventBus>(), _log))
            .AddScoped<IIntegrationEventConsumer<Pong>>(_ => new Recorder(_log, "pong")));

        await bus.PublishAsync(new Ping(), TestContext.Current.CancellationToken);
        _log.Add("returned");

        Assert.Equal(["ping", "pong", "returned"], _log);
    }

    [Fact]
    public async Task AConsumersException_ReachesThePublisher_AndStopsTheRest()
    {
        var bus = Bus(services => services
            .AddScoped<IIntegrationEventConsumer<Ping>>(_ => new Thrower())
            .AddScoped<IIntegrationEventConsumer<Ping>>(_ => new Recorder(_log, "after")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new Ping(), TestContext.Current.CancellationToken));
        Assert.Empty(_log);
    }

    [Fact]
    public async Task AnEventNobodyConsumes_IsFine() =>
        await Bus(_ => { }).PublishAsync(new Ping(), TestContext.Current.CancellationToken);

    private static IEventBus Bus(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<InProcessEventBus>>(NullLogger<InProcessEventBus>.Instance);
        services.AddScoped<IEventBus, InProcessEventBus>();
        register(services);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IEventBus>();
    }

    private sealed record Ping : IIntegrationEvent;

    private sealed record Pong : IIntegrationEvent;

    private sealed class Recorder(List<string> log, string name) : IIntegrationEventConsumer<Ping>, IIntegrationEventConsumer<Pong>
    {
        public Task ConsumeAsync(Ping integrationEvent, CancellationToken cancellationToken) => Record();

        public Task ConsumeAsync(Pong integrationEvent, CancellationToken cancellationToken) => Record();

        private Task Record()
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class Forwarder(IEventBus bus, List<string> log) : IIntegrationEventConsumer<Ping>
    {
        public async Task ConsumeAsync(Ping integrationEvent, CancellationToken cancellationToken)
        {
            log.Add("ping");
            await bus.PublishAsync(new Pong(), cancellationToken);
        }
    }

    private sealed class Thrower : IIntegrationEventConsumer<Ping>
    {
        public Task ConsumeAsync(Ping integrationEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }
}
