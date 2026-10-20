using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;

namespace StockPilot.Web.Services;

public interface IDocumentNumberService
{
    Task<string> NextPurchaseNumberAsync(CancellationToken cancellationToken = default);
    Task<string> NextSaleNumberAsync(CancellationToken cancellationToken = default);
    Task<string> NextAdjustmentNumberAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses PostgreSQL sequences so concurrent document creation never produces duplicates.
/// </summary>
public class DocumentNumberService : IDocumentNumberService
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DocumentNumberService(ApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public Task<string> NextPurchaseNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("purchase_number_seq", "PO", cancellationToken);

    public Task<string> NextSaleNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("sale_number_seq", "SO", cancellationToken);

    public Task<string> NextAdjustmentNumberAsync(CancellationToken cancellationToken = default) =>
        NextAsync("adjustment_number_seq", "ADJ", cancellationToken);

    private async Task<string> NextAsync(string sequence, string prefix, CancellationToken cancellationToken)
    {
        // Sequence names are constants selected from a fixed set, never user input.
#pragma warning disable EF1002
        var value = await _db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequence}') AS \"Value\"")
            .SingleAsync(cancellationToken);
#pragma warning restore EF1002

        var year = _timeProvider.GetUtcNow().UtcDateTime.Year;
        return $"{prefix}-{year}-{value:D5}";
    }
}
