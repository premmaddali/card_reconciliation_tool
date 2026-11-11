using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Services.Recon;

public record ReconAnalysisResult(int RunId, int OriginalRunId, int Matched, int Exceptions);

/// <summary>Replays stored imports into separate, non-operational results using the current rules.</summary>
public class ReconAnalysisService(IDbContextFactory<AppDbContext> factory, IReconEngine engine,
    SecondaryMatchService secondary, AuthenticationStateProvider authentication, IPermissionService permissions)
{
    public async Task<ReconAnalysisResult> RunAsync(int sourceRunId, IProgress<ReconProgress>? progress = null, CancellationToken ct = default)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            throw new UnauthorizedAccessException("Sign in to re-run analysis.");
        var grants = await permissions.GetGrantsAsync(userId);
        if (!grants.TryGetValue("BULKRECON", out var grant) || !grant.CanView || !(grant.CanAdd || grant.CanEdit))
            throw new UnauthorizedAccessException("A Bulk reconciliation view and add/edit grant is required to re-run analysis.");
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(600);
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct))
            throw new UnauthorizedAccessException("An active user account is required.");
        var source = await db.ReconRuns.AsNoTracking().SingleOrDefaultAsync(r => r.ReconRunId == sourceRunId, ct)
            ?? throw new InvalidOperationException("The original batch was not found.");
        if (source.AnalysisOfRunId is int root)
            source = await db.ReconRuns.AsNoTracking().SingleAsync(r => r.ReconRunId == root, ct);
        if (source.Function != ReconFunction.InterchangeToGl || source.Status != ReconRunStatus.Completed)
            throw new InvalidOperationException("Re-run analysis requires a completed Bulk or Daily reconciliation batch.");
        var inputs = await ReconAnalysisInputs.LoadAsync(db, source, ct);
        var batchIds = inputs.ProcessorBatchIds.Concat(inputs.GlBatchIds).Distinct().ToArray();
        if (await db.ImportBatches.CountAsync(b => batchIds.Contains(b.ImportBatchId) && b.Status == ImportStatus.Parsed
                && b.SchemeProfileId == source.SchemeProfileId, ct) != batchIds.Length)
            throw new InvalidOperationException("The original parsed source batches are unavailable or belong to another scheme.");
        foreach (var id in inputs.ProcessorBatchIds)
            if (!await db.StgInterchangeFees.AnyAsync(t => t.ImportBatchId == id, ct))
                throw new InvalidOperationException($"Stored TRX rows for import #{id} are unavailable. No files will be re-uploaded or reconstructed.");
        foreach (var id in inputs.GlBatchIds)
            if (!await db.StgGlEntries.AnyAsync(g => g.ImportBatchId == id, ct))
                throw new InvalidOperationException($"Stored GL rows for import #{id} are unavailable.");

        progress?.Report(new("Initial matching phases using stored TRX and GL…", 0, 0, 0));
        var primary = await engine.RunAsync(source.Function, inputs.ProcessorBatchIds[0], inputs.GlBatchIds[0], source.PeriodKey,
            source.PeriodStart, source.PeriodEnd, ct, progress, schemeProfileId: source.SchemeProfileId,
            applyAmountTolerance: source.ApplyAmountTolerance, amountTolerance: source.AmountTolerance ?? 0m,
            // Historical source associations are retained; this is not a new upload or live run.
            enforcePostingPeriod: false,
            analysis: inputs);
        var runId = primary.ReconRunId;
        try
        {
            await db.ReconRuns.Where(r => r.ReconRunId == runId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReconRunStatus.Running).SetProperty(r => r.CompletedDate, (DateTime?)null), ct);
            await RecordAsync(ReconPassKind.InitialAnalysis, primary.MatchedCount,
                $"Initial phases re-run from stored source data. Original batch #{source.ReconRunId}: {source.MatchedCount} matched, {source.ExceptionCount} exceptions.");

            var accounts = await db.GlSettlementAccounts.Where(a => a.IsActive &&
                    (a.Role == GlAccountRole.CardholderSettlement || a.Role == GlAccountRole.IntermediarySettlement))
                .Select(a => a.AcctNum).ToListAsync(ct);
            var eligibleGl = db.StgGlEntries.Where(g => inputs.GlBatchIds.Contains(g.ImportBatchId) && g.AcctNum != null && accounts.Contains(g.AcctNum));
            var hasValueDates = await eligibleGl.AnyAsync(g => g.ValueDate != null, ct);
            await PhaseAsync(ReconPassKind.SecondaryAmount, "Date/Amount/Currency", hasValueDates ? null : "No GL VALUE_DATE is available.",
                () => secondary.RunAsync(runId, ct));

            var trxCards = await db.StgInterchangeFees.Where(t => inputs.ProcessorBatchIds.Contains(t.ImportBatchId))
                .Select(t => t.CardAccountEntityId).ToListAsync(ct);
            var glCards = await eligibleGl.Select(g => g.TranRmks).ToListAsync(ct);
            var cardReason = !hasValueDates ? "No GL VALUE_DATE is available."
                : !trxCards.Any(c => CardIdentity.Normalize(c) is not null) ? "No complete CARD_ACCOUNT_ENTITY_ID is available in the stored TRX rows."
                : !glCards.Any(c => CardIdentity.Normalize(c) is not null) ? "No complete card number is available in stored GL TRAN_RMKS." : null;
            await PhaseAsync(ReconPassKind.CardNumber, "Card Number", cardReason, () => secondary.RunCardAsync(runId, ct));

            var month = DateOnly.ParseExact(source.PeriodKey + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var adjacent = new[] { month.AddMonths(-1).ToString("yyyy-MM"), month.AddMonths(1).ToString("yyyy-MM") };
            var adjacentIds = await db.ImportBatches.Where(b => b.SourceType == SourceType.GlDump && b.Status == ImportStatus.Parsed
                    && b.SchemeProfileId == source.SchemeProfileId && adjacent.Contains(b.PeriodKey))
                .Select(b => b.ImportBatchId).ToListAsync(ct);
            var adjacentReason = source.PeriodStart is { } from && source.PeriodEnd is { } to && from.ToString("yyyy-MM") != to.ToString("yyyy-MM")
                ? "Previous/Next Period matching requires a single-month range."
                : !await db.StgGlEntries.AnyAsync(g => adjacentIds.Contains(g.ImportBatchId) && g.AcctNum != null && accounts.Contains(g.AcctNum) && g.ValueDate != null, ct)
                    ? $"No stored GL value-date rows are available for {string.Join(" or ", adjacent)}." : null;
            await PhaseAsync(ReconPassKind.CrossPeriod, "Previous/Next Period", adjacentReason, () => secondary.RunAdjacentAsync(runId, ct));

            await db.ReconRuns.Where(r => r.ReconRunId == runId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReconRunStatus.Completed).SetProperty(r => r.CompletedDate, DateTime.UtcNow), ct);
            var result = await db.ReconRuns.AsNoTracking().SingleAsync(r => r.ReconRunId == runId, ct);
            return new(runId, source.ReconRunId, result.MatchedCount, result.ExceptionCount);
        }
        catch
        {
            await db.ReconRuns.Where(r => r.ReconRunId == runId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReconRunStatus.Failed).SetProperty(r => r.CompletedDate, DateTime.UtcNow), CancellationToken.None);
            throw;
        }

        async Task PhaseAsync(ReconPassKind kind, string name, string? skipReason, Func<Task<SecondaryMatchResult>> execute)
        {
            progress?.Report(new($"Re-running {name} matching…", 0, 0, 0));
            if (skipReason is not null) await RecordAsync(kind, 0, "Skipped: " + skipReason);
            else await execute();
        }

        async Task RecordAsync(ReconPassKind kind, int added, string detail)
        {
            db.ReconRunPasses.Add(new ReconRunPass
            {
                ReconRunId = runId, Kind = kind, RunGroup = Guid.NewGuid(), Added = added,
                TargetRunId = source.ReconRunId, Detail = detail, RunAt = DateTime.UtcNow,
                CreatedBy = userId, CreatedDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
