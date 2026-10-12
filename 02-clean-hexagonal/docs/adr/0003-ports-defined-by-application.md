# 0003. Ports are defined by the Application layer

- **Status:** Accepted
- **Date:** 2026-10-05

## Context

The use cases need to load and save aggregates and to charge payments. If they call EF Core or a payment SDK directly, they depend on those technologies. If the interfaces they use live in Infrastructure, the dependency still points outwards: only the file names change.

## Decision

- The interfaces the use cases need (`IProductRepository`, `IOrderRepository`, `IPaymentRepository`, `IPaymentGateway`, `IUnitOfWork`) are declared in `Shop.Clean.Application/Ports`, in the application's own terms: aggregates and value objects, never rows or `DbSet`s.
- The exceptions that are part of a port's contract (`ConcurrencyConflictException`, `DuplicateKeyException`) are declared next to the ports. The adapters translate EF Core and PostgreSQL errors into them.
- `IPaymentGateway.ChargeAsync` takes the order id as an idempotency key.
- No use-case interfaces ("input ports"): each use case has a single caller, the HTTP adapter, so the class itself is the driving port (YAGNI).
- `Ports_AreInterfacesInApplication` checks that the Ports namespace holds only interfaces and their contract exceptions, and that Domain and Infrastructure declare no interfaces.

## Consequences

- Good: dependency inversion. The call goes from use case to database, the source dependency from Infrastructure to Application.
- Good: tests plug in-memory adapters into the same ports.
- Bad: one more indirection per external dependency, and a port must be designed (which methods, which exceptions) instead of "just using EF Core".
