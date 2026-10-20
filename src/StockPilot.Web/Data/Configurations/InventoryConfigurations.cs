using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Data.Configurations;

public class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> b)
    {
        b.HasKey(x => x.ProductId);
        b.Property(x => x.QuantityOnHand).HasPrecision(18, 3);
        b.Property(x => x.AverageCost).HasPrecision(18, 4);
        b.HasIndex(x => x.QuantityOnHand);

        b.HasOne(x => x.Product).WithOne(x => x.InventoryBalance)
            .HasForeignKey<InventoryBalance>(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_InventoryBalances_Quantity_NonNegative", "\"QuantityOnHand\" >= 0");
            t.HasCheckConstraint("CK_InventoryBalances_AverageCost_NonNegative", "\"AverageCost\" >= 0");
        });
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.QuantityChange).HasPrecision(18, 3);
        b.Property(x => x.BalanceAfter).HasPrecision(18, 3);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.ReferenceType).HasMaxLength(30);
        b.Property(x => x.ReferenceNumber).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasIndex(x => new { x.ProductId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.MovementType, x.CreatedAtUtc });
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        b.HasIndex(x => new { x.MovementType, x.ReferenceId })
            .IsUnique()
            .HasDatabaseName("IX_StockMovements_One_Per_Transaction")
            .HasFilter("\"ReferenceId\" IS NOT NULL AND \"MovementType\" IN ('PurchaseReceipt','SaleIssue','SaleReversal','PurchaseReversal')");

        b.HasOne(x => x.Product).WithMany(x => x.StockMovements)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_StockMovements_QuantityChange_NonZero", "\"QuantityChange\" <> 0");
            t.HasCheckConstraint("CK_StockMovements_BalanceAfter_NonNegative", "\"BalanceAfter\" >= 0");
            t.HasCheckConstraint("CK_StockMovements_UnitCost_NonNegative", "\"UnitCost\" >= 0");
        });
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.UserName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(60).IsRequired();
        b.Property(x => x.Details).HasMaxLength(2000);
        b.Property(x => x.IpAddress).HasMaxLength(64);

        b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.Action);

        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.IsActive);
    }
}
