using System.Net;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Shop.ContractTests;
using Xunit;

namespace Shop.Micro.ContractTests;

/// <summary>Tests that stop a service: they run alone, after every other test, so no other test sees the outage.</summary>
[CollectionDefinition(nameof(StopsAService), DisableParallelization = true)]
public sealed class StopsAService;

/// <summary>
/// Not part of the shared contract: what happens when a service is down, something a monolith cannot show.
/// Guide: §8.6, "Partial failure", and ADR 0006.
/// </summary>
[Collection(nameof(StopsAService))]
public sealed class PartialFailureTests(MicroShopApi api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Placing an order needs Catalog's prices synchronously. With Catalog stopped, Ordering's client retries
    /// until its timeout and the order is refused with <c>503</c>, instead of hanging or failing with <c>500</c>.
    /// Once Catalog is back, orders are placed again.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_WhileCatalogIsDown_Returns503_ThenWorksAgain()
    {
        var shop = new ShopClient(api.CreateClient(), isAsynchronous: true);
        var product = await shop.CreateProduct(stock: 5);
        var body = new { customerId = Guid.NewGuid(), lines = new[] { new { productId = product.Id, quantity = 1 } } };

        await RunCommandAsync(KnownResourceCommands.StopCommand);
        try
        {
            await api.App.ResourceNotifications.WaitForResourceAsync("catalog", [KnownResourceStates.Exited, KnownResourceStates.Finished], Ct);

            await ProblemAssert.IsProblem(await shop.PlaceOrderRaw(body), HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await RunCommandAsync(KnownResourceCommands.StartCommand);
            await api.App.ResourceNotifications.WaitForResourceHealthyAsync("catalog", Ct);
        }

        var placed = await shop.PlaceOrder((product.Id, 1));
        Assert.Equal(OrderStatus.AwaitingPayment, (await shop.WaitForSettled(placed.Id)).Status);
    }

    private async Task RunCommandAsync(string command)
    {
        var catalog = api.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == "catalog");
        var result = await api.App.ResourceCommands.ExecuteCommandAsync(catalog, command, Ct);
        Assert.True(result.Success, $"{command} on catalog failed: {result.Message}");
    }
}
