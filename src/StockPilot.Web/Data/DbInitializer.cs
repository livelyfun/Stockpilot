using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockPilot.Web.Constants;
using StockPilot.Web.Data;
using StockPilot.Web.Entities;

namespace StockPilot.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var autoMigrate = configuration.GetValue("Database:AutoMigrate", environment.IsDevelopment());
        if (autoMigrate)
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogWarning("Database is not reachable. Skipping initialization.");
            return;
        }

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var admin = await EnsureAdministratorAsync(userManager, configuration, logger);
        if (admin is null)
        {
            return;
        }

        if (!environment.IsProduction() && configuration.GetValue("Seed:DemoData", false))
        {
            var demoPassword = configuration["Seed:DemoPassword"];
            var staff = await EnsureDemoUserAsync(userManager, "staff@stockpilot.local", "Maya Sharma", AppRoles.InventoryStaff, demoPassword, logger);
            var viewer = await EnsureDemoUserAsync(userManager, "viewer@stockpilot.local", "Arjun Rai", AppRoles.Viewer, demoPassword, logger);

            if (staff is not null && viewer is not null)
            {
                await DemoDataSeeder.SeedAsync(scope.ServiceProvider, admin.Id, staff.Id, logger, cancellationToken);
            }
        }
    }

    private static async Task<ApplicationUser?> EnsureAdministratorAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:AdminEmail / Seed:AdminPassword are not configured; no administrator was created.");
            return null;
        }

        email = email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = configuration["Seed:AdminFullName"]?.Trim() is { Length: > 0 } name ? name : "System Administrator",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Could not create the initial administrator: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return null;
            }

            logger.LogInformation("Initial administrator {Email} created.", email);
        }

        if (!await userManager.IsInRoleAsync(user, AppRoles.Admin))
        {
            await userManager.AddToRoleAsync(user, AppRoles.Admin);
        }

        return user;
    }

    private static async Task<ApplicationUser?> EnsureDemoUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string role,
        string? password,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed:DemoPassword is not configured; skipping demo role account {Email}.", email);
            return null;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogWarning("Could not create demo user {Email}: {Errors}", email,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return null;
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return user;
    }
}
