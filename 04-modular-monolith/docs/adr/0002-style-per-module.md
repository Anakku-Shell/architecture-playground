# 0002. A different internal style per module

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

The three contexts are not equally complex. A product is data with field checks and no lifecycle. An order has a state machine, invariants and money rules. A payment is one charge and one record. Versions 01–03 applied one style to all three, so Catalog carried 02's aggregate and value objects without needing them.

## Decision

- **Catalog: CRUD.** Endpoints use the DbContext directly; the entity has public setters; the rules (`ProductRules`) are plain functions. No service layer, no repository.
- **Ordering: Clean / Hexagonal with a rich domain**, as version 02: `Ordering.Domain`, `Ordering.Application` (use cases, consumers, ports), `Ordering.Infrastructure` (EF Core and HTTP adapters, the module's composition root). The domain lost `Product` and `OrderFulfillment` (they are not Ordering's) and gained a `Pending` state.
- **Payments: vertical slices**, as version 03: one file per use case under `Features/`, one triggered by an event (`ProcessPayment`) and one by HTTP (`GetPayment`).
- The module boundary rules apply to every module; each module's inner rules (02's dependency rule for Ordering) apply only inside it.

## Consequences

- Good: each module costs what its complexity justifies; Catalog is short and obvious, and Ordering keeps fast unit tests.
- Good: it shows that an architecture style is a choice per bounded context, not per system.
- Bad: a developer moving between modules meets three styles and must know which applies where.
- Bad: shared conventions (errors, events) must be style-neutral, which is why they live in the building blocks.
