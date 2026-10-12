using Shop.Clean.Domain.Catalog;
using Shop.Clean.Domain.Common;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Domain.Payments;
using Xunit;

namespace Shop.Clean.UnitTests.Domain;

// The order lifecycle is a state machine owned by the Order aggregate. In version 01 these rules were
// if-statements in OrderService and needed a database to test; here each one is a few lines. Guide: §5.5.
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Place_StartsAwaitingPayment_WithSnapshotAndTotals()
    {
        var mug = Products.Mug(price: 10m);
        var pen = Products.Mug(price: 2.50m);
        var customerId = Guid.NewGuid();

        var order = Order.Place(Guid.NewGuid(), customerId, [OrderLine.Snapshot(mug, Quantity.Of(2)), OrderLine.Snapshot(pen, Quantity.Of(1))], Now);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(Now, order.PlacedAt);
        Assert.Null(order.CancellationReason);
        Assert.Equal(Money.Of(22.50m), order.Total);
        Assert.Equal(Money.Of(20m), order.Lines[0].LineTotal);
        Assert.Equal("Mug", order.Lines[0].ProductName.Value);
    }

    [Fact]
    public void Place_NumbersTheLinesInRequestOrder()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(Products.Mug(), Quantity.Of(1)), OrderLine.Snapshot(Products.Mug(), Quantity.Of(2))], Now);

        Assert.Equal([1, 2], order.Lines.Select(l => l.LineNumber));
        Assert.Equal([1, 2], order.Lines.Select(l => l.Quantity.Value));
    }

    [Fact]
    public void Snapshot_DoesNotFollowLaterPriceChanges()
    {
        var mug = Products.Mug(price: 10m);
        var line = OrderLine.Snapshot(mug, Quantity.Of(1));

        mug.ChangePrice(Money.Of(99m));

        Assert.Equal(Money.Of(10m), line.UnitPrice);
    }

    [Fact]
    public void Reject_IsRejected() =>
        Assert.Equal(OrderStatus.Rejected, Order.Reject(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(Products.Mug(), Quantity.Of(1))], Now).Status);

    [Fact]
    public void Place_WithoutLines_IsInvalid() =>
        Assert.Throws<DomainValidationException>(() => Order.Place(Guid.NewGuid(), Guid.NewGuid(), [], Now));

    [Fact]
    public void Place_WithEmptyCustomer_IsInvalid() =>
        Assert.Throws<DomainValidationException>(() => Order.Place(Guid.NewGuid(), Guid.Empty, [OrderLine.Snapshot(Products.Mug(), Quantity.Of(1))], Now));

    [Fact]
    public void Place_SameProductTwice_IsInvalid()
    {
        var mug = Products.Mug();

        Assert.Throws<DomainValidationException>(() =>
            Order.Place(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(mug, Quantity.Of(1)), OrderLine.Snapshot(mug, Quantity.Of(2))], Now));
    }

    [Fact]
    public void MarkPaid_FromAwaitingPayment_IsPaid()
    {
        var order = AwaitingPayment();

        order.MarkPaid();

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void DeclinePayment_Cancels_WithReasonPaymentDeclined()
    {
        var order = AwaitingPayment();

        order.DeclinePayment();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.PaymentDeclined, order.CancellationReason);
    }

    [Fact]
    public void Cancel_Cancels_WithReasonCustomerCancelled()
    {
        var order = AwaitingPayment();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.CustomerCancelled, order.CancellationReason);
    }

    public static TheoryData<string> InvalidMoves => new() { "pay paid", "cancel paid", "pay cancelled", "cancel cancelled", "pay rejected", "cancel rejected" };

    [Theory]
    [MemberData(nameof(InvalidMoves))]
    public void EveryMoveOutsideAwaitingPayment_IsABusinessRuleViolation_AndChangesNothing(string move)
    {
        var order = move.EndsWith("rejected", StringComparison.Ordinal)
            ? Order.Reject(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(Products.Mug(), Quantity.Of(1))], Now)
            : AwaitingPayment();
        if (move.EndsWith("paid", StringComparison.Ordinal))
        {
            order.MarkPaid();
        }
        else if (move.EndsWith("cancelled", StringComparison.Ordinal))
        {
            order.Cancel();
        }

        var statusBefore = order.Status;
        Action act = move.StartsWith("pay", StringComparison.Ordinal) ? order.MarkPaid : order.Cancel;

        Assert.Throws<BusinessRuleViolationException>(act);
        Assert.Equal(statusBefore, order.Status);
    }

    private static Order AwaitingPayment() =>
        Order.Place(Guid.NewGuid(), Guid.NewGuid(), [OrderLine.Snapshot(Products.Mug(), Quantity.Of(1))], Now);
}

public sealed class OrderFulfillmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Place_WithStockForEveryLine_ReservesAll_AndAwaitsPayment()
    {
        var mug = Products.Mug(stock: 5);
        var pen = Products.Mug(stock: 5);

        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(2)), (pen, Quantity.Of(5))], Now);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(3, mug.Stock);
        Assert.Equal(0, pen.Stock);
    }

    [Fact]
    public void Place_WithoutStockForOneLine_ReservesNothing_AndIsRejected()
    {
        var mug = Products.Mug(stock: 5);
        var pen = Products.Mug(stock: 1);

        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(2)), (pen, Quantity.Of(2))], Now);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(5, mug.Stock);
        Assert.Equal(1, pen.Stock);
    }

    [Fact]
    public void Cancel_ReleasesStock()
    {
        var mug = Products.Mug(stock: 5);
        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(2))], Now);

        OrderFulfillment.Cancel(order, Catalog(mug));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(5, mug.Stock);
    }

    [Fact]
    public void DeclinedPayment_CancelsAndReleasesStock()
    {
        var mug = Products.Mug(stock: 5);
        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(2))], Now);

        OrderFulfillment.RecordPayment(order, PaymentStatus.Declined, Catalog(mug));

        Assert.Equal(CancellationReason.PaymentDeclined, order.CancellationReason);
        Assert.Equal(5, mug.Stock);
    }

    [Fact]
    public void ApprovedPayment_PaysAndKeepsStockReserved()
    {
        var mug = Products.Mug(stock: 5);
        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(2))], Now);

        OrderFulfillment.RecordPayment(order, PaymentStatus.Approved, Catalog(mug));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(3, mug.Stock);
    }

    [Fact]
    public void CancellingTwice_ReleasesStockOnce()
    {
        var mug = Products.Mug(stock: 5);
        var order = OrderFulfillment.Place(Guid.NewGuid(), Guid.NewGuid(), [(mug, Quantity.Of(2))], Now);
        OrderFulfillment.Cancel(order, Catalog(mug));

        Assert.Throws<BusinessRuleViolationException>(() => OrderFulfillment.Cancel(order, Catalog(mug)));
        Assert.Equal(5, mug.Stock);
    }

    private static Dictionary<Guid, Product> Catalog(params Product[] products) => products.ToDictionary(p => p.Id);
}
