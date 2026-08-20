using System.Globalization;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Activity log of one group, rendered as a timeline grouped by day.
/// Search, action filter, date range and sort order are resolved by
/// <see cref="HistoryService.ListHistoryAsync"/>; this page only converts the
/// bounds of the picked local days into utc.
/// </summary>
public class HistoryModel(
    CurrentUserService currentUser,
    GroupService groups,
    HistoryService history) : PageModel
{
    private const int PageSizeValue = Paging.DefaultPageSize;

    private static readonly string[] WeekDayNames =
    [
        "Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"
    ];

    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

    [BindProperty(SupportsGet = true, Name = "search")]
    public string? Search { get; set; }

    /// <summary>Action filter, e.g. "Created" (the property name Action is avoided on purpose).</summary>
    [BindProperty(SupportsGet = true, Name = "action")]
    public string? ActionFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "fromDate")]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "toDate")]
    public DateTime? ToDate { get; set; }

    /// <summary>Sort key: date_desc (default), date, action, action_desc, user, user_desc.</summary>
    [BindProperty(SupportsGet = true, Name = "sort")]
    public string? Sort { get; set; }

    /// <summary>
    /// 1-based page number from <c>?page=</c>, read by hand because Razor Pages
    /// owns the route value "page" (see <see cref="MsPaging"/>).
    /// </summary>
    public int PageNumber => MsPaging.ReadPageNumber(Request);

    public Group Group { get; private set; } = new();

    public PagedResult<HistoryEntry> Result { get; private set; } = PagedResult<HistoryEntry>.Empty(1, PageSizeValue);

    /// <summary>Entries of the current page grouped by (local) day.</summary>
    public List<HistoryDay> Days { get; } = [];

    public List<SelectListItem> ActionOptions { get; } = [];

    public List<SelectListItem> SortOptions { get; } = [];

    /// <summary>True for group admins, drives the group settings tab.</summary>
    public bool CanManage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return RedirectToPage("/Groups/Index");
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        Group = group;
        CanManage = await currentUser.CanManageGroupAsync(GroupId, cancellationToken);

        await LoadResultAsync(cancellationToken);
        BuildDays();
        await BuildOptionsAsync(cancellationToken);
        await ApplyLayoutAsync(cancellationToken);

        return Page();
    }

    /// <summary>Url template for ms-pagination, keeps all active filters.</summary>
    public string BuildPageUrl()
    {
        var parts = new List<string>
        {
            "groupId=" + GroupId.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(Search))
        {
            parts.Add("search=" + Uri.EscapeDataString(Search));
        }

        if (!string.IsNullOrWhiteSpace(ActionFilter))
        {
            parts.Add("action=" + Uri.EscapeDataString(ActionFilter));
        }

        if (FromDate.HasValue)
        {
            parts.Add("fromDate=" + FromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (ToDate.HasValue)
        {
            parts.Add("toDate=" + ToDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            parts.Add("sort=" + Uri.EscapeDataString(Sort));
        }

        parts.Add("page={0}");
        return "/Groups/History?" + string.Join('&', parts);
    }

    /// <summary>Utc timestamp of the record as local time of the server.</summary>
    public static DateTime ToLocal(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>"Mittwoch, 20.08.2026" without depending on the process culture.</summary>
    public static string FormatDay(DateTime localDate)
        => WeekDayNames[(int)localDate.DayOfWeek] + ", " + localDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string FormatTime(DateTime utc)
        => ToLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>German label of an action constant.</summary>
    public static string ActionLabel(string? action) => action switch
    {
        HistoryService.Actions.Created => "Erstellt",
        HistoryService.Actions.Updated => "Geändert",
        HistoryService.Actions.Deleted => "Gelöscht",
        HistoryService.Actions.Joined => "Beigetreten",
        HistoryService.Actions.Left => "Verlassen",
        HistoryService.Actions.Merged => "Zusammengeführt",
        HistoryService.Actions.Uploaded => "Hochgeladen",
        HistoryService.Actions.Settled => "Ausgeglichen",
        HistoryService.Actions.SignedIn => "Angemeldet",
        HistoryService.Actions.SignedOut => "Abgemeldet",
        null or "" => "Aktion",
        _ => action
    };

    /// <summary>German label of an entity type constant.</summary>
    public static string EntityLabel(string? entityType) => entityType switch
    {
        HistoryService.EntityTypes.Group => "Gruppe",
        HistoryService.EntityTypes.GroupMember => "Mitglied",
        HistoryService.EntityTypes.Expense => "Ausgabe",
        HistoryService.EntityTypes.Payment => "Zahlung",
        HistoryService.EntityTypes.Receipt => "Beleg",
        HistoryService.EntityTypes.User => "Benutzer",
        null or "" => "Eintrag",
        _ => entityType
    };

    /// <summary>Icon name from the sprite for an action.</summary>
    public static string ActionIcon(string? action) => action switch
    {
        HistoryService.Actions.Created => "plus",
        HistoryService.Actions.Updated => "edit",
        HistoryService.Actions.Deleted => "trash",
        HistoryService.Actions.Joined => "users",
        HistoryService.Actions.Left => "logout",
        HistoryService.Actions.Merged => "merge",
        HistoryService.Actions.Uploaded => "upload",
        HistoryService.Actions.Settled => "balance",
        HistoryService.Actions.SignedIn => "user",
        HistoryService.Actions.SignedOut => "logout",
        _ => "info"
    };

    /// <summary>Display name of the acting user, "System" for service jobs.</summary>
    public static string ActorName(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrWhiteSpace(entry.User?.DisplayName) ? "System" : entry.User!.DisplayName;
    }

    private async Task LoadResultAsync(CancellationToken cancellationToken)
    {
        Result = await history.ListHistoryAsync(
            GroupId,
            Search,
            ActionFilter,
            PageNumber,
            PageSizeValue,
            ToUtcStart(FromDate),
            ToUtcEnd(ToDate),
            Sort,
            cancellationToken);
    }

    /// <summary>Start of the local day as utc, the service filters on utc.</summary>
    private static DateTime? ToUtcStart(DateTime? localDate)
        => localDate is null
            ? null
            : DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Local).ToUniversalTime();

    /// <summary>End of the local day (inclusive) as utc.</summary>
    private static DateTime? ToUtcEnd(DateTime? localDate)
        => localDate is null
            ? null
            : DateTime.SpecifyKind(localDate.Value.Date.AddDays(1), DateTimeKind.Local)
                .ToUniversalTime()
                .AddTicks(-1);

    private void BuildDays()
    {
        var days = Result.Items.GroupBy(x => ToLocal(x.CreateDate).Date);

        // The day cards always run in a chronological direction, even when the
        // entries themselves are sorted by action or by person.
        var ordered = string.Equals(Sort, "date", StringComparison.OrdinalIgnoreCase)
            ? days.OrderBy(x => x.Key)
            : days.OrderByDescending(x => x.Key);

        foreach (var group in ordered)
        {
            Days.Add(new HistoryDay(group.Key, group.ToList()));
        }
    }

    private async Task BuildOptionsAsync(CancellationToken cancellationToken)
    {
        var actions = await history.ListActionsAsync(GroupId, cancellationToken);

        foreach (var action in actions)
        {
            ActionOptions.Add(new SelectListItem
            {
                Value = action,
                Text = ActionLabel(action),
                Selected = string.Equals(ActionFilter, action, StringComparison.OrdinalIgnoreCase)
            });
        }

        var currentSort = string.IsNullOrWhiteSpace(Sort) ? "date_desc" : Sort!;

        AddSort(currentSort, "date_desc", "Zeitpunkt (neueste zuerst)");
        AddSort(currentSort, "date", "Zeitpunkt (älteste zuerst)");
        AddSort(currentSort, "action", "Aktion (A-Z)");
        AddSort(currentSort, "action_desc", "Aktion (Z-A)");
        AddSort(currentSort, "user", "Person (A-Z)");
        AddSort(currentSort, "user_desc", "Person (Z-A)");
    }

    private void AddSort(string currentSort, string value, string text)
    {
        SortOptions.Add(new SelectListItem
        {
            Value = value,
            Text = text,
            Selected = string.Equals(currentSort, value, StringComparison.OrdinalIgnoreCase)
        });
    }

    private async Task ApplyLayoutAsync(CancellationToken cancellationToken)
    {
        this.SetTitle("Historie", Group.Name, "history");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(Group.Name, "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture)),
            new BreadcrumbItem("Historie"));

        var menu = await GroupMenu.BuildAsync(
            groups,
            currentUser,
            currentUser.RequireUserId(),
            cancellationToken);

        this.SetMenuGroups(menu, GroupId);
    }

    /// <summary>One day of the timeline.</summary>
    /// <param name="Date">Local date of the entries.</param>
    /// <param name="Entries">Entries of that day, newest first.</param>
    public sealed record HistoryDay(DateTime Date, IReadOnlyList<HistoryEntry> Entries);
}
