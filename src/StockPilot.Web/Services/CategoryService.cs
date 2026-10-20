using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record CategoryInput(string Name, string? Description, bool IsActive);

public interface ICategoryService
{
    Task<PaginatedList<Category>> GetPagedAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Category> CreateAsync(CategoryInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Category> UpdateAsync(int id, CategoryInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
}

public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public CategoryService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PaginatedList<Category>> GetPagedAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.Categories.AsNoTracking().Include(c => c.Products).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%"));
        }

        return await PaginatedList<Category>.CreateAsync(query.OrderBy(c => c.Name), page, pageSize, cancellationToken);
    }

    public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<Category> CreateAsync(CategoryInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(input, null, cancellationToken);
        var category = new Category
        {
            Name = input.Name.Trim(),
            Description = Clean(input.Description),
            IsActive = input.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Categories.Add(category);
        await SaveWithConflictCheckAsync(cancellationToken);
        await _audit.LogAsync("CategoryCreated", nameof(Category), category.Id, category.Name, actorUserId, cancellationToken: cancellationToken);
        return category;
    }

    public async Task<Category> UpdateAsync(int id, CategoryInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Category not found.");

        await ValidateAsync(input, id, cancellationToken);
        category.Name = input.Name.Trim();
        category.Description = Clean(input.Description);
        category.IsActive = input.IsActive;
        await SaveWithConflictCheckAsync(cancellationToken);
        await _audit.LogAsync("CategoryUpdated", nameof(Category), category.Id, category.Name, actorUserId, cancellationToken: cancellationToken);
        return category;
    }

    public async Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Category not found.");
        category.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(isActive ? "CategoryActivated" : "CategoryDeactivated", nameof(Category), id,
            category.Name, actorUserId, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Category not found.");

        if (await _db.Products.AnyAsync(p => p.CategoryId == id, cancellationToken))
        {
            throw new BusinessRuleException("This category has products and cannot be deleted. Deactivate it instead.");
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("CategoryDeleted", nameof(Category), id, category.Name, actorUserId, cancellationToken: cancellationToken);
    }

    private async Task ValidateAsync(CategoryInput input, int? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new BusinessRuleException("Category name is required.");
        }

        var name = input.Name.Trim();
        if (await _db.Categories.AnyAsync(c => EF.Functions.ILike(c.Name, name) && (id == null || c.Id != id), cancellationToken))
        {
            throw new BusinessRuleException($"A category named '{name}' already exists.");
        }
    }

    private async Task SaveWithConflictCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new BusinessRuleException("A category with this name already exists.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
