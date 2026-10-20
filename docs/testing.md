# Testing

## Strategy

Stock correctness cannot be proven against an in-memory provider: row locking, unique indexes,
check constraints and transactions only exist in the database. StockPilot therefore runs its
integration tests against a **real PostgreSQL database** with the same migrations and schema as
production.

The suite is split into:

- **Inventory flow tests** – drive the domain services directly (`IPurchaseService`, `ISaleService`,
  `IStockService`) and assert balances, ledger rows and rejection rules.
- **Authorization tests** – boot the real application with `WebApplicationFactory<Program>` and make
  HTTP requests through the MVC pipeline, asserting redirects, status codes and antiforgery behaviour.

## Running the tests

The tests create an isolated database named `stockpilot_test_<random>`, migrate it, run, then drop it.
They need a PostgreSQL server and a connection string.

```bash
set -a && . ./.env && set +a
dotnet test tests/StockPilot.Tests/StockPilot.Tests.csproj
```

The connection string is read from `ConnectionStrings__DefaultConnection`. The configured PostgreSQL
user must be allowed to create and drop databases (true for the local development container).

Test parallelisation is disabled across the assembly because the classes share one fixture and one
database.

## What is covered

| Area | Test |
|---|---|
| Purchase receipt | Receiving adds stock, blends average cost and writes exactly one ledger row |
| Duplicate receive | A second receive raises `BusinessRuleException` and posts nothing |
| Purchase reversal | Reversal removes the received quantity and writes a compensating ledger row |
| Sale completion | Stock is deducted and the issue cost is snapshotted at weighted-average cost |
| Sale reversal | Stock and valuation are restored at the recorded issue cost |
| Insufficient stock | Completion is rejected and the balance is untouched |
| Concurrency | Two concurrent completions for the same product: exactly one succeeds |
| Negative stock | A decrease adjustment below zero is rejected |
| Anonymous access | Protected pages redirect to sign-in |
| Administrator | Can reach `/Users`, `/AuditLogs` and `/Stock/Adjust` |
| Inventory staff | Can create purchases/sales; cannot reach users or inventory adjustments |
| Viewer | Can read catalogue and reports; cannot create documents |
| CSRF | An unsafe POST without a token returns `400`; with a token it reaches the action |

## Adding a test

1. Add a `[Fact]` to `InventoryFlowTests` and create products through the helper
   `CreateProductAsync(...)`, which sets up a category, unit, supplier, product and opening balance.
2. Use `_fixture.Factory.Services.CreateAsyncScope()` to resolve services; use a fresh scope for
   parallel or independent operations.
3. Assert on `IStockService.GetBalanceAsync` and on `ApplicationDbContext.StockMovements` for ledger
   facts.
4. For HTTP behaviour, add to `AuthorizationTests` and use `PostgresFixture.LoginAsync(...)` and
   `ExtractAntiforgeryToken(...)`.

## Manual verification

The UI can also be checked end to end with a browser:

1. Start PostgreSQL and the app with demonstration data (`Seed__DemoData=true`).
2. Sign in as the administrator.
3. Create a draft purchase, receive it, and confirm the stock ledger shows a `Purchase receipt`.
4. Create a draft sale, complete it, and confirm a `Sale issue` row with a cost.
5. Try to sell more than is available and confirm the error message.
6. Record an adjustment, then reverse a sale as an administrator.
7. Export one report as CSV and open the audit log.
