using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using StockPilot.Web.Entities;
using StockPilot.Web.Services;

namespace StockPilot.Web.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ProductFormViewModel
{
    [Required, StringLength(40), Display(Name = "SKU")]
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9\-_/]*$",
        ErrorMessage = "SKU may contain letters, numbers, hyphens, underscores and slashes only.")]
    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a unit.")]
    [Display(Name = "Unit of measurement")]
    public int UnitId { get; set; }

    [Range(0, 999999999, ErrorMessage = "Cost price must be zero or greater.")]
    [Display(Name = "Cost price")]
    public decimal CostPrice { get; set; }

    [Range(0, 999999999, ErrorMessage = "Selling price must be zero or greater.")]
    [Display(Name = "Selling price")]
    public decimal SellingPrice { get; set; }

    [Range(0, 999999999, ErrorMessage = "Reorder level must be zero or greater.")]
    [Display(Name = "Reorder level")]
    public decimal ReorderLevel { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public List<SelectListItem> Categories { get; set; } = [];
    public List<SelectListItem> Units { get; set; } = [];
}

public class CategoryFormViewModel
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class UnitFormViewModel
{
    [Required, StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(20)]
    [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "Code may contain letters, numbers and hyphens only.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class SupplierFormViewModel
{
    [Required, StringLength(150), Display(Name = "Business name")]
    public string BusinessName { get; set; } = string.Empty;

    [StringLength(120), Display(Name = "Contact person")]
    public string? ContactPerson { get; set; }

    [StringLength(40), Phone]
    public string? Phone { get; set; }

    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class PurchaseLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    [Display(Name = "Product")]
    public int? ProductId { get; set; }

    [Range(0.001, 999999999, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 999999999, ErrorMessage = "Unit cost cannot be negative.")]
    [Display(Name = "Unit cost")]
    public decimal UnitCost { get; set; }
}

public class PurchaseFormViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a supplier.")]
    [Display(Name = "Supplier")]
    public int SupplierId { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<PurchaseLineViewModel> Items { get; set; } = [new()];
    public List<SelectListItem> Suppliers { get; set; } = [];
    public List<ProductOption> Products { get; set; } = [];
}

public class SaleLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    [Display(Name = "Product")]
    public int? ProductId { get; set; }

    [Range(0.001, 999999999, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 999999999, ErrorMessage = "Unit price cannot be negative.")]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }
}

public class SaleFormViewModel
{
    [StringLength(150), Display(Name = "Customer name")]
    public string? CustomerName { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<SaleLineViewModel> Items { get; set; } = [new()];
    public List<ProductOption> Products { get; set; } = [];
    public int? CurrentStock { get; set; }
}

public class AdjustmentFormViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a product.")]
    [Display(Name = "Product")]
    public int ProductId { get; set; }

    [Required, Display(Name = "Direction")]
    public string Direction { get; set; } = "increase";

    [Range(0.001, 999999999, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "Unit cost cannot be negative.")]
    [Display(Name = "Unit cost (increase only)")]
    public decimal? UnitCost { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;

    public List<ProductOption> Products { get; set; } = [];
}

public class OpeningStockFormViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a product.")]
    [Display(Name = "Product")]
    public int ProductId { get; set; }

    [Range(0.001, 999999999, ErrorMessage = "Quantity must be greater than zero.")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "Unit cost cannot be negative.")]
    [Display(Name = "Unit cost")]
    public decimal? UnitCost { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = "Initial opening stock count";

    public List<ProductOption> Products { get; set; } = [];
}

public class CreateUserViewModel
{
    [Required, StringLength(150), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 10)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required, Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    public List<SelectListItem> Roles { get; set; } = [];
}

public class EditUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(150), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    [Display(Name = "Active account")]
    public bool IsActive { get; set; }

    public List<SelectListItem> Roles { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

public class ResetPasswordViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 10)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AuditLogFilter
{
    public string? Search { get; set; }
    public string? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class StockReportPageViewModel
{
    public StockReportFilter Filter { get; set; } = new();
    public StockReportPage Page { get; set; } = null!;
    public List<SelectListItem> Categories { get; set; } = [];
    public string Title { get; set; } = "Stock report";
    public string? Description { get; set; }
    public bool IsValuation { get; set; }
    public List<CategoryValuationRow> CategoryValuation { get; set; } = [];
}

public class PurchaseReportPageViewModel
{
    public PurchaseReportFilter Filter { get; set; } = new();
    public Infrastructure.PaginatedList<PurchaseReportRow> Rows { get; set; } = null!;
    public PurchaseReportSummary Summary { get; set; } = null!;
    public List<SelectListItem> Suppliers { get; set; } = [];
}

public class SaleReportPageViewModel
{
    public SaleReportFilter Filter { get; set; } = new();
    public Infrastructure.PaginatedList<SaleReportRow> Rows { get; set; } = null!;
    public SaleReportSummary Summary { get; set; } = null!;
}

public class MovementReportPageViewModel
{
    public StockMovementFilter Filter { get; set; } = new();
    public Infrastructure.PaginatedList<MovementReportRow> Rows { get; set; } = null!;
    public List<SelectListItem> Products { get; set; } = [];
    public List<SelectListItem> Users { get; set; } = [];
}

/// <summary>Read-model for product dropdowns; includes current stock and default prices.</summary>
public record ProductOption(
    int Id,
    string Sku,
    string Name,
    decimal CostPrice,
    decimal SellingPrice,
    decimal StockOnHand,
    string UnitCode,
    decimal ReorderLevel)
{
    public string DisplayName => $"{Sku} — {Name}";
}
