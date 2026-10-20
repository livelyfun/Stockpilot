using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Services;

public record ProductInput(
    string Sku,
    string Name,
    string? Description,
    int CategoryId,
    int UnitId,
    decimal CostPrice,
    decimal SellingPrice,
    decimal ReorderLevel,
    bool IsActive);

public record ProductFilter(
    string? Search = null,
    int? CategoryId = null,
    string? Status = null,
    string? StockStatus = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 15);

public interface IProductService
{
    Task<PaginatedList<Product>> GetPagedAsync(ProductFilter filter, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<Category>> GetCategoriesAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<List<Unit>> GetUnitsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<List<ProductOption>> GetOptionsAsync(bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<Product> CreateAsync(ProductInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Product> UpdateAsync(int id, ProductInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
}

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public ProductService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PaginatedList<Product>> GetPagedAsync(ProductFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Unit)
            .Include(p => p.InventoryBalance)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%") ||
                EF.Functions.ILike(p.Sku, $"%{term}%"));
        }

        if (filter.CategoryId is > 0)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId);
        }

        query = filter.Status switch
        {
            "active" => query.Where(p => p.IsActive),
            "inactive" => query.Where(p => !p.IsActive),
            _ => query
        };

        query = filter.StockStatus switch
        {
            "out" => query.Where(p => p.InventoryBalance == null || p.InventoryBalance.QuantityOnHand <= 0),
            "low" => query.Where(p => p.InventoryBalance != null &&
                                       p.InventoryBalance.QuantityOnHand > 0 &&
                                       p.InventoryBalance.QuantityOnHand <= p.ReorderLevel),
            "in" => query.Where(p => p.InventoryBalance != null &&
                                     p.InventoryBalance.QuantityOnHand > p.ReorderLevel),
            _ => query
        };

        query = filter.Sort switch
        {
            "sku" => query.OrderBy(p => p.Sku),
            "sku_desc" => query.OrderByDescending(p => p.Sku),
            "name_desc" => query.OrderByDescending(p => p.Name),
            "stock" => query.OrderBy(p => p.InventoryBalance!.QuantityOnHand).ThenBy(p => p.Name),
            "stock_desc" => query.OrderByDescending(p => p.InventoryBalance!.QuantityOnHand).ThenBy(p => p.Name),
            "value_desc" => query.OrderByDescending(p => p.InventoryBalance!.QuantityOnHand * p.InventoryBalance!.AverageCost),
            "updated" => query.OrderByDescending(p => p.UpdatedAtUtc),
            _ => query.OrderBy(p => p.Name)
        };

        return await PaginatedList<Product>.CreateAsync(query, filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Unit)
            .Include(p => p.InventoryBalance)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<List<Category>> GetCategoriesAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _db.Categories.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        return query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
    }

    public Task<List<Unit>> GetUnitsAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _db.Units.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(u => u.IsActive);
        }

        return query.OrderBy(u => u.Name).ToListAsync(cancellationToken);
    }

    public Task<List<ProductOption>> GetOptionsAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = _db.Products.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        return query
            .OrderBy(p => p.Name)
            .Select(p => new ProductOption(
                p.Id,
                p.Sku,
                p.Name,
                p.CostPrice,
                p.SellingPrice,
                p.InventoryBalance != null ? p.InventoryBalance.QuantityOnHand : 0,
                p.Unit.Code,
                p.ReorderLevel))
            .ToListAsync(cancellationToken);
    }

    public async Task<Product> CreateAsync(ProductInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var sku = NormalizeSku(input.Sku);
        await ValidateAsync(input with { Sku = sku }, null, cancellationToken);

        var product = new Product
        {
            Sku = sku,
            Name = input.Name.Trim(),
            Description = Clean(input.Description),
            CategoryId = input.CategoryId,
            UnitId = input.UnitId,
            CostPrice = DecimalMath.Money(input.CostPrice),
            SellingPrice = DecimalMath.Money(input.SellingPrice),
            ReorderLevel = DecimalMath.Quantity(input.ReorderLevel),
            IsActive = input.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            InventoryBalance = new InventoryBalance
            {
                QuantityOnHand = 0,
                AverageCost = 0,
                UpdatedAtUtc = DateTime.UtcNow
            }
        };

        _db.Products.Add(product);
        await SaveWithSkuConflictCheckAsync(cancellationToken);
        await _audit.LogAsync("ProductCreated", nameof(Product), product.Id,
            $"SKU {product.Sku}; name {product.Name}", actorUserId, cancellationToken: cancellationToken);
        return product;
    }

    public async Task<Product> UpdateAsync(int id, ProductInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Product not found.");

        var sku = NormalizeSku(input.Sku);
        await ValidateAsync(input with { Sku = sku }, id, cancellationToken);

        product.Sku = sku;
        product.Name = input.Name.Trim();
        product.Description = Clean(input.Description);
        product.CategoryId = input.CategoryId;
        product.UnitId = input.UnitId;
        product.CostPrice = DecimalMath.Money(input.CostPrice);
        product.SellingPrice = DecimalMath.Money(input.SellingPrice);
        product.ReorderLevel = DecimalMath.Quantity(input.ReorderLevel);
        product.IsActive = input.IsActive;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await SaveWithSkuConflictCheckAsync(cancellationToken);
        await _audit.LogAsync("ProductUpdated", nameof(Product), product.Id,
            $"SKU {product.Sku}; name {product.Name}; active {product.IsActive}", actorUserId, cancellationToken: cancellationToken);
        return product;
    }

    public async Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Product not found.");

        product.IsActive = isActive;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(isActive ? "ProductActivated" : "ProductDeactivated", nameof(Product), product.Id,
            $"SKU {product.Sku}", actorUserId, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Product not found.");

        var referenced = await _db.PurchaseItems.AnyAsync(i => i.ProductId == id, cancellationToken)
            || await _db.SaleItems.AnyAsync(i => i.ProductId == id, cancellationToken)
            || await _db.StockMovements.AnyAsync(m => m.ProductId == id, cancellationToken);

        if (referenced)
        {
            throw new BusinessRuleException("This product is referenced by transactions or stock movements and cannot be deleted. Deactivate it instead.");
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("ProductDeleted", nameof(Product), id, $"SKU {product.Sku}; name {product.Name}", actorUserId, cancellationToken: cancellationToken);
    }

    private async Task ValidateAsync(ProductInput input, int? productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Sku))
        {
            throw new BusinessRuleException("SKU is required.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new BusinessRuleException("Product name is required.");
        }

        if (input.CostPrice < 0 || input.SellingPrice < 0 || input.ReorderLevel < 0)
        {
            throw new BusinessRuleException("Prices and reorder level cannot be negative.");
        }

        if (!await _db.Categories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
        {
            throw new BusinessRuleException("Select a valid category.");
        }

        if (!await _db.Units.AnyAsync(u => u.Id == input.UnitId, cancellationToken))
        {
            throw new BusinessRuleException("Select a valid unit of measurement.");
        }

        var exists = await _db.Products.AnyAsync(
            p => p.Sku == input.Sku && (productId == null || p.Id != productId),
            cancellationToken);

        if (exists)
        {
            throw new BusinessRuleException($"A product with SKU '{input.Sku}' already exists.");
        }
    }

    private async Task SaveWithSkuConflictCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new BusinessRuleException("A product with this SKU already exists.");
        }
    }

    private static string NormalizeSku(string sku) => (sku ?? string.Empty).Trim().ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
