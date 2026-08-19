using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Identity;

/// <summary>Page-level CRUD grant per business role. One row per (role, page).</summary>
public class AppAccessMenu : IAuditable, ISoftDeletable, IActivatable
{
    public int AppAccessMenuId { get; set; }
    public int AppAccessId { get; set; }
    public AppAccess AppAccess { get; set; } = null!;
    public int PageId { get; set; }
    public AppPage Page { get; set; } = null!;

    public bool CanAdd { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanView { get; set; }
    public bool DisableContent { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
}
