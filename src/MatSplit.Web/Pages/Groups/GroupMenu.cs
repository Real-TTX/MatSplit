using MatSplit.Web.Services;
using MatSplit.Web.Ui;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Builds the group entries of the left menu. Every group page uses this so the
/// menu is identical everywhere: it lists the groups the user is a member of
/// (administrators included – they do not automatically see foreign groups) and
/// sets the admin badge per group.
/// </summary>
public static class GroupMenu
{
    /// <summary>
    /// Loads the groups the user belongs to, including the per group admin flag
    /// used for the menu badge.
    /// </summary>
    public static async Task<IReadOnlyList<MenuGroupEntry>> BuildAsync(
        GroupService groups,
        CurrentUserService currentUser,
        long userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(currentUser);

        var all = await groups.ListGroupsForUserAsync(userId, includeAll: false, cancellationToken);
        var entries = new List<MenuGroupEntry>(all.Count);

        foreach (var group in all)
        {
            var isGroupAdmin = await groups.IsGroupAdminAsync(group.Id, userId, cancellationToken);
            entries.Add(new MenuGroupEntry(group.Id, group.Name, isGroupAdmin));
        }

        return entries;
    }
}
