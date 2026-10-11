using System.Net;
using System.Net.Http.Json;
using Shop.ContractTests;
using Xunit;

namespace Shop.Micro.ContractTests;

/// <summary>
/// Not part of the shared contract: properties of the edges only this version has, the gateway in front of
/// the services and the HTTP call from Ordering to Catalog. Guide: §8.2 and §8.6.
/// </summary>
public sealed class GatewayTests(MicroShopApi api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The price lookup is for Ordering only: the gateway routes the public API, nothing else. Its answer is
    /// still a ProblemDetails, like every error of 01-04.
    /// </summary>
    [Fact]
    public async Task InternalEndpoints_AreNotReachableThroughTheGateway()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/internal/product-snapshots", UriKind.Relative), new[] { Guid.NewGuid() }, Ct);

        await ProblemAssert.IsProblem(response, HttpStatusCode.NotFound);
    }

    /// <summary>A path no service owns gets the same ProblemDetails 404 as in 01-04, from the gateway itself.</summary>
    [Fact]
    public async Task UnknownRoute_Returns404Problem()
    {
        using var client = api.CreateClient();

        await ProblemAssert.IsProblem(await client.GetAsync(new Uri("/api/unknown", UriKind.Relative), Ct), HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Ordering sends every product id of the order to Catalog in one call. Sent in the query string, a few
    /// hundred ids pass the 8 KB request line Kestrel accepts and the order fails with 503; in the body they fit.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_WithManyDistinctProducts_IsAccepted()
    {
        var shop = new ShopClient(api.CreateClient(), isAsynchronous: true);
        var products = new List<ProductResponse>();
        for (var i = 0; i < 250; i++)
        {
            // One at a time: 250 parallel requests would exhaust the database connections, not test the lookup.
            products.Add(await shop.CreateProduct(stock: 5));
        }

        var placed = await shop.PlaceOrder([.. products.Select(p => (p.Id, 1))]);

        Assert.Equal(250, placed.Lines.Count);
    }
}
