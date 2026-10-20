using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Constants;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class StockController : Controller
{
    private readonly IStockService _stock;
    private readonly IProductService _products;
    private readonly UserManager<ApplicationUser> _userManager;

    public StockController(IStockService stock, IProductService products, UserManager<ApplicationUser> userManager)
    {
        _stock = stock;
        _products = products;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int? productId, StockMovementType? movementType, string? userId, DateTime? from, DateTime? to,
        string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var filter = new StockMovementFilter(productId, movementType, userId, from, to, search, page, 25);
        var model = await _stock.GetMovementsAsync(filter, cancellationToken);

        ViewBag.ProductId = productId;
        ViewBag.MovementType = movementType;
        ViewBag.UserId = userId;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.Search = search;
        ViewBag.Products = await ProductOptionsAsync(cancellationToken);
        ViewBag.Users = await UserOptionsAsync();
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageInventory)]
    public async Task<IActionResult> Adjust(int? productId, CancellationToken cancellationToken)
    {
        return View(new AdjustmentFormViewModel
        {
            ProductId = productId ?? 0,
            Products = await _products.GetOptionsAsync(true, cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageInventory)]
    public async Task<IActionResult> Adjust(AdjustmentFormViewModel model, CancellationToken cancellationToken)
    {
        if (model.Direction is not ("increase" or "decrease"))
        {
            ModelState.AddModelError(nameof(model.Direction), "Select increase or decrease.");
        }

        if (!ModelState.IsValid)
        {
            model.Products = await _products.GetOptionsAsync(true, cancellationToken);
            return View(model);
        }

        try
        {
            var movement = await _stock.AdjustAsync(
                model.ProductId, model.Direction == "increase", model.Quantity,
                model.Direction == "increase" ? model.UnitCost : null,
                model.Reason, User.GetUserId()!, cancellationToken);

            TempData["Success"] = $"Adjustment {movement.ReferenceNumber} recorded. New balance: {movement.BalanceAfter:0.###}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.Products = await _products.GetOptionsAsync(true, cancellationToken);
            return View(model);
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageInventory)]
    public async Task<IActionResult> OpeningStock(CancellationToken cancellationToken)
    {
        return View(new OpeningStockFormViewModel { Products = await _products.GetOptionsAsync(true, cancellationToken) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageInventory)]
    public async Task<IActionResult> OpeningStock(OpeningStockFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Products = await _products.GetOptionsAsync(true, cancellationToken);
            return View(model);
        }

        try
        {
            var movement = await _stock.RecordOpeningStockAsync(
                model.ProductId, model.Quantity, model.UnitCost, model.Reason, User.GetUserId()!, cancellationToken);

            TempData["Success"] = $"Opening stock {movement.ReferenceNumber} recorded.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            model.Products = await _products.GetOptionsAsync(true, cancellationToken);
            return View(model);
        }
    }

    private async Task<List<SelectListItem>> ProductOptionsAsync(CancellationToken cancellationToken)
    {
        var products = await _products.GetOptionsAsync(activeOnly: false, cancellationToken);
        return products.Select(p => new SelectListItem(p.DisplayName, p.Id.ToString())).ToList();
    }

    private async Task<List<SelectListItem>> UserOptionsAsync()
    {
        var users = await _userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync();

        return users.Select(u => new SelectListItem(u.FullName, u.Id)).ToList();
    }
}
