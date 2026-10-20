using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => x.Name).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_Categories_Name_NotEmpty", "length(trim(\"Name\")) > 0"));
    }
}

public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Code).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_Units_Code_NotEmpty", "length(trim(\"Code\")) > 0"));
    }
}
