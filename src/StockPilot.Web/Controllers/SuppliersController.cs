using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Web.Constants;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class SuppliersController : Controller
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers)
    {
        _suppliers = suppliers;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool includeInactive = false, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await _suppliers.GetPagedAsync(search, includeInactive, page, 20, cancellationToken);
        ViewBag.Search = search;
        ViewBag.IncludeInactive = includeInactive;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var supplier = await _suppliers.GetWithPurchaseHistoryAsync(id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        return View(supplier);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public IActionResult Create() => View(new SupplierFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Create(SupplierFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var supplier = await _suppliers.CreateAsync(ToInput(model), User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Supplier created.";
            return RedirectToAction(nameof(Details), new { id = supplier.Id });
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
        var supplier = await _suppliers.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        return View(new SupplierFormViewModel
        {
            BusinessName = supplier.BusinessName,
            ContactPerson = supplier.ContactPerson,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            Notes = supplier.Notes,
            IsActive = supplier.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Edit(int id, SupplierFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _suppliers.UpdateAsync(id, ToInput(model), User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Supplier updated.";
            return RedirectToAction(nameof(Details), new { id });
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
            await _suppliers.SetActiveAsync(id, isActive, User.GetUserId()!, cancellationToken);
            TempData["Success"] = isActive ? "Supplier activated." : "Supplier deactivated.";
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _suppliers.DeleteAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Supplier deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }

    private static SupplierInput ToInput(SupplierFormViewModel model) =>
        new(model.BusinessName, model.ContactPerson, model.Phone, model.Email, model.Address, model.Notes, model.IsActive);
}
