namespace Recon.Web.Domain.Common;

/// <summary>
/// IsActive flag on master/config tables (DataModel.md conventions).
/// Loaders filter IsActive explicitly — deliberately NOT a global query filter,
/// so admin screens can still list and reactivate inactive rows.
/// </summary>
public interface IActivatable
{
    bool IsActive { get; set; }
}
