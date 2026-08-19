using Recon.Web.Domain.Common;

namespace Recon.Web.Domain.Identity;

/// <summary>Page/menu registry. AppAccessMenu grants point at these.</summary>
public class AppPage : IAuditable, ISoftDeletable, IActivatable
{
    public int AppPageId { get; set; }
    public int? ParentPageId { get; set; }
    public AppPage? ParentPage { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string PageCode { get; set; } = string.Empty;
    public string? Route { get; set; }
    public string? Icon { get; set; }
    public int Sequence { get; set; }
    public bool IsMenuItem { get; set; } = true;

    public ICollection<AppPage> Children { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public bool IsDeleted { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
}
