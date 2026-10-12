# 0002. Use the EF Core entities as the business model

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

The business layer needs objects to work on: products, orders, lines, payments. It can define its own classes and map them to and from the database, or work directly on the classes EF Core maps to the tables.

The shop is small, and this version must show the classic layered approach, including its costs.

## Decision

The EF Core entities in `Shop.Layered.Data/Entities` **are** the business model. They have public setters and no behaviour (an anemic model). All rules live in the Business services. The order status enums live next to the entities.

The Api maps entities to response records (`ProductResponse.From`, `OrderResponse.From`) at the last moment, so the JSON does not expose every column (for example the `Version` row version).

Stock concurrency is handled with the database's own tools:

- conditional `UPDATE … WHERE stock >= @quantity` statements (`ExecuteUpdateAsync`) for reserving, releasing and adjusting stock, inside one transaction with the order insert;
- the PostgreSQL `xmin` column as a row version on orders, so concurrent pay and cancel requests cannot both win.

## Consequences

- Good: no mapping layer between business objects and tables; one class per table; database features are used directly.
- Bad: one class is the table row, the business object and almost the API shape. Renaming a field touches Data, Business and Api.
- Bad: nothing protects the rules. Any code holding an `Order` can set `Status = Paid`. Correctness depends on everyone going through `OrderService`.
- Bad: the Api compiles against the entity types (through Business). The rule "Api does not depend on Data" holds for the project file only.
- Version 02 reverses this decision: a domain model with behaviour, and persistence mapped separately in Infrastructure.
