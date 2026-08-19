using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Services;

namespace Recon.Web.Data;

/// <summary>
/// Idempotent startup seeding: GL account map, page registry, default business roles and the
/// initial admin user with full grants. All values indicative/editable.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await SeedGlAccountsAsync(db);
        await SeedCurrenciesAsync(db);
        await SeedIdentityAsync(db, userManager, logger);
    }

    /// <summary>The six MasterCard card-reconciliation GL accounts (idempotent by AcctNum).</summary>
    public static async Task SeedGlAccountsAsync(AppDbContext db)
    {
        var seed = new (string Acct, int Ccy, GlAccountRole Role, string Desc)[]
        {
            ("9849065772", 480, GlAccountRole.CardholderSettlement,   "MUR MASTERCARD CARDHOLDER SETTLEMENT ACCOUNT"),
            ("9849065773", 840, GlAccountRole.CardholderSettlement,   "USD MASTERCARD CARDHOLDER SETTLEMENT ACCOUNT"),
            ("9849065774", 978, GlAccountRole.CardholderSettlement,   "EUR MASTERCARD CARDHOLDER SETTLEMENT ACCOUNT"),
            ("9849077911", 480, GlAccountRole.IntermediarySettlement, "MUR MC - INTERMEDIARY SETTLEMENT CC"),
            ("9849077913", 840, GlAccountRole.IntermediarySettlement, "USD MC - INTERMEDIARY SETTLEMENT CC"),
            ("9849077912", 978, GlAccountRole.IntermediarySettlement, "EUR MC - INTERMEDIARY SETTLEMENT CC"),
        };

        var existing = await db.GlSettlementAccounts.Select(a => a.AcctNum).ToListAsync();
        var toAdd = seed.Where(s => !existing.Contains(s.Acct))
            .Select(s => new GlSettlementAccount
            {
                AcctNum = s.Acct, Ccy = s.Ccy, Role = s.Role, Description = s.Desc,
                CreatedBy = CurrentUserService.SystemUserId, CreatedDate = DateTime.UtcNow
            });
        db.GlSettlementAccounts.AddRange(toAdd);
        await db.SaveChangesAsync();
    }

    public static async Task SeedCurrenciesAsync(AppDbContext db)
    {
        var existing = await db.IsoCurrencies.Select(c => c.NumericCode).ToListAsync();
        db.IsoCurrencies.AddRange(IsoCurrencyCatalog.Entries.Where(c => !existing.Contains(c.Numeric))
            .Select(c => new IsoCurrency { NumericCode = c.Numeric, AlphaCode = c.Alpha, Name = c.Name }));
        await db.SaveChangesAsync();
    }

    private static async Task SeedIdentityAsync(AppDbContext db, UserManager<AppUser> userManager, ILogger logger)
    {
        // ---- Page registry (parent groups are non-routed; children carry routes) ----
        var pages = new (string Code, string Name, string? Route, string? Parent, int Seq, string? Icon)[]
        {
            ("OVERVIEW",   "Overview",         null,           null,       1, null),
            ("DASHBOARD",  "Dashboard",        "/",            "OVERVIEW", 1, "dashboard"),
            ("RECON",      "Reconciliation",   null,           null,       2, null),
            ("ATMRECON",   "ATM recon",        "/recon/atm",   "RECON",    1, "atm"),
            ("POSRECON",   "POS recon",        "/recon/pos",   "RECON",    2, "pos"),
            ("BULKRECON",  "Bulk recon",       "/recon/bulk",  "RECON",    3, "bulk"),
            ("EXCEPTIONS", "Exceptions",       "/exceptions",  "RECON",    4, "alert"),
            ("DATA",       "Data",             null,           null,       3, null),
            ("IMPORT",     "Import files",     "/import",      "DATA",     1, "import"),
            ("BATCHES",    "Import batches",   "/batches",     "DATA",     2, "list"),
            ("ADMIN",      "Administration",   null,           null,       4, null),
            ("USERS",      "Users",            "/admin/users", "ADMIN",    1, "users"),
            ("ROLES",      "Roles & access",   "/admin/roles", "ADMIN",    2, "roles"),
        };

        foreach (var (code, name, route, parent, seq, icon) in pages)
        {
            var page = await db.AppPages.FirstOrDefaultAsync(p => p.PageCode == code);
            var parentId = parent is null ? (int?)null : (await db.AppPages.FirstAsync(p => p.PageCode == parent)).AppPageId;
            if (page is null)
            {
                page = new AppPage { PageCode = code, PageName = name, Route = route, ParentPageId = parentId, Sequence = seq, Icon = icon };
                db.AppPages.Add(page);
            }
            else
            {
                page.PageName = name; page.Route = route; page.ParentPageId = parentId; page.Sequence = seq; page.Icon = icon;
            }
            // File staging is performed from the reconciliation runner; these standalone pages are retired.
            page.IsMenuItem = code is not ("IMPORT" or "BATCHES");
            await db.SaveChangesAsync();
        }

        // ---- Default business roles ----
        string[] roleNames = ["Administrator", "Recon Manager", "Recon Officer", "Viewer"];
        foreach (var roleName in roleNames)
            if (!await db.AppAccesses.AnyAsync(a => a.AccessName == roleName))
                db.AppAccesses.Add(new AppAccess { AccessName = roleName });
        await db.SaveChangesAsync();

        // Baseline grants (View, Add, Edit, Delete) inserted only when the (role, page) pair is absent.
        var grants = new (string Role, string Page, bool V, bool A, bool E, bool D)[]
        {
            ("Recon Officer", "DASHBOARD",  true, false, false, false),
            ("Recon Officer", "ATMRECON",   true, true,  false, false),
            ("Recon Officer", "POSRECON",   true, true,  false, false),
            ("Recon Officer", "BULKRECON",  true, true,  false, false),
            ("Recon Officer", "EXCEPTIONS", true, false, true,  false),
            ("Recon Officer", "IMPORT",     true, true,  false, false),
            ("Recon Officer", "BATCHES",    true, false, false, false),

            ("Recon Manager", "DASHBOARD",  true, false, false, false),
            ("Recon Manager", "ATMRECON",   true, true,  true,  false),
            ("Recon Manager", "POSRECON",   true, true,  true,  false),
            ("Recon Manager", "BULKRECON",  true, true,  true,  false),
            ("Recon Manager", "EXCEPTIONS", true, true,  true,  true),
            ("Recon Manager", "IMPORT",     true, true,  false, false),
            ("Recon Manager", "BATCHES",    true, false, false, false),

            ("Viewer",        "DASHBOARD",  true, false, false, false),
            ("Viewer",        "EXCEPTIONS", true, false, false, false),
        };
        foreach (var (roleName, pageCode, v, a, e, d) in grants)
        {
            var role = await db.AppAccesses.SingleAsync(x => x.AccessName == roleName);
            var page = await db.AppPages.SingleAsync(x => x.PageCode == pageCode);
            if (!await db.AppAccessMenus.AnyAsync(x => x.AppAccessId == role.AppAccessId && x.PageId == page.AppPageId))
                db.AppAccessMenus.Add(new AppAccessMenu { AppAccessId = role.AppAccessId, PageId = page.AppPageId, CanView = v, CanAdd = a, CanEdit = e, CanDelete = d });
        }
        await db.SaveChangesAsync();

        // Administrator gets full CRUD on every routed page.
        var admin = await db.AppAccesses.FirstAsync(a => a.AccessName == "Administrator");
        var routed = await db.AppPages.Where(p => p.Route != null).ToListAsync();
        foreach (var page in routed)
            if (!await db.AppAccessMenus.AnyAsync(m => m.AppAccessId == admin.AppAccessId && m.PageId == page.AppPageId))
                db.AppAccessMenus.Add(new AppAccessMenu { AppAccessId = admin.AppAccessId, PageId = page.AppPageId, CanView = true, CanAdd = true, CanEdit = true, CanDelete = true });
        await db.SaveChangesAsync();

        // ---- Initial admin user ----
        const string adminUserName = "admin";
        var adminUser = await userManager.FindByNameAsync(adminUserName);
        if (adminUser is null)
        {
            adminUser = new AppUser
            {
                Id = CurrentUserService.SystemUserId,
                UserName = adminUserName,
                Email = "admin@local",
                FullName = "System Administrator",
                AuthProvider = AuthProvider.Local,
                IsActive = true,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(adminUser, "Admin@123");
            if (!result.Succeeded)
            {
                logger.LogError("Admin user creation failed: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }
            logger.LogWarning("Seeded initial admin user 'admin' with the default password — change it.");
        }

        if (!await db.UserAppAccesses.AnyAsync(x => x.UserId == adminUser.Id && x.AppAccessId == admin.AppAccessId))
        {
            db.UserAppAccesses.Add(new UserAppAccess { UserId = adminUser.Id, AppAccessId = admin.AppAccessId });
            await db.SaveChangesAsync();
        }
    }
}
