using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Ordering.Application.Common;
using Shop.Micro.Ordering.Domain;
using Xunit;

namespace Shop.Micro.UnitTests.Ordering.Application;

/// <summary>
/// Every transition of the orchestrated saga, and its compensation. The saga's state is the order's status:
/// each reply moves it one step and may send the next command. A reply that does not fit the current state
/// (a late or repeated one) changes nothing and sends nothing. Guide: §8.5.
/// </summary>
public sealed class OrderSagaTests
{
    private readonly OrderingHarness _ordering = new();

    [Fact]
    public async Task StockReserved_MovesPendingToAwaitingPayment()
    {
        var order = _ordering.Seed(OrderStatus.Pending);

        await _ordering.Saga.HandleAsync(new StockReserved(order.Id), OrderingHarness.Ct);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Empty(_ordering.Messages.Sent);
        Assert.Equal(1, _ordering.UnitOfWork.Saves);
    }

    [Fact]
    public async Task StockReservationFailed_RejectsPending_WithoutCompensation()
    {
        var order = _ordering.Seed(OrderStatus.Pending);

        await _ordering.Saga.HandleAsync(new StockReservationFailed(order.Id), OrderingHarness.Ct);

        // Catalog took nothing, so there is nothing to give back.
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Empty(_ordering.Messages.Sent);
    }

    [Fact]
    public async Task PaymentSucceeded_MarksPaymentPendingPaid()
    {
        var order = _ordering.Seed(OrderStatus.PaymentPending);

        await _ordering.Saga.HandleAsync(new PaymentSucceeded(order.Id, Guid.NewGuid()), OrderingHarness.Ct);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Empty(_ordering.Messages.Sent);
    }

    [Fact]
    public async Task PaymentDeclined_CancelsTheOrder_AndCompensatesByReleasingTheStock()
    {
        var order = _ordering.Seed(OrderStatus.PaymentPending);

        await _ordering.Saga.HandleAsync(new PaymentDeclined(order.Id, Guid.NewGuid()), OrderingHarness.Ct);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.PaymentDeclined, order.CancellationReason);
        var release = Assert.IsType<ReleaseStock>(Assert.Single(_ordering.Messages.Sent));
        Assert.Equal(order.Id, release.OrderId);
        Assert.Equal(OrderingHarness.ItemsOf(order), release.Items);
    }

    public static TheoryData<OrderStatus, string> RepliesThatDoNotFit => new()
    {
        { OrderStatus.AwaitingPayment, nameof(StockReserved) },
        { OrderStatus.Rejected, nameof(StockReserved) },
        { OrderStatus.AwaitingPayment, nameof(StockReservationFailed) },
        { OrderStatus.Paid, nameof(PaymentSucceeded) },
        { OrderStatus.AwaitingPayment, nameof(PaymentSucceeded) },
        { OrderStatus.Cancelled, nameof(PaymentDeclined) },
        { OrderStatus.Paid, nameof(PaymentDeclined) },
    };

    [Theory]
    [MemberData(nameof(RepliesThatDoNotFit))]
    public async Task AReplyThatDoesNotFitTheState_IsIgnored(OrderStatus status, string reply)
    {
        var order = _ordering.Seed(status);

        await (reply switch
        {
            nameof(StockReserved) => _ordering.Saga.HandleAsync(new StockReserved(order.Id), OrderingHarness.Ct),
            nameof(StockReservationFailed) => _ordering.Saga.HandleAsync(new StockReservationFailed(order.Id), OrderingHarness.Ct),
            nameof(PaymentSucceeded) => _ordering.Saga.HandleAsync(new PaymentSucceeded(order.Id, Guid.NewGuid()), OrderingHarness.Ct),
            _ => _ordering.Saga.HandleAsync(new PaymentDeclined(order.Id, Guid.NewGuid()), OrderingHarness.Ct),
        });

        Assert.Equal(status, order.Status);
        Assert.Empty(_ordering.Messages.Sent);
        Assert.Equal(0, _ordering.UnitOfWork.Saves);
    }

    [Fact]
    public async Task AReplyForAnUnknownOrder_Fails_SoTheMessageEndsInTheDeadLetterQueue() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _ordering.Saga.HandleAsync(new StockReserved(Guid.NewGuid()), OrderingHarness.Ct));
}
