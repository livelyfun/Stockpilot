using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;
using StockPilot.Web.Infrastructure;
using StockPilot.Web.Services;

namespace StockPilot.Tests;

[Collection("postgres")]
public class InventoryFlowTests
{
    private readonly PostgresFixture _fixture;

    public InventoryFlowTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Receiving_a_purchase_updates_balance_and_ledger_once()
    {
        var (productId, supplierId, adminId, scope) = await CreateProductAsync(openingQuantity: 5m, openingCost: 10m);
        await using var _ = scope;
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        var purchase = await purchases.CreateDraftAsync(new PurchaseInput(supplierId, "Test receipt",
            [new PurchaseLineInput(productId, 4m, 12m)]), adminId);
        await purchases.ReceiveAsync(purchase.Id, adminId);

        var balance = await stock.GetBalanceAsync(productId);
        Assert.NotNull(balance);
        Assert.Equal(9m, balance!.QuantityOnHand);
        Assert.Equal(10.8889m, balance.AverageCost, 4);

        await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId && m.ReferenceId == purchase.Id)
            .ToListAsync();

        var receipt = Assert.Single(movements);
        Assert.Equal(StockMovementType.PurchaseReceipt, receipt.MovementType);
        Assert.Equal(4m, receipt.QuantityChange);
        Assert.Equal("Purchase", receipt.ReferenceType);

        await Assert.ThrowsAsync<BusinessRuleException>(() => purchases.ReceiveAsync(purchase.Id, adminId));
    }

    [Fact]
    public async Task Reversing_a_purchase_removes_received_quantity_and_records_compensating_entry()
    {
        var (productId, supplierId, adminId, scope) = await CreateProductAsync(openingQuantity: 5m, openingCost: 10m);
        await using var _ = scope;
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        var purchase = await purchases.CreateDraftAsync(new PurchaseInput(supplierId, null,
            [new PurchaseLineInput(productId, 4m, 12m)]), adminId);
        await purchases.ReceiveAsync(purchase.Id, adminId);
        await purchases.ReverseReceivedAsync(purchase.Id, "Supplier collected wrong shipment", adminId);

        var balance = await stock.GetBalanceAsync(productId);
        Assert.NotNull(balance);
        Assert.Equal(5m, balance!.QuantityOnHand);

        await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reversal = await db.StockMovements.AsNoTracking().SingleAsync(m =>
            m.ProductId == productId &&
            m.ReferenceId == purchase.Id &&
            m.MovementType == StockMovementType.PurchaseReversal);

        Assert.Equal(-4m, reversal.QuantityChange);
        Assert.Equal(5m, reversal.BalanceAfter);
        Assert.Equal(new StockMovementType[] { StockMovementType.PurchaseReceipt, StockMovementType.PurchaseReversal },
            await db.StockMovements.AsNoTracking()
                .Where(m => m.ProductId == productId && m.ReferenceId == purchase.Id)
                .OrderBy(m => m.Id)
                .Select(m => m.MovementType)
                .ToArrayAsync());

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            purchases.ReverseReceivedAsync(purchase.Id, "Second reversal attempt", adminId));
    }

    [Fact]
    public async Task Completing_a_sale_deducts_stock_at_weighted_average_and_reversal_restores_it()
    {
        var (productId, supplierId, adminId, scope) = await CreateProductAsync(openingQuantity: 10m, openingCost: 10m);
        await using var _ = scope;
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        var sales = scope.ServiceProvider.GetRequiredService<ISaleService>();
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        var purchase = await purchases.CreateDraftAsync(new PurchaseInput(supplierId, null,
            [new PurchaseLineInput(productId, 10m, 20m)]), adminId);
        await purchases.ReceiveAsync(purchase.Id, adminId);

        var sale = await sales.CreateDraftAsync(new SaleInput("Test Customer", null,
            [new SaleLineInput(productId, 5m, 30m)]), adminId);
        await sales.CompleteAsync(sale.Id, adminId);

        var afterSale = await stock.GetBalanceAsync(productId);
        Assert.Equal(15m, afterSale!.QuantityOnHand);
        Assert.Equal(15m, afterSale.AverageCost);

        await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var issue = await db.StockMovements.AsNoTracking().SingleAsync(m =>
            m.ProductId == productId && m.ReferenceId == sale.Id && m.MovementType == StockMovementType.SaleIssue);
        Assert.Equal(-5m, issue.QuantityChange);
        Assert.Equal(15m, issue.UnitCost);

        await sales.ReverseCompletedAsync(sale.Id, "Customer returned the order", adminId);

        var afterReversal = await stock.GetBalanceAsync(productId);
        Assert.Equal(20m, afterReversal!.QuantityOnHand);
        Assert.Equal(15m, afterReversal.AverageCost);

        await Assert.ThrowsAsync<BusinessRuleException>(() => sales.CompleteAsync(sale.Id, adminId));
    }

    [Fact]
    public async Task Sale_completion_is_rejected_when_stock_is_insufficient()
    {
        var (productId, _, adminId, scope) = await CreateProductAsync(openingQuantity: 2m, openingCost: 10m);
        await using var _ = scope;
        var sales = scope.ServiceProvider.GetRequiredService<ISaleService>();
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        var sale = await sales.CreateDraftAsync(new SaleInput("Test Customer", null,
            [new SaleLineInput(productId, 3m, 20m)]), adminId);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => sales.CompleteAsync(sale.Id, adminId));
        Assert.Contains("Insufficient stock", error.Message);

        var balance = await stock.GetBalanceAsync(productId);
        Assert.Equal(2m, balance!.QuantityOnHand);

        await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.StockMovements.AnyAsync(m =>
            m.ProductId == productId && m.ReferenceId == sale.Id));
    }

    [Fact]
    public async Task Concurrent_sale_completions_cannot_oversell_the_same_product()
    {
        var (productId, _, adminId, scope) = await CreateProductAsync(openingQuantity: 1m, openingCost: 10m);
        await using var _ = scope;
        var sales = scope.ServiceProvider.GetRequiredService<ISaleService>();

        var first = await sales.CreateDraftAsync(new SaleInput("First customer", null,
            [new SaleLineInput(productId, 1m, 20m)]), adminId);
        var second = await sales.CreateDraftAsync(new SaleInput("Second customer", null,
            [new SaleLineInput(productId, 1m, 20m)]), adminId);

        var results = await Task.WhenAll(
            CompleteInNewScopeAsync(first.Id, adminId),
            CompleteInNewScopeAsync(second.Id, adminId));

        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result is BusinessRuleException);

        await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.ProductId == productId);
        Assert.Equal(0m, balance.QuantityOnHand);
        Assert.Equal(1, await db.StockMovements.CountAsync(m =>
            m.ProductId == productId && m.MovementType == StockMovementType.SaleIssue));
    }

    [Fact]
    public async Task Stock_decrease_cannot_make_the_balance_negative()
    {
        var (productId, _, adminId, scope) = await CreateProductAsync(openingQuantity: 2m, openingCost: 10m);
        await using var _ = scope;
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            stock.AdjustAsync(productId, increase: false, quantity: 3m, unitCost: null,
                reason: "Test negative adjustment", actorUserId: adminId));

        var balance = await stock.GetBalanceAsync(productId);
        Assert.Equal(2m, balance!.QuantityOnHand);

        await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.StockMovements.AnyAsync(m =>
            m.ProductId == productId && m.MovementType == StockMovementType.AdjustmentDecrease));
    }

    private async Task<Exception?> CompleteInNewScopeAsync(int saleId, string adminId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISaleService>();
        try
        {
            await sales.CompleteAsync(saleId, adminId);
            return null;
        }
        catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException)
        {
            return ex;
        }
    }

    private async Task<(int ProductId, int SupplierId, string AdminId, AsyncServiceScope Scope)> CreateProductAsync(
        decimal openingQuantity, decimal openingCost)
    {
        var scope = _fixture.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var adminId = await _fixture.GetAdminUserIdAsync();
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var categories = services.GetRequiredService<ICategoryService>();
        var units = services.GetRequiredService<IUnitService>();
        var suppliers = services.GetRequiredService<ISupplierService>();
        var products = services.GetRequiredService<IProductService>();
        var stock = services.GetRequiredService<IStockService>();

        var category = await categories.CreateAsync(new CategoryInput("Test category " + tag, null, true), adminId);
        var unit = await units.CreateAsync(new UnitInput("Test unit " + tag, "U" + tag[..6], true), adminId);
        var supplier = await suppliers.CreateAsync(new SupplierInput(
            "Test supplier " + tag, null, null, null, null, null, true), adminId);
        var product = await products.CreateAsync(new ProductInput(
            "T-" + tag, "Test product " + tag, null, category.Id, unit.Id,
            openingCost, openingCost * 2, 1m, true), adminId);

        if (openingQuantity > 0)
        {
            await stock.RecordOpeningStockAsync(product.Id, openingQuantity, openingCost,
                "Test opening balance", adminId);
        }

        return (product.Id, supplier.Id, adminId, scope);
    }
}
