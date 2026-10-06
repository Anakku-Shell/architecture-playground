# 0004. Hand-written transactional outbox and idempotent inbox on RabbitMQ

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

A service that saves a change and then publishes a message performs a **dual write**: a crash in between loses the message, and publishing first can announce a change that is then rolled back. Brokers deliver **at least once**, so a consumer can see the same message twice. MassTransit (commercial since 2025) and MediatR are excluded from this playground, and the point is to see the mechanics.

## Decision

- **Broker:** RabbitMQ through `RabbitMQ.Client`. One topic exchange `shop`; routing key = message type name; one durable **quorum queue** per service, bound to the types it consumes. A failing message is retried by publishing a copy with an `x-attempt` header (counted by the consumer host, because RabbitMQ 4.3 does not count a nack with requeue) and goes to `<queue>.dead-letter` after 5 attempts.
- **Outbox:** `IMessageOutbox.Add` stages an `outbox_messages` row in the service's DbContext, saved with the business change. `OutboxDispatcher` publishes committed rows in order, with publisher confirms and `mandatory`, marks them sent, and uses `FOR UPDATE SKIP LOCKED` so several instances can run. A row the broker refuses gets an attempt count and the error, and the dispatcher goes on with the next rows (no head-of-line blocking); after 5 refusals the row is left for a person. A broker outage stops the batch without counting. It wakes on commit (EF Core interceptors) or every 5 seconds.
- **Inbox:** `RabbitMqConsumerHost` handles each delivery in one local transaction: inbox row (message id as primary key), consumer, outgoing messages, commit, then ack. A known id is acknowledged without running; a failure rolls back and is retried as above.
- The trace context travels in the outbox row and a `traceparent` header.
- A service is healthy only once its queue is declared and consumed.
- Integration tests against real PostgreSQL and RabbitMQ containers pin the behaviour (`InboxTests`, `OutboxTests`).

## Consequences

- Good: a message is published if and only if its change committed; duplicates have no effect (effectively once).
- Good: every step is visible, debuggable and tested.
- Bad: about 750 lines to maintain, and still missing what a library provides: cleanup of old rows, message versioning, dead-letter replay, delayed retries that do not block the consumer.
- Bad: one more table pair and a background worker per service; a little publishing latency.
- In production, prefer a maintained library (MassTransit, Wolverine, NServiceBus) unless there is a strong reason not to.
