using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record PurchaseLineInput(int ProductId, decimal Quantity, decimal UnitCost);

public record PurchaseInput(int SupplierId, string? Notes, IReadOnlyList<PurchaseLineInput> Items);

public record PurchaseFilter(
    string? Search = null,
    PurchaseStatus? Status = null,
    int? SupplierId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 15);

public interface IPurchaseService
{
    Task<PaginatedList<Purchase>> GetPagedAsync(PurchaseFilter filter, CancellationToken cancellationToken = default);
    Task<Purchase?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Purchase> CreateDraftAsync(PurchaseInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Purchase> UpdateDraftAsync(int id, PurchaseInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Purchase> ReceiveAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
    Task<Purchase> CancelDraftAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
    Task<Purchase> ReverseReceivedAsync(int id, string reason, string actorUserId, CancellationToken cancellationToken = default);
}

public class PurchaseService : IPurchaseService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentNumberService _numbers;
    private readonly IAuditService _audit;
    private readonly ILogger<PurchaseService> _logger;

    public PurchaseService(
        ApplicationDbContext db,
        IDocumentNumberService numbers,
        IAuditService audit,
        ILogger<PurchaseService> logger)
    {
        _db = db;
        _numbers = numbers;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PaginatedList<Purchase>> GetPagedAsync(PurchaseFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _db.Purchases
            .AsNoTracking()
            .Include(p => p.Supplier)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.PurchaseNumber, $"%{term}%") ||
                EF.Functions.ILike(p.Supplier.BusinessName, $"%{term}%"));
        }

        if (filter.Status is not null)
        {
            query = query.Where(p => p.Status == filter.Status);
        }

        if (filter.SupplierId is > 0)
        {
            query = query.Where(p => p.SupplierId == filter.SupplierId);
        }

        if (filter.From is not null)
        {
            query = query.Where(p => p.CreatedAtUtc >= filter.From.Value);
        }

        if (filter.To is not null)
        {
            query = query.Where(p => p.CreatedAtUtc < filter.To.Value);
        }

        return await PaginatedList<Purchase>.CreateAsync(
            query.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id),
            filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<Purchase?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Purchases
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.CreatedByUser)
            .Include(p => p.ReceivedByUser)
            .Include(p => p.ReversedByUser)
            .Include(p => p.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Unit)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Purchase> CreateDraftAsync(PurchaseInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var supplier = await GetActiveSupplierAsync(input.SupplierId, cancellationToken);
        var lines = await ValidateLinesAsync(input, cancellationToken);
        var now = DateTime.UtcNow;

        var purchase = new Purchase
        {
            PurchaseNumber = await _numbers.NextPurchaseNumberAsync(cancellationToken),
            SupplierId = supplier.Id,
            Status = PurchaseStatus.Draft,
            Notes = Clean(input.Notes),
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            TotalAmount = lines.Sum(l => DecimalMath.Money(l.Quantity * l.UnitCost)),
            Items = lines.Select(l => new PurchaseItem
            {
                ProductId = l.ProductId,
                Quantity = DecimalMath.Quantity(l.Quantity),
                UnitCost = DecimalMath.Money(l.UnitCost),
                LineTotal = DecimalMath.Money(l.Quantity * l.UnitCost)
            }).ToList()
        };

        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PurchaseDraftCreated", nameof(Purchase), purchase.Id,
            $"{purchase.PurchaseNumber}; supplier {supplier.BusinessName}; total {purchase.TotalAmount:0.00}",
            actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purchase;
    }

    public async Task<Purchase> UpdateDraftAsync(int id, PurchaseInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var purchase = await _db.Purchases
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Purchase not found.");

        if (purchase.Status != PurchaseStatus.Draft)
        {
            throw new BusinessRuleException("Only draft purchases can be edited.");
        }

        var supplier = await GetActiveSupplierAsync(input.SupplierId, cancellationToken);
        var lines = await ValidateLinesAsync(input, cancellationToken);

        // Remove old lines first so the unique (purchase, product) index is not violated.
        _db.PurchaseItems.RemoveRange(purchase.Items);
        await _db.SaveChangesAsync(cancellationToken);

        purchase.SupplierId = supplier.Id;
        purchase.Notes = Clean(input.Notes);
        purchase.TotalAmount = lines.Sum(l => DecimalMath.Money(l.Quantity * l.UnitCost));
        foreach (var line in lines)
        {
            purchase.Items.Add(new PurchaseItem
            {
                PurchaseId = purchase.Id,
                ProductId = line.ProductId,
                Quantity = DecimalMath.Quantity(line.Quantity),
                UnitCost = DecimalMath.Money(line.UnitCost),
                LineTotal = DecimalMath.Money(line.Quantity * line.UnitCost)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PurchaseDraftUpdated", nameof(Purchase), purchase.Id,
            $"{purchase.PurchaseNumber}; total {purchase.TotalAmount:0.00}", actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purchase;
    }

    public async Task<Purchase> ReceiveAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Lock the document first. A concurrent receive waits here, then sees Status = Received.
        var purchase = await LockPurchaseAsync(id, cancellationToken);
        if (purchase.Status != PurchaseStatus.Draft)
        {
            throw new BusinessRuleException($"Purchase {purchase.PurchaseNumber} is {purchase.Status} and cannot be received again.");
        }

        var items = await _db.PurchaseItems
            .Include(i => i.Product)
            .Where(i => i.PurchaseId == id)
            .OrderBy(i => i.ProductId)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            throw new BusinessRuleException("A purchase must have at least one line before it can be received.");
        }

        var now = DateTime.UtcNow;

        foreach (var item in items)
        {
            var balance = await InventoryLedger.LockBalanceAsync(_db, item.ProductId, cancellationToken);
            var newQuantity = DecimalMath.Quantity(balance.QuantityOnHand + item.Quantity);
            var newAverageCost = DecimalMath.WeightedAverage(
                balance.QuantityOnHand, balance.AverageCost, item.Quantity, item.UnitCost);

            balance.QuantityOnHand = newQuantity;
            balance.AverageCost = newAverageCost;
            balance.UpdatedAtUtc = now;

            _db.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = StockMovementType.PurchaseReceipt,
                QuantityChange = DecimalMath.Quantity(item.Quantity),
                BalanceAfter = newQuantity,
                UnitCost = item.UnitCost,
                ReferenceType = "Purchase",
                ReferenceId = purchase.Id,
                ReferenceNumber = purchase.PurchaseNumber,
                Reason = "Purchase received",
                UserId = actorUserId,
                CreatedAtUtc = now
            });
        }

        purchase.Status = PurchaseStatus.Received;
        purchase.ReceivedAtUtc = now;
        purchase.ReceivedByUserId = actorUserId;

        // The unique movement index is the final database guard against duplicate receipts.
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PurchaseReceived", nameof(Purchase), purchase.Id,
            $"{purchase.PurchaseNumber}; {items.Count} line(s); total {purchase.TotalAmount:0.00}",
            actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Purchase {PurchaseNumber} received by {UserId}", purchase.PurchaseNumber, actorUserId);
        return purchase;
    }

    public async Task<Purchase> CancelDraftAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var purchase = await LockPurchaseAsync(id, cancellationToken);
        if (purchase.Status != PurchaseStatus.Draft)
        {
            throw new BusinessRuleException("Only draft purchases can be cancelled.");
        }

        purchase.Status = PurchaseStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PurchaseCancelled", nameof(Purchase), purchase.Id,
            purchase.PurchaseNumber, actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purchase;
    }

    public async Task<Purchase> ReverseReceivedAsync(int id, string reason, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            throw new BusinessRuleException("A reversal reason of at least 5 characters is required.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var purchase = await LockPurchaseAsync(id, cancellationToken);
        if (purchase.Status != PurchaseStatus.Received)
        {
            throw new BusinessRuleException("Only received purchases can be reversed.");
        }

        var items = await _db.PurchaseItems
            .Include(i => i.Product)
            .Where(i => i.PurchaseId == id)
            .OrderBy(i => i.ProductId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var balance = await InventoryLedger.LockBalanceAsync(_db, item.ProductId, cancellationToken);
            if (balance.QuantityOnHand < item.Quantity)
            {
                throw new BusinessRuleException(
                    $"Cannot reverse {purchase.PurchaseNumber}: only {balance.QuantityOnHand:0.###} {item.Product.Unit?.Code ?? "units"} of {item.Product.Name} remain, but {item.Quantity:0.###} must be removed.");
            }

            var newQuantity = DecimalMath.Quantity(balance.QuantityOnHand - item.Quantity);
            var removedValue = item.Quantity * item.UnitCost;
            var newValue = (balance.QuantityOnHand * balance.AverageCost) - removedValue;
            if (newValue < 0)
            {
                throw new BusinessRuleException(
                    $"Cannot reverse {purchase.PurchaseNumber}: inventory valuation would become negative. Use a stock adjustment instead.");
            }

            var newAverageCost = newQuantity <= 0 ? 0 : DecimalMath.AverageCost(newValue / newQuantity);
            balance.QuantityOnHand = newQuantity;
            balance.AverageCost = newAverageCost;
            balance.UpdatedAtUtc = now;

            _db.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = StockMovementType.PurchaseReversal,
                QuantityChange = DecimalMath.Quantity(-item.Quantity),
                BalanceAfter = newQuantity,
                UnitCost = item.UnitCost,
                ReferenceType = "Purchase",
                ReferenceId = purchase.Id,
                ReferenceNumber = purchase.PurchaseNumber,
                Reason = reason.Trim(),
                UserId = actorUserId,
                CreatedAtUtc = now
            });
        }

        purchase.Status = PurchaseStatus.Reversed;
        purchase.ReversedAtUtc = now;
        purchase.ReversedByUserId = actorUserId;
        purchase.ReversalReason = reason.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PurchaseReversed", nameof(Purchase), purchase.Id,
            $"{purchase.PurchaseNumber}; reason: {reason.Trim()}", actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purchase;
    }

    private async Task<Purchase> LockPurchaseAsync(int id, CancellationToken cancellationToken) =>
        await _db.Purchases
            .FromSqlInterpolated($"SELECT * FROM \"Purchases\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new EntityNotFoundException("Purchase not found.");

    private async Task<Supplier> GetActiveSupplierAsync(int supplierId, CancellationToken cancellationToken)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken)
            ?? throw new BusinessRuleException("Select a valid supplier.");
        if (!supplier.IsActive)
        {
            throw new BusinessRuleException("The selected supplier is inactive.");
        }

        return supplier;
    }

    private async Task<List<PurchaseLineInput>> ValidateLinesAsync(PurchaseInput input, CancellationToken cancellationToken)
    {
        if (input.Items.Count == 0)
        {
            throw new BusinessRuleException("Add at least one product line.");
        }

        if (input.Items.Any(i => i.Quantity <= 0))
        {
            throw new BusinessRuleException("Every line quantity must be greater than zero.");
        }

        if (input.Items.Any(i => i.UnitCost < 0))
        {
            throw new BusinessRuleException("Unit costs cannot be negative.");
        }

        if (input.Items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException("Each product may appear only once per purchase.");
        }

        var ids = input.Items.Select(i => i.ProductId).ToList();
        var validProducts = await _db.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (validProducts.Count != ids.Count)
        {
            throw new BusinessRuleException("One or more selected products are invalid or inactive.");
        }

        return input.Items
            .Select(i => i with { Quantity = DecimalMath.Quantity(i.Quantity), UnitCost = DecimalMath.Money(i.UnitCost) })
            .ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
