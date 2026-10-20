using Microsoft.AspNetCore.Identity;

namespace StockPilot.Web.Entities;

/// <summary>
/// Application user. Public registration is disabled; administrators create accounts.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAtUtc { get; set; }
}
