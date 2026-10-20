# Use cases

## Actors

| Actor | Description |
|---|---|
| **Administrator** | Owns the system: manages users, master data, inventory adjustments and reversals, and reviews audits. |
| **Inventory staff** | Day-to-day operator: maintains master data, creates and receives purchases, creates and completes sales. |
| **Viewer** | Read-only stakeholder: dashboard, catalogue, stock position and reports. |
| **System** | Startup tasks (migrations, roles, administrator bootstrap, optional demo seeding) and auditing. |

## UC-1 Sign in

- **Actor:** all.
- **Preconditions:** an active account exists.
- **Flow:** the user opens the sign-in page, enters email and password. Identity verifies the
  credentials and lockout state. On success the app records the sign-in in the audit log and redirects
  to the dashboard (or to the originally requested page).
- **Alternative:** invalid credentials or an inactive account show a generic error and are audited;
  repeated failures lock the account for 15 minutes.
- **Notes:** there is no self-registration and no password-reset-by-email flow; an administrator
  resets passwords.

## UC-2 Maintain master data

- **Actor:** administrator, inventory staff.
- **Flow:** the user creates or edits a category, unit, product or supplier. Validation runs on the
  server (unique names/SKUs, non-negative prices, valid references).
- **Alternative:** a product referenced by transactions cannot be deleted; it is deactivated instead so
  history remains readable.
- **Postconditions:** changes are audited.

## UC-3 Purchase stock

- **Actor:** administrator, inventory staff.
- **Preconditions:** the supplier and products exist and are active.
- **Flow:**
  1. Create a draft purchase and add one line per product with quantity and unit cost.
  2. Optionally edit the draft while it is still a draft.
  3. Receive the purchase. Inside one transaction the system locks the document, locks each product
     balance, blends the weighted-average cost, writes one `PurchaseReceipt` ledger row per line and
     marks the purchase `Received`.
- **Alternative:** cancelling a draft is allowed; a received purchase must be reversed (administrator)
  with a reason.
- **Postconditions:** stock and valuation increase exactly once; the action is audited.

## UC-4 Sell stock

- **Actor:** administrator, inventory staff.
- **Preconditions:** the products exist, are active, and enough stock is on hand.
- **Flow:**
  1. Create a draft sale with a customer name and one line per product.
  2. Optionally edit the draft.
  3. Complete the sale. Inside one transaction the system locks the document and each product balance,
     re-checks availability, deducts stock, snapshots the average cost at issue time on a `SaleIssue`
     ledger row, and marks the sale `Completed`.
- **Alternative:** if stock is insufficient the completion is rejected and nothing is written. A
  completed sale can be reversed (administrator) with a reason, restoring the exact issue cost.
- **Postconditions:** stock decreases exactly once; COGS and margin are derivable from the ledger.

## UC-5 Record opening stock or an adjustment

- **Actor:** administrator.
- **Flow:** select a product, choose increase or decrease, enter quantity, optional unit cost (increase
  only) and a reason of at least five characters.
- **Alternative:** a decrease that would take the balance below zero is rejected.
- **Postconditions:** a ledger row (`OpeningStock`, `AdjustmentIncrease`, `AdjustmentDecrease`) is
  written and audited.

## UC-6 Review stock position and reports

- **Actor:** all roles.
- **Flow:** open the dashboard for live value and stock alerts; open the stock ledger to search and
  filter movements by product, type, user and date; open reports for current stock, low stock, out of
  stock, valuation, purchases, sales or the ledger.
- **Notes:** every report can be exported to CSV with the same filters.

## UC-7 Administer users

- **Actor:** administrator.
- **Flow:** create an account with a role, edit profile/role/active status, reset a password.
- **Guards:** an administrator cannot deactivate their own account, cannot remove their own
  administrator role, and the system refuses to remove the last active administrator.
- **Postconditions:** every change is audited.

## UC-8 Review the audit log

- **Actor:** administrator.
- **Flow:** filter the audit log by free text, action and date range, then open an entry to see the
  full details, user and IP address.
- **Notes:** audit entries are append-only from the application's perspective.

## End-to-end scenario

> An administrator sets up categories, units and products, records opening stock, then creates a
> purchase and receives it. Inventory staff sell part of that stock. The administrator reviews the
> valuation report, notices a damaged item, records a decrease adjustment with a reason, reverses a
> mistaken sale, and finally inspects the audit log and stock ledger to confirm every change is
> explained.
