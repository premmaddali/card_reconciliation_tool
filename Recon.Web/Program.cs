using Flowbite.Services;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Components;
using Recon.Web.Data;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Services;
using Recon.Web.Services.Import;
using Recon.Web.Services.Recon;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFlowbite();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IReconEngine, ReconEngine>();
builder.Services.AddScoped<IExceptionWorkflowService, ExceptionWorkflowService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ILdapAuthService, LdapAuthService>();
builder.Services.Configure<MatchWeights>(builder.Configuration.GetSection(MatchWeights.SectionName));
builder.Services.Configure<ActiveDirectoryOptions>(
    builder.Configuration.GetSection(ActiveDirectoryOptions.SectionName));

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Scoped DbContext for Identity + a factory for Blazor components/services.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});
builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
}, lifetime: ServiceLifetime.Scoped);

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    }));

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ---- Auth endpoints (SSR form posts) --------------------------------------
app.MapPost("/auth/login", async (
    HttpContext http,
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    ILdapAuthService ldap) =>
{
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var provider = form["provider"].ToString(); // "local" | "ad"

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return Results.Redirect("/login?error=missing");

    if (provider == "ad")
    {
        if (!ldap.Validate(username, password))
            return Results.Redirect("/login?error=invalid");

        var adUser = await userManager.Users
            .FirstOrDefaultAsync(u => u.AdAccountName == username || u.UserName == username);
        if (adUser is null || !adUser.IsActive)
            return Results.Redirect("/login?error=notprovisioned");

        await signInManager.SignInAsync(adUser, isPersistent: false);
        return Results.Redirect("/");
    }

    var user = await userManager.FindByNameAsync(username);
    if (user is null || !user.IsActive || user.AuthProvider != AuthProvider.Local)
        return Results.Redirect("/login?error=invalid");

    var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
    if (!result.Succeeded)
        return Results.Redirect(result.IsLockedOut ? "/login?error=locked" : "/login?error=invalid");

    await signInManager.SignInAsync(user, isPersistent: false);
    return Results.Redirect("/");
}).DisableAntiforgery(); // login form is SSR-rendered before a circuit exists

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(IdentityConstants.ApplicationScheme);
    return Results.Redirect("/login");
});

// Downloads every row selected by the Match results filters (not merely the current page).
app.MapGet("/api/recon/{runId:int}/matches.csv", async (
    int runId, string? result, string? reference, string? processorAccount, string? glAccount,
    int? txnCcy, int? billingCcy, int? settlementCcy, int? reconCcy, int? glCcy, decimal? maxDifference,
    AppDbContext db, CancellationToken ct) =>
{
    IQueryable<ReconMatch> query = db.ReconMatches.AsNoTracking().Where(m => m.ReconRunId == runId);
    if (string.Equals(result, "matched", StringComparison.OrdinalIgnoreCase))
        query = query.Where(m => m.MatchStatus == MatchStatus.Matched);
    else if (string.Equals(result, "exception", StringComparison.OrdinalIgnoreCase))
        query = query.Where(m => m.MatchStatus != MatchStatus.Matched);
    if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(m => m.ReferenceNumber != null && m.ReferenceNumber.Contains(reference));
    if (!string.IsNullOrWhiteSpace(processorAccount)) query = query.Where(m => m.ProcessorAccountNumber != null && m.ProcessorAccountNumber.Contains(processorAccount));
    if (!string.IsNullOrWhiteSpace(glAccount)) query = query.Where(m => m.GlAccountNumber != null && m.GlAccountNumber.Contains(glAccount));
    if (txnCcy.HasValue) query = query.Where(m => m.ProcessorCcy == txnCcy);
    if (billingCcy.HasValue) query = query.Where(m => m.BillingCcy == billingCcy);
    if (settlementCcy.HasValue) query = query.Where(m => m.SettlementCcy == settlementCcy);
    if (reconCcy.HasValue) query = query.Where(m => m.Ccy == reconCcy);
    if (glCcy.HasValue) query = query.Where(m => (m.GlCcy ?? 480) == glCcy);
    if (maxDifference.HasValue) query = query.Where(m => m.ReconciliationAmount != null && m.GlAmount != null && Math.Abs(m.ReconciliationAmount.Value - m.GlAmount.Value) <= maxDifference);

    var matches = await query.OrderByDescending(m => m.MatchStatus == MatchStatus.AmountMismatch)
        .ThenBy(m => m.ReconMatchId).ToListAsync(ct);
    var csv = new StringBuilder("\uFEFFResult,Reference,Processor Account,GL Account,Transaction Amount,Transaction Currency,Billing Amount,Billing Currency,Settlement Amount,Settlement Currency,Reconciliation Amount,Reconciliation Currency,GL Amount,GL Currency,Difference,Match Comment,Confidence,Score Breakdown\r\n");
    foreach (var m in matches)
    {
        var fields = new[]
        {
            m.MatchStatus.ToString(), m.ReferenceNumber, m.ProcessorAccountNumber, m.GlAccountNumber,
            Amount(m.ProcessorAmount), Currency(m.ProcessorCcy), Amount(m.BillingAmount), Currency(m.BillingCcy),
            Amount(m.SettlementAmount), Currency(m.SettlementCcy), Amount(m.ReconciliationAmount), Currency(m.Ccy),
            Amount(m.GlAmount), Currency(m.GlCcy ?? 480), Amount(m.ReconciliationAmount is { } p && m.GlAmount is { } g ? Math.Abs(p - g) : null), m.MatchComment, m.Confidence.ToString("0.0", CultureInfo.InvariantCulture), m.ScoreBreakdown
        };
        csv.AppendLine(string.Join(',', fields.Select(Csv)));
    }
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"reconciliation-{runId}-{result ?? "all"}.csv");

    static string Amount(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
    static string Currency(int? code) => code is null ? "" : IsoCurrencyCatalog.Display(code);
    static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Apply any pending migrations on startup and seed reference + identity data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
await DbSeeder.SeedAsync(app.Services);

app.Run();
