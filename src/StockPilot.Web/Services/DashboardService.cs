using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetAsync(DashboardFilter filter, CancellationToken cancellationToken = default);
}

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;

    public DashboardService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardViewModel> GetAsync(DashboardFilter filter, CancellationToken cancellationToken = default)
    {
        var from = filter.FromUtc.Date;
        var toExclusive = filter.ToUtc.Date.AddDays(1);
        if (from >= toExclusive)
        {
            throw new ArgumentException("The start date must be before the end date.");
        }

        var model = new DashboardViewModel
        {
            FromUtc = from,
            ToUtc = toExclusive.AddTicks(-1),
            ActiveProducts = await _db.Products.CountAsync(p => p.IsActive, cancellationToken),
            ActiveCategories = await _db.Categories.CountAsync(c => c.IsActive, cancellationToken),
            ActiveSuppliers = await _db.Suppliers.CountAsync(s => s.IsActive, cancellationToken),
            OutOfStockCount = await _db.InventoryBalances.CountAsync(b => b.QuantityOnHand <= 0, cancellationToken),
            LowStockCount = await _db.InventoryBalances.CountAsync(b =>
                b.QuantityOnHand > 0 && b.QuantityOnHand <= b.Product.ReorderLevel, cancellationToken),
            InventoryValue = await _db.InventoryBalances
                .Select(b => (decimal?)(b.QuantityOnHand * b.AverageCost))
                .SumAsync(cancellationToken) ?? 0m,
            PeriodPurchaseTotal = await _db.Purchases
                .Where(p => p.Status == PurchaseStatus.Received &&
                            p.ReceivedAtUtc >= from && p.ReceivedAtUtc < toExclusive)
                .Select(p => (decimal?)p.TotalAmount)
                .SumAsync(cancellationToken) ?? 0m,
            PeriodSalesTotal = await _db.Sales
                .Where(s => s.Status == SaleStatus.Completed &&
                            s.CompletedAtUtc >= from && s.CompletedAtUtc < toExclusive)
                .Select(s => (decimal?)s.TotalAmount)
                .SumAsync(cancellationToken) ?? 0m
        };

        model.PeriodCostOfGoodsSold = await _db.StockMovements
            .Where(m => (m.MovementType == StockMovementType.SaleIssue ||
                         m.MovementType == StockMovementType.SaleReversal) &&
                        m.CreatedAtUtc >= from && m.CreatedAtUtc < toExclusive)
            .Select(m => (decimal?)(-m.QuantityChange * m.UnitCost))
            .SumAsync(cancellationToken) ?? 0m;

        model.RecentPurchases = await _db.Purchases
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(5)
            .Select(p => new DashboardPurchaseRow(
                p.Id, p.PurchaseNumber, p.Supplier.BusinessName, p.Status, p.CreatedAtUtc, p.TotalAmount))
            .ToListAsync(cancellationToken);

        model.RecentSales = await _db.Sales
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(5)
            .Select(s => new DashboardSaleRow(
                s.Id, s.SaleNumber, s.CustomerName, s.Status, s.CreatedAtUtc, s.TotalAmount))
            .ToListAsync(cancellationToken);

        model.RecentMovements = await _db.StockMovements
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Take(8)
            .Select(m => new DashboardMovementRow(
                m.Id, m.Product.Sku, m.Product.Name, m.MovementType, m.QuantityChange,
                m.BalanceAfter, m.ReferenceNumber, m.User.UserName ?? "system", m.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        // 14-day activity series, with missing days filled in application code.
        var seriesStart = toExclusive.AddDays(-14);
        var purchaseDays = await _db.Purchases
            .Where(p => p.Status == PurchaseStatus.Received &&
                        p.ReceivedAtUtc >= seriesStart && p.ReceivedAtUtc < toExclusive)
            .GroupBy(p => p.ReceivedAtUtc!.Value.Date)
            .Select(g => new { Day = g.Key, Total = g.Sum(p => p.TotalAmount) })
            .ToListAsync(cancellationToken);

        var saleDays = await _db.Sales
            .Where(s => s.Status == SaleStatus.Completed &&
                        s.CompletedAtUtc >= seriesStart && s.CompletedAtUtc < toExclusive)
            .GroupBy(s => s.CompletedAtUtc!.Value.Date)
            .Select(g => new { Day = g.Key, Total = g.Sum(s => s.TotalAmount) })
            .ToListAsync(cancellationToken);

        for (var day = seriesStart; day < toExclusive; day = day.AddDays(1))
        {
            model.ActivityDays.Add(new DashboardDayRow(
                day,
                purchaseDays.FirstOrDefault(x => x.Day == day)?.Total ?? 0m,
                saleDays.FirstOrDefault(x => x.Day == day)?.Total ?? 0m));
        }

        return model;
    }
}
