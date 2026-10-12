# 0002. Use a rich domain model

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

In version 01 the entities had public setters and the rules lived in services, so any code could set an order to `Paid` without the checks. The rules were also spread over several services and needed a database to test.

## Decision

- `Product`, `Order` (with its `OrderLine`s) and `Payment` are aggregates with private setters. State changes only through methods (`Reserve`, `Release`, `AdjustStock`, `MarkPaid`, `DeclinePayment`, `Cancel`), which throw `BusinessRuleViolationException` on an invalid move.
- `Money`, `Sku`, `ProductName` and `Quantity` are value objects. They validate on creation (`DomainValidationException`), so an invalid one cannot exist.
- `OrderFulfillment` is a domain service for the rules that span two aggregates: reserve every line or none, and release stock on every cancellation.
- The use cases do input validation (which JSON field is wrong). The domain enforces its invariants whoever calls it.
- EF Core maps the domain classes directly. They get a private parameterless constructor for EF Core, and the row version is a shadow property, so the domain has no persistence attributes or properties.
- Values read back from the database are rehydrated (`Money.Rehydrate`, `Quantity.Rehydrate`…) without re-running today's rules, so tightening a rule never makes stored data unreadable. Only Infrastructure may call `Rehydrate`, which an architecture test checks.
- Stock concurrency becomes optimistic: `xmin` row version on products and orders, plus a bounded retry (15 attempts) of the whole use case.

## Consequences

- Good: the rules are in one place, named, impossible to bypass, and unit-testable as plain objects.
- Good: `DomainModel_HasNoPublicSetters` makes "nobody bypasses the methods" a checked rule.
- Bad: the domain is not fully persistence-ignorant (private constructors for EF Core). A separate persistence model would remove that at the cost of twice the mapping.
- Bad: the database can no longer apply the stock rule atomically, as 01's conditional `UPDATE` did. Under contention, losers retry.
- Bad: placing an order changes several aggregates in one transaction. That is acceptable in a single database; versions 04 and 05 revisit it.
