using Microsoft.Extensions.Logging.Abstractions;
using Shop.Clean.Application.Common;
using Shop.Clean.Application.UseCases.Catalog;
using Shop.Clean.Application.UseCases.Ordering;
using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;
using Shop.Clean.UnitTests.Domain;
using Xunit;

namespace Shop.Clean.UnitTests.Application;

// Use cases tested through their ports with in-memory fakes: the whole "place an order" flow, including
// a concurrency retry, runs without a database. Compare with 01, where the same logic needed PostgreSQL.
public sealed class PlaceOrderTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeUnitOfWork _unitOfWork;

    public PlaceOrderTests() => _unitOfWork = new FakeUnitOfWork(_store);

    private PlaceOrder UseCase => new(new InMemoryProducts(_store), new InMemoryOrders(_store), _unitOfWork, new FixedTime(), NullLogger<PlaceOrder>.Instance);

    [Fact]
    public async Task WithStock_AwaitsPayment_ReservesStock_AndSavesOnce()
    {
        var mug = Seed(Products.Mug(price: 10m, stock: 5));

        var order = await UseCase.ExecuteAsync(Command(mug.Id, 2), CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(FixedTime.Now, order.PlacedAt);
        Assert.Equal(3, StockOf(mug));
        Assert.Same(order, _store.Orders[order.Id]);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task WithoutStock_IsRejected_AndStockUnchanged()
    {
        var mug = Seed(Products.Mug(stock: 1));

        var order = await UseCase.ExecuteAsync(Command(mug.Id, 2), CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(1, StockOf(mug));
    }

    [Fact]
    public async Task InvalidInput_ReportsEveryFieldInOneValidationException()
    {
        var mug = Seed(Products.Mug());
        var command = new PlaceOrderCommand(Guid.Empty, [new(mug.Id, 0), new(mug.Id, 1)]);

        var error = await Assert.ThrowsAsync<ValidationException>(() => UseCase.ExecuteAsync(command, CancellationToken.None));

        Assert.Contains("customerId", error.Errors.Keys);
        Assert.Contains("lines[0].quantity", error.Errors.Keys);
        Assert.Contains("lines", error.Errors.Keys);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task UnknownProduct_IsAValidationErrorOnThatLine()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => UseCase.ExecuteAsync(Command(Guid.NewGuid(), 1), CancellationToken.None));

        Assert.Contains("lines[0].productId", error.Errors.Keys);
    }

    [Fact]
    public async Task ConcurrencyConflict_IsRetried_FromFreshData_WithoutApplyingTwice()
    {
        var mug = Seed(Products.Mug(stock: 5));
        _unitOfWork.ConflictsToThrow = 1;

        var order = await UseCase.ExecuteAsync(Command(mug.Id, 1), CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(1, _unitOfWork.Discards);
        Assert.Equal(1, _unitOfWork.Saves);
        Assert.Equal(4, StockOf(mug));
        Assert.Single(_store.Orders);
    }

    [Fact]
    public async Task EndlessConflicts_GiveUp_WithAConflictException_AndChangeNothing()
    {
        var mug = Seed(Products.Mug(stock: 5));
        _unitOfWork.ConflictsToThrow = int.MaxValue;

        await Assert.ThrowsAsync<ConflictException>(() => UseCase.ExecuteAsync(Command(mug.Id, 1), CancellationToken.None));

        Assert.Equal(5, StockOf(mug));
        Assert.Empty(_store.Orders);
    }

    private Product Seed(Product product)
    {
        _store.Seed(product);
        return product;
    }

    private int StockOf(Product product) => _store.Products[product.Id].Stock;

    private static PlaceOrderCommand Command(Guid productId, int quantity) => new(Guid.NewGuid(), [new(productId, quantity)]);
}

public sealed class PayOrderTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeUnitOfWork _unitOfWork;

    public PayOrderTests() => _unitOfWork = new FakeUnitOfWork(_store);

    [Fact]
    public async Task Approved_PaysTheOrder_AndRecordsThePayment()
    {
        var (order, mug) = PlacedOrder(stock: 5, quantity: 2);

        var paid = await UseCase(new StubPaymentGateway(PaymentStatus.Approved)).ExecuteAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.Equal(PaymentStatus.Approved, Assert.Single(_store.Payments).Status);
        Assert.Equal(3, _store.Products[mug.Id].Stock);
    }

    [Fact]
    public async Task Declined_CancelsTheOrder_AndReleasesStock()
    {
        var (order, mug) = PlacedOrder(stock: 5, quantity: 2);

        var result = await UseCase(new StubPaymentGateway(PaymentStatus.Declined)).ExecuteAsync(order.Id, CancellationToken.None);

        Assert.Equal(CancellationReason.PaymentDeclined, result.CancellationReason);
        Assert.Equal(5, _store.Products[mug.Id].Stock);
    }

    [Fact]
    public async Task PayingTwice_IsABusinessRuleViolation_AndChargesOnce()
    {
        var (order, _) = PlacedOrder(stock: 5, quantity: 1);
        var gateway = new StubPaymentGateway(PaymentStatus.Approved);
        await UseCase(gateway).ExecuteAsync(order.Id, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => UseCase(gateway).ExecuteAsync(order.Id, CancellationToken.None));

        Assert.Equal(1, gateway.Charges);
        Assert.Single(_store.Payments);
    }

    [Fact]
    public async Task UnknownOrder_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            UseCase(new StubPaymentGateway(PaymentStatus.Approved)).ExecuteAsync(Guid.NewGuid(), CancellationToken.None));

    private PayOrder UseCase(StubPaymentGateway gateway) =>
        new(new InMemoryOrders(_store), new InMemoryProducts(_store), new InMemoryPayments(_store), gateway, _unitOfWork, new FixedTime(), NullLogger<PayOrder>.Instance);

    /// <summary>An order already placed and saved: its stock is reserved in the committed product.</summary>
    private (Order Order, Product Mug) PlacedOrder(int stock, int quantity)
    {
        var mug = Products.Mug(stock: stock);
        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(quantity))], FixedTime.Now);
        _store.Seed(mug);
        _store.Orders[order.Id] = order;
        return (order, mug);
    }
}

public sealed class CreateProductTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeUnitOfWork _unitOfWork;

    public CreateProductTests() => _unitOfWork = new FakeUnitOfWork(_store);

    private CreateProduct UseCase => new(new InMemoryProducts(_store), _unitOfWork, NullLogger<CreateProduct>.Instance);

    [Fact]
    public async Task ValidCommand_CreatesTheProduct()
    {
        var product = await UseCase.ExecuteAsync(new CreateProductCommand("Mug", "mug-1", 10m, 5), CancellationToken.None);

        Assert.Equal("MUG-1", product.Sku.Value);
        Assert.Same(product, _store.Products[product.Id]);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task DuplicateSku_InAnyCase_IsAConflict()
    {
        await UseCase.ExecuteAsync(new CreateProductCommand("Mug", "MUG-1", 10m, 5), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            UseCase.ExecuteAsync(new CreateProductCommand("Other", "mug-1", 10m, 5), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidValues_AreReportedPerField()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            UseCase.ExecuteAsync(new CreateProductCommand("", "", 10.999m, 1), CancellationToken.None));

        Assert.Equal(["name", "price", "sku"], error.Errors.Keys.Order(StringComparer.Ordinal));
    }
}
