using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record SaleLineInput(int ProductId, decimal Quantity, decimal UnitPrice);

public record SaleInput(string? CustomerName, string? Notes, IReadOnlyList<SaleLineInput> Items);

public record SaleFilter(
    string? Search = null,
    SaleStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 15);

public interface ISaleService
{
    Task<PaginatedList<Sale>> GetPagedAsync(SaleFilter filter, CancellationToken cancellationToken = default);
    Task<Sale?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Sale> CreateDraftAsync(SaleInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Sale> UpdateDraftAsync(int id, SaleInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<Sale> CompleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
    Task<Sale> CancelDraftAsync(int id, string actorUserId, CancellationToken cancellationToken = default);
    Task<Sale> ReverseCompletedAsync(int id, string reason, string actorUserId, CancellationToken cancellationToken = default);
}

public class SaleService : ISaleService
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentNumberService _numbers;
    private readonly IAuditService _audit;
    private readonly ILogger<SaleService> _logger;

    public SaleService(
        ApplicationDbContext db,
        IDocumentNumberService numbers,
        IAuditService audit,
        ILogger<SaleService> logger)
    {
        _db = db;
        _numbers = numbers;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PaginatedList<Sale>> GetPagedAsync(SaleFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _db.Sales.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s =>
                EF.Functions.ILike(s.SaleNumber, $"%{term}%") ||
                (s.CustomerName != null && EF.Functions.ILike(s.CustomerName, $"%{term}%")));
        }

        if (filter.Status is not null)
        {
            query = query.Where(s => s.Status == filter.Status);
        }

        if (filter.From is not null)
        {
            query = query.Where(s => s.CreatedAtUtc >= filter.From.Value.Date);
        }

        if (filter.To is not null)
        {
            var toExclusive = filter.To.Value.Date.AddDays(1);
            query = query.Where(s => s.CreatedAtUtc < toExclusive);
        }

        return await PaginatedList<Sale>.CreateAsync(
            query.OrderByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Id),
            filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<Sale?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Sales
            .AsNoTracking()
            .Include(s => s.CreatedByUser)
            .Include(s => s.CompletedByUser)
            .Include(s => s.ReversedByUser)
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Unit)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<Sale> CreateDraftAsync(SaleInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var lines = await ValidateLinesAsync(input, cancellationToken);

        var sale = new Sale
        {
            SaleNumber = await _numbers.NextSaleNumberAsync(cancellationToken),
            CustomerName = Clean(input.CustomerName),
            Notes = Clean(input.Notes),
            Status = SaleStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorUserId,
            TotalAmount = lines.Sum(l => DecimalMath.Money(l.Quantity * l.UnitPrice)),
            Items = lines.Select(l => new SaleItem
            {
                ProductId = l.ProductId,
                Quantity = DecimalMath.Quantity(l.Quantity),
                UnitPrice = DecimalMath.Money(l.UnitPrice),
                LineTotal = DecimalMath.Money(l.Quantity * l.UnitPrice)
            }).ToList()
        };

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SaleDraftCreated", nameof(Sale), sale.Id,
            $"{sale.SaleNumber}; total {sale.TotalAmount:0.00}", actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sale;
    }

    public async Task<Sale> UpdateDraftAsync(int id, SaleInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var sale = await LockSaleAsync(id, cancellationToken);
        await _db.Entry(sale).Collection(s => s.Items).LoadAsync(cancellationToken);

        if (sale.Status != SaleStatus.Draft)
        {
            throw new BusinessRuleException("Only draft sales can be edited.");
        }

        var lines = await ValidateLinesAsync(input, cancellationToken);

        _db.SaleItems.RemoveRange(sale.Items);
        await _db.SaveChangesAsync(cancellationToken);

        sale.CustomerName = Clean(input.CustomerName);
        sale.Notes = Clean(input.Notes);
        sale.TotalAmount = lines.Sum(l => DecimalMath.Money(l.Quantity * l.UnitPrice));
        foreach (var line in lines)
        {
            sale.Items.Add(new SaleItem
            {
                SaleId = sale.Id,
                ProductId = line.ProductId,
                Quantity = DecimalMath.Quantity(line.Quantity),
                UnitPrice = DecimalMath.Money(line.UnitPrice),
                LineTotal = DecimalMath.Money(line.Quantity * line.UnitPrice)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SaleDraftUpdated", nameof(Sale), sale.Id,
            $"{sale.SaleNumber}; total {sale.TotalAmount:0.00}", actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sale;
    }

    /// <summary>
    /// Completes a sale inside one transaction. Inventory rows are locked in product-id
    /// order to avoid deadlocks, and each deduction uses a conditional quantity check.
    /// </summary>
    public async Task<Sale> CompleteAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var sale = await LockSaleAsync(id, cancellationToken);
        if (sale.Status != SaleStatus.Draft)
        {
            throw new BusinessRuleException($"Sale {sale.SaleNumber} is {sale.Status} and cannot be completed again.");
        }

        var items = await _db.SaleItems
            .Include(i => i.Product).ThenInclude(p => p.Unit)
            .Where(i => i.SaleId == id)
            .OrderBy(i => i.ProductId)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            throw new BusinessRuleException("A sale must have at least one line before it can be completed.");
        }

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            // Row lock serializes concurrent completions for the same product.
            var balance = await InventoryLedger.LockBalanceAsync(_db, item.ProductId, cancellationToken);
            if (balance.QuantityOnHand < item.Quantity)
            {
                throw new BusinessRuleException(
                    $"Insufficient stock for {item.Product.Name}. Available: {balance.QuantityOnHand:0.###} {item.Product.Unit.Code}, requested: {item.Quantity:0.###}.");
            }

            var newQuantity = DecimalMath.Quantity(balance.QuantityOnHand - item.Quantity);
            InventoryLedger.EnsureNonNegativeQuantity(newQuantity, item.Product.Name);

            // Sales leave the weighted-average cost unchanged; the cost at issue time is
            // snapshotted on the ledger row for reversal and margin reporting.
            var costAtIssue = balance.AverageCost;
            balance.QuantityOnHand = newQuantity;
            balance.UpdatedAtUtc = now;

            _db.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = StockMovementType.SaleIssue,
                QuantityChange = DecimalMath.Quantity(-item.Quantity),
                BalanceAfter = newQuantity,
                UnitCost = costAtIssue,
                ReferenceType = "Sale",
                ReferenceId = sale.Id,
                ReferenceNumber = sale.SaleNumber,
                Reason = "Sale completed",
                UserId = actorUserId,
                CreatedAtUtc = now
            });
        }

        sale.Status = SaleStatus.Completed;
        sale.CompletedAtUtc = now;
        sale.CompletedByUserId = actorUserId;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SaleCompleted", nameof(Sale), sale.Id,
            $"{sale.SaleNumber}; {items.Count} line(s); total {sale.TotalAmount:0.00}",
            actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Sale {SaleNumber} completed by {UserId}", sale.SaleNumber, actorUserId);
        return sale;
    }

    public async Task<Sale> CancelDraftAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var sale = await LockSaleAsync(id, cancellationToken);
        if (sale.Status != SaleStatus.Draft)
        {
            throw new BusinessRuleException("Only draft sales can be cancelled.");
        }

        sale.Status = SaleStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SaleCancelled", nameof(Sale), sale.Id, sale.SaleNumber, actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sale;
    }

    public async Task<Sale> ReverseCompletedAsync(int id, string reason, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            throw new BusinessRuleException("A reversal reason of at least 5 characters is required.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var sale = await LockSaleAsync(id, cancellationToken);
        if (sale.Status != SaleStatus.Completed)
        {
            throw new BusinessRuleException("Only completed sales can be reversed.");
        }

        var items = await _db.SaleItems
            .Include(i => i.Product)
            .Where(i => i.SaleId == id)
            .OrderBy(i => i.ProductId)
            .ToListAsync(cancellationToken);

        var issueCosts = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.ReferenceType == "Sale" && m.ReferenceId == id && m.MovementType == StockMovementType.SaleIssue)
            .ToDictionaryAsync(m => m.ProductId, m => m.UnitCost, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var balance = await InventoryLedger.LockBalanceAsync(_db, item.ProductId, cancellationToken);
            var cost = issueCosts.TryGetValue(item.ProductId, out var stored) ? stored : balance.AverageCost;
            var newQuantity = DecimalMath.Quantity(balance.QuantityOnHand + item.Quantity);
            var newAverageCost = DecimalMath.WeightedAverage(balance.QuantityOnHand, balance.AverageCost, item.Quantity, cost);

            balance.QuantityOnHand = newQuantity;
            balance.AverageCost = newAverageCost;
            balance.UpdatedAtUtc = now;

            _db.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = StockMovementType.SaleReversal,
                QuantityChange = DecimalMath.Quantity(item.Quantity),
                BalanceAfter = newQuantity,
                UnitCost = cost,
                ReferenceType = "Sale",
                ReferenceId = sale.Id,
                ReferenceNumber = sale.SaleNumber,
                Reason = reason.Trim(),
                UserId = actorUserId,
                CreatedAtUtc = now
            });
        }

        sale.Status = SaleStatus.Reversed;
        sale.ReversedAtUtc = now;
        sale.ReversedByUserId = actorUserId;
        sale.ReversalReason = reason.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("SaleReversed", nameof(Sale), sale.Id,
            $"{sale.SaleNumber}; reason: {reason.Trim()}", actorUserId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sale;
    }

    private async Task<Sale> LockSaleAsync(int id, CancellationToken cancellationToken) =>
        await _db.Sales
            .FromSqlInterpolated($"SELECT * FROM \"Sales\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new EntityNotFoundException("Sale not found.");

    private async Task<List<SaleLineInput>> ValidateLinesAsync(SaleInput input, CancellationToken cancellationToken)
    {
        if (input.Items.Count == 0)
        {
            throw new BusinessRuleException("Add at least one product line.");
        }

        if (input.Items.Any(i => i.Quantity <= 0))
        {
            throw new BusinessRuleException("Every line quantity must be greater than zero.");
        }

        if (input.Items.Any(i => i.UnitPrice < 0))
        {
            throw new BusinessRuleException("Unit prices cannot be negative.");
        }

        if (input.Items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException("Each product may appear only once per sale.");
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
            .Select(i => i with { Quantity = DecimalMath.Quantity(i.Quantity), UnitPrice = DecimalMath.Money(i.UnitPrice) })
            .ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
