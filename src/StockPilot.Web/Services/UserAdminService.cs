using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Constants;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;

namespace StockPilot.Web.Services;

public record UserFilter(string? Search = null, string? Role = null, string? Status = null, int Page = 1, int PageSize = 20);

public record UserListItem(
    string Id,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public record CreateUserInput(string FullName, string Email, string Password, string Role);

public record UpdateUserInput(string FullName, string Email, string Role, bool IsActive);

public interface IUserAdminService
{
    Task<PaginatedList<UserListItem>> GetPagedAsync(UserFilter filter, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user);
    Task<ApplicationUser> CreateAsync(CreateUserInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<ApplicationUser> UpdateAsync(string id, UpdateUserInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(string id, string newPassword, string actorUserId, CancellationToken cancellationToken = default);
}

public class UserAdminService : IUserAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public UserAdminService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
    }

    public async Task<PaginatedList<UserListItem>> GetPagedAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, $"%{term}%") ||
                (u.Email != null && EF.Functions.ILike(u.Email, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var role = filter.Role;
            query = query.Where(u => _db.UserRoles.Any(ur =>
                ur.UserId == u.Id &&
                _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        }

        query = filter.Status switch
        {
            "active" => query.Where(u => u.IsActive),
            "inactive" => query.Where(u => !u.IsActive),
            _ => query
        };

        var users = await PaginatedList<ApplicationUser>.CreateAsync(
            query.OrderBy(u => u.FullName), filter.Page, filter.PageSize, cancellationToken);

        var items = new List<UserListItem>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            items.Add(new UserListItem(
                user.Id,
                user.FullName,
                user.Email ?? user.UserName ?? string.Empty,
                roles.ToList(),
                user.IsActive,
                user.CreatedAtUtc,
                user.LastLoginAtUtc));
        }

        return new PaginatedList<UserListItem>(items, users.TotalCount, users.PageIndex, users.PageSize);
    }

    public Task<ApplicationUser?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user) =>
        (await _userManager.GetRolesAsync(user)).ToList();

    public async Task<ApplicationUser> CreateAsync(CreateUserInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        ValidateRole(input.Role);
        var email = input.Email.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            throw new BusinessRuleException("A user with this email address already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = input.FullName.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            throw new BusinessRuleException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        var roleResult = await _userManager.AddToRoleAsync(user, input.Role);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            throw new BusinessRuleException(string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        await _audit.LogAsync("UserCreated", nameof(ApplicationUser), user.Id,
            $"Email {email}; role {input.Role}", actorUserId, cancellationToken: cancellationToken);
        return user;
    }

    public async Task<ApplicationUser> UpdateAsync(string id, UpdateUserInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        ValidateRole(input.Role);
        var user = await _userManager.FindByIdAsync(id)
            ?? throw new EntityNotFoundException("User not found.");

        if (actorUserId == user.Id && !input.IsActive)
        {
            throw new BusinessRuleException("You cannot deactivate your own account.");
        }

        if (actorUserId == user.Id && input.Role != AppRoles.Admin)
        {
            throw new BusinessRuleException("You cannot remove your own administrator role.");
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var wasAdmin = currentRoles.Contains(AppRoles.Admin);
        var remainsActiveAdmin = input.IsActive && input.Role == AppRoles.Admin;

        if (wasAdmin && !remainsActiveAdmin && await CountActiveAdminsAsync(user.Id) == 0)
        {
            throw new BusinessRuleException("At least one active administrator must remain.");
        }

        var email = input.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var setEmail = await _userManager.SetEmailAsync(user, email);
            if (!setEmail.Succeeded)
            {
                throw new BusinessRuleException(string.Join(" ", setEmail.Errors.Select(e => e.Description)));
            }

            var setUserName = await _userManager.SetUserNameAsync(user, email);
            if (!setUserName.Succeeded)
            {
                throw new BusinessRuleException(string.Join(" ", setUserName.Errors.Select(e => e.Description)));
            }
        }

        user.FullName = input.FullName.Trim();
        user.IsActive = input.IsActive;
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            throw new BusinessRuleException(string.Join(" ", update.Errors.Select(e => e.Description)));
        }

        if (!currentRoles.Contains(input.Role))
        {
            var remove = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!remove.Succeeded)
            {
                throw new BusinessRuleException(string.Join(" ", remove.Errors.Select(e => e.Description)));
            }

            var add = await _userManager.AddToRoleAsync(user, input.Role);
            if (!add.Succeeded)
            {
                throw new BusinessRuleException(string.Join(" ", add.Errors.Select(e => e.Description)));
            }
        }

        // Invalidate existing cookies when access changes.
        await _userManager.UpdateSecurityStampAsync(user);

        await _audit.LogAsync("UserUpdated", nameof(ApplicationUser), user.Id,
            $"Email {email}; role {input.Role}; active {input.IsActive}", actorUserId, cancellationToken: cancellationToken);
        return user;
    }

    public async Task ResetPasswordAsync(string id, string newPassword, string actorUserId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id)
            ?? throw new EntityNotFoundException("User not found.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            throw new BusinessRuleException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        await _userManager.UpdateSecurityStampAsync(user);
        await _audit.LogAsync("UserPasswordReset", nameof(ApplicationUser), user.Id,
            $"Password reset for {user.Email}", actorUserId, cancellationToken: cancellationToken);
    }

    private static void ValidateRole(string role)
    {
        if (!AppRoles.All.Contains(role))
        {
            throw new BusinessRuleException("Select a valid role.");
        }
    }

    private async Task<int> CountActiveAdminsAsync(string excludingUserId) =>
        await _db.Users.CountAsync(u =>
            u.IsActive && u.Id != excludingUserId &&
            _db.UserRoles.Any(ur => ur.UserId == u.Id &&
                _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Admin)));
}
