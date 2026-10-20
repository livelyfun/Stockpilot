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
public class PurchasesController : Controller
{
    private readonly IPurchaseService _purchases;
    private readonly ISupplierService _suppliers;
    private readonly IProductService _products;

    public PurchasesController(IPurchaseService purchases, ISupplierService suppliers, IProductService products)
    {
        _purchases = purchases;
        _suppliers = suppliers;
        _products = products;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, PurchaseStatus? status, int? supplierId, DateTime? from, DateTime? to,
        int page = 1, CancellationToken cancellationToken = default)
    {
        var model = await _purchases.GetPagedAsync(
            new PurchaseFilter(search, status, supplierId, from, to, page), cancellationToken);

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.SupplierId = supplierId;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.Suppliers = await SupplierOptionsAsync(cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var purchase = await _purchases.GetByIdAsync(id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        ViewBag.CanReverse = purchase.Status == PurchaseStatus.Received && User.IsInRole(AppRoles.Admin);
        return View(purchase);
    }

    [HttpGet]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(await BuildFormAsync(new PurchaseFormViewModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Create(PurchaseFormViewModel model, CancellationToken cancellationToken)
    {
        NormalizeLines(model);
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        try
        {
            var purchase = await _purchases.CreateDraftAsync(ToInput(model), User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Draft purchase {purchase.PurchaseNumber} created. Review it before receiving.";
            return RedirectToAction(nameof(Details), new { id = purchase.Id });
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
        var purchase = await _purchases.GetByIdAsync(id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        if (purchase.Status != PurchaseStatus.Draft)
        {
            TempData["Error"] = "Only draft purchases can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new PurchaseFormViewModel
        {
            SupplierId = purchase.SupplierId,
            Notes = purchase.Notes,
            Items = purchase.Items.Select(i => new PurchaseLineViewModel
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitCost = i.UnitCost
            }).ToList()
        };

        return View(await BuildFormAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.ManageTransactions)]
    public async Task<IActionResult> Edit(int id, PurchaseFormViewModel model, CancellationToken cancellationToken)
    {
        NormalizeLines(model);
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        try
        {
            var purchase = await _purchases.UpdateDraftAsync(id, ToInput(model), User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Draft purchase {purchase.PurchaseNumber} updated.";
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
    public async Task<IActionResult> Receive(int id, CancellationToken cancellationToken)
    {
        try
        {
            var purchase = await _purchases.ReceiveAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Purchase {purchase.PurchaseNumber} received. Stock balances were updated.";
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
            var purchase = await _purchases.CancelDraftAsync(id, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Purchase {purchase.PurchaseNumber} cancelled.";
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
            var purchase = await _purchases.ReverseReceivedAsync(id, reason, User.GetUserId()!, cancellationToken);
            TempData["Success"] = $"Purchase {purchase.PurchaseNumber} reversed. Reversal movements were recorded.";
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private void NormalizeLines(PurchaseFormViewModel model)
    {
        model.Items = model.Items.Where(i => i.ProductId is > 0).ToList();
        if (model.Items.Count == 0)
        {
            ModelState.AddModelError("Items", "Add at least one product line.");
        }
    }

    private static PurchaseInput ToInput(PurchaseFormViewModel model) =>
        new(model.SupplierId, model.Notes,
            model.Items.Select(i => new PurchaseLineInput(i.ProductId!.Value, i.Quantity, i.UnitCost)).ToList());

    private async Task<PurchaseFormViewModel> BuildFormAsync(PurchaseFormViewModel model, CancellationToken cancellationToken)
    {
        var suppliers = await _suppliers.GetActiveAsync(cancellationToken);
        model.Suppliers = suppliers
            .Select(s => new SelectListItem(s.BusinessName, s.Id.ToString(), s.Id == model.SupplierId))
            .ToList();
        model.Products = await _products.GetOptionsAsync(activeOnly: true, cancellationToken);
        if (model.Items.Count == 0)
        {
            model.Items.Add(new PurchaseLineViewModel());
        }

        return model;
    }

    private async Task<List<SelectListItem>> SupplierOptionsAsync(CancellationToken cancellationToken)
    {
        var suppliers = await _suppliers.GetActiveAsync(cancellationToken);
        return suppliers.Select(s => new SelectListItem(s.BusinessName, s.Id.ToString())).ToList();
    }
}
