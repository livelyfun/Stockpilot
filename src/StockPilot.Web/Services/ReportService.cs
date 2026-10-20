using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Services;

public interface IReportService
{
    Task<StockReportPage> GetStockReportAsync(StockReportFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<StockReportRow>> GetStockRowsForExportAsync(StockReportFilter filter, CancellationToken cancellationToken = default);
    Task<List<CategoryValuationRow>> GetCategoryValuationAsync(StockReportFilter filter, CancellationToken cancellationToken = default);
    Task<(PaginatedList<PurchaseReportRow> Rows, PurchaseReportSummary Summary)> GetPurchaseReportAsync(PurchaseReportFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<PurchaseReportRow>> GetPurchaseRowsForExportAsync(PurchaseReportFilter filter, CancellationToken cancellationToken = default);
    Task<(PaginatedList<SaleReportRow> Rows, SaleReportSummary Summary)> GetSaleReportAsync(SaleReportFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<SaleReportRow>> GetSaleRowsForExportAsync(SaleReportFilter filter, CancellationToken cancellationToken = default);
    Task<PaginatedList<MovementReportRow>> GetMovementReportAsync(StockMovementFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<MovementReportRow>> GetMovementRowsForExportAsync(StockMovementFilter filter, CancellationToken cancellationToken = default);
    Task<List<ActivityDayRow>> GetActivityDaysAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}

public class ReportService : IReportService
{
    private const int MaxExportRows = 10000;
    private readonly ApplicationDbContext _db;

    public ReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<StockReportPage> GetStockReportAsync(StockReportFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = BuildStockQuery(filter);
        var summary = await BuildStockSummaryAsync(query, cancellationToken);
        var projected = ProjectStock(query, filter.Sort);
        var rows = await PaginatedList<StockReportRow>.CreateAsync(projected, page, pageSize, cancellationToken);
        return new StockReportPage(rows, summary);
    }

    public async Task<List<StockReportRow>> GetStockRowsForExportAsync(StockReportFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildStockQuery(filter);
        return await ProjectStock(query, filter.Sort).Take(MaxExportRows).ToListAsync(cancellationToken);
    }

    public async Task<List<CategoryValuationRow>> GetCategoryValuationAsync(StockReportFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildStockQuery(filter);
        return await query
            .GroupBy(p => p.Category.Name)
            .Select(g => new CategoryValuationRow(
                g.Key,
                g.Count(),
                g.Sum(p => p.InventoryBalance!.QuantityOnHand),
                g.Sum(p => p.InventoryBalance!.QuantityOnHand * p.InventoryBalance!.AverageCost)))
            .OrderByDescending(r => r.TotalValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<(PaginatedList<PurchaseReportRow> Rows, PurchaseReportSummary Summary)> GetPurchaseReportAsync(
        PurchaseReportFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = BuildPurchaseQuery(filter);
        var rows = await PaginatedList<PurchaseReportRow>.CreateAsync(
            ProjectPurchases(query), page, pageSize, cancellationToken);
        return (rows, await BuildPurchaseSummaryAsync(query, cancellationToken));
    }

    public Task<List<PurchaseReportRow>> GetPurchaseRowsForExportAsync(PurchaseReportFilter filter, CancellationToken cancellationToken = default) =>
        ProjectPurchases(BuildPurchaseQuery(filter)).Take(MaxExportRows).ToListAsync(cancellationToken);

    public async Task<(PaginatedList<SaleReportRow> Rows, SaleReportSummary Summary)> GetSaleReportAsync(
        SaleReportFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = BuildSaleQuery(filter);
        var rows = await PaginatedList<SaleReportRow>.CreateAsync(
            ProjectSales(query), page, pageSize, cancellationToken);
        return (rows, await BuildSaleSummaryAsync(query, cancellationToken));
    }

    public Task<List<SaleReportRow>> GetSaleRowsForExportAsync(SaleReportFilter filter, CancellationToken cancellationToken = default) =>
        ProjectSales(BuildSaleQuery(filter)).Take(MaxExportRows).ToListAsync(cancellationToken);

    public Task<PaginatedList<MovementReportRow>> GetMovementReportAsync(
        StockMovementFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = BuildMovementQueryInternal(filter);
        return PaginatedList<MovementReportRow>.CreateAsync(
            ProjectMovements(query), page, pageSize, cancellationToken);
    }

    public Task<List<MovementReportRow>> GetMovementRowsForExportAsync(StockMovementFilter filter, CancellationToken cancellationToken = default) =>
        ProjectMovements(BuildMovementQueryInternal(filter)).Take(MaxExportRows).ToListAsync(cancellationToken);

    public async Task<List<ActivityDayRow>> GetActivityDaysAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = fromUtc.Date;
        var toExclusive = toUtc.Date.AddDays(1);
        if (from >= toExclusive)
        {
            throw new ArgumentException("The start date must be before the end date.");
        }

        var purchases = await _db.Purchases
            .Where(p => p.Status == PurchaseStatus.Received &&
                        p.ReceivedAtUtc >= from && p.ReceivedAtUtc < toExclusive)
            .GroupBy(p => p.ReceivedAtUtc!.Value.Date)
            .Select(g => new { Day = g.Key, Total = g.Sum(p => p.TotalAmount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        var sales = await _db.Sales
            .Where(s => s.Status == SaleStatus.Completed &&
                        s.CompletedAtUtc >= from && s.CompletedAtUtc < toExclusive)
            .GroupBy(s => s.CompletedAtUtc!.Value.Date)
            .Select(g => new { Day = g.Key, Total = g.Sum(s => s.TotalAmount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        var result = new List<ActivityDayRow>();
        for (var day = from; day < toExclusive; day = day.AddDays(1))
        {
            var purchase = purchases.FirstOrDefault(x => x.Day == day);
            var sale = sales.FirstOrDefault(x => x.Day == day);
            result.Add(new ActivityDayRow(day, purchase?.Total ?? 0m, sale?.Total ?? 0m, sale?.Count ?? 0, purchase?.Count ?? 0));
        }

        return result;
    }

    private IQueryable<Product> BuildStockQuery(StockReportFilter filter)
    {
        var query = _db.Products.AsNoTracking().AsQueryable();

        if (!filter.IncludeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%") ||
                EF.Functions.ILike(p.Sku, $"%{term}%"));
        }

        if (filter.CategoryId is > 0)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId);
        }

        query = filter.StockStatus switch
        {
            "out" => query.Where(p => p.InventoryBalance!.QuantityOnHand <= 0),
            "low" => query.Where(p => p.InventoryBalance!.QuantityOnHand > 0 &&
                                      p.InventoryBalance.QuantityOnHand <= p.ReorderLevel),
            "in" => query.Where(p => p.InventoryBalance!.QuantityOnHand > p.ReorderLevel),
            _ => query
        };

        return query;
    }

    private static IQueryable<StockReportRow> ProjectStock(IQueryable<Product> query, string? sort)
    {
        query = sort switch
        {
            "sku" => query.OrderBy(p => p.Sku),
            "value" => query.OrderByDescending(p => p.InventoryBalance!.QuantityOnHand * p.InventoryBalance!.AverageCost),
            "quantity" => query.OrderByDescending(p => p.InventoryBalance!.QuantityOnHand),
            "category" => query.OrderBy(p => p.Category.Name).ThenBy(p => p.Name),
            _ => query.OrderBy(p => p.Name)
        };

        return query.Select(p => new StockReportRow(
            p.Id,
            p.Sku,
            p.Name,
            p.Category.Name,
            p.Unit.Name,
            p.Unit.Code,
            p.InventoryBalance!.QuantityOnHand,
            p.ReorderLevel,
            p.InventoryBalance.AverageCost,
            p.IsActive));
    }

    private static async Task<StockReportSummary> BuildStockSummaryAsync(IQueryable<Product> query, CancellationToken cancellationToken)
    {
        var productCount = await query.CountAsync(cancellationToken);
        var low = await query.CountAsync(p => p.InventoryBalance!.QuantityOnHand > 0 &&
                                              p.InventoryBalance.QuantityOnHand <= p.ReorderLevel, cancellationToken);
        var outOfStock = await query.CountAsync(p => p.InventoryBalance!.QuantityOnHand <= 0, cancellationToken);
        var totalQuantity = await query
            .Select(p => (decimal?)p.InventoryBalance!.QuantityOnHand)
            .SumAsync(cancellationToken) ?? 0m;
        var totalValue = await query
            .Select(p => (decimal?)(p.InventoryBalance!.QuantityOnHand * p.InventoryBalance.AverageCost))
            .SumAsync(cancellationToken) ?? 0m;

        return new StockReportSummary(productCount, low, outOfStock, totalQuantity, totalValue);
    }

    private IQueryable<Purchase> BuildPurchaseQuery(PurchaseReportFilter filter)
    {
        var query = _db.Purchases.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.PurchaseNumber, $"%{term}%") ||
                EF.Functions.ILike(p.Supplier.BusinessName, $"%{term}%"));
        }

        if (filter.SupplierId is > 0)
        {
            query = query.Where(p => p.SupplierId == filter.SupplierId);
        }

        if (filter.Status is not null)
        {
            query = query.Where(p => p.Status == filter.Status);
        }

        if (filter.From is not null)
        {
            query = query.Where(p => p.CreatedAtUtc >= filter.From.Value.Date);
        }

        if (filter.To is not null)
        {
            var toExclusive = filter.To.Value.Date.AddDays(1);
            query = query.Where(p => p.CreatedAtUtc < toExclusive);
        }

        return query;
    }

    private static IQueryable<PurchaseReportRow> ProjectPurchases(IQueryable<Purchase> query) =>
        query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id)
            .Select(p => new PurchaseReportRow(
                p.Id,
                p.PurchaseNumber,
                p.Supplier.BusinessName,
                p.Status,
                p.CreatedAtUtc,
                p.ReceivedAtUtc,
                p.ReceivedByUser != null ? p.ReceivedByUser.UserName : null,
                p.TotalAmount,
                p.Items.Count));

    private static async Task<PurchaseReportSummary> BuildPurchaseSummaryAsync(IQueryable<Purchase> query, CancellationToken cancellationToken)
    {
        var groups = await query
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(p => p.TotalAmount) })
            .ToListAsync(cancellationToken);

        return new PurchaseReportSummary(
            groups.Sum(g => g.Count),
            groups.Where(g => g.Status == PurchaseStatus.Received).Sum(g => g.Count),
            groups.Where(g => g.Status == PurchaseStatus.Draft).Sum(g => g.Count),
            groups.Where(g => g.Status == PurchaseStatus.Cancelled).Sum(g => g.Count),
            groups.Where(g => g.Status == PurchaseStatus.Reversed).Sum(g => g.Count),
            groups.Where(g => g.Status == PurchaseStatus.Received).Sum(g => g.Total));
    }

    private IQueryable<Sale> BuildSaleQuery(SaleReportFilter filter)
    {
        var query = _db.Sales.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s =>
                EF.Functions.ILike(s.SaleNumber, $"%{term}%") ||
                (s.CustomerName != null && EF.Functions.ILike(s.CustomerName, $"%{term}%")));
        }

        if (filter.Status is not null)
        {
            query = query.Where(s => s.Status == filter.Status);
        }

        if (filter.From is not null)
        {
            query = query.Where(s => s.CreatedAtUtc >= filter.From.Value.Date);
        }

        if (filter.To is not null)
        {
            var toExclusive = filter.To.Value.Date.AddDays(1);
            query = query.Where(s => s.CreatedAtUtc < toExclusive);
        }

        return query;
    }

    private static IQueryable<SaleReportRow> ProjectSales(IQueryable<Sale> query) =>
        query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Select(s => new SaleReportRow(
                s.Id,
                s.SaleNumber,
                s.CustomerName,
                s.Status,
                s.CreatedAtUtc,
                s.CompletedAtUtc,
                s.CompletedByUser != null ? s.CompletedByUser.UserName : null,
                s.TotalAmount,
                s.Items.Count));

    private static async Task<SaleReportSummary> BuildSaleSummaryAsync(IQueryable<Sale> query, CancellationToken cancellationToken)
    {
        var groups = await query
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(s => s.TotalAmount) })
            .ToListAsync(cancellationToken);

        return new SaleReportSummary(
            groups.Sum(g => g.Count),
            groups.Where(g => g.Status == SaleStatus.Completed).Sum(g => g.Count),
            groups.Where(g => g.Status == SaleStatus.Draft).Sum(g => g.Count),
            groups.Where(g => g.Status == SaleStatus.Cancelled).Sum(g => g.Count),
            groups.Where(g => g.Status == SaleStatus.Reversed).Sum(g => g.Count),
            groups.Where(g => g.Status == SaleStatus.Completed).Sum(g => g.Total));
    }

    private static IQueryable<MovementReportRow> ProjectMovements(IQueryable<StockMovement> query) =>
        query
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Select(m => new MovementReportRow(
                m.Id,
                m.Product.Sku,
                m.Product.Name,
                m.MovementType,
                m.QuantityChange,
                m.BalanceAfter,
                m.UnitCost,
                m.QuantityChange * m.UnitCost,
                m.ReferenceNumber,
                m.Reason,
                m.User.UserName ?? "system",
                m.CreatedAtUtc));

    private IQueryable<StockMovement> BuildMovementQueryInternal(StockMovementFilter filter)
    {
        var query = _db.StockMovements.AsNoTracking().AsQueryable();

        if (filter.ProductId is > 0)
        {
            query = query.Where(m => m.ProductId == filter.ProductId);
        }

        if (filter.MovementType is not null)
        {
            query = query.Where(m => m.MovementType == filter.MovementType);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            query = query.Where(m => m.UserId == filter.UserId);
        }

        if (filter.From is not null)
        {
            query = query.Where(m => m.CreatedAtUtc >= filter.From.Value.Date);
        }

        if (filter.To is not null)
        {
            var toExclusive = filter.To.Value.Date.AddDays(1);
            query = query.Where(m => m.CreatedAtUtc < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(m =>
                EF.Functions.ILike(m.Product.Name, $"%{term}%") ||
                EF.Functions.ILike(m.Product.Sku, $"%{term}%") ||
                (m.ReferenceNumber != null && EF.Functions.ILike(m.ReferenceNumber, $"%{term}%")));
        }

        return query;
    }
}
