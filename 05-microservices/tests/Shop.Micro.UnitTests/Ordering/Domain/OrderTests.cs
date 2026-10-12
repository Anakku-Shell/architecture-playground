using Shop.Micro.Ordering.Domain;
using Shop.Micro.Ordering.Domain.Common;
using Xunit;

namespace Shop.Micro.UnitTests.Ordering.Domain;

// The order lifecycle of version 05. Two states are now visible from outside because the answers arrive
// later, through messages: Pending (waiting for Catalog) and PaymentPending (waiting for Payments).
// Guide: §8.5.
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Place_StartsPending_WithSnapshotAndTotals()
    {
        var customerId = Guid.NewGuid();

        var order = Order.Place(Guid.NewGuid(), customerId, [Line(price: 10m, quantity: 2), Line(price: 2.50m, quantity: 1)], Now);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(Now, order.PlacedAt);
        Assert.Null(order.CancellationReason);
        Assert.Equal(Money.Of(22.50m), order.Total);
        Assert.Equal(Money.Of(20m), order.Lines[0].LineTotal);
    }

    [Fact]
    public void Place_NumbersTheLinesInRequestOrder()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), [Line(quantity: 1), Line(quantity: 2)], Now);

        Assert.Equal([1, 2], order.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void Place_WithoutLines_IsInvalid() =>
        Assert.Throws<DomainValidationException>(() => Order.Place(Guid.NewGuid(), Guid.NewGuid(), [], Now));

    [Fact]
    public void Place_WithEmptyCustomer_IsInvalid() =>
        Assert.Throws<DomainValidationException>(() => Order.Place(Guid.NewGuid(), Guid.Empty, [Line()], Now));

    [Fact]
    public void Place_SameProductTwice_IsInvalid()
    {
        var productId = Guid.NewGuid();

        Assert.Throws<DomainValidationException>(() =>
            Order.Place(Guid.NewGuid(), Guid.NewGuid(), [Line(productId: productId), Line(productId: productId)], Now));
    }

    [Fact]
    public void ConfirmStockReserved_FromPending_AwaitsPayment()
    {
        var order = Pending();

        order.ConfirmStockReserved();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RejectForLackOfStock_FromPending_IsRejected()
    {
        var order = Pending();

        order.RejectForLackOfStock();

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Null(order.CancellationReason);
    }

    [Fact]
    public void RequestPayment_FromAwaitingPayment_IsPaymentPending()
    {
        var order = AwaitingPayment();

        order.RequestPayment();

        Assert.Equal(OrderStatus.PaymentPending, order.Status);
    }

    [Fact]
    public void RequestPayment_WhilePaymentPending_IsABusinessRuleViolation()
    {
        var order = PaymentPending();

        Assert.Throws<BusinessRuleViolationException>(order.RequestPayment);
        Assert.Equal(OrderStatus.PaymentPending, order.Status);
    }

    [Fact]
    public void MarkPaid_FromPaymentPending_IsPaid()
    {
        var order = PaymentPending();

        order.MarkPaid();

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void DeclinePayment_FromPaymentPending_Cancels_WithReasonPaymentDeclined()
    {
        var order = PaymentPending();

        order.DeclinePayment();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.PaymentDeclined, order.CancellationReason);
    }

    [Fact]
    public void Cancel_FromAwaitingPayment_Cancels_WithReasonCustomerCancelled()
    {
        var order = AwaitingPayment();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.CustomerCancelled, order.CancellationReason);
    }

    /// <summary>
    /// Once the payment has been asked for, the customer can no longer cancel: the money may be moving.
    /// This closes the pay-versus-cancel race of 02 and 03 without a lock spanning services.
    /// </summary>
    [Fact]
    public void Cancel_WhilePaymentPending_IsABusinessRuleViolation()
    {
        var order = PaymentPending();

        Assert.Throws<BusinessRuleViolationException>(order.Cancel);
        Assert.Equal(OrderStatus.PaymentPending, order.Status);
    }

    public static TheoryData<OrderStatus, string> InvalidMoves => new()
    {
        { OrderStatus.Pending, "pay" }, { OrderStatus.Pending, "cancel" }, { OrderStatus.Pending, "paid" },
        { OrderStatus.AwaitingPayment, "confirm" }, { OrderStatus.AwaitingPayment, "paid" }, { OrderStatus.AwaitingPayment, "declined" },
        { OrderStatus.Rejected, "pay" }, { OrderStatus.Rejected, "cancel" },
        { OrderStatus.Paid, "pay" }, { OrderStatus.Paid, "cancel" }, { OrderStatus.Paid, "paid" },
        { OrderStatus.Cancelled, "pay" }, { OrderStatus.Cancelled, "cancel" }, { OrderStatus.Cancelled, "declined" },
    };

    [Theory]
    [MemberData(nameof(InvalidMoves))]
    public void EveryMoveOutsideItsState_IsABusinessRuleViolation_AndChangesNothing(OrderStatus from, string move)
    {
        var order = InStatus(from);
        Action act = move switch
        {
            "confirm" => order.ConfirmStockReserved,
            "pay" => order.RequestPayment,
            "paid" => order.MarkPaid,
            "declined" => order.DeclinePayment,
            _ => order.Cancel,
        };

        Assert.Throws<BusinessRuleViolationException>(act);
        Assert.Equal(from, order.Status);
    }

    private static Order InStatus(OrderStatus status)
    {
        var order = Pending();
        switch (status)
        {
            case OrderStatus.Rejected:
                order.RejectForLackOfStock();
                break;
            case OrderStatus.AwaitingPayment:
                order.ConfirmStockReserved();
                break;
            case OrderStatus.Paid:
                order.ConfirmStockReserved();
                order.RequestPayment();
                order.MarkPaid();
                break;
            case OrderStatus.Cancelled:
                order.ConfirmStockReserved();
                order.Cancel();
                break;
        }

        return order;
    }

    private static Order Pending() => Order.Place(Guid.NewGuid(), Guid.NewGuid(), [Line()], Now);

    private static Order AwaitingPayment() => InStatus(OrderStatus.AwaitingPayment);

    private static Order PaymentPending()
    {
        var order = AwaitingPayment();
        order.RequestPayment();
        return order;
    }

    private static OrderLine Line(decimal price = 10m, int quantity = 1, Guid? productId = null) =>
        OrderLine.Snapshot(productId ?? Guid.NewGuid(), ProductName.Of("Mug"), Money.Of(price), Quantity.Of(quantity));
}
