# 0005. An API gateway with YARP

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

The public API (paths, shapes, status codes) must stay identical to 01–04, and the contract suite must run against one address. With three services, clients would otherwise need three addresses and know which service owns which path.

## Decision

- `Shop.Micro.Gateway` uses **YARP** (Yet Another Reverse Proxy) with three routes configured in code: `/api/products/**` → `catalog`, `/api/orders/**` → `ordering`, `/api/payments/**` → `payments`.
- Destinations are service names (`http://catalog`), resolved by service discovery (`Microsoft.Extensions.ServiceDiscovery.Yarp`).
- A destination that does not answer for 10 seconds gets `504 Gateway Timeout` (`ActivityTimeout`), instead of YARP's default 100 seconds.
- The gateway has no business logic and references no service (`Gateway_ReferencesNoService`). `/internal/*` paths are not routed.
- Listens on port 5105; the AppHost starts it after the three services are healthy.

## Consequences

- Good: clients see one API; services can move, scale or split without clients noticing.
- Good: one place for cross-cutting concerns (timeouts here; authentication, rate limiting and caching in a real system).
- Bad: one more process and one more network hop on every request.
- Bad: a gateway that accumulates logic becomes a bottleneck and a coupling point; it must stay a router.
