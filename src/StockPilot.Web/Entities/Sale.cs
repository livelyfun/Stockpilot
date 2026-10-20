namespace StockPilot.Web.Entities;

public class Sale
{
    public int Id { get; set; }
    public string SaleNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedByUserId { get; set; }
    public ApplicationUser? CompletedByUser { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public string? ReversedByUserId { get; set; }
    public ApplicationUser? ReversedByUser { get; set; }
    public string? ReversalReason { get; set; }
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
