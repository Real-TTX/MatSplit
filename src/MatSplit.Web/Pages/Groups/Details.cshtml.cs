using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Overview of a single group: key figures, open settlements, the newest
/// expenses and the latest activity. Read only, every action links to the
/// dedicated sub page.
/// </summary>
public class DetailsModel(
    CurrentUserService currentUser,
    GroupService groups,
    ExpenseService expenses,
    BalanceService balances,
    HistoryService history) : PageModel
{
    private const int PreviewSize = 5;

    [BindProperty(SupportsGet = true)]
    public long GroupId { get; set; }

    public Group Group { get; private set; } = null!;

    public IReadOnlyList<GroupMember> Members { get; private set; } = [];

    public BalanceResult Balance { get; private set; } = new();

    /// <summary>Balance row of the signed in user, null for administrators outside the group.</summary>
    public MemberBalance? MyBalance { get; private set; }

    public IReadOnlyList<Expense> RecentExpenses { get; private set; } = [];

    public IReadOnlyList<HistoryEntry> RecentHistory { get; private set; } = [];

    public bool CanManage { get; private set; }

    public int ExpenseCount { get; private set; }

    /// <summary>Anonymous invite link for this group (/Join?token=...).</summary>
    public string InviteUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/Join?token={Group?.InviteToken}";

    /// <summary>Prefilled message when sharing the invite link.</summary>
    public string ShareText => $"Tritt unserer MatSplit-Gruppe »{Group?.Name}« bei:";

    /// <summary>Direct WhatsApp share link – works even without JavaScript.</summary>
    public string WhatsAppUrl => "https://wa.me/?text=" + Uri.EscapeDataString($"{ShareText} {InviteUrl}");

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

        var expensePage = await expenses.ListExpensesAsync(
            GroupId, page: 1, pageSize: PreviewSize, cancellationToken: cancellationToken);

        RecentExpenses = expensePage.Items;
        ExpenseCount = expensePage.TotalCount;

        var historyPage = await history.ListHistoryAsync(
            GroupId, page: 1, pageSize: PreviewSize, cancellationToken: cancellationToken);

        RecentHistory = historyPage.Items;

        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, GroupId);
        this.SetTitle(group.Name, string.IsNullOrWhiteSpace(group.Description) ? "Gruppenübersicht" : group.Description, "group");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(group.Name));

        return Page();
    }

    /// <summary>Formats an audit timestamp (stored as UTC) in local time.</summary>
    public static string FormatMoment(DateTime utc) => IndexModel.FormatMoment(utc);

    /// <summary>
    /// Formats an expense date relative to today: "Heute", "Gestern" or the
    /// German short date. Used for the mobile "Letzte Ausgaben" list rows.
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

        var amounts = RecentExpenses
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
