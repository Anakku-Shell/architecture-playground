# 0004. In-process integration events in one shared transaction

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

Placing, paying and cancelling an order involve two or three modules. In 02 one domain service changed an `Order` and several `Product`s in one transaction. Modules may not touch each other's aggregates, so they must exchange messages. In one process and one database, the messages can be method calls, and the work can still be atomic.

## Decision

- Modules publish **integration events** declared in their contracts (`OrderPlaced`, `StockReserved`, `StockReservationFailed`, `PaymentRequested`, `PaymentSucceeded`, `PaymentDeclined`, `OrderCancelled`) through `IEventBus`.
- The bus is **in-process and synchronous** (`InProcessEventBus`): it resolves the consumers registered in the request's services and awaits them in order. Nested publishes complete before `PublishAsync` returns; a consumer's exception reaches the publisher.
- **One transaction for all modules.** One `DbConnection` per request is shared by every module's DbContext; `SharedTransaction` begins a transaction on it, enlists every DbContext and commits at the end. Ordering starts it, through its `IUnitOfWork` port, for every flow it orchestrates.
- **Pessimistic locking instead of optimistic retries.** Catalog locks an order's products (`SELECT … FOR UPDATE`, in id order) before reserving; Ordering locks the order row before paying or cancelling. Retrying a whole multi-module conversation would be costly and would repeat side effects such as charging.
- Ordering's use cases fail loudly if nobody answers their question (`Pending` or `AwaitingPayment` left after publishing), and Catalog refuses to reserve outside a transaction.

## Consequences

- Good: an order and its stock are committed together or not at all; no outbox, inbox or saga is needed.
- Good: the second of two concurrent pays (or a pay and a cancel) waits for the first and is refused before any money is requested, closing the pay-versus-cancel race of versions 02 and 03.
- Good: the code is shaped for a broker: events are message-shaped records and consumers are classes (version 05 reuses the design).
- Bad: modules are coupled in time and failure: a slow consumer slows the publisher, a failing one fails it, and locks are held across the whole conversation, including the payment call.
- Bad: an external call (the payment charge) runs inside the transaction but is not part of it: if the commit fails after an approved charge, the money has moved and no row records it (guide §7.5). Only a refund or a reconciliation recovers it.
- Bad: moving any module out of process breaks the shared transaction and brings the dual write problem (guide §7.3).
