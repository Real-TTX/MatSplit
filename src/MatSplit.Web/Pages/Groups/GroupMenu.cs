using MatSplit.Web.Services;
using MatSplit.Web.Ui;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Builds the group entries of the left menu. Every group page uses this so the
/// menu is identical everywhere: members see their groups, administrators see
/// all of them, and the admin badge is set per group.
/// </summary>
public static class GroupMenu
{
    /// <summary>
    /// Loads the groups of the user (all groups for administrators) including
    /// the per group admin flag used for the menu badge.
    /// </summary>
    public static async Task<IReadOnlyList<MenuGroupEntry>> BuildAsync(
        GroupService groups,
        CurrentUserService currentUser,
        long userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(currentUser);

        var all = await groups.ListGroupsForUserAsync(userId, currentUser.IsAdmin, cancellationToken);
        var entries = new List<MenuGroupEntry>(all.Count);

        foreach (var group in all)
        {
            var isGroupAdmin = currentUser.IsAdmin
                || await groups.IsGroupAdminAsync(group.Id, userId, cancellationToken);

            entries.Add(new MenuGroupEntry(group.Id, group.Name, isGroupAdmin));
        }

        return entries;
    }
}
