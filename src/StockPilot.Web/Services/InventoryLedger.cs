using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

/// <summary>
/// Central helper for locking inventory rows. PostgreSQL row locks are held until
/// the surrounding transaction commits or rolls back.
/// </summary>
public static class InventoryLedger
{
    public static async Task<InventoryBalance> LockBalanceAsync(
        ApplicationDbContext db,
        int productId,
        CancellationToken cancellationToken = default)
    {
        var balance = await db.InventoryBalances
            .FromSqlInterpolated($"SELECT * FROM \"InventoryBalances\" WHERE \"ProductId\" = {productId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("The product has no inventory balance record.");

        return balance;
    }

    public static void EnsureNonNegativeQuantity(decimal quantity, string productName)
    {
        if (quantity < 0)
        {
            throw new BusinessRuleException($"Insufficient stock for {productName}.");
        }
    }
}
