using Flowbite.Services;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Recon.Web.Components;
using Recon.Web.Data;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Platform.Adapters;
using Recon.Web.Services;
using Recon.Web.Services.Import;
using Recon.Web.Services.Recon;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFlowbite();
builder.Services.AddRadzenComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
// Platform adapters (generic multi-scheme). New schemes/CMS/CBS register here.
builder.Services.AddSingleton<IAdapterRegistry>(sp => new AdapterRegistry(
    [new BankOneDebitSettlementAdapter(), new BankOneInterchangeAdapter(), new BankOneNetworkT464Adapter(), new VisaDemoTransactionAdapter()],
    [new BankOneGlAdapter()]));
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IReconEngine, ReconEngine>();
builder.Services.AddScoped<IAtmNetworkValidationService, AtmNetworkValidationService>();
builder.Services.AddScoped<INetworkAtmReconService, NetworkAtmReconService>();
builder.Services.AddScoped<IExceptionWorkflowService, ExceptionWorkflowService>();
builder.Services.AddScoped<IReconVerificationService, ReconVerificationService>();
builder.Services.AddScoped<IBatchArchiveService, BatchArchiveService>();
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

app.MapPost("/auth/change-password", async (HttpContext http, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager) =>
{
    var userIdValue = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId)) return Results.Forbid();

    var form = await http.Request.ReadFormAsync();
    var currentPassword = form["currentPassword"].ToString();
    var newPassword = form["newPassword"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();
    if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword)) return Results.Redirect("/account/password?error=missing");
    if (newPassword != confirmPassword) return Results.Redirect("/account/password?error=mismatch");

    var user = await userManager.FindByIdAsync(userId.ToString());
    if (user is null || user.AuthProvider != AuthProvider.Local) return Results.Redirect("/account/password?error=provider");
    var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
    if (!result.Succeeded) return Results.Redirect("/account/password?error=invalid");

    await signInManager.RefreshSignInAsync(user);
    return Results.Redirect("/account/password?changed=1");
}).RequireAuthorization();

// Downloads the frozen run summary and its exception register as an analyst-ready workbook.
app.MapGet("/api/recon/{runId:int}/summary.xlsx", async (
    int runId, HttpContext http, IPermissionService permissions, AppDbContext db, CancellationToken ct) =>
{
    var userIdValue = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId)) return Results.Forbid();

    var run = await db.ReconRuns.AsNoTracking().SingleOrDefaultAsync(item => item.ReconRunId == runId, ct);
    var pageCode = run?.Function switch
    {
        ReconFunction.AtmToGl => "ATMRECON",
        ReconFunction.PosToGl => "POSRECON",
        ReconFunction.InterchangeToGl => "BULKRECON",
        ReconFunction.NetworkToAtm => "NETATMRECON",
        _ => null
    };
    if (pageCode is null || !await permissions.CanViewAsync(userId, pageCode)) return Results.Forbid();

    var summaries = await db.ReconPassSummaries.AsNoTracking()
        .Where(summary => summary.ReconRunId == runId && summary.Day == null)
        .OrderBy(summary => summary.SettlementLeg).ThenBy(summary => summary.Ccy)
        .ToListAsync(ct);
    var exceptions = await db.ReconExceptions.AsNoTracking()
        .Where(exception => exception.ReconRunId == runId)
        .OrderBy(exception => exception.Status).ThenBy(exception => exception.ExceptionType).ThenBy(exception => exception.ReferenceNumber)
        .ToListAsync(ct);

    using var workbook = new XLWorkbook();
    var summarySheet = workbook.AddWorksheet("Run summary");
    summarySheet.Cell(1, 1).Value = "Reconciliation run";
    summarySheet.Cell(1, 2).Value = run!.ReconRunId;
    summarySheet.Cell(2, 1).Value = "Function";
    summarySheet.Cell(2, 2).Value = run.Function switch
    {
        ReconFunction.AtmToGl => "ATM reconciliation",
        ReconFunction.PosToGl => "POS reconciliation",
        _ => "Bulk reconciliation"
    };
    summarySheet.Cell(3, 1).Value = "Period";
    summarySheet.Cell(3, 2).Value = run.PeriodKey;
    summarySheet.Cell(4, 1).Value = "Verification status";
    summarySheet.Cell(4, 2).Value = run.VerificationStatus.ToString();
    summarySheet.Cell(6, 1).InsertData(new[] { new[] { "Leg", "Currency", "Matched count", "Matched amount", "Exception count", "Exception amount", "Average confidence" } });
    var summaryRow = 7;
    foreach (var summary in summaries)
    {
        summarySheet.Cell(summaryRow, 1).Value = summary.SettlementLeg == ReconSettlementLeg.DebitCardholder ? "Debit cardholder" : "Credit intermediary";
        summarySheet.Cell(summaryRow, 2).Value = summary.Ccy == 0 ? "Other" : IsoCurrencyCatalog.Display(summary.Ccy);
        summarySheet.Cell(summaryRow, 3).Value = summary.MatchedCount;
        summarySheet.Cell(summaryRow, 4).Value = summary.MatchedAmount;
        summarySheet.Cell(summaryRow, 5).Value = summary.ExceptionCount;
        summarySheet.Cell(summaryRow, 6).Value = summary.ExceptionAmount;
        summarySheet.Cell(summaryRow, 7).Value = summary.AverageConfidence / 100m;
        summaryRow++;
    }
    summarySheet.Range(6, 1, 6, 7).Style.Font.Bold = true;
    summarySheet.Range(6, 1, Math.Max(6, summaryRow - 1), 7).CreateTable();
    summarySheet.Column(4).Style.NumberFormat.Format = "#,##0.00";
    summarySheet.Column(6).Style.NumberFormat.Format = "#,##0.00";
    summarySheet.Column(7).Style.NumberFormat.Format = "0.0%";
    summarySheet.Columns().AdjustToContents();

    var exceptionSheet = workbook.AddWorksheet("Exceptions");
    exceptionSheet.Cell(1, 1).InsertData(new[] { new[] { "Status", "Type", "Reference", "Processor account", "GL account", "Leg", "Currency", "Processor amount", "GL amount", "Difference", "Transaction date", "Confidence", "Narrative" } });
    var exceptionRow = 2;
    foreach (var exception in exceptions)
    {
        exceptionSheet.Cell(exceptionRow, 1).Value = exception.Status.ToString();
        exceptionSheet.Cell(exceptionRow, 2).Value = exception.ExceptionType.ToString();
        exceptionSheet.Cell(exceptionRow, 3).Value = exception.ReferenceNumber ?? "";
        exceptionSheet.Cell(exceptionRow, 4).Value = exception.ProcessorAccountNumber ?? "";
        exceptionSheet.Cell(exceptionRow, 5).Value = exception.GlAccountNumber ?? "";
        exceptionSheet.Cell(exceptionRow, 6).Value = exception.SettlementLeg?.ToString() ?? "";
        exceptionSheet.Cell(exceptionRow, 7).Value = exception.Ccy is { } currency ? IsoCurrencyCatalog.Display(currency) : "";
        exceptionSheet.Cell(exceptionRow, 8).Value = exception.ActualAmount;
        exceptionSheet.Cell(exceptionRow, 9).Value = exception.ExpectedAmount;
        exceptionSheet.Cell(exceptionRow, 10).Value = exception.Difference;
        exceptionSheet.Cell(exceptionRow, 11).Value = exception.TxnDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        exceptionSheet.Cell(exceptionRow, 12).Value = exception.Confidence / 100m;
        exceptionSheet.Cell(exceptionRow, 13).Value = exception.Narrative;
        exceptionRow++;
    }
    exceptionSheet.Range(1, 1, 1, 13).Style.Font.Bold = true;
    exceptionSheet.Range(1, 1, Math.Max(1, exceptionRow - 1), 13).CreateTable();
    exceptionSheet.Columns(8, 10).Style.NumberFormat.Format = "#,##0.00";
    exceptionSheet.Column(12).Style.NumberFormat.Format = "0.0%";
    exceptionSheet.Columns().AdjustToContents();
    exceptionSheet.Column(13).Width = 60;

    await using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return Results.File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"reconciliation-{runId}-summary.xlsx");
}).RequireAuthorization();

// Downloads every row selected by the Match results filters (not merely the current page).
app.MapGet("/api/recon/{runId:int}/matches.csv", async (
    int runId, string? result, string? reference, string? microfilm, string? glParticular2, string? processorAccount, string? glAccount,
    string? search, string? cardType, string? merchant, string? processorDescription,
    int? txnCcy, int? billingCcy, int? settlementCcy, int? reconCcy, bool? otherReconCcy, byte? leg, int? glCcy, decimal? maxDifference,
    DateOnly? processorDate, DateOnly? glDate, DateOnly? glPostingDate,
    string? sort, string? sortDescending,
    HttpContext http, IPermissionService permissions, AppDbContext db, CancellationToken ct) =>
{
    // Keep downloads resilient to a trailing '?' added by a browser/navigation layer. Minimal-API
    // nullable bool binding rejects values such as "true?" before this handler can respond.
    var descending = bool.TryParse(sortDescending?.TrimEnd('?'), out var parsedDescending) && parsedDescending;

    var userIdValue = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId)) return Results.Forbid();

    var function = await db.ReconRuns.AsNoTracking()
        .Where(run => run.ReconRunId == runId)
        .Select(run => (ReconFunction?)run.Function)
        .SingleOrDefaultAsync(ct);
    var pageCode = function switch
    {
        ReconFunction.AtmToGl => "ATMRECON",
        ReconFunction.PosToGl => "POSRECON",
        ReconFunction.InterchangeToGl => "BULKRECON",
        ReconFunction.NetworkToAtm => "NETATMRECON",
        _ => null
    };
    if (pageCode is null || !await permissions.CanViewAsync(userId, pageCode)) return Results.Forbid();

    IQueryable<ReconMatch> query = db.ReconMatches.AsNoTracking().Where(m => m.ReconRunId == runId);
    query = (result ?? "").ToLowerInvariant() switch
    {
        "matched" => query.Where(m => m.MatchStatus == MatchStatus.Matched),
        "exception" => query.Where(m => m.MatchStatus != MatchStatus.Matched),
        "amount_mismatch" => query.Where(m => m.MatchStatus == MatchStatus.AmountMismatch),
        "no_gl_match" => query.Where(m => m.MatchStatus == MatchStatus.UnmatchedProcessor),
        "gl_unmatched" => query.Where(m => m.MatchStatus == MatchStatus.UnmatchedGl),
        "ambiguous" => query.Where(m => m.MatchStatus == MatchStatus.Ambiguous),
        "duplicate" => query.Where(m => m.MatchStatus == MatchStatus.Duplicate),
        _ => query
    };
    if (!string.IsNullOrWhiteSpace(search))
        query = query.Where(m => (m.ReferenceNumber != null && m.ReferenceNumber.Contains(search)) || (m.ProcessorMerchant != null && m.ProcessorMerchant.Contains(search)));
    if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(m => m.ReferenceNumber != null && m.ReferenceNumber.Contains(reference));
    if (!string.IsNullOrWhiteSpace(microfilm)) query = query.Where(m => m.MicrofilmRefNumber != null && m.MicrofilmRefNumber.Contains(microfilm));
    if (!string.IsNullOrWhiteSpace(glParticular2)) query = query.Where(m => m.GlParticular2 != null && m.GlParticular2.Contains(glParticular2));
    if (!string.IsNullOrWhiteSpace(processorAccount)) query = query.Where(m => m.ProcessorAccountNumber != null && m.ProcessorAccountNumber.Contains(processorAccount));
    if (string.Equals(cardType, "blank", StringComparison.OrdinalIgnoreCase)) query = query.Where(m => m.ProcessorProductType == null || m.ProcessorProductType == "");
    else if (!string.IsNullOrWhiteSpace(cardType)) query = query.Where(m => m.ProcessorProductType != null && m.ProcessorProductType == cardType);
    if (!string.IsNullOrWhiteSpace(merchant)) query = query.Where(m => m.ProcessorMerchant != null && m.ProcessorMerchant.Contains(merchant));
    if (string.Equals(processorDescription, "blank", StringComparison.OrdinalIgnoreCase)) query = query.Where(m => m.ProcessorDescription == null || m.ProcessorDescription == "");
    else if (!string.IsNullOrWhiteSpace(processorDescription)) query = query.Where(m => m.ProcessorDescription == processorDescription);
    if (!string.IsNullOrWhiteSpace(glAccount)) query = query.Where(m => m.GlAccountNumber != null && m.GlAccountNumber.Contains(glAccount));
    if (txnCcy.HasValue) query = query.Where(m => m.ProcessorCcy == txnCcy);
    if (billingCcy.HasValue) query = query.Where(m => m.BillingCcy == billingCcy);
    if (settlementCcy.HasValue) query = query.Where(m => m.SettlementCcy == settlementCcy);
    if (otherReconCcy == true) query = query.Where(m => m.Ccy == null || (m.Ccy != 480 && m.Ccy != 840 && m.Ccy != 978));
    else if (reconCcy.HasValue) query = query.Where(m => m.Ccy == reconCcy);
    if (leg.HasValue) query = query.Where(m => (byte)m.SettlementLeg == leg.Value);
    if (glCcy.HasValue) query = query.Where(m => (m.GlCcy ?? 480) == glCcy);
    if (processorDate.HasValue)
    {
        var dayStart = processorDate.Value.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        query = query.Where(m => (m.ProcessorTransactionDateTime != null && m.ProcessorTransactionDateTime >= dayStart && m.ProcessorTransactionDateTime < dayEnd) || m.ProcessorDate == processorDate);
    }
    if (glDate.HasValue) query = query.Where(m => m.GlDate == glDate);
    if (glPostingDate.HasValue)
    {
        var dayStart = glPostingDate.Value.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        query = query.Where(m => m.GlPostingDateTime >= dayStart && m.GlPostingDateTime < dayEnd);
    }
    if (maxDifference.HasValue) query = query.Where(m => m.ReconciliationAmount != null && m.GlAmount != null && Math.Abs(m.ReconciliationAmount.Value - m.GlAmount.Value) <= maxDifference);

    query = (sort ?? "date").ToLowerInvariant() switch
    {
        "date" when descending => query.OrderByDescending(m => m.ProcessorTransactionDateTime).ThenByDescending(m => m.ProcessorDate).ThenByDescending(m => m.ReconMatchId),
        "date" => query.OrderBy(m => m.ProcessorTransactionDateTime).ThenBy(m => m.ProcessorDate).ThenBy(m => m.ReconMatchId),
        "gl_date" when descending => query.OrderByDescending(m => m.GlDate).ThenByDescending(m => m.GlPostingDateTime).ThenByDescending(m => m.ReconMatchId),
        "gl_date" => query.OrderBy(m => m.GlDate).ThenBy(m => m.GlPostingDateTime).ThenBy(m => m.ReconMatchId),
        "gl_posting" when descending => query.OrderByDescending(m => m.GlPostingDateTime).ThenByDescending(m => m.GlDate).ThenByDescending(m => m.ReconMatchId),
        "gl_posting" => query.OrderBy(m => m.GlPostingDateTime).ThenBy(m => m.GlDate).ThenBy(m => m.ReconMatchId),
        "confidence" when descending => query.OrderByDescending(m => m.Confidence).ThenByDescending(m => m.ProcessorTransactionDateTime).ThenByDescending(m => m.ReconMatchId),
        "confidence" => query.OrderBy(m => m.Confidence).ThenBy(m => m.ProcessorTransactionDateTime).ThenBy(m => m.ReconMatchId),
        _ => query.OrderByDescending(m => m.ReconMatchId)
    };

    var matches = await query.ToListAsync(ct);
    var csv = new StringBuilder("\uFEFFResult,Reference,Transaction Date Time,Processor Account,Card Type,Merchant,Processor Description,GL Account,GL Reference,GL Description,Transaction Amount,Transaction Currency,Billing Amount,Billing Currency,Settlement Amount,Settlement Currency,Reconciliation Amount,Reconciliation Currency,GL Transaction Date,GL Posting Date Time,GL Amount,GL Currency,Difference,Match Comment,Confidence,Score Breakdown\r\n");
    foreach (var m in matches)
    {
        var fields = new (string? Value, bool ProtectForExcel)[]
        {
            (ResultName(m.MatchStatus), true), (m.ReferenceNumber, true), (DateTimeValue(m.ProcessorTransactionDateTime, m.ProcessorDate), false), (m.ProcessorAccountNumber, true), (m.ProcessorProductType, true), (m.ProcessorMerchant, true), (m.ProcessorDescription, true), (m.GlAccountNumber, true), (m.GlReference, true), (m.GlDescription, true),
            (Amount(m.ProcessorAmount), false), (Currency(m.ProcessorCcy), true), (Amount(m.BillingAmount), false), (Currency(m.BillingCcy), true),
            (Amount(m.SettlementAmount), false), (Currency(m.SettlementCcy), true), (Amount(m.ReconciliationAmount), false), (Currency(m.Ccy), true),
            (DateOnlyValue(m.GlDate), false), (DateTimeValue(m.GlPostingDateTime, null), false), (Amount(m.GlAmount), false), (Currency(m.GlCcy ?? 480), true), (Amount(m.ReconciliationAmount is { } p && m.GlAmount is { } g ? Math.Abs(p - g) : null), false), (m.MatchComment, true), (m.Confidence.ToString("0.0", CultureInfo.InvariantCulture), false), (m.ScoreBreakdown, true)
        };
        csv.AppendLine(string.Join(',', fields.Select(field => Csv(field.Value, field.ProtectForExcel))));
    }
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"reconciliation-{runId}-{result ?? "all"}.csv");

    static string Amount(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
    static string DateTimeValue(DateTime? timestamp, DateOnly? fallbackDate) => timestamp.HasValue
        ? timestamp.Value.ToString(timestamp.Value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : fallbackDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    static string DateOnlyValue(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    static string Currency(int? code) => code is null ? "" : IsoCurrencyCatalog.Display(code);
    static string Csv(string? value, bool protectForExcel)
    {
        value ??= "";
        if (protectForExcel && value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
    static string ResultName(MatchStatus status) => status switch
    {
        MatchStatus.Matched => "Matched",
        MatchStatus.AmountMismatch => "Amount mismatch",
        MatchStatus.UnmatchedProcessor => "No GL match",
        MatchStatus.UnmatchedGl => "GL unmatched",
        MatchStatus.Ambiguous => "Ambiguous",
        MatchStatus.Duplicate => "Duplicate",
        _ => status.ToString()
    };
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
