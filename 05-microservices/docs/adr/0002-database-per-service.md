# 0002. One database per service

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

In 04 each module had its own schema in one shared database, and one transaction could span modules. Services that share a database are coupled through it: a schema change, a heavy query or a migration of one affects the others, and nothing stops a service from reading another's tables.

## Decision

- Each service owns one database: `catalogdb`, `orderingdb`, `paymentsdb`. For convenience they live on one PostgreSQL server started by the AppHost; each service receives only its own connection string.
- Each database also holds that service's `outbox_messages` and `inbox_messages` tables, so they share its local transactions.
- Each service migrates its own database at startup, in Development only (`InitialCreate` per service).
- Data owned by another service is referenced by id only (`order_lines.ProductId`, `payments.OrderId`) and copied by value when needed (the order line's name and price snapshot).
- Orders use the `xmin` row version for optimistic concurrency; Catalog keeps `SELECT … FOR UPDATE` for stock reservation.

## Consequences

- Good: no service can join, read or change another's tables; schemas evolve independently.
- Good: each database can later move to its own server, technology or scale without touching the others.
- Bad: no transaction across services: consistency is eventual and coordinated by the saga (ADR 0003).
- Bad: no joins across contexts; reports need their own read model or API calls.
- Bad: one server account can still open every database in this setup; production would give each service its own database user.
