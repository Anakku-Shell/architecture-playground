using System.Net;
using Xunit;

namespace Shop.ContractTests;

/// <summary>Ordering endpoints: placing, paying and cancelling orders, and the stock they reserve.</summary>
public abstract class OrderContractTests(IShopApi api) : ContractTests(api)
{
    public static TheoryData<string> InvalidOrderCases => new(InvalidOrders.Keys);

    // Each case gets a real product id, so the 400 is caused by the rule under test and nothing else.
    private static readonly Dictionary<string, Func<Guid, object>> InvalidOrders = new()
    {
        ["no lines"] = _ => new { customerId = Guid.NewGuid(), lines = Array.Empty<object>() },
        ["quantity 0"] = id => new { customerId = Guid.NewGuid(), lines = new[] { new { productId = id, quantity = 0 } } },
        ["quantity 1001"] = id => new { customerId = Guid.NewGuid(), lines = new[] { new { productId = id, quantity = 1001 } } },
        ["empty customer id"] = id => new { customerId = Guid.Empty, lines = new[] { new { productId = id, quantity = 1 } } },
        ["same product twice"] = id => new
        {
            customerId = Guid.NewGuid(),
            lines = new[] { new { productId = id, quantity = 1 }, new { productId = id, quantity = 2 } },
        },
    };

    [Fact]
    public async Task PlaceOrder_WithStock_EndsAwaitingPayment_AndReservesStock()
    {
        var product = await Shop.CreateProduct(name: "Mug", price: 10.00m, stock: 5);
        var customerId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        var response = await Shop.PlaceOrderRaw(new { customerId, lines = new[] { new { productId = product.Id, quantity = 2 } } });

        await Shop.AssertCompletedOrAccepted(response, HttpStatusCode.Created);
        var placed = await ShopClient.ReadSuccess<OrderResponse>(response);
        Assert.EndsWith($"/api/orders/{placed.Id}", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Shop.IsAsynchronous ? OrderStatus.Pending : OrderStatus.AwaitingPayment, placed.Status);

        var order = await Shop.WaitForSettled(placed.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(customerId, order.CustomerId);
        Assert.InRange(order.PlacedAt, before, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Null(order.CancellationReason);
        Assert.Equal(20.00m, order.Total);
        var line = Assert.Single(order.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal("Mug", line.ProductName);
        Assert.Equal(10.00m, line.UnitPrice);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(20.00m, line.LineTotal);
        Assert.Equal(3, (await Shop.GetProduct(product.Id)).Stock);
    }

    [Fact]
    public async Task PlaceOrder_WithoutEnoughStock_EndsRejected_AndStockUnchanged()
    {
        var product = await Shop.CreateProduct(stock: 1);

        var placed = await Shop.PlaceOrder((product.Id, 2));

        Assert.Equal(OrderStatus.Rejected, (await Shop.WaitForSettled(placed.Id)).Status);
        Assert.Equal(1, (await Shop.GetProduct(product.Id)).Stock);
    }

    [Theory]
    [MemberData(nameof(InvalidOrderCases))]
    public async Task PlaceOrder_WithInvalidBody_Returns400(string invalidCase)
    {
        var product = await Shop.CreateProduct();

        var response = await Shop.PlaceOrderRaw(InvalidOrders[invalidCase](product.Id));

        await ProblemAssert.IsValidationProblem(response);
    }

    [Fact]
    public async Task PlaceOrder_WithUnknownProduct_Returns400()
    {
        var body = new { customerId = Guid.NewGuid(), lines = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } };

        await ProblemAssert.IsProblem(await Shop.PlaceOrderRaw(body), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PriceChangeAfterPlacement_DoesNotChangeOrder()
    {
        var product = await Shop.CreateProduct(price: 10.00m, stock: 5);
        var placed = await Shop.PlaceOrder((product.Id, 1));
        await Shop.WaitForSettled(placed.Id);

        await ShopClient.ReadSuccess<ProductResponse>(await Shop.ChangePriceRaw(product.Id, 99.00m));

        var order = await Shop.GetOrder(placed.Id);
        Assert.Equal(10.00m, Assert.Single(order.Lines).UnitPrice);
        Assert.Equal(10.00m, order.Total);
    }

    [Fact]
    public async Task ConcurrentOrdersForLastUnits_NeverOversell()
    {
        // Ten customers race for the last two units. Two requests rarely overlap enough to expose a
        // read-check-write bug; ten usually do. Exactly two orders may win, and stock must end at 0.
        const int customers = 10;
        const int units = 2;
        var product = await Shop.CreateProduct(stock: units);

        var placed = await Task.WhenAll(Enumerable.Range(0, customers).Select(_ => Shop.PlaceOrder((product.Id, 1))));

        var statuses = await Task.WhenAll(placed.Select(async o => (await Shop.WaitForSettled(o.Id)).Status));
        Assert.Equal(units, statuses.Count(s => s == OrderStatus.AwaitingPayment));
        Assert.Equal(customers - units, statuses.Count(s => s == OrderStatus.Rejected));
        Assert.Equal(0, (await Shop.GetProduct(product.Id)).Stock);
    }

    [Fact]
    public async Task CancelAwaitingPayment_Cancels_AndReleasesStock()
    {
        var product = await Shop.CreateProduct(stock: 5);
        var placed = await Shop.PlaceOrder((product.Id, 2));
        await Shop.WaitForSettled(placed.Id);

        var response = await Shop.CancelRaw(placed.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await ShopClient.ReadSuccess<OrderResponse>(response);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("CustomerCancelled", order.CancellationReason);
        await Shop.WaitForProduct(product.Id, p => p.Stock == 5);
    }

    [Fact]
    public async Task CancellingPaidOrder_Returns409()
    {
        var product = await Shop.CreateProduct(price: 10.00m);
        var placed = await Shop.PlaceOrder((product.Id, 1));
        await Shop.WaitForSettled(placed.Id);
        Assert.Equal(OrderStatus.Paid, (await Shop.Pay(placed.Id)).Status);

        await ProblemAssert.IsProblem(await Shop.CancelRaw(placed.Id), HttpStatusCode.Conflict);

        Assert.Equal(OrderStatus.Paid, (await Shop.GetOrder(placed.Id)).Status);
    }

    [Fact]
    public async Task CancellingRejectedOrder_Returns409()
    {
        var product = await Shop.CreateProduct(stock: 0);
        var placed = await Shop.PlaceOrder((product.Id, 1));
        Assert.Equal(OrderStatus.Rejected, (await Shop.WaitForSettled(placed.Id)).Status);

        await ProblemAssert.IsProblem(await Shop.CancelRaw(placed.Id), HttpStatusCode.Conflict);

        Assert.Equal(OrderStatus.Rejected, (await Shop.GetOrder(placed.Id)).Status);
    }

    [Fact]
    public async Task CancellingTwice_Returns409_AndStockReleasedOnce()
    {
        var product = await Shop.CreateProduct(stock: 5);
        var placed = await Shop.PlaceOrder((product.Id, 2));
        await Shop.WaitForSettled(placed.Id);
        await ShopClient.ReadSuccess<OrderResponse>(await Shop.CancelRaw(placed.Id));

        await ProblemAssert.IsProblem(await Shop.CancelRaw(placed.Id), HttpStatusCode.Conflict);

        await Shop.WaitForProduct(product.Id, p => p.Stock == 5);
        Assert.Equal(5, (await Shop.GetProduct(product.Id)).Stock);
    }

    [Fact]
    public async Task PayingTwice_Returns409()
    {
        var product = await Shop.CreateProduct(price: 10.00m);
        var placed = await Shop.PlaceOrder((product.Id, 1));
        await Shop.WaitForSettled(placed.Id);
        Assert.Equal(OrderStatus.Paid, (await Shop.Pay(placed.Id)).Status);

        await ProblemAssert.IsProblem(await Shop.PayRaw(placed.Id), HttpStatusCode.Conflict);

        Assert.Equal(OrderStatus.Paid, (await Shop.GetOrder(placed.Id)).Status);
        Assert.Equal(10.00m, (await Shop.GetPayment(placed.Id)).Amount);
    }

    [Fact]
    public async Task PayingRejectedOrder_Returns409()
    {
        var product = await Shop.CreateProduct(stock: 0);
        var placed = await Shop.PlaceOrder((product.Id, 1));
        Assert.Equal(OrderStatus.Rejected, (await Shop.WaitForSettled(placed.Id)).Status);

        await ProblemAssert.IsProblem(await Shop.PayRaw(placed.Id), HttpStatusCode.Conflict);

        Assert.Equal(OrderStatus.Rejected, (await Shop.GetOrder(placed.Id)).Status);
        await ProblemAssert.IsProblem(await Shop.GetPaymentRaw(placed.Id), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PayingCancelledOrder_Returns409()
    {
        var product = await Shop.CreateProduct();
        var placed = await Shop.PlaceOrder((product.Id, 1));
        await Shop.WaitForSettled(placed.Id);
        await ShopClient.ReadSuccess<OrderResponse>(await Shop.CancelRaw(placed.Id));

        await ProblemAssert.IsProblem(await Shop.PayRaw(placed.Id), HttpStatusCode.Conflict);

        Assert.Equal(OrderStatus.Cancelled, (await Shop.GetOrder(placed.Id)).Status);
        await ProblemAssert.IsProblem(await Shop.GetPaymentRaw(placed.Id), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrder_Unknown_Returns404()
    {
        await ProblemAssert.IsProblem(await Shop.GetOrderRaw(Guid.NewGuid()), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PayUnknownOrder_Returns404()
    {
        await ProblemAssert.IsProblem(await Shop.PayRaw(Guid.NewGuid()), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelUnknownOrder_Returns404()
    {
        await ProblemAssert.IsProblem(await Shop.CancelRaw(Guid.NewGuid()), HttpStatusCode.NotFound);
    }
}
