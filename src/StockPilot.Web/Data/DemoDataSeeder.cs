using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Data;
using StockPilot.Web.Services;

namespace StockPilot.Web.Data;

/// <summary>
/// Development-only demonstration data. It is inserted through the same domain services
/// used by the application, so stock movements and balances stay consistent.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        string adminUserId,
        string staffUserId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (await db.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var categories = services.GetRequiredService<ICategoryService>();
        var units = services.GetRequiredService<IUnitService>();
        var products = services.GetRequiredService<IProductService>();
        var suppliers = services.GetRequiredService<ISupplierService>();
        var purchases = services.GetRequiredService<IPurchaseService>();
        var sales = services.GetRequiredService<ISaleService>();
        var stock = services.GetRequiredService<IStockService>();

        logger.LogInformation("Seeding demonstration inventory data...");

        var computing = await categories.CreateAsync(new CategoryInput("Computing", "Laptops, monitors and peripherals", true), adminUserId, cancellationToken);
        var office = await categories.CreateAsync(new CategoryInput("Office Supplies", "Day-to-day office consumables", true), adminUserId, cancellationToken);
        var networking = await categories.CreateAsync(new CategoryInput("Networking", "Routers, cables and connectivity", true), adminUserId, cancellationToken);
        var accessories = await categories.CreateAsync(new CategoryInput("Accessories", "Small accessories and add-ons", true), adminUserId, cancellationToken);

        var pieces = await units.CreateAsync(new UnitInput("Pieces", "PCS", true), adminUserId, cancellationToken);
        var boxes = await units.CreateAsync(new UnitInput("Box", "BOX", true), adminUserId, cancellationToken);
        var packs = await units.CreateAsync(new UnitInput("Pack", "PK", true), adminUserId, cancellationToken);

        var lumina = await suppliers.CreateAsync(new SupplierInput(
            "Lumina Components", "Sita Gurung", "+977-1-555-0142", "orders@luminacomponents.example",
            "14 Trade Avenue, Lalitpur", "Primary hardware supplier. Net 30 terms.", true), adminUserId, cancellationToken);

        var orbit = await suppliers.CreateAsync(new SupplierInput(
            "Orbit Office Supply", "Bikash Thapa", "+977-1-555-0177", "sales@orbitoffice.example",
            "82 Market Road, Kathmandu", "Office consumables; weekly delivery.", true), adminUserId, cancellationToken);

        var bluepeak = await suppliers.CreateAsync(new SupplierInput(
            "BluePeak Distribution", "Nisha Karki", "+977-1-555-0193", "hello@bluepeak.example",
            "5 Summit Lane, Bhaktapur", "Networking equipment distributor.", true), adminUserId, cancellationToken);

        var productSeeds = new[]
        {
            new { Sku = "SP-LAP-001", Name = "AeroBook 14 Laptop", Desc = "14-inch business laptop, 16 GB RAM, 512 GB SSD", Cat = computing, Unit = pieces, Cost = 820m, Sell = 1099m, Reorder = 4m, Opening = 12m },
            new { Sku = "SP-MON-001", Name = "ClearView 24 Monitor", Desc = "24-inch IPS monitor with height adjustment", Cat = computing, Unit = pieces, Cost = 145m, Sell = 199m, Reorder = 5m, Opening = 18m },
            new { Sku = "SP-KEY-001", Name = "QuietType Wireless Keyboard", Desc = "Low-profile wireless keyboard", Cat = computing, Unit = pieces, Cost = 28m, Sell = 44.50m, Reorder = 10m, Opening = 40m },
            new { Sku = "SP-MOU-001", Name = "GlidePoint Mouse", Desc = "Ergonomic wireless mouse", Cat = accessories, Unit = pieces, Cost = 12.50m, Sell = 24m, Reorder = 15m, Opening = 60m },
            new { Sku = "SP-SSD-001", Name = "Vault 1TB External SSD", Desc = "USB-C portable solid-state drive", Cat = computing, Unit = pieces, Cost = 72m, Sell = 109m, Reorder = 6m, Opening = 25m },
            new { Sku = "SP-USB-001", Name = "LinkHub USB-C Dock", Desc = "8-in-1 USB-C docking station", Cat = accessories, Unit = pieces, Cost = 48m, Sell = 79m, Reorder = 8m, Opening = 22m },
            new { Sku = "SP-PAP-001", Name = "A4 Copy Paper", Desc = "80 gsm A4 copy paper, 500 sheets per box", Cat = office, Unit = boxes, Cost = 22m, Sell = 34m, Reorder = 20m, Opening = 80m },
            new { Sku = "SP-PEN-001", Name = "Precision Gel Pens", Desc = "Black gel pens, pack of 10", Cat = office, Unit = packs, Cost = 3.20m, Sell = 6.50m, Reorder = 30m, Opening = 150m },
            new { Sku = "SP-NOTE-001", Name = "Grid Notebook A5", Desc = "Hard-cover grid notebook, 160 pages", Cat = office, Unit = pieces, Cost = 4.10m, Sell = 8.90m, Reorder = 25m, Opening = 90m },
            new { Sku = "SP-RTR-001", Name = "WaveLink AX Router", Desc = "Dual-band Wi-Fi 6 router", Cat = networking, Unit = pieces, Cost = 95m, Sell = 149m, Reorder = 5m, Opening = 15m },
            new { Sku = "SP-CAB-001", Name = "Cat6 Patch Cable 2m", Desc = "Snagless Cat6 Ethernet cable", Cat = networking, Unit = pieces, Cost = 2.10m, Sell = 5.50m, Reorder = 50m, Opening = 220m },
            new { Sku = "SP-INK-001", Name = "EcoTone Ink Cartridge", Desc = "High-yield black ink cartridge", Cat = office, Unit = pieces, Cost = 18m, Sell = 32m, Reorder = 12m, Opening = 35m }
        };

        var created = new Dictionary<string, int>();
        var names = new Dictionary<string, string>();
        foreach (var seed in productSeeds)
        {
            var product = await products.CreateAsync(new ProductInput(
                seed.Sku, seed.Name, seed.Desc, seed.Cat.Id, seed.Unit.Id,
                seed.Cost, seed.Sell, seed.Reorder, true), adminUserId, cancellationToken);

            created[seed.Sku] = product.Id;
            names[seed.Sku] = seed.Name;
            await stock.RecordOpeningStockAsync(
                product.Id, seed.Opening, seed.Cost, "Initial opening stock count", adminUserId, cancellationToken);
        }

        var purchaseOne = await purchases.CreateDraftAsync(new PurchaseInput(
            lumina.Id,
            "Q4 hardware replenishment",
            [
                new PurchaseLineInput(created["SP-LAP-001"], 8, 800m),
                new PurchaseLineInput(created["SP-SSD-001"], 15, 70m)
            ]), staffUserId, cancellationToken);
        await purchases.ReceiveAsync(purchaseOne.Id, adminUserId, cancellationToken);

        var purchaseTwo = await purchases.CreateDraftAsync(new PurchaseInput(
            orbit.Id,
            "Monthly office consumables",
            [
                new PurchaseLineInput(created["SP-PAP-001"], 40, 21m),
                new PurchaseLineInput(created["SP-PEN-001"], 100, 3.00m),
                new PurchaseLineInput(created["SP-NOTE-001"], 50, 4.00m)
            ]), staffUserId, cancellationToken);
        await purchases.ReceiveAsync(purchaseTwo.Id, staffUserId, cancellationToken);

        await purchases.CreateDraftAsync(new PurchaseInput(
            bluepeak.Id,
            "Awaiting confirmation from supplier",
            [
                new PurchaseLineInput(created["SP-RTR-001"], 5, 92m),
                new PurchaseLineInput(created["SP-CAB-001"], 100, 2.00m)
            ]), staffUserId, cancellationToken);

        var saleOne = await sales.CreateDraftAsync(new SaleInput(
            "Himalayan Retail Pvt. Ltd.",
            "Customer purchase order HR-2291",
            [
                new SaleLineInput(created["SP-LAP-001"], 2, 1099m),
                new SaleLineInput(created["SP-MON-001"], 2, 199m),
                new SaleLineInput(created["SP-KEY-001"], 5, 44.50m)
            ]), staffUserId, cancellationToken);
        await sales.CompleteAsync(saleOne.Id, staffUserId, cancellationToken);

        var saleTwo = await sales.CreateDraftAsync(new SaleInput(
            "Everest Coworking",
            null,
            [
                new SaleLineInput(created["SP-CAB-001"], 10, 5.50m),
                new SaleLineInput(created["SP-USB-001"], 3, 79m),
                new SaleLineInput(created["SP-PAP-001"], 5, 34m)
            ]), staffUserId, cancellationToken);
        await sales.CompleteAsync(saleTwo.Id, staffUserId, cancellationToken);

        await sales.CreateDraftAsync(new SaleInput(
            "Pending quotation",
            "Quote sent, awaiting customer approval",
            [
                new SaleLineInput(created["SP-RTR-001"], 1, 149m),
                new SaleLineInput(created["SP-MOU-001"], 2, 24m)
            ]), staffUserId, cancellationToken);

        await stock.AdjustAsync(created["SP-MOU-001"], true, 5, 12.50m,
            "Cycle count surplus found during quarterly stocktake", adminUserId, cancellationToken);
        await stock.AdjustAsync(created["SP-PEN-001"], false, 2, null,
            "Two packs damaged by water in storage", adminUserId, cancellationToken);

        logger.LogInformation("Demonstration data seeded: {Products} products, 3 purchases, 3 sales.", productSeeds.Length);
    }
}
