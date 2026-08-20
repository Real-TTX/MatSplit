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
}
