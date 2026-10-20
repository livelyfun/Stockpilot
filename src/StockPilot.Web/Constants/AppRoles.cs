namespace StockPilot.Web.Constants;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string InventoryStaff = "InventoryStaff";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, InventoryStaff, Viewer];
}

public static class AppPolicies
{
    /// <summary>Create and maintain categories, units, products and suppliers.</summary>
    public const string ManageMasterData = "ManageMasterData";

    /// <summary>Create and receive purchases, create and complete sales.</summary>
    public const string ManageTransactions = "ManageTransactions";

    /// <summary>Opening stock, adjustments and transaction reversals.</summary>
    public const string ManageInventory = "ManageInventory";

    public const string AdminOnly = "AdminOnly";
}
