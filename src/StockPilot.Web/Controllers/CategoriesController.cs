using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Web.Constants;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class CategoriesController : Controller
{
    private readonly ICategoryService _categories;

    public CategoriesController(ICategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool includeInactive = false, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await _categories.GetPagedAsync(search, includeInactive, page, 20, cancellationToken);
        ViewBag.Search = search;
        ViewBag.IncludeInactive = includeInactive;
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public IActionResult Create() => View(new CategoryFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Create(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _categories.CreateAsync(new CategoryInput(model.Name, model.Description, model.IsActive),
                User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Category created.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await _categories.GetByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        return View(new CategoryFormViewModel
        {
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Edit(int id, CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _categories.UpdateAsync(id, new CategoryInput(model.Name, model.Description, model.IsActive),
                User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Category updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> ToggleActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        try
        {
            await _categories.SetActiveAsync(id, isActive, User.GetUserId()!, cancellationToken);
            TempData["Success"] = isActive ? "Category activated." : "Category deactivated.";
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _categories.DeleteAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Category deleted.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Index));
    }
}
