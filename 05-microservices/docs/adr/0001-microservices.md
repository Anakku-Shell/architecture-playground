# 0001. Split the modular monolith into microservices

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

Version 04 has three modules with enforced boundaries in one process and one database. The playground's last step is to show what it costs, and what it buys, to run each bounded context as an independently deployable service. The shop itself does not need it; the goal is to make every mechanism a real system would need visible and testable.

## Decision

- Each 04 module becomes a **service** with its own process and database: `Shop.Micro.Catalog.Api`, the four `Shop.Micro.Ordering.*` projects, `Shop.Micro.Payments.Api`. Their internal styles stay as in 04 (CRUD, clean/hexagonal, vertical slices).
- An **API gateway** keeps the public API identical to 01–04 (ADR 0005).
- Services share only three projects: `Contracts` (messages), `Messaging` (outbox/inbox plumbing) and `ServiceDefaults` (hosting defaults). Business code and even error types are duplicated per service rather than shared.
- Communication is **asynchronous by default** (RabbitMQ, ADR 0004), coordinated by an orchestrated saga (ADR 0003), with one synchronous exception (ADR 0006).
- **.NET Aspire** runs the system for development and tests (AppHost, ServiceDefaults, `Aspire.Hosting.Testing`). The Aspire CLI is optional (`ASPIRE010` suppressed: DCP and the dashboard come from NuGet).
- The architecture tests (`ServiceRulesTests`) forbid references between services and allow only the three shared projects.

## Consequences

- Good: each service can be built, deployed, scaled and fail on its own; data boundaries are physical.
- Good: the services are almost unchanged inside, which validates 04's design.
- Bad: eventual consistency, `202 Accepted` and transient states (`Pending`, `PaymentPending`) become part of the API's behaviour.
- Bad: much more infrastructure and code (broker, outbox, inbox, dispatcher, gateway, tracing) and slower, more complex tests.
- Bad: a little duplicated code per service (errors, ProblemDetails handling), accepted to avoid a shared kernel.
