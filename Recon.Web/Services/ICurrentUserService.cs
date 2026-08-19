namespace Recon.Web.Services;

/// <summary>
/// Resolves the acting user for audit stamping. Backed by the authenticated
/// principal once Identity lands (Phase 1); until then returns the system user.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }
}

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    /// <summary>Well-known id used for seeding and pre-auth operations.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-000000000001");

    public Guid UserId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : SystemUserId;
        }
    }
}
