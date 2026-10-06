# 0003. An orchestrated saga, owned by Ordering

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

Placing and paying an order involve three services and three databases. No transaction can cover them, and a declined payment must undo a stock reservation that another service has already committed. Version 04 coordinated the modules by choreography: each reacted to the others' events inside one transaction.

## Decision

- Ordering is the **orchestrator**. `OrderSaga` (in `Ordering.Application`) handles every reply and decides the next step; use cases start the saga (`PlaceOrder`, `PayOrder`) or end it (`CancelOrder`).
- Messages are **commands** with imperative names, owned by their receiver (`ReserveStock`, `ReleaseStock` in Catalog's contracts; `ProcessPayment` in Payments'), and **reply events** (`StockReserved`, `StockReservationFailed`, `PaymentSucceeded`, `PaymentDeclined`).
- The **saga state is the order status**, with two transient states: `Pending` (waiting for Catalog) and `PaymentPending` (waiting for Payments). No separate saga table.
- **Compensation:** a declined payment cancels the order (`PaymentDeclined`) and sends `ReleaseStock`. Catalog never refuses a release.
- A reply that does not fit the order's current state is logged and ignored; a reply for an unknown order fails and ends in the dead-letter queue.
- `PaymentPending` refuses a second pay and a cancel, replacing 04's row lock as the guard against the pay-versus-cancel race.

## Consequences

- Good: the whole flow is in one class, unit-tested transition by transition with fakes.
- Good: participants stay simple: they execute commands and answer, knowing nothing about the flow.
- Bad: Ordering knows every step and every participant's commands: a new step means changing the orchestrator.
- Bad: no isolation between steps: during a payment that will be declined, the stock looks taken to other customers.
- Bad: clients see transient states and must poll.
