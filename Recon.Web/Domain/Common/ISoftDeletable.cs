namespace Recon.Web.Domain.Common;

/// <summary>
/// Soft-delete triplet (DataModel.md conventions). A global query filter hides
/// deleted rows; deletes are converted to updates by the interceptor.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    Guid? DeletedBy { get; set; }
    DateTime? DeletedDate { get; set; }
}
