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
/// Activity log of one group, rendered as a mobile first activity feed (see the
/// mockup screen "Aktivitaeten"). The coarse category pills (Alle / Ausgaben /
/// Zahlungen / Beitritte) map onto the existing history actions and entity types
/// and drive the very same <c>search</c>/<c>action</c> query parameters that the
/// desktop toolbar uses, so no service change is needed. The toolbar stays as a
/// desktop only addition for free text search, date range and sort order.
/// </summary>
public class HistoryModel(
    CurrentUserService currentUser,
    GroupService groups,
    HistoryService history) : PageModel
{
    private const int PageSizeValue = Paging.DefaultPageSize;

    /// <summary>Pill values carried in <c>?filter=</c>.</summary>
    public const string FilterAll = "all";
    public const string FilterExpenses = "expenses";
    public const string FilterPayments = "payments";
    public const string FilterJoins = "joins";

    private static readonly string[] MonthNames =
    [
        "Januar", "Februar", "März", "April", "Mai", "Juni",
        "Juli", "August", "September", "Oktober", "November", "Dezember"
    ];

    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

    [BindProperty(SupportsGet = true, Name = "search")]
    public string? Search { get; set; }

    /// <summary>Action filter, e.g. "Created" (the property name Action is avoided on purpose).</summary>
    [BindProperty(SupportsGet = true, Name = "action")]
    public string? ActionFilter { get; set; }

    /// <summary>Category pill, one of all/expenses/payments/joins.</summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? Filter { get; set; }

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

    /// <summary>Entries of the current page, newest first (feed order).</summary>
    public IReadOnlyList<HistoryEntry> Entries => Result.Items;

    public List<SelectListItem> ActionOptions { get; } = [];

    public List<SelectListItem> SortOptions { get; } = [];

    /// <summary>True for group admins, drives the group settings tab.</summary>
    public bool CanManage { get; private set; }

    /// <summary>The active pill, defaulted and lower cased.</summary>
    public string ActiveFilter => Filter?.Trim().ToLowerInvariant() switch
    {
        FilterExpenses => FilterExpenses,
        FilterPayments => FilterPayments,
        FilterJoins => FilterJoins,
        _ => FilterAll
    };

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
        await BuildOptionsAsync(cancellationToken);
        await ApplyLayoutAsync(cancellationToken);

        return Page();
    }

    /// <summary>Url of a category pill, keeping every other active filter.</summary>
    public string BuildFilterUrl(string filter)
        => "/Groups/History?" + string.Join('&', BuildQueryParts(filter));

    /// <summary>Url template for ms-pagination, keeps all active filters.</summary>
    public string BuildPageUrl()
    {
        var parts = BuildQueryParts(ActiveFilter);
        parts.Add("page={0}");
        return "/Groups/History?" + string.Join('&', parts);
    }

    /// <summary>Utc timestamp of the record as local time of the server.</summary>
    public static DateTime ToLocal(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>Relative timestamp like the mockup: "Heute, 14:30" or "17. Mai 2024".</summary>
    public static string RelativeTime(DateTime utc)
    {
        var local = ToLocal(utc);
        var today = DateTime.Now.Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (local.Date == today)
        {
            return "Heute, " + time;
        }

        if (local.Date == today.AddDays(-1))
        {
            return "Gestern, " + time;
        }

        return local.Day.ToString(CultureInfo.InvariantCulture)
            + ". " + MonthNames[local.Month - 1]
            + " " + local.Year.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>German label of an action constant (drives the toolbar dropdown).</summary>
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

    /// <summary>Display name of the acting user, "System" for service jobs.</summary>
    public static string ActorName(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrWhiteSpace(entry.User?.DisplayName) ? "System" : entry.User!.DisplayName;
    }

    /// <summary>The sentence after the bold actor name, e.g. "hat eine Ausgabe hinzugefügt".</summary>
    public static string FeedSentence(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (entry.EntityType, entry.Action) switch
        {
            (HistoryService.EntityTypes.Expense, HistoryService.Actions.Created) => "hat eine Ausgabe hinzugefügt",
            (HistoryService.EntityTypes.Expense, HistoryService.Actions.Updated) => "hat eine Ausgabe bearbeitet",
            (HistoryService.EntityTypes.Expense, HistoryService.Actions.Deleted) => "hat eine Ausgabe gelöscht",
            (HistoryService.EntityTypes.Payment, HistoryService.Actions.Settled) => "hat den Saldo ausgeglichen",
            (HistoryService.EntityTypes.Payment, HistoryService.Actions.Created) => "hat eine Zahlung hinzugefügt",
            (HistoryService.EntityTypes.Payment, HistoryService.Actions.Updated) => "hat eine Zahlung bearbeitet",
            (HistoryService.EntityTypes.Payment, HistoryService.Actions.Deleted) => "hat eine Zahlung gelöscht",
            (HistoryService.EntityTypes.GroupMember, HistoryService.Actions.Joined) => "ist der Gruppe beigetreten",
            (HistoryService.EntityTypes.GroupMember, HistoryService.Actions.Left) => "hat die Gruppe verlassen",
            (HistoryService.EntityTypes.GroupMember, _) => "hat ein Mitglied aktualisiert",
            (HistoryService.EntityTypes.Group, HistoryService.Actions.Created) => "hat die Gruppe erstellt",
            (HistoryService.EntityTypes.Group, HistoryService.Actions.Updated) => "hat die Gruppe bearbeitet",
            (HistoryService.EntityTypes.Group, HistoryService.Actions.Deleted) => "hat die Gruppe gelöscht",
            (HistoryService.EntityTypes.Receipt, HistoryService.Actions.Uploaded) => "hat einen Beleg hochgeladen",
            (HistoryService.EntityTypes.Receipt, HistoryService.Actions.Deleted) => "hat einen Beleg gelöscht",
            _ => "hat " + EntityLabel(entry.EntityType) + " geändert"
        };
    }

    /// <summary>Bold title line of the feed entry (the quoted subject) or null.</summary>
    public static string? FeedTitle(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var summary = entry.Summary;
        if (string.IsNullOrEmpty(summary))
        {
            return null;
        }

        var open = summary.IndexOf('"');
        if (open < 0)
        {
            return null;
        }

        var close = summary.IndexOf('"', open + 1);
        if (close <= open)
        {
            return null;
        }

        var title = summary[(open + 1)..close].Trim();
        return string.IsNullOrEmpty(title) ? null : title;
    }

    /// <summary>
    /// Icon for entries the mockup shows without an avatar (balance settlement,
    /// membership); null means "render the actor's avatar instead".
    /// </summary>
    public static string? FeedIcon(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (entry.EntityType, entry.Action) switch
        {
            (HistoryService.EntityTypes.Payment, HistoryService.Actions.Settled) => "balance",
            (HistoryService.EntityTypes.GroupMember, HistoryService.Actions.Joined) => "users",
            (HistoryService.EntityTypes.GroupMember, HistoryService.Actions.Left) => "logout",
            _ => null
        };
    }

    /// <summary>
    /// Amount + currency parsed out of the summary (the entry itself stores no
    /// cents), so <c>ms-money</c> can render it in the usual German style. Null
    /// when the entry carries no amount.
    /// </summary>
    public static FeedMoney? FeedAmount(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var summary = entry.Summary;
        if (string.IsNullOrEmpty(summary))
        {
            return null;
        }

        // Amount bearing summaries read "… über <amount> <currency> wurde …" or
        // "… über <amount> <currency> verbucht." — grab what sits between them.
        const string marker = "über ";
        var start = summary.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var rest = summary[start..];

        var end = rest.Length;
        var stopWrote = rest.IndexOf(" wurde", StringComparison.Ordinal);
        var stopSettled = rest.IndexOf(" verbucht", StringComparison.Ordinal);
        if (stopWrote >= 0)
        {
            end = Math.Min(end, stopWrote);
        }

        if (stopSettled >= 0)
        {
            end = Math.Min(end, stopSettled);
        }

        if (end == rest.Length)
        {
            // Neither marker is present, so this is not an amount bearing summary.
            return null;
        }

        var token = rest[..end].Trim();
        var space = token.LastIndexOf(' ');
        if (space <= 0)
        {
            return null;
        }

        var numberText = token[..space].Trim();
        var currency = token[(space + 1)..].Trim().TrimEnd('.');
        if (currency.Length == 0)
        {
            currency = "EUR";
        }

        // A German formatted amount always carries the decimal comma (N2); the
        // settlement summary uses the invariant dot instead, so switch on that.
        var culture = numberText.Contains(',')
            ? CultureInfo.GetCultureInfo("de-DE")
            : CultureInfo.InvariantCulture;

        if (!decimal.TryParse(numberText, NumberStyles.Number, culture, out var value))
        {
            return null;
        }

        var cents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
        return new FeedMoney(cents, currency);
    }

    private List<string> BuildQueryParts(string filter)
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

        if (!string.Equals(filter, FilterAll, StringComparison.Ordinal))
        {
            parts.Add("filter=" + Uri.EscapeDataString(filter));
        }

        return parts;
    }

    private async Task LoadResultAsync(CancellationToken cancellationToken)
    {
        var (search, action) = ResolveFilter();

        Result = await history.ListHistoryAsync(
            GroupId,
            search,
            action,
            PageNumber,
            PageSizeValue,
            ToUtcStart(FromDate),
            ToUtcEnd(ToDate),
            Sort,
            cancellationToken);
    }

    /// <summary>
    /// Maps the active pill onto the existing service parameters. A pill only
    /// fills a slot the desktop toolbar left empty, so an explicit search or
    /// action from the toolbar always wins. The service already treats
    /// <c>search</c> as a match against the entity type, which is exactly what
    /// the "Ausgaben"/"Zahlungen" categories need.
    /// </summary>
    private (string? Search, string? Action) ResolveFilter()
    {
        var search = Search;
        var action = ActionFilter;

        switch (ActiveFilter)
        {
            case FilterExpenses:
                if (string.IsNullOrWhiteSpace(search))
                {
                    search = HistoryService.EntityTypes.Expense;
                }

                break;
            case FilterPayments:
                if (string.IsNullOrWhiteSpace(search))
                {
                    search = HistoryService.EntityTypes.Payment;
                }

                break;
            case FilterJoins:
                if (string.IsNullOrWhiteSpace(action))
                {
                    action = HistoryService.Actions.Joined;
                }

                break;
        }

        return (search, action);
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
            new BreadcrumbItem(Group.Name, "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture)),
            new BreadcrumbItem("Historie"));

        var menu = await GroupMenu.BuildAsync(
            groups,
            currentUser,
            currentUser.RequireUserId(),
            cancellationToken);

        this.SetMenuGroups(menu, GroupId);
    }

    /// <summary>An amount recovered from a history summary.</summary>
    /// <param name="Cents">Value in cents for <c>ms-money</c>.</param>
    /// <param name="Currency">Iso currency code, EUR when unknown.</param>
    public sealed record FeedMoney(long Cents, string Currency);
}
