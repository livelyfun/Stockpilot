using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Sku).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.CostPrice).HasPrecision(18, 2);
        b.Property(x => x.SellingPrice).HasPrecision(18, 2);
        b.Property(x => x.ReorderLevel).HasPrecision(18, 3);

        b.HasIndex(x => x.Sku).IsUnique();
        b.HasIndex(x => x.Name);
        b.HasIndex(x => new { x.CategoryId, x.IsActive });
        b.HasIndex(x => x.IsActive);

        b.HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Unit)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_CostPrice_NonNegative", "\"CostPrice\" >= 0");
            t.HasCheckConstraint("CK_Products_SellingPrice_NonNegative", "\"SellingPrice\" >= 0");
            t.HasCheckConstraint("CK_Products_ReorderLevel_NonNegative", "\"ReorderLevel\" >= 0");
            t.HasCheckConstraint("CK_Products_Sku_NotEmpty", "length(trim(\"Sku\")) > 0");
        });
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(x => x.BusinessName).HasMaxLength(150).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.BusinessName);
        b.HasIndex(x => x.IsActive);
    }
}
