# Demo script

A guided walkthrough for a presentation, viva or review. It takes roughly 10–15 minutes and shows
every module. The screenshots in `docs/screenshots/` show the expected result at each step.

## Before you start

```bash
podman start stockpilot-pg            # or: docker start stockpilot-pg
set -a && . ./.env && set +a
dotnet run --project src/StockPilot.Web --urls http://127.0.0.1:5080
```

With `Seed__DemoData=true` the app seeds demonstration data through the real domain services, so the
ledger and balances are already consistent. Sign in as the administrator (`Seed__AdminEmail` /
`Seed__AdminPassword`).

## 1. Sign in (UC-1)

- Open <http://127.0.0.1:5080>. The login page is the only page reachable without a session.
- Show the empty-state messaging and accessible labels.
- Sign in. You land on the dashboard.

**Talking point:** there is no public registration; accounts are created by an administrator.

## 2. Dashboard

- Point out inventory value, product/category/supplier counts, low-stock and out-of-stock indicators,
  the period performance figures (revenue, COGS, purchase spend, gross margin) and the 14-day activity
  chart — all derived from the stock ledger.
- Note the responsive sidebar and header, and that every number links to the relevant report.

## 3. Master data

- **Products** – filter and search, open a product to see its balance, average cost and movement
  history. Note the reorder level and active/inactive state.
- Briefly show **Categories**, **Units** and **Suppliers**.

**Talking point:** products used in transactions are deactivated, never deleted, so history stays
readable.

## 4. Purchase stock (UC-3)

1. **Purchases → New purchase**. Choose a supplier, add a line (product, quantity, unit cost). Save the
   draft.
2. On the draft's detail page, show the status badge and the actions.
3. Click **Receive**. Confirm the dialog.

**Result:** the status becomes `Received`; the product's quantity and weighted-average cost increase;
a `Purchase receipt` row appears in the stock ledger.

**Talking point:** receiving twice is rejected — try it and show the error, or explain that the
document locks and the unique ledger index guard against double posting.

## 5. Sell stock (UC-4)

1. **Sales → New sale**. Enter a customer and a line. Save the draft.
2. Click **Complete**. Confirm.

**Result:** stock is deducted after a re-check; the ledger shows a `Sale issue` with the average cost
**at issue time**; the sale is `Completed`.

3. Show the insufficient-stock case: create a sale for more than is on hand and complete it. The
   completion is rejected with a clear message and nothing is written.

**Talking point:** the check is performed under a row lock, so two people completing sales at the same
moment cannot oversell the same product (covered by an automated test).

## 6. Reverse a transaction (Admin)

- On the completed sale, scroll to **Reverse sale**, enter a reason and confirm.

**Result:** the status becomes `Reversed`; stock is restored at the *recorded issue cost*; a
compensating ledger row is written; the reason is stored and audited.

**Talking point:** nothing is deleted — reversals keep history intact.

## 7. Stock adjustments (UC-5)

- **Stock movements → Adjust stock**. Pick a product, choose increase or decrease, enter a quantity and
  a reason.
- Try a decrease larger than the balance to show the rejection.

**Result:** a ledger row (`Adjustment increase`/`Adjustment decrease`) with your reason.

- Also show **Opening stock** for setting a starting balance.

## 8. Reports (UC-6)

- Open **Reports** and visit:
  - **Current stock** – live quantity, reorder level, cost and value.
  - **Low stock** and **Out of stock** – replenishment views.
  - **Inventory valuation** – value per product plus the per-category breakdown.
  - **Purchase report** and **Sales report** – document status and totals.
  - **Stock ledger** – full movement history with cost and value impact.
- On any report, click **Export CSV** and show the downloaded file (headers match the table).

## 9. Users and audit (UC-7, UC-8)

- **Users & roles** – create a user with the **Viewer** role, then sign in as that user in a second
  browser profile to show read-only access (create buttons disappear, protected pages redirect to
  access denied).
- Show the guards: you cannot deactivate yourself or remove the last administrator.
- **Audit logs** – filter by action or date and open an entry to show the full details, user and IP.

## 10. Wrap up

- Show the test suite: `dotnet test tests/StockPilot.Tests/StockPilot.Tests.csproj` — real PostgreSQL,
  concurrency and authorization tests.
- Point at `docs/database.md` for the ER diagram and integrity rules, and `docs/deployment.md` for the
  container stack.
