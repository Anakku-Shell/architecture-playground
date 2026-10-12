using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shop.ContractTests;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(Shop.Modular.ContractTests.ModularShopApi))]

namespace Shop.Modular.ContractTests;

/// <summary>
/// Starts the modular monolith once for the whole test assembly: a throwaway PostgreSQL container
/// (Testcontainers) and the real <c>Program</c> in memory (WebApplicationFactory). The environment is
/// Development, so the API applies its migrations at startup exactly as it does on a laptop.
/// Guide: §2.8.
/// </summary>
public sealed class ModularShopApi : WebApplicationFactory<Program>, IShopApi, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17").Build();

    public bool IsAsynchronous => false;

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        // Build the server now, so migrations run once before the first test instead of inside it.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Shop", _database.GetConnectionString());

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

public sealed class ProductTests(ModularShopApi api) : ProductContractTests(api);

public sealed class OrderTests(ModularShopApi api) : OrderContractTests(api);

public sealed class PaymentTests(ModularShopApi api) : PaymentContractTests(api);
