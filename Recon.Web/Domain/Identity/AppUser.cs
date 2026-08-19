using Microsoft.AspNetCore.Identity;

namespace Recon.Web.Domain.Identity;

public enum AuthProvider : byte
{
    Local = 0,
    ActiveDirectory = 1
}

/// <summary>
/// AspNetUsers + extension columns. Authentication only — business authorization comes from
/// AppAccess grants, not AspNetRoles.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string? Designation { get; set; }
    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;
    public string? AdAccountName { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<UserAppAccess> UserAppAccesses { get; set; } = [];
}
