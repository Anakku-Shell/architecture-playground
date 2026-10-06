using Shop.Micro.Contracts.Catalog;
using Shop.Micro.Contracts.Payments;
using Shop.Micro.Messaging;
using Shop.Micro.Ordering.Application.Sagas;

namespace Shop.Micro.Ordering.Infrastructure.Messaging;

// Driving adapters for messages, as OrderEndpoints is for HTTP: they receive the reply from the broker and
// hand it to the application's saga. The messaging building block has already opened the transaction and
// recorded the message in the inbox. Guide: §8.2.

internal sealed class StockReservedConsumer(OrderSaga saga) : IMessageConsumer<StockReserved>
{
    public Task ConsumeAsync(StockReserved message, CancellationToken cancellationToken) => saga.HandleAsync(message, cancellationToken);
}

internal sealed class StockReservationFailedConsumer(OrderSaga saga) : IMessageConsumer<StockReservationFailed>
{
    public Task ConsumeAsync(StockReservationFailed message, CancellationToken cancellationToken) => saga.HandleAsync(message, cancellationToken);
}

internal sealed class PaymentSucceededConsumer(OrderSaga saga) : IMessageConsumer<PaymentSucceeded>
{
    public Task ConsumeAsync(PaymentSucceeded message, CancellationToken cancellationToken) => saga.HandleAsync(message, cancellationToken);
}

internal sealed class PaymentDeclinedConsumer(OrderSaga saga) : IMessageConsumer<PaymentDeclined>
{
    public Task ConsumeAsync(PaymentDeclined message, CancellationToken cancellationToken) => saga.HandleAsync(message, cancellationToken);
}
