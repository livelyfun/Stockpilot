using System.Security.Claims;

namespace StockPilot.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string GetDisplayName(this ClaimsPrincipal? principal) =>
        principal?.FindFirstValue("FullName")
        ?? principal?.Identity?.Name
        ?? "Unknown user";

    public static string GetUserName(this ClaimsPrincipal? principal) =>
        principal?.Identity?.Name ?? "system";
}
