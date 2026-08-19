namespace Recon.Web.Domain.Common;

/// <summary>
/// Audit columns present on every non-staging table (DataModel.md conventions).
/// Stamped automatically by <c>AuditSaveChangesInterceptor</c> — never set manually.
/// </summary>
public interface IAuditable
{
    Guid CreatedBy { get; set; }
    DateTime CreatedDate { get; set; }
    Guid? UpdatedBy { get; set; }
    DateTime? UpdatedDate { get; set; }
}
