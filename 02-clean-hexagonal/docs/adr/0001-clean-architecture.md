# 0001. Use Clean / Hexagonal Architecture

- **Status:** Accepted (replaces version 01's layered structure for this version)
- **Date:** 2026-10-05

## Context

Version 01 put the business rules in a layer that depends on the data layer. The rules therefore depended on EF Core and PostgreSQL: they could only be tested against a database, and changing storage reached the business code. The shop's ordering rules (a lifecycle with states, stock that must never be oversold) are the part most worth protecting and testing.

## Decision

Organise the code in four projects, with every source dependency pointing inwards:

- `Shop.Clean.Domain`: aggregates, value objects, a domain service and domain exceptions. No project or package references.
- `Shop.Clean.Application`: one use-case class per operation, plus the ports (interfaces) the use cases need. It references Domain and only the logging/DI abstractions.
- `Shop.Clean.Infrastructure`: the driven adapters (EF Core repositories, unit of work, payment gateway). They implement the ports. The `DbContext` and the adapters are `internal`.
- `Shop.Clean.Api`: the driving adapter (HTTP) and the composition root. Only `Program.cs` touches Infrastructure.

The rules are enforced by `Shop.Clean.ArchitectureTests` (guide §5.6).

## Consequences

- Good: the domain and the use cases are unit-tested without a database (69 tests in about a second).
- Good: switching the database or the payment provider changes Infrastructure only.
- Good: the location of every kind of code is predictable, for people and for AI agents, and checked by tests.
- Bad: about 50% more code than 01: commands, value objects, converters, response mapping, ports.
- Bad: following a request means more hops (endpoint → use case → port → adapter).
- The public API and the database schema are unchanged.
