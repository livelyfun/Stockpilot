using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Data.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.Property(x => x.SaleNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(150);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.ReversalReason).HasMaxLength(500);

        b.HasIndex(x => x.SaleNumber).IsUnique();
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        b.HasIndex(x => x.CompletedAtUtc);

        b.HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CompletedByUser).WithMany()
            .HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReversedByUser).WithMany()
            .HasForeignKey(x => x.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Sales_Total_NonNegative", "\"TotalAmount\" >= 0");
            t.HasCheckConstraint("CK_Sales_Completed_Has_Timestamp",
                "\"Status\" <> 'Completed' OR (\"CompletedAtUtc\" IS NOT NULL AND \"CompletedByUserId\" IS NOT NULL)");
        });
    }
}

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> b)
    {
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Property(x => x.LineTotal).HasPrecision(18, 2);

        b.HasIndex(x => new { x.SaleId, x.ProductId }).IsUnique();

        b.HasOne(x => x.Sale).WithMany(x => x.Items)
            .HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany(x => x.SaleItems)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SaleItems_Quantity_Positive", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_SaleItems_UnitPrice_NonNegative", "\"UnitPrice\" >= 0");
            t.HasCheckConstraint("CK_SaleItems_LineTotal_NonNegative", "\"LineTotal\" >= 0");
        });
    }
}
