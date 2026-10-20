# Database design

StockPilot targets PostgreSQL 17 and uses EF Core migrations as the single source of schema truth.
Naming follows the EF model (PascalCase tables and columns) and enums are stored as strings for
readable exports and ledger inspection.

## Entity-relationship diagram

```mermaid
erDiagram
    ApplicationUser ||--o{ Purchase : "creates / receives / reverses"
    ApplicationUser ||--o{ Sale : "creates / completes / reverses"
    ApplicationUser ||--o{ StockMovement : "records"
    ApplicationUser ||--o{ AuditLog : "performs"

    Category ||--o{ Product : "classifies"
    Unit ||--o{ Product : "measures"
    Product ||--|| InventoryBalance : "has balance"
    Product ||--o{ StockMovement : "ledger entries"
    Product ||--o{ PurchaseItem : "ordered as"
    Product ||--o{ SaleItem : "sold as"

    Supplier ||--o{ Purchase : "supplies"
    Purchase ||--o{ PurchaseItem : "contains"
    Sale ||--o{ SaleItem : "contains"

    Category {
        int Id PK
        string Name UK
        string Description
        bool IsActive
    }
    Unit {
        int Id PK
        string Name UK
        string Code UK
        bool IsActive
    }
    Product {
        int Id PK
        string Sku UK
        string Name
        int CategoryId FK
        int UnitId FK
        numeric CostPrice
        numeric SellingPrice
        numeric ReorderLevel
        bool IsActive
    }
    InventoryBalance {
        int ProductId PK_FK
        numeric QuantityOnHand
        numeric AverageCost
        datetime UpdatedAtUtc
    }
    StockMovement {
        bigint Id PK
        int ProductId FK
        string MovementType
        numeric QuantityChange
        numeric BalanceAfter
        numeric UnitCost
        string ReferenceType
        int ReferenceId
        string ReferenceNumber
        string Reason
        string UserId FK
        datetime CreatedAtUtc
    }
    Supplier {
        int Id PK
        string BusinessName
        bool IsActive
    }
    Purchase {
        int Id PK
        string PurchaseNumber UK
        int SupplierId FK
        string Status
        numeric TotalAmount
        datetime ReceivedAtUtc
    }
    PurchaseItem {
        int Id PK
        int PurchaseId FK
        int ProductId FK
        numeric Quantity
        numeric UnitCost
        numeric LineTotal
    }
    Sale {
        int Id PK
        string SaleNumber UK
        string CustomerName
        string Status
        numeric TotalAmount
        datetime CompletedAtUtc
    }
    SaleItem {
        int Id PK
        int SaleId FK
        int ProductId FK
        numeric Quantity
        numeric UnitPrice
        numeric LineTotal
    }
    AuditLog {
        bigint Id PK
        string UserName
        string Action
        string EntityType
        string EntityId
        string Details
        string IpAddress
        datetime CreatedAtUtc
    }
```

## Tables

### Master data

| Table | Purpose | Key constraints |
|---|---|---|
| `Categories` | Product grouping | unique `Name` |
| `Units` | Unit of measurement (name + short code) | unique `Name`, unique `Code` |
| `Products` | Sellable/purchasable item | unique `Sku`, FK to category and unit, check non-negative prices |
| `Suppliers` | Supplier directory | FKs from purchases |

### Identity

ASP.NET Core Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, …) plus an extended
`ApplicationUser` with `FullName`, `IsActive`, `CreatedAtUtc` and `LastLoginAtUtc`. There is no public
registration; an administrator creates accounts.

### Inventory

| Table | Purpose |
|---|---|
| `InventoryBalances` | One row per product: quantity on hand and weighted-average cost |
| `StockMovements` | Immutable ledger of quantity changes with balance snapshots, cost and references |

`StockMovementType` values: `OpeningStock`, `PurchaseReceipt`, `SaleIssue`, `AdjustmentIncrease`,
`AdjustmentDecrease`, `SaleReversal`, `PurchaseReversal`.

### Documents

| Table | Purpose |
|---|---|
| `Purchases` / `PurchaseItems` | Supplier orders, one line per product |
| `Sales` / `SaleItems` | Customer sales, one line per product |
| `AuditLogs` | Append-only audit record |

Document statuses:

- `PurchaseStatus`: `Draft`, `Received`, `Cancelled`, `Reversed`
- `SaleStatus`: `Draft`, `Completed`, `Cancelled`, `Reversed`

## Integrity rules

### Check constraints

- `CK_InventoryBalances_Quantity_NonNegative` – `QuantityOnHand >= 0`
- `CK_InventoryBalances_AverageCost_NonNegative` – `AverageCost >= 0`
- `CK_StockMovements_QuantityChange_NonZero` – `QuantityChange <> 0`
- `CK_StockMovements_BalanceAfter_NonNegative` – `BalanceAfter >= 0`
- `CK_StockMovements_UnitCost_NonNegative` – `UnitCost >= 0`

### Unique indexes

| Index | Columns | Meaning |
|---|---|---|
| `IX_Products_Sku` | `Sku` | one product per SKU |
| `IX_Purchases_PurchaseNumber` | `PurchaseNumber` | unique document number |
| `IX_Sales_SaleNumber` | `SaleNumber` | unique document number |
| `IX_PurchaseItems_Purchase_Product` | `PurchaseId`, `ProductId` | one line per product per purchase |
| `IX_SaleItems_Sale_Product` | `SaleId`, `ProductId` | one line per product per sale |
| `IX_StockMovements_One_Per_Transaction` | `MovementType`, `ReferenceId`, `ProductId` (filtered, unique) | one posting per document line per movement type |

The filtered uniqueness predicate is:

```sql
"ReferenceId" IS NOT NULL
AND "MovementType" IN ('PurchaseReceipt','SaleIssue','SaleReversal','PurchaseReversal')
```

### Query indexes

The ledger also indexes `(ProductId, CreatedAtUtc)`, `(MovementType, CreatedAtUtc)`,
`(UserId, CreatedAtUtc)` and `(ReferenceType, ReferenceId)` to keep reports and product history fast.

### Delete behaviour

- Deleting a product cascades to its `InventoryBalances` row but **restricts** if ledger entries or
  document lines reference it; products used in history are deactivated instead of deleted.
- Ledger rows and audit rows reference users with restrictive / `SET NULL` behaviour so history is
  preserved when accounts change.

## Migrations

```bash
set -a && . ./.env && set +a
dotnet tool restore
dotnet ef migrations add <Name> --project src/StockPilot.Web --startup-project src/StockPilot.Web
dotnet ef database update      --project src/StockPilot.Web --startup-project src/StockPilot.Web
```

Existing migrations:

1. `InitialCreate` – full schema, sequences, constraints and indexes.
2. `FixStockMovementTransactionIndex` – widens the duplicate-posting guard to include `ProductId`, so a
   multi-line purchase or sale can post one ledger row per line while still rejecting duplicates.

Migrations run automatically at startup when `Database__AutoMigrate=true`, which is convenient for
development and single-instance deployment. For zero-downtime deployments, run migrations as a
separate step and set the flag to `false`.
