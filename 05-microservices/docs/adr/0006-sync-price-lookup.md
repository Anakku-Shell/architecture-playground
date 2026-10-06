# 0006. A synchronous HTTP price lookup from Ordering to Catalog

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

To place an order, Ordering needs each product's name and current price, and must reject unknown products with `400` while the customer waits. In 04 it asked `ICatalogQueries` in process. The options in 05: ask Catalog over HTTP; keep a local copy of products in Ordering, fed by Catalog events; or move pricing into the saga.

## Decision

- Ordering calls `GET http://catalog/internal/product-snapshots?ids=…` through its `ICatalogClient` port (`CatalogHttpClient` adapter). The response type `ProductSnapshot` lives in Contracts.
- The call has the service defaults' resilience (retries, circuit breaker) and a 5-second overall timeout. Any failure becomes `CatalogUnavailableException` → `503 Service Unavailable`, and nothing is saved.
- `/internal/*` is not routed by the gateway.

## Consequences

- Good: simple and always current; unknown products are reported synchronously, as in 01–04.
- Bad: placing an order depends on Catalog being up (temporal coupling); a slow Catalog slows order placement.
- Alternative not taken: a local, event-fed copy of names and prices in Ordering would remove that dependency, at the cost of more messages, a second copy of the data and prices that may be slightly stale.
