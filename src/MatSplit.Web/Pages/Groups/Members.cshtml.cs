using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Read only membership list of one group plus the invite link (show, copy,
/// regenerate, switch off). Adding, editing and removing a membership happens
/// on the separate sub page /Groups/MemberEdit, as the ui guideline forbids
/// inline edits inside a list.
/// </summary>
public class MembersModel(
    CurrentUserService currentUser,
    GroupService groups,
    HistoryService history,
    AppConfigService appConfig) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long GroupId { get; set; }

    /// <summary>Free text filter on display name and e-mail.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>Sort key: name, name_desc, factor, factor_desc, admin, joined.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>
    /// 1-based page number from <c>?page=</c>. Read from the query string by
    /// hand: Razor Pages owns the route value "page" (it holds the page path),
    /// so binding a property named "page" would always add a model error.
    /// </summary>
    public int PageNumber => IndexModel.ReadPageNumber(Request);

    public Group Group { get; private set; } = null!;

    public bool CanManage { get; private set; }

    public PagedResult<MemberRow> Result { get; private set; } = PagedResult<MemberRow>.Empty();

    public IReadOnlyList<SelectListItem> SortOptions { get; private set; } = [];

    public string InviteUrl { get; private set; } = string.Empty;

    /// <summary>False when link invites are switched off for the whole installation.</summary>
    public bool AllowAnonymousJoin { get; private set; } = true;

    public int MemberCount { get; private set; }

    public string PageUrl =>
        $"/Groups/Members?groupId={GroupId}&search={Uri.EscapeDataString(Search ?? string.Empty)}"
        + $"&sort={Uri.EscapeDataString(Sort ?? string.Empty)}&page={{0}}";

    /// <summary>
    /// Current url including the query string. Row forms post here so groupId,
    /// search, sort and page survive the round trip.
    /// </summary>
    public string SelfUrl => Request.Path + Request.QueryString;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        return await LoadAsync(cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostRegenerateInviteAsync(CancellationToken cancellationToken)
    {
        var guard = await GuardManageAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var result = await groups.RegenerateInviteTokenAsync(GroupId, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToSelf();
        }

        this.Flash("Der Einladungslink wurde neu erzeugt, der alte Link ist ungültig.");
        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostToggleInviteAsync(bool enabled, CancellationToken cancellationToken)
    {
        var guard = await GuardManageAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var result = await groups.SetInviteEnabledAsync(GroupId, enabled, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToSelf();
        }

        await history.LogAsync(
            GroupId,
            currentUser.UserId,
            HistoryService.EntityTypes.Group,
            GroupId,
            HistoryService.Actions.Updated,
            enabled ? "Einladung per Link wurde aktiviert." : "Einladung per Link wurde deaktiviert.",
            cancellationToken: cancellationToken);

        this.Flash(enabled ? "Einladung per Link ist aktiv." : "Einladung per Link ist deaktiviert.");
        return RedirectToSelf();
    }

    /// <summary>Formats an audit timestamp (stored as UTC) in local time.</summary>
    public static string FormatMoment(DateTime utc) => IndexModel.FormatMoment(utc);

    /// <summary>
    /// Loads group and members. Returns a redirect when the group is
    /// gone or the user may not see it, otherwise null.
    /// </summary>
    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        if (GroupId <= 0)
        {
            return RedirectToPage("./Index");
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);

        if (group is null)
        {
            this.FlashError("Die Gruppe wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        Group = group;
        CanManage = await currentUser.CanManageGroupAsync(GroupId, cancellationToken);
        AllowAnonymousJoin = (await appConfig.GetAsync(cancellationToken)).AllowAnonymousJoin;
        InviteUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/Join?token={group.InviteToken}";

        var members = await groups.ListMembersAsync(GroupId, cancellationToken);
        MemberCount = members.Count;
        SortOptions = BuildSortOptions(Sort);

        var rows = members
            .Select(m => new MemberRow(
                m.UserId,
                m.User?.DisplayName ?? "Unbekannt",
                m.User?.Email,
                m.User?.IsAnonymous ?? false,
                m.ShareFactor,
                m.IsGroupAdmin,
                m.CreateDate,
                m.UserId == userId))
            .ToList();

        rows = Filter(rows, Search);
        rows = SortRows(rows, Sort);

        var (page, pageSize) = Paging.Normalize(PageNumber, Paging.DefaultPageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)pageSize));

        if (page > totalPages)
        {
            page = totalPages;
        }

        Result = new PagedResult<MemberRow>(
            [.. rows.Skip(Paging.Skip(page, pageSize)).Take(pageSize)],
            page,
            pageSize,
            rows.Count);

        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, GroupId);
        this.SetTitle("Mitglieder", group.Name, "users");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(group.Name, $"/Groups/Details?groupId={GroupId}"),
            new BreadcrumbItem("Mitglieder"));

        return null;
    }

    /// <summary>Returns a redirect/forbid result when the user may not manage the group.</summary>
    private async Task<IActionResult?> GuardManageAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();

        if (GroupId <= 0)
        {
            return RedirectToPage("./Index");
        }

        if (!await currentUser.CanManageGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);

        if (group is null)
        {
            this.FlashError("Die Gruppe wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        return null;
    }

    /// <summary>
    /// PRG target after a POST. Built by hand because "page" must not be handed
    /// to the page link generator (it would be read as the page path).
    /// </summary>
    private IActionResult RedirectToSelf()
    {
        var url = $"/Groups/Members?groupId={GroupId}";

        if (!string.IsNullOrWhiteSpace(Search))
        {
            url += $"&search={Uri.EscapeDataString(Search)}";
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            url += $"&sort={Uri.EscapeDataString(Sort)}";
        }

        var page = PageNumber;

        if (page > 1)
        {
            url += $"&page={page.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return LocalRedirect(url);
    }

    private static List<MemberRow> Filter(List<MemberRow> source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return source;
        }

        var term = search.Trim();

        return
        [
            .. source.Where(r =>
                r.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.Email is not null && r.Email.Contains(term, StringComparison.OrdinalIgnoreCase)))
        ];
    }

    private static List<MemberRow> SortRows(List<MemberRow> source, string? sort) => sort switch
    {
        "name_desc" => [.. source.OrderByDescending(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)],
        "factor" => [.. source.OrderBy(r => r.ShareFactor).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)],
        "factor_desc" => [.. source.OrderByDescending(r => r.ShareFactor).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)],
        "admin" => [.. source.OrderByDescending(r => r.IsGroupAdmin).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)],
        "joined" => [.. source.OrderBy(r => r.JoinedUtc)],
        _ => [.. source.OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)]
    };

    private static List<SelectListItem> BuildSortOptions(string? current)
    {
        var selected = string.IsNullOrWhiteSpace(current) ? "name" : current;

        return
        [
            new SelectListItem("Name (A-Z)", "name", selected == "name"),
            new SelectListItem("Name (Z-A)", "name_desc", selected == "name_desc"),
            new SelectListItem("Faktor aufsteigend", "factor", selected == "factor"),
            new SelectListItem("Faktor absteigend", "factor_desc", selected == "factor_desc"),
            new SelectListItem("Administratoren zuerst", "admin", selected == "admin"),
            new SelectListItem("Beitritt (älteste zuerst)", "joined", selected == "joined")
        ];
    }

    /// <summary>One row of the member list.</summary>
    /// <param name="UserId">User behind the membership.</param>
    /// <param name="DisplayName">Display name of the user.</param>
    /// <param name="Email">E-mail, null for anonymous users.</param>
    /// <param name="IsAnonymous">True for link guests without a password.</param>
    /// <param name="ShareFactor">Share weight (family = 3).</param>
    /// <param name="IsGroupAdmin">True when the member administers the group.</param>
    /// <param name="JoinedUtc">Creation date of the membership.</param>
    /// <param name="IsCurrentUser">True for the signed in user.</param>
    public sealed record MemberRow(
        long UserId,
        string DisplayName,
        string? Email,
        bool IsAnonymous,
        int ShareFactor,
        bool IsGroupAdmin,
        DateTime JoinedUtc,
        bool IsCurrentUser);
}
