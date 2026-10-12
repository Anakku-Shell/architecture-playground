# 0003. Light CQRS: commands through the domain, queries as projections

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

In 01 and 02 every read went through the same service or use case as the writes and loaded full entities or aggregates (an `Order` with all its lines), only to copy a few properties into a response. Reads cannot break an invariant, so they do not need the model.

## Decision

- **Command slices** (create product, change price, adjust stock, place, pay, cancel) load tracked aggregates, let the domain decide and save, with the optimistic `xmin` check and the bounded retry of 02 (`RetryOnConflictAsync`).
- **Query slices** (`Get*`, `List*`) project only the columns of the response; they build no aggregate and write nothing (`AsNoTracking()` marks the intent; a projection is not tracked anyway).
- One database and one EF Core model for both. No separate read store.
- `Queries_DoNotModifyState` checks that query slices call nothing that writes (`SaveChanges`, `ExecuteUpdate`/`ExecuteDelete`, raw SQL commands, `Add`/`Update`/`Remove`, the change tracker, the retry helper). It reads the compiled IL, so calls inside lambdas and async methods count too.

## Consequences

- Good: reads are cheaper and can evolve freely (joins, extra columns, raw SQL, a read replica later) without touching a rule.
- Good: the naming convention makes a slice's kind visible at a glance.
- Bad: the read and write code for one entity are separate, so a new column must be added to both sides.
- Bad: EF Core still materialises value objects in projections (converters run on read), so queries are not fully free of the domain types.
- A second database for reads (full CQRS) is deliberately not done: it would bring eventual consistency for no benefit at this size.
