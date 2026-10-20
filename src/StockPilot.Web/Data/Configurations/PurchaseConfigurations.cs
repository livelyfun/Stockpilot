using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Data.Configurations;

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.Property(x => x.PurchaseNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ReversalReason).HasMaxLength(500);

        b.HasIndex(x => x.PurchaseNumber).IsUnique();
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        b.HasIndex(x => x.ReceivedAtUtc);
        b.HasIndex(x => x.SupplierId);

        b.HasOne(x => x.Supplier).WithMany(x => x.Purchases)
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReceivedByUser).WithMany()
            .HasForeignKey(x => x.ReceivedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReversedByUser).WithMany()
            .HasForeignKey(x => x.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Purchases_Total_NonNegative", "\"TotalAmount\" >= 0");
            t.HasCheckConstraint("CK_Purchases_Received_Has_Timestamp",
                "\"Status\" <> 'Received' OR (\"ReceivedAtUtc\" IS NOT NULL AND \"ReceivedByUserId\" IS NOT NULL)");
        });
    }
}

public class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> b)
    {
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitCost).HasPrecision(18, 2);
        b.Property(x => x.LineTotal).HasPrecision(18, 2);

        b.HasIndex(x => new { x.PurchaseId, x.ProductId }).IsUnique();

        b.HasOne(x => x.Purchase).WithMany(x => x.Items)
            .HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany(x => x.PurchaseItems)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PurchaseItems_Quantity_Positive", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_PurchaseItems_UnitCost_NonNegative", "\"UnitCost\" >= 0");
            t.HasCheckConstraint("CK_PurchaseItems_LineTotal_NonNegative", "\"LineTotal\" >= 0");
        });
    }
}
