# 0001. Use a layered (N-tier) architecture

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

This is the first version of the shop and the baseline for the other four. It must be the structure most developers already know, so the later versions can be compared against it. The shop is small: three areas (catalog, ordering, payments), one database, one team, one deployable.

Without any structure, endpoints would contain SQL, rules and HTTP handling in the same method.

## Decision

Split the code into three projects, one per layer, with project references only downwards:

- `Shop.Layered.Api` (presentation): Minimal API endpoints, request and response records, mapping of exceptions to ProblemDetails.
- `Shop.Layered.Business`: one service per area (`ProductService`, `OrderService`, `PaymentService`) holding validation, rules and transactions; business failures are exceptions that know nothing about HTTP.
- `Shop.Layered.Data`: the EF Core `ShopDbContext`, the entities and the migrations.

`Api → Business → Data`. Each layer registers its own services. The Api only calls Business: `AddShopBusiness()` at startup and, in Development, `MigrateDatabaseAsync()`. Business forwards both to Data.

The rules are checked by `Shop.Layered.ArchitectureTests`.

## Consequences

- Good: familiar to everyone. A request can be read top-down in three files. Very little code per feature.
- Good: business failures are reported without HTTP knowledge, so the Api is the only place mapping them to status codes.
- Bad: the business layer depends on the data layer, and therefore on EF Core and PostgreSQL (it even catches `PostgresException`). Changing storage reaches the rules.
- Bad: business logic cannot be unit-tested without a database. The contract tests, with a real PostgreSQL in a container, carry that weight.
- Bad: the Api still sees the Data types through a transitive reference; only the project reference and the use of the `DbContext` can be enforced (see ADR 0002 and the guide, §4.6).
