using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StockPilot.Web.Constants;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class ProductsController : Controller
{
    private readonly IProductService _products;
    private readonly IStockService _stock;

    public ProductsController(IProductService products, IStockService stock)
    {
        _products = products;
        _stock = stock;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, int? categoryId, string? status, string? stockStatus, string? sort,
        int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await _products.GetPagedAsync(
            new ProductFilter(search, categoryId, status, stockStatus, sort, page), cancellationToken);

        ViewBag.Search = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.Status = status;
        ViewBag.StockStatus = stockStatus;
        ViewBag.Sort = sort;
        ViewBag.Categories = await CategoryOptionsAsync(cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var movements = await _stock.GetMovementsAsync(
            new StockMovementFilter(ProductId: id, Page: 1, PageSize: 10), cancellationToken);
        ViewBag.Movements = movements;
        return View(product);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(await BuildFormAsync(new ProductFormViewModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        try
        {
            var product = await _products.CreateAsync(
                new ProductInput(model.Sku, model.Name, model.Description, model.CategoryId, model.UnitId,
                    model.CostPrice, model.SellingPrice, model.ReorderLevel, model.IsActive),
                User.GetUserId()!, cancellationToken);

            TempData["Success"] = $"Product {product.Sku} created.";
            return RedirectToAction(nameof(Details), new { id = product.Id });
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model, cancellationToken));
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var model = new ProductFormViewModel
        {
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            UnitId = product.UnitId,
            CostPrice = product.CostPrice,
            SellingPrice = product.SellingPrice,
            ReorderLevel = product.ReorderLevel,
            IsActive = product.IsActive
        };

        return View(await BuildFormAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        try
        {
            await _products.UpdateAsync(id,
                new ProductInput(model.Sku, model.Name, model.Description, model.CategoryId, model.UnitId,
                    model.CostPrice, model.SellingPrice, model.ReorderLevel, model.IsActive),
                User.GetUserId()!, cancellationToken);

            TempData["Success"] = "Product updated.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model, cancellationToken));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageMasterData)]
    public async Task<IActionResult> ToggleActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        try
        {
            await _products.SetActiveAsync(id, isActive, User.GetUserId()!, cancellationToken);
            TempData["Success"] = isActive ? "Product activated." : "Product deactivated.";
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
            await _products.DeleteAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = "Product deleted.";
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

    private async Task<ProductFormViewModel> BuildFormAsync(ProductFormViewModel model, CancellationToken cancellationToken)
    {
        var categories = await _products.GetCategoriesAsync(activeOnly: false, cancellationToken);
        var units = await _products.GetUnitsAsync(activeOnly: false, cancellationToken);
        model.Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == model.CategoryId)).ToList();
        model.Units = units.Select(u => new SelectListItem($"{u.Name} ({u.Code})", u.Id.ToString(), u.Id == model.UnitId)).ToList();
        return model;
    }

    private async Task<List<SelectListItem>> CategoryOptionsAsync(CancellationToken cancellationToken)
    {
        var categories = await _products.GetCategoriesAsync(activeOnly: false, cancellationToken);
        return categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
    }
}
