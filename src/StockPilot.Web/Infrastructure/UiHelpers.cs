using System.Globalization;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Infrastructure;

public static class UiHelpers
{
    public static string Money(decimal value) => "Rs " + value.ToString("N2", CultureInfo.InvariantCulture);
    public static string Number(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    public static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Date(DateTime? value) =>
        value is null ? "—" : value.Value.ToUniversalTime().ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
    public static string DateOnly(DateTime? value) =>
        value is null ? "—" : value.Value.ToUniversalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string PurchaseStatusClass(PurchaseStatus status) => status switch
    {
        PurchaseStatus.Draft => "badge-soft-secondary",
        PurchaseStatus.Received => "badge-soft-success",
        PurchaseStatus.Cancelled => "badge-soft-danger",
        PurchaseStatus.Reversed => "badge-soft-warning",
        _ => "badge-soft-secondary"
    };

    public static string SaleStatusClass(SaleStatus status) => status switch
    {
        SaleStatus.Draft => "badge-soft-secondary",
        SaleStatus.Completed => "badge-soft-success",
        SaleStatus.Cancelled => "badge-soft-danger",
        SaleStatus.Reversed => "badge-soft-warning",
        _ => "badge-soft-secondary"
    };

    public static string MovementTypeClass(StockMovementType type) => type switch
    {
        StockMovementType.PurchaseReceipt => "badge-soft-success",
        StockMovementType.OpeningStock => "badge-soft-info",
        StockMovementType.SaleIssue => "badge-soft-primary",
        StockMovementType.AdjustmentIncrease => "badge-soft-success",
        StockMovementType.AdjustmentDecrease => "badge-soft-danger",
        StockMovementType.SaleReversal => "badge-soft-warning",
        StockMovementType.PurchaseReversal => "badge-soft-warning",
        _ => "badge-soft-secondary"
    };

    public static string MovementTypeLabel(StockMovementType type) => type switch
    {
        StockMovementType.OpeningStock => "Opening stock",
        StockMovementType.PurchaseReceipt => "Purchase receipt",
        StockMovementType.SaleIssue => "Sale issue",
        StockMovementType.AdjustmentIncrease => "Adjustment +",
        StockMovementType.AdjustmentDecrease => "Adjustment −",
        StockMovementType.SaleReversal => "Sale reversal",
        StockMovementType.PurchaseReversal => "Purchase reversal",
        _ => type.ToString()
    };

    public static string StockClass(decimal quantity, decimal reorderLevel) =>
        quantity <= 0 ? "badge-soft-danger" : quantity <= reorderLevel ? "badge-soft-warning" : "badge-soft-success";

    public static string StockLabel(decimal quantity, decimal reorderLevel) =>
        quantity <= 0 ? "Out of stock" : quantity <= reorderLevel ? "Low stock" : "In stock";

    public static string RoleClass(string role) => role switch
    {
        "Admin" => "badge-soft-primary",
        "InventoryStaff" => "badge-soft-info",
        _ => "badge-soft-secondary"
    };

    public static string RoleLabel(string role) => role switch
    {
        "InventoryStaff" => "Inventory Staff",
        _ => role
    };
}

public interface IPaginatedMetadata
{
    int PageIndex { get; }
    int PageSize { get; }
    int TotalCount { get; }
    int TotalPages { get; }
    bool HasPreviousPage { get; }
    bool HasNextPage { get; }
    int FirstItemIndex { get; }
    int LastItemIndex { get; }
}
