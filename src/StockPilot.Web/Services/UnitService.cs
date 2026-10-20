using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record UnitInput(string Name, string Code, bool IsActive);

public interface IUnitService
{
    Task<List<Unit>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<Unit?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Unit> CreateAsync(UnitInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Unit> UpdateAsync(int id, UnitInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
}

public class UnitService : IUnitService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public UnitService(ApplicationDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<Unit>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = _db.Units.AsNoTracking().Include(u => u.Products).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(u => u.IsActive);
        }

        return query.OrderBy(u => u.Name).ToListAsync(cancellationToken);
    }

    public Task<Unit?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<Unit> CreateAsync(UnitInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(input.Code);
        await ValidateAsync(input with { Code = code }, null, cancellationToken);
        var unit = new Unit
        {
            Name = input.Name.Trim(),
            Code = code,
            IsActive = input.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Units.Add(unit);
        await SaveWithConflictCheckAsync(cancellationToken);
        await _audit.LogAsync("UnitCreated", nameof(Unit), unit.Id, $"{unit.Name} ({unit.Code})", actorUserId, cancellationToken: cancellationToken);
        return unit;
    }

    public async Task<Unit> UpdateAsync(int id, UnitInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Unit not found.");

        var code = NormalizeCode(input.Code);
        await ValidateAsync(input with { Code = code }, id, cancellationToken);
        unit.Name = input.Name.Trim();
        unit.Code = code;
        unit.IsActive = input.IsActive;
        await SaveWithConflictCheckAsync(cancellationToken);
        await _audit.LogAsync("UnitUpdated", nameof(Unit), unit.Id, $"{unit.Name} ({unit.Code})", actorUserId, cancellationToken: cancellationToken);
        return unit;
    }

    public async Task SetActiveAsync(int id, bool isActive, string actorUserId, CancellationToken cancellationToken = default)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Unit not found.");
        unit.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(isActive ? "UnitActivated" : "UnitDeactivated", nameof(Unit), id,
            unit.Name, actorUserId, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Unit not found.");

        if (await _db.Products.AnyAsync(p => p.UnitId == id, cancellationToken))
        {
            throw new BusinessRuleException("This unit is used by products and cannot be deleted. Deactivate it instead.");
        }

        _db.Units.Remove(unit);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("UnitDeleted", nameof(Unit), id, unit.Name, actorUserId, cancellationToken: cancellationToken);
    }

    private async Task ValidateAsync(UnitInput input, int? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new BusinessRuleException("Unit name is required.");
        }

        if (string.IsNullOrWhiteSpace(input.Code))
        {
            throw new BusinessRuleException("Unit code is required.");
        }

        var name = input.Name.Trim();
        if (await _db.Units.AnyAsync(u => EF.Functions.ILike(u.Name, name) && (id == null || u.Id != id), cancellationToken))
        {
            throw new BusinessRuleException($"A unit named '{name}' already exists.");
        }

        if (await _db.Units.AnyAsync(u => u.Code == input.Code && (id == null || u.Id != id), cancellationToken))
        {
            throw new BusinessRuleException($"A unit with code '{input.Code}' already exists.");
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
            throw new BusinessRuleException("A unit with this name or code already exists.");
        }
    }

    private static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}
