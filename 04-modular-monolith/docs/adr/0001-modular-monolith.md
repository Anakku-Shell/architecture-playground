# 0001. A modular monolith: one deployable, three modules with enforced boundaries

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

Versions 01–03 are one model: any class can use any other, any query can read any table. The shop has three bounded contexts (Catalog, Ordering, Payments) with different rules and different rates of change. Splitting them into services (version 05) would enforce the boundaries, but it would also bring networks, separate deployments and eventual consistency, which this system does not need yet.

## Decision

- One process (`Shop.Modular.Host`) and one database, split into three **modules**, one per bounded context.
- Each module's code is `internal`, except one public class implementing `IModule` (register services, map endpoints, migrate). The Host lists the modules explicitly in `Program.cs`.
- Each module publishes a `*.Contracts` project: the integration events it publishes and the query interfaces it answers. Modules reference other modules' contracts only, never their code.
- Shared plumbing is split in two: `BuildingBlocks` (event interfaces and shared errors, no dependencies), referenced by contracts and inner layers, and `BuildingBlocks.Infrastructure` (module interface, bus, shared transaction, error handler, with ASP.NET Core and EF Core), referenced only by module entry projects and the Host.
- Architecture tests guard the project references, the internals, the contracts' dependencies and the schemas (`ModuleRulesTests`).

## Consequences

- Good: boundaries are compiler errors, not conventions; a module can be understood, owned and changed on its own.
- Good: one deployable and one database keep operations and debugging simple.
- Good: modules already talk as services would, so extracting one later is mostly mechanical.
- Bad: more projects (eleven in `src/`) and plumbing than versions 01–03.
- Bad: no shortcuts across modules (joins, shared classes); data crosses by value, and contract changes must be agreed.
