using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages;

/// <summary>
/// Public, read-only view of a group, reachable through its read-only link
/// (/View?token=...). No login, no join, no editing: it shows the balance, the
/// settlements, the transactions and the members of the group and nothing else.
/// Renders without the app menu (bare shell).
/// </summary>
[AllowAnonymous]
public class ViewModel(
    GroupService groups,
    BalanceService balances,
    TransactionService transactions) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public Group? Group { get; private set; }

    public bool Found => Group is not null;

    public BalanceResult Balance { get; private set; } = new();

    public IReadOnlyList<TransactionRow> Transactions { get; private set; } = [];

    public IReadOnlyList<GroupMember> Members { get; private set; } = [];

    public string Currency => string.IsNullOrWhiteSpace(Group?.Currency) ? "EUR" : Group!.Currency;

    /// <summary>Sum of all open settlement transfers.</summary>
    public long OpenSettlementCents => Balance.Settlements.Sum(x => x.AmountCents);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData[LayoutKeys.HideMenu] = true;

        var group = await groups.GetGroupByReadOnlyTokenAsync(Token, cancellationToken);

        if (group is null)
        {
            this.SetTitle("Nur-Lese-Ansicht", "Geteilte Gruppe", "link");
            return Page();
        }

        Group = group;
        Balance = await balances.CalculateBalancesAsync(group.Id, cancellationToken);
        Members = await groups.ListMembersAsync(group.Id, cancellationToken);

        var result = await transactions.ListTransactionsAsync(
            group.Id, kind: null, page: 1, pageSize: 100, cancellationToken: cancellationToken);
        Transactions = result.Items;

        this.SetTitle(group.Name, "Nur-Lese-Ansicht", "group");
        return Page();
    }
}
