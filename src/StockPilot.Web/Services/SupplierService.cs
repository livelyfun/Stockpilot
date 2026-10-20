using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record SupplierInput(
    string BusinessName,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive);

public interface ISupplierService
{
    Task<PaginatedList<Supplier>> GetPagedAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Supplier?> GetWithPurchaseHistoryAsync(int id, CancellationToken cancellationToken = default);
    Task<Supplier> CreateAsync(SupplierInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Supplier> UpdateAsync(int id, SupplierInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
}

public class SupplierService : ISupplierService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public SupplierService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PaginatedList<Supplier>> GetPagedAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.Suppliers.AsNoTracking().Include(s => s.Purchases).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                EF.Functions.ILike(s.BusinessName, $"%{term}%") ||
                (s.ContactPerson != null && EF.Functions.ILike(s.ContactPerson, $"%{term}%")) ||
                (s.Email != null && EF.Functions.ILike(s.Email, $"%{term}%")));
        }

        return await PaginatedList<Supplier>.CreateAsync(query.OrderBy(s => s.BusinessName), page, pageSize, cancellationToken);
    }

    public Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Supplier?> GetWithPurchaseHistoryAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Suppliers
            .AsNoTracking()
            .Include(s => s.Purchases.OrderByDescending(p => p.CreatedAtUtc))
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<Supplier> CreateAsync(SupplierInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var supplier = new Supplier
        {
            BusinessName = input.BusinessName.Trim(),
            ContactPerson = Clean(input.ContactPerson),
            Phone = Clean(input.Phone),
            Email = Clean(input.Email),
            Address = Clean(input.Address),
            Notes = Clean(input.Notes),
            IsActive = input.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SupplierCreated", nameof(Supplier), supplier.Id, supplier.BusinessName, actorUserId, cancellationToken: cancellationToken);
        return supplier;
    }

    public async Task<Supplier> UpdateAsync(int id, SupplierInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Supplier not found.");

        Validate(input);
        supplier.BusinessName = input.BusinessName.Trim();
        supplier.ContactPerson = Clean(input.ContactPerson);
        supplier.Phone = Clean(input.Phone);
        supplier.Email = Clean(input.Email);
        supplier.Address = Clean(input.Address);
        supplier.Notes = Clean(input.Notes);
        supplier.IsActive = input.IsActive;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SupplierUpdated", nameof(Supplier), supplier.Id, supplier.BusinessName, actorUserId, cancellationToken: cancellationToken);
        return supplier;
    }

    public async Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Supplier not found.");
        supplier.IsActive = isActive;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(isActive ? "SupplierActivated" : "SupplierDeactivated", nameof(Supplier), id,
            supplier.BusinessName, actorUserId, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Supplier not found.");

        if (await _db.Purchases.AnyAsync(p => p.SupplierId == id, cancellationToken))
        {
            throw new BusinessRuleException("This supplier has purchase history and cannot be deleted. Deactivate it instead.");
        }

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SupplierDeleted", nameof(Supplier), id, supplier.BusinessName, actorUserId, cancellationToken: cancellationToken);
    }

    private static void Validate(SupplierInput input)
    {
        if (string.IsNullOrWhiteSpace(input.BusinessName))
        {
            throw new BusinessRuleException("Business name is required.");
        }

        if (!string.IsNullOrWhiteSpace(input.Email) && !MailAddress.TryCreate(input.Email.Trim(), out _))
        {
            throw new BusinessRuleException("Enter a valid supplier email address.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
