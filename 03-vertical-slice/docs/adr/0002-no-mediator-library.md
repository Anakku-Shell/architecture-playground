# 0002. No mediator library; endpoints discovered by reflection

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

Vertical-slice codebases often use a mediator library (MediatR) to send each request to its handler and to run cross-cutting "behaviours" (validation, logging, transactions) around every handler. Since version 13 (2025) MediatR is dual-licensed (commercial, with a free community tier), and this repo does not use it. More importantly, Minimal APIs already map a route straight to a handler, and ASP.NET Core already has middleware and endpoint filters for cross-cutting work.

## Decision

- Each slice contains a sealed class implementing `IEndpoint` (`void Map(IEndpointRouteBuilder app)`) that maps its route to a private static handler method.
- `AddEndpoints(assembly)` finds every `IEndpoint` by reflection at startup; `MapEndpoints()` maps them. `Program.cs` has no list of routes.
- Cross-cutting concerns use what ASP.NET Core offers: the exception handler (errors), logging per slice, endpoint filters if ever needed.
- `Endpoints_LiveInsideASlice` checks that every `IEndpoint` is sealed and lives in a slice namespace.

## Consequences

- Good: no dependency, about twenty lines of discovery code, and the handler is a plain method whose parameters say exactly what it needs.
- Good: one indirection fewer than a mediator (no request object sent to a handler found at runtime).
- Bad: no uniform "handler" type, so a behaviour that must wrap every command (say, auditing) is an endpoint filter on a route group rather than a pipeline step.
- Bad: reflection at startup; a slice whose class is not an `IEndpoint` simply has no route (the contract tests would catch a missing one).
