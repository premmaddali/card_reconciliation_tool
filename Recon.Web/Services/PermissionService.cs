using Microsoft.EntityFrameworkCore;
using Recon.Web.Data;

namespace Recon.Web.Services;

public record PagePermission(bool CanView, bool CanAdd, bool CanEdit, bool CanDelete)
{
    public static readonly PagePermission None = new(false, false, false, false);
}

public record MenuItem(string PageName, string? Route, string? Icon, int Sequence,
    string? GroupName, int GroupSequence);

public interface IPermissionService
{
    /// <summary>Merged page grants for a user (OR across all their active AppAccess roles), keyed by PageCode.</summary>
    Task<IReadOnlyDictionary<string, PagePermission>> GetGrantsAsync(Guid userId);

    /// <summary>Whether the user has a view grant for a page code.</summary>
    Task<bool> CanViewAsync(Guid userId, string pageCode);

    /// <summary>Menu items the user can view, grouped by parent page, in sequence order.</summary>
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(Guid userId);
}

public class PermissionService(IDbContextFactory<AppDbContext> dbFactory) : IPermissionService
{
    public async Task<IReadOnlyDictionary<string, PagePermission>> GetGrantsAsync(Guid userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var rows = await (
            from ua in db.UserAppAccesses
            where ua.UserId == userId && ua.IsActive
            join access in db.AppAccesses on ua.AppAccessId equals access.AppAccessId
            where access.IsActive
            join menu in db.AppAccessMenus on access.AppAccessId equals menu.AppAccessId
            where menu.IsActive
            join page in db.AppPages on menu.PageId equals page.AppPageId
            where page.IsActive
            select new { page.PageCode, menu.CanView, menu.CanAdd, menu.CanEdit, menu.CanDelete })
            .ToListAsync();

        return rows
            .GroupBy(r => r.PageCode)
            .ToDictionary(
                g => g.Key,
                g => new PagePermission(
                    g.Any(x => x.CanView),
                    g.Any(x => x.CanAdd),
                    g.Any(x => x.CanEdit),
                    g.Any(x => x.CanDelete)));
    }

    public async Task<bool> CanViewAsync(Guid userId, string pageCode)
    {
        var grants = await GetGrantsAsync(userId);
        return grants.TryGetValue(pageCode, out var grant) && grant.CanView;
    }

    public async Task<IReadOnlyList<MenuItem>> GetMenuAsync(Guid userId)
    {
        var grants = await GetGrantsAsync(userId);
        await using var db = await dbFactory.CreateDbContextAsync();

        var pages = await db.AppPages
            .Where(p => p.IsActive && p.IsMenuItem)
            .Include(p => p.ParentPage)
            .OrderBy(p => p.Sequence)
            .ToListAsync();

        return pages
            .Where(p => p.Route != null
                        && grants.TryGetValue(p.PageCode, out var g) && g.CanView)
            .Select(p => new MenuItem(
                p.PageName, p.Route, p.Icon, p.Sequence,
                p.ParentPage?.PageName, p.ParentPage?.Sequence ?? 0))
            .OrderBy(m => m.GroupSequence).ThenBy(m => m.Sequence)
            .ToList();
    }
}
