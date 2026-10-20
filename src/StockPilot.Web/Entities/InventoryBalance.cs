namespace StockPilot.Web.Entities;

/// <summary>
/// Current, concurrency-protected stock balance. One row per product.
/// AverageCost is the weighted-average cost used for inventory valuation.
/// </summary>
public class InventoryBalance
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal QuantityOnHand { get; set; }
    public decimal AverageCost { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
