using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Shop.ContractTests;
using Xunit;

[assembly: AssemblyFixture(typeof(Shop.Micro.ContractTests.MicroShopApi))]

namespace Shop.Micro.ContractTests;

/// <summary>
/// Starts the whole of version 05 once for the test assembly: Aspire's testing host runs the real AppHost
/// (PostgreSQL and RabbitMQ containers, the three services, the gateway) and the tests talk only to the
/// gateway, like any client. The same contract suite as 01-04; only <see cref="IsAsynchronous"/> differs.
/// Guide: §8.3.
/// </summary>
public sealed class MicroShopApi : IShopApi, IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    public bool IsAsynchronous => true;

    /// <summary>The running system, for the 05-only tests that reach past the gateway (broker, databases).</summary>
    public DistributedApplication App => _app ?? throw new InvalidOperationException("The application has not started.");

    public HttpClient CreateClient() =>
        (_app ?? throw new InvalidOperationException("The application has not started.")).CreateHttpClient("gateway");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shop_Micro_AppHost>(timeout.Token);
        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);

        // The gateway waits for the three services, which are healthy only once their queues exist.
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

public sealed class ProductTests(MicroShopApi api) : ProductContractTests(api);

public sealed class OrderTests(MicroShopApi api) : OrderContractTests(api);

public sealed class PaymentTests(MicroShopApi api) : PaymentContractTests(api);
