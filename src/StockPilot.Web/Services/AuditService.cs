using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityType,
        object entityId,
        string? details = null,
        string? userId = null,
        string? userName = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes durable audit records. Never log passwords, tokens or other secrets here.
/// </summary>
public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        ApplicationDbContext db,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        object entityId,
        string? details = null,
        string? userId = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        userId ??= httpContext?.User.GetUserId();
        userName ??= httpContext?.User.GetUserName();

        if (string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(userId))
        {
            userName = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var log = new AuditLog
        {
            UserId = userId,
            UserName = string.IsNullOrWhiteSpace(userName) ? "system" : userName,
            Action = Truncate(action, 80)!,
            EntityType = Truncate(entityType, 80)!,
            EntityId = Truncate(entityId.ToString(), 60)!,
            Details = Truncate(details, 2000),
            IpAddress = Truncate(httpContext?.Connection.RemoteIpAddress?.ToString(), 64),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Audit {Action} on {EntityType}/{EntityId} by {User}", log.Action, log.EntityType, log.EntityId, log.UserName);
    }

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : (value.Length <= length ? value : value[..length]);
}
