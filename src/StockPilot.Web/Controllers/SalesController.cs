using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Web.Constants;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;
using StockPilot.Web.ViewModels;

namespace StockPilot.Web.Controllers;

[Authorize]
public class SalesController : Controller
{
    private readonly ISaleService _sales;
    private readonly IProductService _products;

    public SalesController(ISaleService sales, IProductService products)
    {
        _sales = sales;
        _products = products;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, SaleStatus? status, DateTime? from, DateTime? to,
        int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await _sales.GetPagedAsync(new SaleFilter(search, status, from, to, page), cancellationToken);
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var sale = await _sales.GetByIdAsync(id, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        var options = await _products.GetOptionsAsync(activeOnly: false, cancellationToken);
        ViewBag.Stock = options.ToDictionary(p => p.Id, p => p.StockOnHand);
        ViewBag.CanReverse = sale.Status == SaleStatus.Completed && User.IsInRole(AppRoles.Admin);
        return View(sale);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(await BuildFormAsync(new SaleFormViewModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Create(SaleFormViewModel model, CancellationToken cancellationToken)
    {
        NormalizeLines(model);
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        try
        {
            var sale = await _sales.CreateDraftAsync(ToInput(model), User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Draft sale {sale.SaleNumber} created. Complete it when the customer confirms.";
            return RedirectToAction(nameof(Details), new { id = sale.Id });
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model, cancellationToken));
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var sale = await _sales.GetByIdAsync(id, cancellationToken);
        if (sale is null)
        {
            return NotFound();
        }

        if (sale.Status != SaleStatus.Draft)
        {
            TempData["Error"] = "Only draft sales can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new SaleFormViewModel
        {
            CustomerName = sale.CustomerName,
            Notes = sale.Notes,
            Items = sale.Items.Select(i => new SaleLineViewModel
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        return View(await BuildFormAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Edit(int id, SaleFormViewModel model, CancellationToken cancellationToken)
    {
        NormalizeLines(model);
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        try
        {
            var sale = await _sales.UpdateDraftAsync(id, ToInput(model), User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Draft sale {sale.SaleNumber} updated.";
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
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await _sales.CompleteAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Sale {sale.SaleNumber} completed. Stock was issued.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await _sales.CancelDraftAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Sale {sale.SaleNumber} cancelled.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> Reverse(int id, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await _sales.ReverseCompletedAsync(id, reason, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Sale {sale.SaleNumber} reversed. Returned stock was recorded.";
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private void NormalizeLines(SaleFormViewModel model)
    {
        model.Items = model.Items.Where(i => i.ProductId is > 0).ToList();
        if (model.Items.Count == 0)
        {
            ModelState.AddModelError("Items", "Add at least one product line.");
        }
    }

    private static SaleInput ToInput(SaleFormViewModel model) =>
        new(model.CustomerName, model.Notes,
            model.Items.Select(i => new SaleLineInput(i.ProductId!.Value, i.Quantity, i.UnitPrice)).ToList());

    private async Task<SaleFormViewModel> BuildFormAsync(SaleFormViewModel model, CancellationToken cancellationToken)
    {
        model.Products = await _products.GetOptionsAsync(activeOnly: true, cancellationToken);
        if (model.Items.Count == 0)
        {
            model.Items.Add(new SaleLineViewModel());
        }

        return model;
    }
}
