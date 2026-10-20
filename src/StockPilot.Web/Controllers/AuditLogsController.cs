using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Constants;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public class AuditLogsController : Controller
{
    private readonly ApplicationDbContext _db;

    public AuditLogsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? action, DateTime? from, DateTime? to,
        int page = 1, CancellationToken cancellationToken = default)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a =>
                EF.Functions.ILike(a.UserName, $"%{term}%") ||
                EF.Functions.ILike(a.EntityType, $"%{term}%") ||
                EF.Functions.ILike(a.EntityId, $"%{term}%") ||
                (a.Details != null && EF.Functions.ILike(a.Details, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (from is not null)
        {
            query = query.Where(a => a.CreatedAtUtc >= from.Value.Date);
        }

        if (to is not null)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            query = query.Where(a => a.CreatedAtUtc < toExclusive);
        }

        var model = await PaginatedList<AuditLog>.CreateAsync(
            query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id),
            page, 25, cancellationToken);

        ViewBag.Search = search;
        ViewBag.Action = action;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.Actions = await _db.AuditLogs.AsNoTracking()
            .Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var log = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return log is null ? NotFound() : View(log);
    }
}
