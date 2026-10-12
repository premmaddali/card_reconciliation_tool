using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;

namespace Recon.Web.Services.Recon;

internal static class GlAssignmentLock
{
    internal static Task AcquireAsync(AppDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'Recon.GL.Assignments', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000; IF @result < 0 THROW 51000, 'Another reconciliation is assigning GL entries. Please retry shortly.', 1;", ct);
}
