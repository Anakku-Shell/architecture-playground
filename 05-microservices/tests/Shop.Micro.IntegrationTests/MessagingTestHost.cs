using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RabbitMQ.Client;
using Shop.Micro.Contracts;
using Shop.Micro.Messaging;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

[assembly: AssemblyFixture(typeof(Shop.Micro.IntegrationTests.Infrastructure))]

namespace Shop.Micro.IntegrationTests;

/// <summary>One PostgreSQL and one RabbitMQ container for the whole test assembly.</summary>
public sealed class Infrastructure : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3").Build();

    public IConnection Broker { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        Broker = await new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) }.CreateConnectionAsync();
    }

    /// <summary>A connection string to a database no other test uses.</summary>
    public string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = $"test_{Guid.NewGuid():N}" }.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        await Broker.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }
}

/// <summary>A message only these tests send. <paramref name="Fail"/> makes its consumer throw.</summary>
public sealed record TestMessage(Guid Id, bool Fail = false) : IIntegrationMessage;

/// <summary>A message no queue is bound to: the broker refuses it (mandatory publish).</summary>
public sealed record UnroutedMessage(Guid Id) : IIntegrationMessage;

/// <summary>A row the consumer writes, inside the delivery's transaction, each time it handles a message.</summary>
public sealed class HandledMessage
{
    public Guid Id { get; set; }

    public Guid TestMessageId { get; set; }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<HandledMessage> Handled => Set<HandledMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddOutboxAndInbox();
}

/// <summary>How many times each message reached the consumer, committed or not.</summary>
public sealed class Attempts
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public void Add(Guid id) => _counts.AddOrUpdate(id, 1, (_, n) => n + 1);

    public int Of(Guid id) => _counts.GetValueOrDefault(id);
}

public sealed class TestConsumer(TestDbContext db, Attempts attempts) : IMessageConsumer<TestMessage>
{
    public Task ConsumeAsync(TestMessage message, CancellationToken cancellationToken)
    {
        attempts.Add(message.Id);
        if (message.Fail)
        {
            throw new InvalidOperationException("The consumer failed on purpose.");
        }

        db.Handled.Add(new HandledMessage { Id = Guid.CreateVersion7(), TestMessageId = message.Id });
        return Task.CompletedTask;
    }
}

/// <summary>
/// A small service built the way the real ones are: a DbContext with the outbox and inbox tables, and the
/// messaging building block on a broker. Each host gets its own database, exchange and queue, so tests
/// running in parallel never see each other's messages.
/// </summary>
public sealed class MessagingTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    private MessagingTestHost(IHost host, string exchange, string queue, IConnection broker)
    {
        _host = host;
        Exchange = exchange;
        Queue = queue;
        Broker = broker;
    }

    public string Exchange { get; }

    public string Queue { get; }

    public IConnection Broker { get; }

    public Attempts Attempts => _host.Services.GetRequiredService<Attempts>();

    public static async Task<MessagingTestHost> StartAsync(Infrastructure infrastructure, bool consume)
    {
        var exchange = $"test-{Guid.NewGuid():N}";
        var queue = $"test-{Guid.NewGuid():N}";
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(infrastructure.Broker);
        builder.Services.AddSingleton<Attempts>();
        var database = infrastructure.NewDatabase();
        builder.Services.AddDbContext<TestDbContext>(options => options.UseNpgsql(database));
        var messaging = builder.Services.AddMessaging<TestDbContext>(queue, options => options.Exchange = exchange);
        if (consume)
        {
            messaging.Consume<TestMessage, TestConsumer>();
        }

        var host = builder.Build();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        await host.StartAsync();
        await WaitUntil(() => Task.FromResult(host.Services.GetRequiredService<ConsumerReadiness>().IsReady), "the consumer to start");
        return new MessagingTestHost(host, exchange, queue, infrastructure.Broker);
    }

    /// <summary>Runs <paramref name="work"/> with a fresh DbContext, as one request or delivery would.</summary>
    public async Task<T> InScope<T>(Func<TestDbContext, IMessageOutbox, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TestDbContext>(), scope.ServiceProvider.GetRequiredService<IMessageOutbox>());
    }

    /// <summary>Publishes straight to the broker, bypassing any outbox: what a redelivery looks like to the consumer.</summary>
    public async Task PublishRawAsync(TestMessage message, Guid messageId)
    {
        await using var channel = await Broker.CreateChannelAsync();
        var properties = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Type = nameof(TestMessage),
            ContentType = "application/json",
            Persistent = true,
        };
        await channel.BasicPublishAsync(Exchange, nameof(TestMessage), mandatory: true, properties, JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web));
    }

    public static async Task WaitUntil(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting for {what}.");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
