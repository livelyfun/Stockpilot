using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record StockMovementFilter(
    int? ProductId = null,
    StockMovementType? MovementType = null,
    string? UserId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

public interface IStockService
{
    Task<PaginatedList<StockMovement>> GetMovementsAsync(StockMovementFilter filter, CancellationToken cancellationToken = default);
    Task<InventoryBalance?> GetBalanceAsync(int productId, CancellationToken cancellationToken = default);
    Task<StockMovement> RecordOpeningStockAsync(int productId, decimal quantity, decimal? unitCost, string reason, string actorUserId, CancellationToken cancellationToken = default);
    Task<StockMovement> AdjustAsync(int productId, bool increase, decimal quantity, decimal? unitCost, string reason, string actorUserId, CancellationToken cancellationToken = default);
}

public class StockService : IStockService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentNumberService _numbers;
    private readonly IAuditService _audit;

    public StockService(ApplicationDbContext db, IDocumentNumberService numbers, IAuditService audit)
    {
        _db = db;
        _numbers = numbers;
        _audit = audit;
    }

    public async Task<PaginatedList<StockMovement>> GetMovementsAsync(StockMovementFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _db.StockMovements
            .AsNoTracking()
            .Include(m => m.Product).ThenInclude(p => p.Unit)
            .Include(m => m.User)
            .AsQueryable();

        if (filter.ProductId is > 0)
        {
            query = query.Where(m => m.ProductId == filter.ProductId);
        }

        if (filter.MovementType is not null)
        {
            query = query.Where(m => m.MovementType == filter.MovementType);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            query = query.Where(m => m.UserId == filter.UserId);
        }

        if (filter.From is not null)
        {
            query = query.Where(m => m.CreatedAtUtc >= filter.From.Value);
        }

        if (filter.To is not null)
        {
            query = query.Where(m => m.CreatedAtUtc < filter.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(m =>
                EF.Functions.ILike(m.Product.Name, $"%{term}%") ||
                EF.Functions.ILike(m.Product.Sku, $"%{term}%") ||
                (m.ReferenceNumber != null && EF.Functions.ILike(m.ReferenceNumber, $"%{term}%")));
        }

        return await PaginatedList<StockMovement>.CreateAsync(
            query.OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id),
            filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<InventoryBalance?> GetBalanceAsync(int productId, CancellationToken cancellationToken = default) =>
        _db.InventoryBalances
            .AsNoTracking()
            .Include(b => b.Product).ThenInclude(p => p.Unit)
            .FirstOrDefaultAsync(b => b.ProductId == productId, cancellationToken);

    public async Task<StockMovement> RecordOpeningStockAsync(
        int productId,
        decimal quantity,
        decimal? unitCost,
        string reason,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("Opening stock quantity must be greater than zero.");
        }

        if (unitCost is < 0)
        {
            throw new BusinessRuleException("Unit cost cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            throw new BusinessRuleException("A reason of at least 5 characters is required.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new EntityNotFoundException("Product not found.");

        var balance = await InventoryLedger.LockBalanceAsync(_db, productId, cancellationToken);
        if (await _db.StockMovements.AnyAsync(m => m.ProductId == productId, cancellationToken))
        {
            throw new BusinessRuleException("Opening stock has already been recorded for this product. Use a stock adjustment instead.");
        }

        if (balance.QuantityOnHand != 0)
        {
            throw new BusinessRuleException("Opening stock can only be recorded when the current balance is zero.");
        }

        var cost = unitCost ?? product.CostPrice;
        var now = DateTime.UtcNow;
        var reference = await _numbers.NextAdjustmentNumberAsync(cancellationToken);
        var newQuantity = DecimalMath.Quantity(quantity);

        balance.QuantityOnHand = newQuantity;
        balance.AverageCost = DecimalMath.AverageCost(cost);
        balance.UpdatedAtUtc = now;

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = StockMovementType.OpeningStock,
            QuantityChange = newQuantity,
            BalanceAfter = newQuantity,
            UnitCost = DecimalMath.AverageCost(cost),
            ReferenceType = "OpeningStock",
            ReferenceNumber = reference,
            Reason = reason.Trim(),
            UserId = actorUserId,
            CreatedAtUtc = now
        };
        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("OpeningStockRecorded", nameof(Product), productId,
            $"{product.Sku}; quantity {newQuantity:0.###}; cost {cost:0.0000}; reason: {reason.Trim()}",
            actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return movement;
    }

    public async Task<StockMovement> AdjustAsync(
        int productId,
        bool increase,
        decimal quantity,
        decimal? unitCost,
        string reason,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("Adjustment quantity must be greater than zero.");
        }

        if (unitCost is < 0)
        {
            throw new BusinessRuleException("Unit cost cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            throw new BusinessRuleException("A mandatory adjustment reason of at least 5 characters is required.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new EntityNotFoundException("Product not found.");

        var balance = await InventoryLedger.LockBalanceAsync(_db, productId, cancellationToken);
        var adjustment = DecimalMath.Quantity(quantity);
        var now = DateTime.UtcNow;
        var reference = await _numbers.NextAdjustmentNumberAsync(cancellationToken);

        decimal newQuantity;
        decimal appliedCost;

        if (increase)
        {
            newQuantity = DecimalMath.Quantity(balance.QuantityOnHand + adjustment);
            appliedCost = unitCost ?? (balance.AverageCost > 0 ? balance.AverageCost : product.CostPrice);
            balance.AverageCost = DecimalMath.WeightedAverage(
                balance.QuantityOnHand, balance.AverageCost, adjustment, appliedCost);
        }
        else
        {
            if (balance.QuantityOnHand < adjustment)
            {
                throw new BusinessRuleException(
                    $"Cannot decrease {product.Name} by {adjustment:0.###}: only {balance.QuantityOnHand:0.###} on hand.");
            }

            newQuantity = DecimalMath.Quantity(balance.QuantityOnHand - adjustment);
            appliedCost = balance.AverageCost;
        }

        balance.QuantityOnHand = newQuantity;
        balance.UpdatedAtUtc = now;

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = increase ? StockMovementType.AdjustmentIncrease : StockMovementType.AdjustmentDecrease,
            QuantityChange = increase ? adjustment : -adjustment,
            BalanceAfter = newQuantity,
            UnitCost = DecimalMath.AverageCost(appliedCost),
            ReferenceType = "Adjustment",
            ReferenceNumber = reference,
            Reason = reason.Trim(),
            UserId = actorUserId,
            CreatedAtUtc = now
        };
        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(increase ? "StockAdjustmentIncrease" : "StockAdjustmentDecrease", nameof(Product), productId,
            $"{product.Sku}; quantity {(increase ? "+" : "-")}{adjustment:0.###}; balance {newQuantity:0.###}; reason: {reason.Trim()}",
            actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return movement;
    }
}
