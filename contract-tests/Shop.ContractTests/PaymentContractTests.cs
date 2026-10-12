using System.Net;
using Xunit;

namespace Shop.ContractTests;

/// <summary>Payments: the fake gateway's 1000.00 limit and what a declined payment does to the order.</summary>
public abstract class PaymentContractTests(IShopApi api) : ContractTests(api)
{
    [Fact]
    public async Task TotalOfExactly1000_IsApproved()
    {
        var product = await Shop.CreateProduct(price: 500.00m, stock: 5);
        var placed = await Shop.PlaceOrder((product.Id, 2));
        await Shop.WaitForSettled(placed.Id);

        var order = await Shop.Pay(placed.Id);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.CancellationReason);
        var payment = await Shop.GetPayment(placed.Id);
        Assert.Equal(placed.Id, payment.OrderId);
        Assert.Equal(1000.00m, payment.Amount);
        Assert.Equal("Approved", payment.Status);
        Assert.Equal(3, (await Shop.GetProduct(product.Id)).Stock);
    }

    [Fact]
    public async Task TotalAbove1000_IsDeclined_AndStockIsReleased()
    {
        var product = await Shop.CreateProduct(price: 1000.01m, stock: 3);
        var placed = await Shop.PlaceOrder((product.Id, 1));
        await Shop.WaitForSettled(placed.Id);

        var order = await Shop.Pay(placed.Id);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("PaymentDeclined", order.CancellationReason);
        var payment = await Shop.GetPayment(placed.Id);
        Assert.Equal(1000.01m, payment.Amount);
        Assert.Equal("Declined", payment.Status);
        await Shop.WaitForProduct(product.Id, p => p.Stock == 3);
    }

    [Fact]
    public async Task GetPayment_WithoutOrderId_Returns400()
    {
        await ProblemAssert.IsProblem(await Shop.GetPaymentRaw(orderId: null), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPayment_ForOrderWithoutPayment_Returns404()
    {
        var product = await Shop.CreateProduct();
        var placed = await Shop.PlaceOrder((product.Id, 1));
        await Shop.WaitForSettled(placed.Id);

        await ProblemAssert.IsProblem(await Shop.GetPaymentRaw(placed.Id), HttpStatusCode.NotFound);
    }
}
