# architecture-playground

One small shop implemented **five times** in .NET, each time with a different architecture, so the styles can be compared side by side:

| # | Folder | Architecture | Status |
|---|---|---|---|
| 01 | [`01-layered`](01-layered/README.md) | N-tier layered (`Api → Business → Data`) | ready |
| 02 | [`02-clean-hexagonal`](02-clean-hexagonal/README.md) | Clean / Hexagonal (ports and adapters) with a rich domain model | ready |
| 03 | `03-vertical-slice` | Vertical slices (one folder per use case) with light CQRS | coming |
| 04 | `04-modular-monolith` | Modular monolith: one process, three modules, a different style per module | coming |
| 05 | `05-microservices` | Microservices: one service per module, RabbitMQ messaging, a saga, an API gateway | coming |

The shop has three parts: **Catalog** (products and stock), **Ordering** (orders and their life cycle) and **Payments** (a fake payment gateway). The public API is identical in every version, and every version passes the same **contract tests** (`contract-tests/`). Only the inside changes.

Everything is explained in **[docs/ARCHITECTURE_GUIDE.md](docs/ARCHITECTURE_GUIDE.md)**: the concepts from zero, how a request travels through each architecture layer by layer, how the styles combine, and how to choose between them.

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

## License

[GPL-3.0](LICENSE)
