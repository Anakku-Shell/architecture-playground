# Shop.ContractTests — the shared API contract suite

Every version of the shop must behave **exactly the same from the outside**. This library holds the tests that prove it. It is written once, and every version runs it against its own API. If all five versions pass the same suite, comparing their insides is a fair comparison.

This is a *contract test* suite: it only talks HTTP to the public API (see the guide, chapter 2), and it never looks at classes, tables or messages. That is why it can test a layered monolith and a set of microservices with the same code.

## What is inside

| File | Purpose |
|---|---|
| `IShopApi.cs` | What a version provides: an `HttpClient` pointing at its API, and `IsAsynchronous` |
| `ContractTests.cs` | Common base: gives each test a `ShopClient` |
| `ShopClient.cs` | Typed calls to every endpoint, plus `WaitFor…` helpers that poll until a condition holds |
| `Dtos.cs` | The JSON shapes of the API, as a client sees them |
| `ProblemAssert.cs` | Checks that errors are ProblemDetails (RFC 9457) |
| `ProductContractTests.cs`, `OrderContractTests.cs`, `PaymentContractTests.cs` | The tests, as **abstract** classes |

## Synchronous and asynchronous versions

Versions 01–04 finish an order or a payment inside the request. Version 05 accepts it (`202 Accepted`) and finishes it later through messaging. The suite supports both without knowing which one it is testing:

- `IShopApi.IsAsynchronous` says which kind the version is. `ShopClient.AssertCompletedOrAccepted` then requires exactly `201`/`200` from a synchronous version and exactly `202` from an asynchronous one when placing and paying an order. That is the only intended difference in the contract.
- `ShopClient.WaitForSettled` polls `GET /api/orders/{id}` until the order leaves the transient states (`Pending`, `PaymentPending`); `WaitForPaymentOutcome` until it is `Paid` or `Cancelled`; `WaitForProduct` until the stock matches. In 01–04 the first read already matches.

## Using it from a version

A version's `Shop.<V>.ContractTests` project (an xUnit v3 test project) references this library and adds three things:

```csharp
using Shop.ContractTests;
using Xunit;

// 1. A fixture that starts the API once for the whole test assembly and implements IShopApi.
public sealed class LayeredShopApi : IShopApi, IAsyncLifetime
{
    public bool IsAsynchronous => false;
    // CreateClient(), InitializeAsync(), DisposeAsync(): WebApplicationFactory + Testcontainers
}

// 2. Register it as an assembly fixture (xUnit v3): one API and one database for all the tests.
[assembly: AssemblyFixture(typeof(LayeredShopApi))]

// 3. One concrete class per abstract class. xUnit runs the inherited tests on them.
public sealed class ProductTests(LayeredShopApi api) : ProductContractTests(api);
public sealed class OrderTests(LayeredShopApi api) : OrderContractTests(api);
public sealed class PaymentTests(LayeredShopApi api) : PaymentContractTests(api);
```

Tests share one database, so each test creates its own products (with a unique SKU from `ShopClient.UniqueSku()`) and never asserts on totals across the whole catalog.
