using Microsoft.AspNetCore.Identity;

namespace Recon.Web.Domain.Identity;

/// <summary>AspNetRoles — Identity plumbing only. Business roles live in AppAccess.</summary>
public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }
    public AppRole(string roleName) : base(roleName) { }
}
