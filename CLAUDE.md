# CLAUDE.md

A learning playground about **software architecture**. One small shop (Catalog, Ordering, Payments) is implemented five times in .NET 10, once per architecture, so the styles can be compared. The owner knows .NET well and is learning architecture: the explanations matter as much as the code.

The explanations live in `docs/ARCHITECTURE_GUIDE.md`. **Keep it in sync with the code**: any change to a concept's code updates its guide section, and every new term gets a glossary entry.

## Layout

- `01-layered/`, `02-clean-hexagonal/`, `03-vertical-slice/`, `04-modular-monolith/`, `05-microservices/`: one **standalone** solution each (`Shop.slnx`, `src/`, `tests/`, `README.md`, `docs/adr/`).
  - **Never reference code across version folders.** The only shared code is `contract-tests/`. Duplication between versions is intentional: each version must be readable on its own.
- `contract-tests/Shop.ContractTests/`: the shared API contract suite (abstract xUnit classes). Each version's `Shop.<V>.ContractTests` project inherits it. See its README.
- Root build files apply to every version: `global.json` (SDK), `Directory.Build.props` (compiler settings), `Directory.Packages.props` (all package versions), `.editorconfig`.
- `compose.yaml` + `docker/postgres/init.sql`: PostgreSQL for 01–04 on `localhost:5433` (`shop`/`shop`, dev only), databases `shop_layered`, `shop_clean`, `shop_slice`, `shop_modular`.
- `http/shop.http`: sample requests for every version (REST Client).

## The public API is a contract

The API (paths, JSON shapes, status codes, ProblemDetails errors) is identical in all versions and is defined by the contract tests. Do not change it in one version only. The single intended difference: version 05 answers `202 Accepted` when placing and paying an order.

## Architecture rules are tests

Each version has `Shop.<V>.ArchitectureTests` (ArchUnitNET). They encode the rules of that version's architecture (for example, in 02 `Domain` depends on nothing). **Never weaken or delete an architecture test to make a change compile**: change the design instead, or stop and ask. Each version's `docs/adr/` explains the decisions behind the rules.

## Conventions

- Project names: `Shop.<V>.<Part>` with `<V>` = `Layered`, `Clean`, `Slice`, `Modular`, `Micro`; tests `Shop.<V>.UnitTests`, `Shop.<V>.ArchitectureTests`, `Shop.<V>.ContractTests`.
- Ports: 01 → 5101, 02 → 5102, 03 → 5103, 04 → 5104, 05 gateway → 5105. Connection string: `Host=localhost;Port=5433;Database=<db>;Username=shop;Password=shop`.
- Stock concurrency: every version must pass `ConcurrentOrdersForLastUnits_NeverOversell` (optimistic concurrency with retry, or a conditional `UPDATE`); the version's guide chapter says which.
- Architecture tests: one test per rule, named after the rule (`Domain_DependsOnNothing`), with a comment on why the rule exists.
- Logging through the `[LoggerMessage]` source generator (analyzer CA1848 is on); culture-safe string calls (`ToUpperInvariant`, `CultureInfo.InvariantCulture`).
- Everything in English. Comments explain the *why*. A class that introduces a concept has a `// Guide: §N.M` pointer.
- Docs explain every term: acronyms spelled out on first use, a plain-language introduction before code.
- Minimal APIs everywhere. Errors are ProblemDetails (validation `400` with `errors`, not found `404`, business rule / invalid state / duplicate SKU `409`). JSON enums as strings.
- `sealed` classes by default, DTOs are `record`s, constructor injection only, time from `TimeProvider`, money is `decimal` with at most 2 decimals.
- Package versions only in `Directory.Packages.props`. **Not allowed:** MediatR, MassTransit, AutoMapper, FluentAssertions.
- EF Core migrations live in the data/infrastructure project of each version and are applied at startup in Development only.
- Tests: xUnit v3 with plain `Assert`. Testcontainers for anything that needs PostgreSQL or RabbitMQ.

## Commands

```bash
docker compose up -d                                   # PostgreSQL for 01-04 (Docker Desktop must be running)
dotnet build <version>/Shop.slnx -warnaserror          # build one version
dotnet test <version>/Shop.slnx                        # all its tests (unit, architecture, contract)
dotnet test <version>/Shop.slnx --filter "FullyQualifiedName~OrderTests"   # a subset
dotnet format <version>/Shop.slnx --verify-no-changes  # formatting check
dotnet run --project 01-layered/src/Shop.Layered.Api   # run a version (ports 5101-5104)
dotnet run --project 05-microservices/src/Shop.Micro.AppHost   # run version 05 (gateway on 5105)
dotnet build contract-tests/Shop.ContractTests -warnaserror      # the shared suite must always build
```

`dotnet test` runs on Microsoft.Testing.Platform (set in `global.json`). Docker Desktop must be running for contract tests and Testcontainers.

## Workflow

- Work happens on `feature/architecture-playground`, one commit per phase (Conventional Commits, e.g. `feat(layered): …`), pushed to origin. Never merge; the owner merges into `develop`.
- Do not install software on the machine; ask the owner.
- Before declaring a change done: build with `-warnaserror`, all tests of the version green, `dotnet format --verify-no-changes`, docs updated.
