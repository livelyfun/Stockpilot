using StockPilot.Web.Entities;

namespace StockPilot.Web.ViewModels;

public record StockReportFilter(
    string? Search = null,
    int? CategoryId = null,
    string? StockStatus = null,
    bool IncludeInactive = true,
    string? Sort = null);

public record StockReportRow(
    int ProductId,
    string Sku,
    string Name,
    string Category,
    string Unit,
    string UnitCode,
    decimal Quantity,
    decimal ReorderLevel,
    decimal AverageCost,
    bool IsActive)
{
    public decimal StockValue => Quantity * AverageCost;
    public string StockStatus => Quantity <= 0 ? "Out of stock" : Quantity <= ReorderLevel ? "Low stock" : "In stock";
}

public record StockReportSummary(
    int ProductCount,
    int LowStockCount,
    int OutOfStockCount,
    decimal TotalQuantity,
    decimal TotalValue);

public record StockReportPage(
    Infrastructure.PaginatedList<StockReportRow> Rows,
    StockReportSummary Summary);

public record CategoryValuationRow(
    string Category,
    int ProductCount,
    decimal TotalQuantity,
    decimal TotalValue);

public record PurchaseReportFilter(
    string? Search = null,
    int? SupplierId = null,
    PurchaseStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null);

public record PurchaseReportRow(
    int Id,
    string Number,
    string Supplier,
    PurchaseStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ReceivedAtUtc,
    string? ReceivedBy,
    decimal TotalAmount,
    int LineCount);

public record PurchaseReportSummary(
    int DocumentCount,
    int ReceivedCount,
    int DraftCount,
    int CancelledCount,
    int ReversedCount,
    decimal ReceivedTotal);

public record SaleReportFilter(
    string? Search = null,
    SaleStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null);

public record SaleReportRow(
    int Id,
    string Number,
    string? Customer,
    SaleStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? CompletedBy,
    decimal TotalAmount,
    int LineCount);

public record SaleReportSummary(
    int DocumentCount,
    int CompletedCount,
    int DraftCount,
    int CancelledCount,
    int ReversedCount,
    decimal CompletedTotal);

public record MovementReportRow(
    long Id,
    string Sku,
    string Product,
    StockMovementType MovementType,
    decimal QuantityChange,
    decimal BalanceAfter,
    decimal UnitCost,
    decimal ValueChange,
    string? ReferenceNumber,
    string? Reason,
    string User,
    DateTime CreatedAtUtc);

public record ActivityFilter(DateTime From, DateTime To);

public record ActivityDayRow(DateTime DateUtc, decimal Purchases, decimal Sales, int SalesCount, int PurchaseCount);
