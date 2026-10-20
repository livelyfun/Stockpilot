namespace StockPilot.Web.Entities;

public enum PurchaseStatus
{
    Draft = 0,
    Received = 1,
    Cancelled = 2,
    Reversed = 3
}

public enum SaleStatus
{
    Draft = 0,
    Completed = 1,
    Cancelled = 2,
    Reversed = 3
}

public enum StockMovementType
{
    OpeningStock = 0,
    PurchaseReceipt = 1,
    SaleIssue = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
    SaleReversal = 5,
    PurchaseReversal = 6
}
