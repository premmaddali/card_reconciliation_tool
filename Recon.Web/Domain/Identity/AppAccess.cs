using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Identity;

public enum AppUserType : byte
{
    Employee = 1,
    External = 2
}

/// <summary>Business role — Administrator, Recon Manager, Recon Officer, Viewer.</summary>
public class AppAccess : IAuditable, ISoftDeletable, IActivatable
{
    public int AppAccessId { get; set; }
    public string AccessName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AppUserType UserType { get; set; } = AppUserType.Employee;
    public int? LandingPageId { get; set; }
    public AppPage? LandingPage { get; set; }
    public bool DisableContent { get; set; }

    public ICollection<AppAccessMenu> Menus { get; set; } = [];
    public ICollection<UserAppAccess> Users { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
}
