using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Group hub: the big balance hero (links to /Groups/Balance), a pill tab bar
/// and, depending on the active tab, either the combined transaction list
/// (expenses and payments merged for display) or a compact member preview.
/// Every action links to the dedicated sub page.
/// </summary>
public class DetailsModel(
    CurrentUserService currentUser,
    GroupService groups,
    ExpenseService expenses,
    BalanceService balances,
    TransactionService transactions) : PageModel
{
    private const int TransactionsPageSize = 10;

    private const int SparklineSampleSize = 12;

    public const string TabTransactions = "transaktionen";

    public const string TabMembers = "mitglieder";

    public const string TypeAll = "alle";

    public const string TypeExpenses = "ausgaben";

    public const string TypePayments = "zahlungen";

    public const string MemberAll = "alle";

    public const string MemberAdmins = "admins";

    public const string MemberUsers = "benutzer";

    public const string MemberAnon = "anonyme";

    [BindProperty(SupportsGet = true)]
    public long GroupId { get; set; }

    /// <summary>Active tab: <c>transaktionen</c> (default) or <c>mitglieder</c>.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    /// <summary>Type filter of the transaction list: <c>alle</c>, <c>ausgaben</c> or <c>zahlungen</c>.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    /// <summary>Member filter of the members tab: <c>alle</c>, <c>admins</c>, <c>benutzer</c> or <c>anonyme</c>.</summary>
    [BindProperty(SupportsGet = true, Name = "mtype")]
    public string? MemberType { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public Group Group { get; private set; } = null!;

    public IReadOnlyList<GroupMember> Members { get; private set; } = [];

    /// <summary>Members after applying the active members-tab filter.</summary>
    public IReadOnlyList<GroupMember> FilteredMembers { get; private set; } = [];

    public BalanceResult Balance { get; private set; } = new();

    /// <summary>Balance row of the signed in user, null for administrators outside the group.</summary>
    public MemberBalance? MyBalance { get; private set; }

    /// <summary>Combined, chronological transactions of the active tab / type filter.</summary>
    public PagedResult<TransactionRow> Transactions { get; private set; } = PagedResult<TransactionRow>.Empty();

    public bool CanManage { get; private set; }

    /// <summary>False for link guests, who may not hand the invite link on.</summary>
    public bool CanInvite => currentUser.CanInvite;

    /// <summary>Normalised active tab, always one of the Tab* constants.</summary>
    public string ActiveTab { get; private set; } = TabTransactions;

    /// <summary>Normalised active type filter, always one of the Type* constants.</summary>
    public string ActiveType { get; private set; } = TypeAll;

    /// <summary>Normalised active member filter, always one of the Member* constants.</summary>
    public string ActiveMemberType { get; private set; } = MemberAll;

    private IReadOnlyList<Expense> sparklineExpenses = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
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
        Members = await groups.ListMembersAsync(GroupId, cancellationToken);
        Balance = await balances.CalculateBalancesAsync(GroupId, cancellationToken);
        MyBalance = Balance.Balances.FirstOrDefault(x => x.UserId == userId);

        ActiveTab = NormalizeTab(Tab);
        ActiveType = NormalizeType(Type);
        ActiveMemberType = NormalizeMemberType(MemberType);
        FilteredMembers = FilterMembers(Members, ActiveMemberType);

        // The hero sparkline is driven by the newest expenses regardless of the
        // active tab, so the trend line stays stable while browsing.
        var sparklinePage = await expenses.ListExpensesAsync(
            GroupId, page: 1, pageSize: SparklineSampleSize, cancellationToken: cancellationToken);
        sparklineExpenses = sparklinePage.Items;

        if (ActiveTab == TabTransactions)
        {
            Transactions = await transactions.ListTransactionsAsync(
                GroupId,
                kind: TypeToKind(ActiveType),
                page: PageNumber,
                pageSize: TransactionsPageSize,
                cancellationToken: cancellationToken);
        }

        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, GroupId);
        this.SetTitle(group.Name, string.IsNullOrWhiteSpace(group.Description) ? "Gruppenübersicht" : group.Description, "group");
        this.SetBreadcrumb(
            new BreadcrumbItem(group.Name));

        return Page();
    }

    /// <summary>Builds a tab/type-preserving url for the transaction pagination.</summary>
    public string TransactionsPageUrl()
        => $"/Groups/Details?groupId={GroupId}&tab={TabTransactions}&type={ActiveType}";

    /// <summary>Url of a tab link, keeping the current type filter for the transaction tab.</summary>
    public string TabUrl(string tab)
        => tab == TabTransactions
            ? $"/Groups/Details?groupId={GroupId}&tab={TabTransactions}&type={ActiveType}"
            : $"/Groups/Details?groupId={GroupId}&tab={tab}";

    /// <summary>Url of a type filter pill inside the transaction tab.</summary>
    public string TypeUrl(string type)
        => $"/Groups/Details?groupId={GroupId}&tab={TabTransactions}&type={type}";

    /// <summary>Url of a member filter pill inside the members tab.</summary>
    public string MemberTypeUrl(string memberType)
        => $"/Groups/Details?groupId={GroupId}&tab={TabMembers}&mtype={memberType}";

    private static string NormalizeTab(string? tab)
        => string.Equals(tab, TabMembers, StringComparison.OrdinalIgnoreCase)
            ? TabMembers
            : TabTransactions;

    private static string NormalizeType(string? type) => type?.ToLowerInvariant() switch
    {
        // German values are used by the hub's own filter pills; the English
        // "expenses"/"payments" aliases keep the redirects from the old
        // standalone list pages preselecting the right filter.
        TypeExpenses or "expenses" => TypeExpenses,
        TypePayments or "payments" => TypePayments,
        _ => TypeAll
    };

    private static TransactionKind? TypeToKind(string type) => type switch
    {
        TypeExpenses => TransactionKind.Expense,
        TypePayments => TransactionKind.Payment,
        _ => null
    };

    private static string NormalizeMemberType(string? memberType) => memberType?.ToLowerInvariant() switch
    {
        MemberAdmins => MemberAdmins,
        MemberUsers => MemberUsers,
        MemberAnon => MemberAnon,
        _ => MemberAll
    };

    /// <summary>
    /// Filters the members by category. "Benutzer" are all registered
    /// (non-anonymous) members; "Admins" are the group administrators among them
    /// (a subset, not a separate group); "Anonyme" are link guests.
    /// </summary>
    private static IReadOnlyList<GroupMember> FilterMembers(IReadOnlyList<GroupMember> members, string memberType) => memberType switch
    {
        MemberAdmins => [.. members.Where(m => m.IsGroupAdmin)],
        MemberUsers => [.. members.Where(m => !(m.User?.IsAnonymous ?? false))],
        MemberAnon => [.. members.Where(m => m.User?.IsAnonymous ?? false)],
        _ => members
    };

    /// <summary>
    /// Formats a transaction date relative to today: "Heute", "Gestern" or the
    /// German short date. Used for the transaction list rows.
    /// </summary>
    public static string RelativeDay(DateTime date)
    {
        var day = date.Date;
        var today = DateTime.Today;

        if (day == today)
        {
            return "Heute";
        }

        if (day == today.AddDays(-1))
        {
            return "Gestern";
        }

        return day.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }

    /// <summary>
    /// Builds the SVG path for the balance hero sparkline from the cumulative
    /// spend of the recent expenses (oldest to newest), so the line trends
    /// upwards like in the mockup. Falls back to a gentle placeholder curve when
    /// there are not enough data points to plot.
    /// </summary>
    public string BalanceSparkline()
    {
        const string placeholder = "M2 34 20 28 38 32 56 18 74 22 92 8 106 12";

        var amounts = sparklineExpenses
            .OrderBy(expense => expense.ExpenseDate)
            .ThenBy(expense => expense.Id)
            .Select(expense => expense.AmountCents)
            .ToList();

        if (amounts.Count < 2)
        {
            return placeholder;
        }

        var cumulative = new long[amounts.Count];
        long running = 0;

        for (var i = 0; i < amounts.Count; i++)
        {
            running += amounts[i];
            cumulative[i] = running;
        }

        var min = cumulative.Min();
        var max = cumulative.Max();
        double range = max - min;

        if (range <= 0)
        {
            range = 1;
        }

        const double xLeft = 2, xRight = 106, yTop = 6, yBottom = 40;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var path = new System.Text.StringBuilder();

        for (var i = 0; i < cumulative.Length; i++)
        {
            var x = xLeft + ((xRight - xLeft) * i / (cumulative.Length - 1));
            var y = yBottom - ((cumulative[i] - min) / range * (yBottom - yTop));

            path.Append(i == 0 ? "M" : " L");
            path.Append(x.ToString("0.#", culture));
            path.Append(' ');
            path.Append(y.ToString("0.#", culture));
        }

        return path.ToString();
    }
}
