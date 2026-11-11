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
        if (!await db.ReconParameters.AnyAsync())
        {
            db.ReconParameters.Add(new ReconParameters { AmountTolerance = 0m });
            await db.SaveChangesAsync();
        }
        await SeedSchemeProfilesAsync(db);
        await SeedIdentityAsync(db, userManager, logger);
        await SeedSchemeMenusAsync(db);
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider);
    }

    /// <summary>
    /// Builds the sidebar menu from the scheme profiles: one group per scheme with its enabled
    /// functions beneath it (POS / ATM / Bulk / Network↔ATM recon). Idempotent.
    /// </summary>
    private static async Task SeedSchemeMenusAsync(AppDbContext db)
    {
        var profiles = await db.SchemeProfiles.OrderBy(p => p.SchemeProfileId).ToListAsync();
        var parentSeq = 3; // between RECON (2) and ADMIN (6)

        foreach (var p in profiles)
        {
            // Group header page (non-routed).
            var group = await db.AppPages.FirstOrDefaultAsync(x => x.PageCode == $"SCHEME_{p.Scheme.ToUpperInvariant()}");
            if (group is null)
            {
                group = new AppPage { PageCode = $"SCHEME_{p.Scheme.ToUpperInvariant()}", PageName = p.Scheme, Route = null };
                db.AppPages.Add(group);
                await db.SaveChangesAsync();
            }
            group.PageName = p.Scheme;
            group.Sequence = parentSeq;
            group.Icon = "bulk";
            group.IsActive = true;
            group.IsMenuItem = false; // headers are non-routed; children carry routes

            var functions = new (string Fn, string Label, string Icon, string? LegacyRoute)[]
            {
                ("PosToGl",         "POS recon",             "pos",   "/recon/pos"),
                ("AtmToGl",         "ATM recon",             "atm",   "/recon/atm"),
                ("InterchangeToGl", "Bulk recon",            "bulk",  "/recon/bulk"),
                ("NetworkToAtm",    "Network ↔ ATM recon",   "atm",   "/recon/network-atm"),
            };

            var childSeq = 1;
            foreach (var (fn, label, icon, legacyRoute) in functions)
            {
                var code = $"SCHEME_{p.Scheme.ToUpperInvariant()}_{fn.ToUpperInvariant()}";
                // Mastercard's items point at the original battle-tested screens; other schemes
                // use the generic per-scheme function page.
                var route = p.Scheme == "Mastercard" && legacyRoute is not null ? legacyRoute : $"/recon/scheme/{p.SchemeProfileId}/{fn}";
                var page = await db.AppPages.FirstOrDefaultAsync(x => x.PageCode == code);
                if (page is null)
                {
                    page = new AppPage { PageCode = code, PageName = label, Route = route, ParentPageId = group.AppPageId };
                    db.AppPages.Add(page);
                    await db.SaveChangesAsync();
                }
                page.PageName = label;
                page.Route = route;
                page.ParentPageId = group.AppPageId;
                page.Sequence = childSeq++;
                page.Icon = icon;
                page.IsActive = p.IsActive && p.Functions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(fn, StringComparer.OrdinalIgnoreCase);
                page.IsMenuItem = true;

                // Administrator always gets view access to new scheme pages.
                var admin = await db.AppAccesses.FirstAsync(a => a.AccessName == "Administrator");
                if (!await db.AppAccessMenus.AnyAsync(m => m.AppAccessId == admin.AppAccessId && m.PageId == page.AppPageId))
                    db.AppAccessMenus.Add(new AppAccessMenu
                    {
                        AppAccessId = admin.AppAccessId, PageId = page.AppPageId,
                        CanView = true, CanAdd = true, CanEdit = true, CanDelete = true
                    });
            }
            parentSeq++;
        }
        await db.SaveChangesAsync();
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

    private static async Task SeedSchemeProfilesAsync(AppDbContext db)
    {
        // Placeholder profiles for every supported scheme. They are active so their menu
        // groups and dummy recon pages are visible; each shows a "coming soon" placeholder
        // until real adapters are registered for that scheme.
        var seed = new (string Scheme, string Cms, string TxAdapter, bool Active)[]
        {
            ("Mastercard", "Core banking", "bankone-interchange", true),
            ("Visa",       "Placeholder", "placeholder",         true),
            ("UnionPay",   "Placeholder", "placeholder",         true),
            ("RuPay",      "Placeholder", "placeholder",         true),
            ("Domestic",   "Placeholder", "placeholder",         true),
        };

        foreach (var (scheme, cms, txAdapter, active) in seed)
        {
            var p = await db.SchemeProfiles.FirstOrDefaultAsync(x => x.Scheme == scheme);
            if (p is null)
            {
                db.SchemeProfiles.Add(new Platform.Model.SchemeProfile
                {
                    Name = $"{scheme} — {cms}",
                    Scheme = scheme,
                    Cms = cms,
                    TransactionAdapterCode = txAdapter,
                    GlcAdapterCode = scheme == "Mastercard" ? "bankone-gl-dump" : "placeholder",
                    // Mastercard ATM validation has no GL side.
                    Functions = scheme == "Mastercard"
                        ? "PosToGl;InterchangeToGl;NetworkToAtm"
                        : "AtmToGl;PosToGl;InterchangeToGl",
                    IsActive = active,
                    CreatedBy = CurrentUserService.SystemUserId,
                    CreatedDate = DateTime.UtcNow
                });
            }
            else
            {
                // The seed represents a generic default deployment, not a particular bank.
                p.Cms = cms;
                if (scheme == "Mastercard")
                    p.Functions = "PosToGl;InterchangeToGl;NetworkToAtm";
                else if (string.IsNullOrEmpty(p.Functions) || p.Functions == "InterchangeToGl")
                    p.Functions = "AtmToGl;PosToGl;InterchangeToGl";
                p.IsActive = active;
            }
        }
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
            ("SCHEMES",    "Schemes",          "/recon",       "RECON",    0, "bulk"),
            ("ALLJOBS",    "All jobs",         "/recon/jobs",  "RECON",    8, "list"),
            ("ATMRECON",   "ATM recon",        "/recon/atm",   "RECON",    1, "atm"),
            ("POSRECON",   "POS recon",        "/recon/pos",   "RECON",    2, "pos"),
            ("BULKRECON",  "Bulk recon",       "/recon/bulk",  "RECON",    3, "bulk"),
            ("DAILYRECON", "Daily / period recon", "/recon/daily", "RECON", 4, "bulk"),
            ("NETATMRECON", "Network ↔ ATM recon", "/recon/network-atm", "RECON", 5, "atm"),
            ("EXCEPTIONS", "Exceptions",       "/exceptions",  "RECON",    6, "alert"),
            ("ANALYSIS",   "Advanced analysis", "/analysis",    "RECON",    7, "analysis"),
            ("DATA",       "Data",             null,           null,       7, null),
            ("IMPORT",     "Import files",     "/import",      "DATA",     1, "import"),
            ("BATCHES",    "Import batches",   "/batches",     "DATA",     2, "list"),
            ("ADMIN",      "Administration",   null,           null,       99, null),
            ("USERS",      "Users",            "/admin/users", "ADMIN",    1, "users"),
            ("ROLES",      "Roles & access",   "/admin/roles", "ADMIN",    2, "roles"),
            ("WORKFLOWS",  "Verification workflows", "/admin/workflows", "ADMIN", 3, "workflow"),
            ("PARAMETERS", "Run parameters",   "/admin/parameters", "ADMIN", 4, "workflow"),
            ("AUDITREPORTS", "Audit reports",   "/admin/audit-reports", "ADMIN", 5, "list"),
            ("ARCHIVE_BATCH", "Archive batch",  null,           "ADMIN", 6, null),
            ("FREEZE_PERIOD", "Freeze period",  null,           "ADMIN", 7, null),
            ("MANUAL_GL_MATCH", "Manual GL match", null,         "ADMIN", 8, null),
            ("DATA_CLEANUP", "Data cleanup", "/admin/data-cleanup", "ADMIN", 9, "list"),
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
            // The four recon functions moved under the Mastercard scheme group.
            page.IsMenuItem = code is not ("IMPORT" or "BATCHES" or "ATMRECON" or "POSRECON" or "BULKRECON" or "NETATMRECON");
            await db.SaveChangesAsync();
        }

        // ---- Default business roles ----
        string[] roleNames = ["Administrator", "Recon Manager", "Recon Officer", "Viewer"];
        foreach (var roleName in roleNames)
            if (!await db.AppAccesses.AnyAsync(a => a.AccessName == roleName))
                db.AppAccesses.Add(new AppAccess { AccessName = roleName });
        await db.SaveChangesAsync();

        // Default configurable maker/checker workflow. Administrators can change steps and roles
        // from the Verification workflows screen without changing reconciliation code.
        if (!await db.ReconWorkflowDefinitions.AnyAsync())
        {
            var maker = await db.AppAccesses.SingleAsync(a => a.AccessName == "Recon Officer");
            var checker = await db.AppAccesses.SingleAsync(a => a.AccessName == "Recon Manager");
            var workflow = new ReconWorkflowDefinition { Name = "Standard maker / checker", IsDefault = true };
            var submit = new ReconWorkflowStep { StepOrder = 1, Label = "Maker submission" };
            submit.Roles.Add(new ReconWorkflowStepRole { AppAccessId = maker.AppAccessId });
            var verify = new ReconWorkflowStep { StepOrder = 2, Label = "Checker verification" };
            verify.Roles.Add(new ReconWorkflowStepRole { AppAccessId = checker.AppAccessId });
            workflow.Steps.Add(submit);
            workflow.Steps.Add(verify);
            db.ReconWorkflowDefinitions.Add(workflow);
            await db.SaveChangesAsync();
        }

        // Baseline grants (View, Add, Edit, Delete) inserted only when the (role, page) pair is absent.
        var grants = new (string Role, string Page, bool V, bool A, bool E, bool D)[]
        {
            ("Recon Officer", "DASHBOARD",  true, false, false, false),
            ("Recon Officer", "SCHEMES",    true, false, false, false),
            ("Recon Officer", "ALLJOBS",    true, false, false, false),
            ("Recon Officer", "ATMRECON",   true, true,  false, false),
            ("Recon Officer", "POSRECON",   true, true,  false, false),
            ("Recon Officer", "BULKRECON",  true, true,  false, false),
            ("Recon Officer", "DAILYRECON", true, true,  false, false),
            ("Recon Officer", "NETATMRECON", true, true,  false, false),
            ("Recon Officer", "EXCEPTIONS", true, false, true,  false),
            ("Recon Officer", "ANALYSIS",   true, false, false, false),
            ("Recon Officer", "IMPORT",     true, true,  false, false),
            ("Recon Officer", "BATCHES",    true, false, false, false),

            ("Recon Manager", "DASHBOARD",  true, false, false, false),
            ("Recon Manager", "SCHEMES",    true, false, false, false),
            ("Recon Manager", "ALLJOBS",    true, false, false, false),
            ("Recon Manager", "ATMRECON",   true, true,  true,  false),
            ("Recon Manager", "POSRECON",   true, true,  true,  false),
            ("Recon Manager", "BULKRECON",  true, true,  true,  false),
            ("Recon Manager", "DAILYRECON", true, true,  true,  false),
            ("Recon Manager", "NETATMRECON", true, true,  true,  false),
            ("Recon Manager", "EXCEPTIONS", true, true,  true,  true),
            ("Recon Manager", "ANALYSIS",   true, false, false, false),
            ("Recon Manager", "IMPORT",     true, true,  false, false),
            ("Recon Manager", "BATCHES",    true, false, false, false),
            ("Recon Manager", "ARCHIVE_BATCH", false, false, true, false),
            ("Recon Manager", "FREEZE_PERIOD", false, false, true, false),

            ("Viewer",        "DASHBOARD",  true, false, false, false),
            ("Viewer",        "SCHEMES",    true, false, false, false),
            ("Viewer",        "NETATMRECON", true, false, false, false),
            ("Viewer",        "EXCEPTIONS", true, false, false, false),
            ("Viewer",        "ANALYSIS",   true, false, false, false),
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
        foreach (var pageCode in new[] { "ARCHIVE_BATCH", "FREEZE_PERIOD", "MANUAL_GL_MATCH" })
        {
            var page = await db.AppPages.SingleAsync(x => x.PageCode == pageCode);
            if (!await db.AppAccessMenus.AnyAsync(m => m.AppAccessId == admin.AppAccessId && m.PageId == page.AppPageId))
                db.AppAccessMenus.Add(new AppAccessMenu { AppAccessId = admin.AppAccessId, PageId = page.AppPageId, CanView = false, CanAdd = false, CanEdit = true, CanDelete = false });
        }
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
