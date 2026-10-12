using Shop.Modular.Ordering.Domain;
using Shop.Modular.Ordering.Domain.Common;
using Xunit;

namespace Shop.Modular.UnitTests.Ordering.Domain;

// The order lifecycle, as in version 02, with one state more: an order starts Pending until Catalog says
// whether its stock is reserved. In 04 that answer arrives within the same request, so nobody outside ever
// sees Pending; in 05 it arrives later, and Pending becomes visible. Guide: §7.5.
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

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
        Assert.Equal("Mug", order.Lines[0].ProductName.Value);
    }

    [Fact]
    public void Place_NumbersTheLinesInRequestOrder()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), [Line(quantity: 1), Line(quantity: 2)], Now);

        Assert.Equal([1, 2], order.Lines.Select(l => l.LineNumber));
        Assert.Equal([1, 2], order.Lines.Select(l => l.Quantity.Value));
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
    public void StockAnswers_OutsidePending_AreBusinessRuleViolations()
    {
        var order = AwaitingPayment();

        Assert.Throws<BusinessRuleViolationException>(order.ConfirmStockReserved);
        Assert.Throws<BusinessRuleViolationException>(order.RejectForLackOfStock);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
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

    [Fact]
    public void EnsureCanBePaid_AcceptsOnlyAwaitingPayment()
    {
        AwaitingPayment().EnsureCanBePaid();

        Assert.Throws<BusinessRuleViolationException>(Pending().EnsureCanBePaid);
    }

    public static TheoryData<string> InvalidMoves => new()
    {
        "pay pending", "cancel pending", "pay paid", "cancel paid", "pay cancelled", "cancel cancelled", "pay rejected", "cancel rejected",
    };

    [Theory]
    [MemberData(nameof(InvalidMoves))]
    public void EveryPayOrCancelOutsideAwaitingPayment_IsABusinessRuleViolation_AndChangesNothing(string move)
    {
        var order = Pending();
        if (move.EndsWith("rejected", StringComparison.Ordinal))
        {
            order.RejectForLackOfStock();
        }
        else if (!move.EndsWith("pending", StringComparison.Ordinal))
        {
            order.ConfirmStockReserved();
            if (move.EndsWith("paid", StringComparison.Ordinal))
            {
                order.MarkPaid();
            }
            else
            {
                order.Cancel();
            }
        }

        var statusBefore = order.Status;
        Action act = move.StartsWith("pay", StringComparison.Ordinal) ? order.MarkPaid : order.Cancel;

        Assert.Throws<BusinessRuleViolationException>(act);
        Assert.Throws<BusinessRuleViolationException>(order.EnsureCanBePaid);
        Assert.Equal(statusBefore, order.Status);
    }

    private static Order Pending() => Order.Place(Guid.NewGuid(), Guid.NewGuid(), [Line()], Now);

    private static Order AwaitingPayment()
    {
        var order = Pending();
        order.ConfirmStockReserved();
        return order;
    }

    private static OrderLine Line(decimal price = 10m, int quantity = 1, Guid? productId = null) =>
        OrderLine.Snapshot(productId ?? Guid.NewGuid(), ProductName.Of("Mug"), Money.Of(price), Quantity.Of(quantity));
}
