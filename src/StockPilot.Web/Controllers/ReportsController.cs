using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly IReportService _reports;
    private readonly IDashboardService _dashboard;
    private readonly IProductService _products;
    private readonly ISupplierService _suppliers;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReportsController(
        IReportService reports,
        IDashboardService dashboard,
        IProductService products,
        ISupplierService suppliers,
        UserManager<ApplicationUser> userManager)
    {
        _reports = reports;
        _dashboard = dashboard;
        _products = products;
        _suppliers = suppliers;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var model = await _dashboard.GetAsync(new DashboardFilter(today.AddDays(-29), today), cancellationToken);
        return View(model);
    }

    [HttpGet]
    public Task<IActionResult> Stock(string? search, int? categoryId, string? stockStatus, bool includeInactive = true,
        string? sort = null, int page = 1, CancellationToken cancellationToken = default) =>
        StockViewAsync(new StockReportFilter(search, categoryId, stockStatus, includeInactive, sort), page,
            "Current stock", "Live balances and valuation from the stock ledger.", false, cancellationToken);

    [HttpGet]
    public Task<IActionResult> LowStock(string? search, int? categoryId, bool includeInactive = true,
        string? sort = null, int page = 1, CancellationToken cancellationToken = default) =>
        StockViewAsync(new StockReportFilter(search, categoryId, "low", includeInactive, sort), page,
            "Low-stock report", "Active stock at or below its reorder level.", false, cancellationToken);

    [HttpGet]
    public Task<IActionResult> OutOfStock(string? search, int? categoryId, bool includeInactive = true,
        string? sort = null, int page = 1, CancellationToken cancellationToken = default) =>
        StockViewAsync(new StockReportFilter(search, categoryId, "out", includeInactive, sort), page,
            "Out-of-stock report", "Products with no available quantity.", false, cancellationToken);

    [HttpGet]
    public Task<IActionResult> Valuation(string? search, int? categoryId, bool includeInactive = true,
        string? sort = "value", int page = 1, CancellationToken cancellationToken = default) =>
        StockViewAsync(new StockReportFilter(search, categoryId, null, includeInactive, sort), page,
            "Inventory valuation", "Weighted-average cost valuation. Selling prices are never used.", true, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> ExportStock(string? search, int? categoryId, string? stockStatus,
        bool includeInactive = true, string? sort = null, CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetStockRowsForExportAsync(
            new StockReportFilter(search, categoryId, stockStatus, includeInactive, sort), cancellationToken);
        return CsvFile("stock-report", StockCsvHeaders, rows.Select(Row));
    }

    [HttpGet]
    public async Task<IActionResult> ExportLowStock(string? search, int? categoryId, bool includeInactive = true,
        string? sort = null, CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetStockRowsForExportAsync(
            new StockReportFilter(search, categoryId, "low", includeInactive, sort), cancellationToken);
        return CsvFile("low-stock-report", StockCsvHeaders, rows.Select(Row));
    }

    [HttpGet]
    public async Task<IActionResult> ExportOutOfStock(string? search, int? categoryId, bool includeInactive = true,
        string? sort = null, CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetStockRowsForExportAsync(
            new StockReportFilter(search, categoryId, "out", includeInactive, sort), cancellationToken);
        return CsvFile("out-of-stock-report", StockCsvHeaders, rows.Select(Row));
    }

    [HttpGet]
    public async Task<IActionResult> ExportValuation(string? search, int? categoryId, bool includeInactive = true,
        string? sort = "value", CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetStockRowsForExportAsync(
            new StockReportFilter(search, categoryId, null, includeInactive, sort), cancellationToken);
        return CsvFile("inventory-valuation", StockCsvHeaders, rows.Select(Row));
    }

    [HttpGet]
    public async Task<IActionResult> Purchases(string? search, int? supplierId, PurchaseStatus? status,
        DateTime? from, DateTime? to, int page = 1, CancellationToken cancellationToken = default)
    {
        var (rows, summary) = await _reports.GetPurchaseReportAsync(
            new PurchaseReportFilter(search, supplierId, status, from, to), page, 20, cancellationToken);

        var suppliers = await _suppliers.GetActiveAsync(cancellationToken);
        return View(new PurchaseReportPageViewModel
        {
            Filter = new PurchaseReportFilter(search, supplierId, status, from, to),
            Rows = rows,
            Summary = summary,
            Suppliers = suppliers.Select(s => new SelectListItem(s.BusinessName, s.Id.ToString())).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExportPurchases(string? search, int? supplierId, PurchaseStatus? status,
        DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetPurchaseRowsForExportAsync(
            new PurchaseReportFilter(search, supplierId, status, from, to), cancellationToken);

        return CsvFile("purchases", ["Number", "Supplier", "Status", "Created (UTC)", "Received (UTC)", "Received by", "Lines", "Total"],
            rows.Select(r => new object?[]
            {
                r.Number, r.Supplier, r.Status.ToString(), r.CreatedAtUtc, r.ReceivedAtUtc, r.ReceivedBy, r.LineCount, r.TotalAmount
            }));
    }

    [HttpGet]
    public async Task<IActionResult> Sales(string? search, SaleStatus? status, DateTime? from, DateTime? to,
        int page = 1, CancellationToken cancellationToken = default)
    {
        var (rows, summary) = await _reports.GetSaleReportAsync(
            new SaleReportFilter(search, status, from, to), page, 20, cancellationToken);

        return View(new SaleReportPageViewModel
        {
            Filter = new SaleReportFilter(search, status, from, to),
            Rows = rows,
            Summary = summary
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExportSales(string? search, SaleStatus? status, DateTime? from, DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetSaleRowsForExportAsync(new SaleReportFilter(search, status, from, to), cancellationToken);
        return CsvFile("sales", ["Number", "Customer", "Status", "Created (UTC)", "Completed (UTC)", "Completed by", "Lines", "Total"],
            rows.Select(r => new object?[]
            {
                r.Number, r.Customer, r.Status.ToString(), r.CreatedAtUtc, r.CompletedAtUtc, r.CompletedBy, r.LineCount, r.TotalAmount
            }));
    }

    [HttpGet]
    public async Task<IActionResult> Movements(int? productId, StockMovementType? movementType, string? userId,
        DateTime? from, DateTime? to, string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var filter = new StockMovementFilter(productId, movementType, userId, from, to, search, page, 25);
        var rows = await _reports.GetMovementReportAsync(filter, page, 25, cancellationToken);

        var products = await _products.GetOptionsAsync(activeOnly: false, cancellationToken);
        var users = await _userManager.Users.AsNoTracking().OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName }).ToListAsync(cancellationToken);

        return View(new MovementReportPageViewModel
        {
            Filter = filter,
            Rows = rows,
            Products = products.Select(p => new SelectListItem(p.DisplayName, p.Id.ToString())).ToList(),
            Users = users.Select(u => new SelectListItem(u.FullName, u.Id)).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExportMovements(int? productId, StockMovementType? movementType, string? userId,
        DateTime? from, DateTime? to, string? search, CancellationToken cancellationToken = default)
    {
        var rows = await _reports.GetMovementRowsForExportAsync(
            new StockMovementFilter(productId, movementType, userId, from, to, search), cancellationToken);

        return CsvFile("stock-movements", ["Date (UTC)", "SKU", "Product", "Type", "Quantity change", "Balance after", "Unit cost", "Value change", "Reference", "Reason", "User"],
            rows.Select(r => new object?[]
            {
                r.CreatedAtUtc, r.Sku, r.Product, r.MovementType.ToString(), r.QuantityChange, r.BalanceAfter,
                r.UnitCost, r.ValueChange, r.ReferenceNumber, r.Reason, r.User
            }));
    }

    private async Task<IActionResult> StockViewAsync(
        StockReportFilter filter, int page, string title, string description, bool isValuation,
        CancellationToken cancellationToken)
    {
        var report = await _reports.GetStockReportAsync(filter, page, 25, cancellationToken);
        var categories = await _products.GetCategoriesAsync(activeOnly: false, cancellationToken);

        return View("Stock", new StockReportPageViewModel
        {
            Filter = filter,
            Page = report,
            Title = title,
            Description = description,
            IsValuation = isValuation,
            Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList(),
            CategoryValuation = isValuation
                ? await _reports.GetCategoryValuationAsync(filter, cancellationToken)
                : []
        });
    }

    private static readonly string[] StockCsvHeaders =
        ["SKU", "Product", "Category", "Unit", "Quantity on hand", "Reorder level", "Average cost", "Stock value", "Status", "Active"];

    private static object?[] Row(StockReportRow r) =>
    [
        r.Sku, r.Name, r.Category, r.UnitCode, r.Quantity, r.ReorderLevel, r.AverageCost, r.StockValue, r.StockStatus, r.IsActive
    ];

    private IActionResult CsvFile(string name, IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows) =>
        File(CsvExport.Build(headers, rows), "text/csv", $"{name}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
}
