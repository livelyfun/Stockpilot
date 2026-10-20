using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Web.Constants;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public class UsersController : Controller
{
    private readonly IUserAdminService _users;

    public UsersController(IUserAdminService users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? role, string? status, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await _users.GetPagedAsync(new UserFilter(search, role, status, page), cancellationToken);
        ViewBag.Search = search;
        ViewBag.Role = role;
        ViewBag.Status = status;
        ViewBag.Roles = AppRoles.All;
        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateUserViewModel { Roles = RoleOptions() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken cancellationToken)
    {
        if (!AppRoles.All.Contains(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Select a valid role.");
        }

        if (!ModelState.IsValid)
        {
            model.Roles = RoleOptions();
            return View(model);
        }

        try
        {
            await _users.CreateAsync(new CreateUserInput(model.FullName, model.Email, model.Password, model.Role),
                User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"User {model.Email} created.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.Roles = RoleOptions();
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var roles = await _users.GetRolesAsync(user);
        return View(new EditUserViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            Role = roles.FirstOrDefault() ?? AppRoles.Viewer,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc,
            LastLoginAtUtc = user.LastLoginAtUtc,
            Roles = RoleOptions()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, EditUserViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!AppRoles.All.Contains(model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Select a valid role.");
        }

        if (!ModelState.IsValid)
        {
            model.Roles = RoleOptions();
            return View(model);
        }

        try
        {
            await _users.UpdateAsync(id, new UpdateUserInput(model.FullName, model.Email, model.Role, model.IsActive),
                User.GetUserId()!, cancellationToken);
            TempData["Success"] = "User updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.Roles = RoleOptions();
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return View(new ResetPasswordViewModel
        {
            UserId = user.Id,
            UserName = user.FullName
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _users.ResetPasswordAsync(model.UserId, model.NewPassword, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Password reset for {model.UserName}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    private static List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> RoleOptions() =>
        AppRoles.All.Select(r => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(r, r)).ToList();
}
