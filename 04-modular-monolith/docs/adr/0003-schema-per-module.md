# 0003. One database schema per module, no foreign keys across modules

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

Code boundaries are worthless if modules can still read and write each other's tables: the database would couple what the code separated, and moving a module to its own database would mean untangling queries first.

## Decision

- One database (`shop_modular`), one schema per module: `catalog`, `ordering`, `payments`. Each module has its own DbContext, mapped to its own schema, with its migrations history table in that schema, and migrates independently (`IModule.MigrateAsync`).
- No module reads or writes another module's schema. Other modules' data comes through contracts (`ICatalogQueries`) and events.
- References across modules are ids by value (`order_lines.ProductId`, `payments.OrderId`), with no foreign key. The only foreign key is inside Ordering (`order_lines → orders`).
- `EachModule_MapsOnlyToItsOwnSchema` checks every table in each module's EF Core model.

## Consequences

- Good: each module owns its data as it owns its code; a schema can move to its own database without touching the others.
- Good: one server, one backup and one connection keep the operational simplicity of a monolith, including transactions across schemas (ADR 0004).
- Bad: the database no longer guarantees that an order line's product exists; the application does (Ordering checks the products through Catalog when the order is placed).
- Bad: reports that need data from several modules cannot join their tables; they go through contracts or a dedicated read model.
