using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Recon.Web.Domain.Common;
using Recon.Web.Services;

namespace Recon.Web.Data;

/// <summary>
/// Stamps IAuditable columns and converts hard deletes of ISoftDeletable
/// entities into soft deletes. Registered once in Program.cs.
/// </summary>
public class AuditSaveChangesInterceptor(ICurrentUserService currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var now = DateTime.UtcNow;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditable auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedBy = userId;
                        auditable.CreatedDate = now;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedBy = userId;
                        auditable.UpdatedDate = now;
                        break;
                }
            }

            if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable deletable })
            {
                entry.State = EntityState.Modified;
                deletable.IsDeleted = true;
                deletable.DeletedBy = userId;
                deletable.DeletedDate = now;
            }
        }
    }
}
