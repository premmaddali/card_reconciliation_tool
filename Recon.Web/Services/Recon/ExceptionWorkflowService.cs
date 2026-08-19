using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Services.Recon;

public interface IExceptionWorkflowService
{
    Task SetStatusAsync(int exceptionId, ExceptionStatus status, Guid userId, CancellationToken ct = default);
    Task AddCommentAsync(int exceptionId, string body, CancellationToken ct = default);
}

/// <summary>Analyst actions on reconciliation exceptions: status transitions and sanitized notes.</summary>
public class ExceptionWorkflowService(IDbContextFactory<AppDbContext> dbFactory) : IExceptionWorkflowService
{
    private static readonly HtmlSanitizer Sanitizer = new();

    public async Task SetStatusAsync(int exceptionId, ExceptionStatus status, Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ex = await db.ReconExceptions.FirstOrDefaultAsync(x => x.ReconExceptionId == exceptionId, ct)
                 ?? throw new InvalidOperationException($"Exception {exceptionId} not found.");
        ex.Status = status;
        if (status is ExceptionStatus.Resolved or ExceptionStatus.WrittenOff)
        {
            ex.ResolvedBy = userId;
            ex.ResolvedDate = DateTime.UtcNow;
        }
        else
        {
            ex.ResolvedBy = null;
            ex.ResolvedDate = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task AddCommentAsync(int exceptionId, string body, CancellationToken ct = default)
    {
        var clean = Sanitizer.Sanitize(body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean)) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ReconExceptionComments.Add(new ReconExceptionComment { ReconExceptionId = exceptionId, Body = clean });
        await db.SaveChangesAsync(ct);
    }
}
