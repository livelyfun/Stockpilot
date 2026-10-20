# Architecture

StockPilot is a server-rendered ASP.NET Core MVC application. The architecture is deliberately
simple: controllers handle HTTP, services own business rules and transactions, Entity Framework Core
owns persistence, and Razor views render the UI.

## Layers

```
┌──────────────────────────────────────────────────────────────────────┐
│ Presentation        Controllers · ViewModels · Razor views · wwwroot │
├──────────────────────────────────────────────────────────────────────┤
│ Application         Services (purchases, sales, stock, reports, …)   │
│                     BusinessRuleException / EntityNotFoundException  │
├──────────────────────────────────────────────────────────────────────┤
│ Domain              Entities · enums · DecimalMath / InventoryLedger │
├──────────────────────────────────────────────────────────────────────┤
│ Infrastructure      ApplicationDbContext · EF configurations ·      │
│                     migrations · Identity · audit · document numbers │
├──────────────────────────────────────────────────────────────────────┤
│ Database            PostgreSQL 17                                    │
└──────────────────────────────────────────────────────────────────────┘
```

- **Controllers** are thin. They validate model state, call a service, translate known exceptions into
  model-state errors, set flash messages and redirect. They never manipulate balances directly.
- **Services** are transactional. Every stock-affecting operation opens a database transaction, locks
  the rows it needs, writes balances and ledger entries together, calls the audit service and then
  commits. Services are registered per request through DI.
- **Entities** are persistence-focused POCOs with no behaviour that requires a database.
- **Infrastructure** contains cross-cutting helpers: `DecimalMath`, `InventoryLedger`
  (`SELECT … FOR UPDATE` and non-negative checks), `CsvExport`, `PaginatedList`, `UiHelpers`, the audit
  service and the document-number service.

## Request flow

1. `UseStatusCodePagesWithReExecute` maps `403`, `404`, `500` and friends to friendly pages.
2. A middleware pipeline adds security headers (nosniff, frame options, referrer policy, permissions
   policy, CSP) to every response.
3. `UseRouting`, `UseAuthentication`, `UseAuthorization` run before the controller.
4. MVC validates the model. Every unsafe request requires a valid antiforgery token because
   `AutoValidateAntiforgeryTokenAttribute` is registered globally.
5. The controller calls a service. Business failures surface as `BusinessRuleException` (rule
   violation) or `EntityNotFoundException` (missing document) and become model-state errors.
6. Successful mutations set a `TempData` flash message and redirect (POST/Redirect/GET), which prevents
   accidental re-submission on refresh.
7. `DbInitializer` runs once at startup: optional migrations, role creation, administrator
   bootstrap and optional development seeding.

## Dependency injection

| Registration | Lifetime | Notes |
|---|---|---|
| `ApplicationDbContext` | Scoped | Npgsql provider, 30-second command timeout |
| Identity (`UserManager`, `SignInManager`, `RoleManager`) | Scoped | EF Core stores, custom claims factory |
| Domain services | Scoped | One instance per HTTP request / test scope |
| `TimeProvider` | Singleton | Injectable clock for deterministic tests |
| `IHttpContextAccessor` | Singleton | Used by the audit service for user/IP |

## Concurrency strategy

Stock correctness depends on locking the right rows in the right order.

- **Document lock first.** `ReceiveAsync`, `CompleteAsync`, `ReverseReceivedAsync`,
  `ReverseCompletedAsync`, `UpdateDraftAsync` and `CancelDraftAsync` lock the purchase/sale row with
  `SELECT … FOR UPDATE`. A concurrent receive therefore waits and then observes
  `Status = Received` and is rejected.
- **Balance lock second.** Stock updates lock `InventoryBalances` rows in ascending product-id order.
  Consistent ordering prevents deadlocks when a document touches several products.
- **Conditional checks.** A deduction compares the requested quantity with the locked balance and
  throws `BusinessRuleException` when stock is insufficient.
- **Unique index as a final guard.** `IX_StockMovements_One_Per_Transaction` is a unique, filtered
  index on `(MovementType, ReferenceId, ProductId)` for purchase receipts, sale issues and reversals.
  Even if application logic were bypassed, the database refuses a second posting for the same
  document line.
- **Optimistic document state.** Status transitions only move forward. Re-completing a completed sale
  or re-receiving a received purchase raises a business-rule error rather than duplicating stock.

## Money and quantity handling

`DecimalMath` centralises rounding so every layer agrees:

- quantities are rounded to 3 decimal places;
- money and costs are rounded to 2 decimal places;
- weighted-average cost is rounded to 4 decimal places;
- the weighted-average formula is `(oldQty·oldCost + inQty·inCost) / (oldQty + inQty)`.

The database mirrors these precisions with `numeric(18,3)`, `numeric(18,2)` and `numeric(18,4)`
columns and check constraints for non-negative quantities, costs and balances.

## Audit and document numbering

- `IAuditService` writes an append-only `AuditLogs` row with user, action, entity, details, IP address
  and timestamp. It is called inside the same transaction as the business change.
- `IDocumentNumberService` generates human-readable, gap-free sequences for purchases (`PO-…`), sales
  (`SO-…`) and adjustments (`ADJ-…`) using PostgreSQL sequences.

## Error handling

- Known business failures are caught by controllers and shown as form errors.
- Unknown exceptions are logged and, outside Development, rendered by the friendly error page.
- Status codes are re-executed through `HomeController.HttpStatusCode`, which renders a consistent
  status page inside the application shell where possible.

## Extensibility

- New inventory documents follow the same pattern: entity + configuration + service with a locked
  transaction + ledger rows + audit entry + controller + views.
- A location or lot dimension can be added by extending `InventoryBalances` and the ledger without
  changing the valuation helpers.
- Report queries live in `ReportService` and can be reused by an API surface later.
