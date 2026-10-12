# 0001. Organise the code by use case (vertical slices)

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

In versions 01 and 02 one use case is spread over every layer: placing an order touches an endpoint, a command, a use case, ports, repositories and a unit of work, in four projects. Most change requests are about one feature, not one layer. Version 02's ports and repositories also protect against changes (another database, another ORM) that this shop does not expect.

## Decision

- One project, `Shop.Slice.Api`. Each use case is one file under `Features/<Context>/`, in its own namespace `Features.<Context>.<UseCase>`, holding the request record, input validation, handler and route.
- Response shapes shared by a context's slices live at the context level (`Features/Ordering/OrderResponse.cs`): they are the API contract, not logic.
- The domain model from 02 (aggregates, value objects, `OrderFulfillment`) is kept in `Domain/`: the rules still pay off as a model.
- Slices use `ShopDbContext` directly. There are no repositories and no ports.
- Shared technical code lives in `Infrastructure/` (EF Core, retry helper, payment gateway) and `Common/` (endpoint discovery, errors, validation).
- Boundaries are architecture tests (`SliceRulesTests`), because a single project has no references to enforce them.

## Consequences

- Good: a use case is read, changed, added or removed in one place; new use cases never edit existing files.
- Good: fewer types per operation than 02 (no command, no use-case class, no port).
- Bad: slices depend on EF Core directly. Changing the data-access technology touches slices.
- Bad: use-case logic outside the domain is tested through HTTP against a real database, not with fast unit tests.
- Bad: duplication of access code between slices is accepted by design and needs judgement (guide §6.6).
