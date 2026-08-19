using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Identity;

/// <summary>User ↔ business role assignment. Revoking keeps history via soft delete.</summary>
public class UserAppAccess : IAuditable, ISoftDeletable, IActivatable
{
    public int UserAppAccessId { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public int AppAccessId { get; set; }
    public AppAccess AppAccess { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
}
