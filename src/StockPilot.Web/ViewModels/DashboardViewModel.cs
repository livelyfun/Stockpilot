using StockPilot.Web.Entities;

namespace StockPilot.Web.ViewModels;

public class DashboardViewModel
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int ActiveProducts { get; set; }
    public int ActiveCategories { get; set; }
    public int ActiveSuppliers { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal PeriodPurchaseTotal { get; set; }
    public decimal PeriodSalesTotal { get; set; }
    public decimal PeriodCostOfGoodsSold { get; set; }
    public decimal PeriodGrossMargin => PeriodSalesTotal - PeriodCostOfGoodsSold;
    public List<DashboardPurchaseRow> RecentPurchases { get; set; } = [];
    public List<DashboardSaleRow> RecentSales { get; set; } = [];
    public List<DashboardMovementRow> RecentMovements { get; set; } = [];
    public List<DashboardDayRow> ActivityDays { get; set; } = [];
}

public record DashboardPurchaseRow(
    int Id, string Number, string Supplier, PurchaseStatus Status, DateTime CreatedAtUtc, decimal Total);

public record DashboardSaleRow(
    int Id, string Number, string? Customer, SaleStatus Status, DateTime CreatedAtUtc, decimal Total);

public record DashboardMovementRow(
    long Id, string Sku, string Product, StockMovementType Type, decimal QuantityChange,
    decimal BalanceAfter, string? Reference, string User, DateTime CreatedAtUtc);

public record DashboardDayRow(DateTime DateUtc, decimal Purchases, decimal Sales);

public record DashboardFilter(DateTime FromUtc, DateTime ToUtc);
