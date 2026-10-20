using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Web.Constants;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class UnitsController : Controller
{
    private readonly IUnitService _units;

    public UnitsController(IUnitService units)
    {
        _units = units;
    }

    [HttpGet]
    public async Task<IActionResult> Index(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        ViewBag.IncludeInactive = includeInactive;
        return View(await _units.GetAllAsync(includeInactive, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public IActionResult Create() => View(new UnitFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Create(UnitFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _units.CreateAsync(new UnitInput(model.Name, model.Code, model.IsActive), User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Unit created.";
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
        var unit = await _units.GetByIdAsync(id, cancellationToken);
        if (unit is null)
        {
            return NotFound();
        }

        return View(new UnitFormViewModel { Name = unit.Name, Code = unit.Code, IsActive = unit.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Edit(int id, UnitFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _units.UpdateAsync(id, new UnitInput(model.Name, model.Code, model.IsActive), User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Unit updated.";
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
            await _units.SetActiveAsync(id, isActive, User.GetUserId()!, cancellationToken);
            TempData["Success"] = isActive ? "Unit activated." : "Unit deactivated.";
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
            await _units.DeleteAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Unit deleted.";
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
