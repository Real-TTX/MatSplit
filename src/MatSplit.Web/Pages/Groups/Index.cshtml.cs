using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// List of all groups the current user belongs to. Administrators see every
/// group of the installation. Search, sorting and paging happen in memory
/// because the service layer only offers a paged query for the admin area and
/// the per user group count is small.
/// </summary>
public class IndexModel(
    CurrentUserService currentUser,
    GroupService groups,
    BalanceService balances,
    HistoryService history) : PageModel
{
    /// <summary>Free text filter on name and description.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>Sort key: name, name_desc, created_desc, created.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>
    /// 1-based page number from <c>?page=</c>. Read from the query string by
    /// hand on purpose: Razor Pages already owns the route value "page" (it
    /// holds the page path), so model binding it would always fail.
    /// </summary>
    public int PageNumber => ReadPageNumber(Request);

    public PagedResult<GroupRow> Result { get; private set; } = PagedResult<GroupRow>.Empty();

    public bool IsAdmin => currentUser.IsAdmin;

    /// <summary>False for link guests, who may not open groups of their own.</summary>
    public bool CanCreateGroup => currentUser.CanCreateGroup;

    /// <summary>
    /// Highest total-expenses value among the groups on the current page.
    /// Used purely visually to scale the progress bar of the mobile group
    /// cards (each bar shows its share of the biggest spender). 0 when empty.
    /// </summary>
    public long MaxExpensesCents { get; private set; }

    /// <summary>Options of the sort dropdown, the current key is preselected.</summary>
    public IReadOnlyList<SelectListItem> SortOptions { get; private set; } = [];

    /// <summary>Url template for ms-pagination, keeps search and sort.</summary>
    public string PageUrl =>
        $"/Groups?search={Uri.EscapeDataString(Search ?? string.Empty)}"
        + $"&sort={Uri.EscapeDataString(Sort ?? string.Empty)}&page={{0}}";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        // Administrators are not automatically members of every group; the
        // overview shows only the groups the user actually belongs to.
        var all = await groups.ListGroupsForUserAsync(userId, includeAll: false, cancellationToken);

        var manageFlags = new Dictionary<long, bool>(all.Count);

        foreach (var group in all)
        {
            manageFlags[group.Id] = currentUser.IsAdmin
                || await groups.IsGroupAdminAsync(group.Id, userId, cancellationToken);
        }

        this.SetMenuGroups(all.Select(g => new MenuGroupEntry(g.Id, g.Name, manageFlags[g.Id])).ToList());
        this.SetTitle("Meine Gruppen", "Deine Gruppen", "group");
        // Start page: no breadcrumb (a single "Gruppen" crumb would only echo the title).

        SortOptions = BuildSortOptions(Sort);

        var matching = SortGroups(Filter(all, Search), Sort);
        var (page, pageSize) = Paging.Normalize(PageNumber, Paging.DefaultPageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(matching.Count / (double)pageSize));

        if (page > totalPages)
        {
            page = totalPages;
        }

        var rows = new List<GroupRow>();

        foreach (var group in matching.Skip(Paging.Skip(page, pageSize)).Take(pageSize))
        {
            var members = await groups.ListMembersAsync(group.Id, cancellationToken);

            // One full balance snapshot per row (N+1): needed for the member's
            // own saldo shown on the mobile group cards. TotalExpensesCents is
            // taken from the same snapshot, so no extra expense query is issued.
            var balance = await balances.CalculateBalancesAsync(group.Id, cancellationToken);
            var ownBalanceCents = balance.Balances
                .FirstOrDefault(b => b.UserId == userId)?.BalanceCents ?? 0;

            var lastEntries = await history.ListHistoryAsync(
                group.Id, page: 1, pageSize: 1, cancellationToken: cancellationToken);

            var lastActivity = lastEntries.Items.Count > 0
                ? lastEntries.Items[0].CreateDate
                : group.UpdateDate;

            rows.Add(new GroupRow(
                group.Id,
                group.Name,
                group.Description,
                group.Currency,
                members.Count,
                members.Sum(m => m.ShareFactor),
                balance.TotalExpensesCents,
                ownBalanceCents,
                lastActivity,
                manageFlags[group.Id]));
        }

        MaxExpensesCents = rows.Count > 0 ? rows.Max(r => r.TotalExpensesCents) : 0;
        Result = new PagedResult<GroupRow>(rows, page, pageSize, matching.Count);
    }

    /// <summary>
    /// Reads the 1-based page number from the query string. Razor Pages keeps
    /// the page path in the route value "page", so a bound property named
    /// "page" would receive that path and add a model error instead.
    /// </summary>
    public static int ReadPageNumber(HttpRequest request) => MsPaging.ReadPageNumber(request);

    /// <summary>Formats an audit timestamp (stored as UTC) in local time.</summary>
    public static string FormatMoment(DateTime utc)
    {
        if (utc == default)
        {
            return "–";
        }

        return DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<Group> Filter(IReadOnlyList<Group> source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [.. source];
        }

        var term = search.Trim();

        return
        [
            .. source.Where(g =>
                g.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (g.Description is not null && g.Description.Contains(term, StringComparison.OrdinalIgnoreCase)))
        ];
    }

    private static List<Group> SortGroups(List<Group> source, string? sort) => sort switch
    {
        "name_desc" => [.. source.OrderByDescending(g => g.Name, StringComparer.CurrentCultureIgnoreCase)],
        "created" => [.. source.OrderBy(g => g.CreateDate)],
        "created_desc" => [.. source.OrderByDescending(g => g.CreateDate)],
        _ => [.. source.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)]
    };

    private static List<SelectListItem> BuildSortOptions(string? current)
    {
        var selected = string.IsNullOrWhiteSpace(current) ? "name" : current;

        return
        [
            new SelectListItem("Name (A-Z)", "name", selected == "name"),
            new SelectListItem("Name (Z-A)", "name_desc", selected == "name_desc"),
            new SelectListItem("Neueste zuerst", "created_desc", selected == "created_desc"),
            new SelectListItem("\u00c4lteste zuerst", "created", selected == "created")
        ];
    }

    /// <summary>One row of the group list including its aggregated numbers.</summary>
    /// <param name="Id">Group id.</param>
    /// <param name="Name">Group name.</param>
    /// <param name="Description">Optional description.</param>
    /// <param name="Currency">Currency code of the group.</param>
    /// <param name="MemberCount">Number of active members.</param>
    /// <param name="ShareTotal">Sum of all member share factors.</param>
    /// <param name="TotalExpensesCents">Sum of all expenses in cents.</param>
    /// <param name="OwnBalanceCents">Current user's saldo in this group (positive = credit).</param>
    /// <param name="LastActivityUtc">Timestamp of the newest history entry.</param>
    /// <param name="CanManage">True when the user may edit the group.</param>
    public sealed record GroupRow(
        long Id,
        string Name,
        string? Description,
        string Currency,
        int MemberCount,
        int ShareTotal,
        long TotalExpensesCents,
        long OwnBalanceCents,
        DateTime LastActivityUtc,
        bool CanManage);
}
