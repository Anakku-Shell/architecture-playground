# architecture-playground

One small shop implemented **five times** in .NET, each time with a different architecture, so the styles can be compared side by side:

| # | Folder | Architecture | Status |
|---|---|---|---|
| 01 | [`01-layered`](01-layered/README.md) | N-tier layered (`Api → Business → Data`) | ready |
| 02 | [`02-clean-hexagonal`](02-clean-hexagonal/README.md) | Clean / Hexagonal (ports and adapters) with a rich domain model | ready |
| 03 | [`03-vertical-slice`](03-vertical-slice/README.md) | Vertical slices (one file per use case) with light CQRS | ready |
| 04 | [`04-modular-monolith`](04-modular-monolith/README.md) | Modular monolith: one process, three modules, a different style per module | ready |
| 05 | [`05-microservices`](05-microservices/README.md) | Microservices: one service per module, RabbitMQ messaging with outbox and inbox, an orchestrated saga, an API gateway | ready |

The shop has three parts: **Catalog** (products and stock), **Ordering** (orders and their life cycle) and **Payments** (a fake payment gateway). The public API is identical in every version, and every version passes the same **contract tests** (`contract-tests/`). Only the inside changes.

Everything is explained in **[docs/ARCHITECTURE_GUIDE.md](docs/ARCHITECTURE_GUIDE.md)**: the concepts from zero, how a request travels through each architecture layer by layer, how the styles combine, and how to choose between them.

## How to read this repo

1. **The map.** [Guide chapter 3](docs/ARCHITECTURE_GUIDE.md#3-the-map-and-the-primers): what architecture is, the four independent axes (code organisation, domain modelling, deployment, data flow) and short primers on DDD, CQRS, events and transactions.
2. **One version at a time, in order.** Each folder's README is a summary; its guide chapter (4–8) follows `POST /api/orders` through the layers. Each version was built by copying the previous one and refactoring it; the "What changed" section of each chapter says what moved and why.
3. **Side by side.** [Chapter 9](docs/ARCHITECTURE_GUIDE.md#9-combining-styles) puts the same request in all five versions in one table and shows how styles combine; [chapter 10](docs/ARCHITECTURE_GUIDE.md#10-decision-guide) turns them into a decision guide and compares the five versions in numbers.
4. **Beyond the five.** [Chapter 11](docs/ARCHITECTURE_GUIDE.md#11-styles-explained-but-not-implemented) explains the styles not built here (MVC/MVVM, microkernel, pipes and filters, event-driven, event sourcing, SOA, serverless, micro-frontends). [Chapter 12](docs/ARCHITECTURE_GUIDE.md#12-architecture-and-ai-agents) covers how to state and enforce architecture rules for AI coding agents; this repo's own [`CLAUDE.md`](CLAUDE.md) is the example. The [appendix](docs/ARCHITECTURE_GUIDE.md#appendix-javaspring-equivalences) maps every piece to Java and Spring.

| | 01 | 02 | 03 | 04 | 05 |
|---|---|---|---|---|---|
| Projects in `src/` | 3 | 4 | 1 | 11 | 11 |
| Deployables | 1 | 1 | 1 | 1 | 4 |
| C# lines in `src/` (without migrations) | 1,014 | 1,604 | 1,498 | 1,890 | 2,767 |
| Architecture rules (tests) | 5 | 8 | 6 | 12 | 11 |
| `POST /api/orders` answers | `201` | `201` | `201` | `201` | `202`, then the saga |

The full comparison, with what each version makes easy and hard, is in [guide §10.3](docs/ARCHITECTURE_GUIDE.md#103-the-five-versions-compared).

## Prerequisites

| Tool | Version | How to install | Notes |
|---|---|---|---|
| .NET SDK | 10 (pinned to 10.0.401 by `global.json`) | `winget install Microsoft.DotNet.SDK.10` | Includes the C# compiler and the `dotnet` CLI. Check with `dotnet --list-sdks` in a **new** terminal. |
| Docker Desktop | any recent | [docker.com](https://www.docker.com/products/docker-desktop/) | Runs PostgreSQL (and RabbitMQ in 05), and the containers used by the tests |
| VS Code | any recent | `winget install Microsoft.VisualStudioCode` | Open the repo and accept the recommended extensions (`.vscode/extensions.json`). Visual Studio or Rider work too. |

Version 05 uses **.NET Aspire**, which arrives as NuGet packages: nothing else to install. The guide (chapter 1) explains each tool and the optional Aspire CLI.

## Quick start

```bash
docker compose up -d                                    # PostgreSQL on localhost:5433, one database per version
dotnet run --project 01-layered/src/Shop.Layered.Api    # version 01 on http://localhost:5101
dotnet test 01-layered/Shop.slnx                        # unit, architecture and contract tests of version 01
```

Each version listens on its own port (01 → 5101 … 04 → 5104), so several can run at once. Send requests with [`http/shop.http`](http/shop.http) (VS Code REST Client extension).

Version 05 starts differently: run its Aspire host, which starts PostgreSQL, RabbitMQ, the three services and the gateway (http://localhost:5105), and opens the Aspire dashboard:

```bash
dotnet run --project 05-microservices/src/Shop.Micro.AppHost
```

`docker compose down` stops the database; `docker compose down -v` also deletes its data.

### The database on a new machine

Nothing to set up by hand. `docker compose up -d` starts PostgreSQL and creates one empty database per version ([`docker/postgres/init.sql`](docker/postgres/init.sql)). Each API creates its own tables when it starts in Development, by applying its EF Core migrations, the versioned source of truth for the schema. To get the SQL instead, for example to review it or to create a database without running the app:

```bash
scripts/create-schemas.sh          # PowerShell: ./scripts/create-schemas.ps1 ; writes artifacts/sql/<version>.sql
```

Details: [guide §1.6](docs/ARCHITECTURE_GUIDE.md#16-first-run-on-a-new-machine-where-the-database-comes-from).

## License

[GPL-3.0](LICENSE)
