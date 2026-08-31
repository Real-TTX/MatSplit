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
/// (/View?token=...). No login, no join, no editing. A pill tab bar switches
/// between the transactions (expenses + payments, the important part) and the
/// members with their balances and the open settlements. Renders without the
/// app menu (bare shell).
/// </summary>
[AllowAnonymous]
public class ViewModel(
    GroupService groups,
    BalanceService balances,
    TransactionService transactions) : PageModel
{
    public const string TabTransactions = "transaktionen";
    public const string TabMembers = "mitglieder";

    public const string TypeAll = "alle";
    public const string TypeExpenses = "ausgaben";
    public const string TypePayments = "zahlungen";

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    public Group? Group { get; private set; }

    public bool Found => Group is not null;

    public BalanceResult Balance { get; private set; } = new();

    public IReadOnlyList<TransactionRow> Transactions { get; private set; } = [];

    public IReadOnlyList<GroupMember> Members { get; private set; } = [];

    public string ActiveTab { get; private set; } = TabTransactions;

    public string ActiveType { get; private set; } = TypeAll;

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
        ActiveTab = NormalizeTab(Tab);
        ActiveType = NormalizeType(Type);

        Balance = await balances.CalculateBalancesAsync(group.Id, cancellationToken);
        Members = await groups.ListMembersAsync(group.Id, cancellationToken);

        var result = await transactions.ListTransactionsAsync(
            group.Id, kind: TypeToKind(ActiveType), page: 1, pageSize: 200, cancellationToken: cancellationToken);
        Transactions = result.Items;

        this.SetTitle(group.Name, "Nur-Lese-Ansicht", "group");
        return Page();
    }

    /// <summary>Url of a tab link, keeping the current type filter and the token.</summary>
    public string TabUrl(string tab)
        => tab == TabTransactions
            ? $"/View?token={Token}&tab={TabTransactions}&type={ActiveType}"
            : $"/View?token={Token}&tab={tab}";

    /// <summary>Url of a type filter pill inside the transactions tab.</summary>
    public string TypeUrl(string type)
        => $"/View?token={Token}&tab={TabTransactions}&type={type}";

    private static string NormalizeTab(string? tab)
        => string.Equals(tab, TabMembers, StringComparison.OrdinalIgnoreCase)
            ? TabMembers
            : TabTransactions;

    private static string NormalizeType(string? type) => type?.ToLowerInvariant() switch
    {
        TypeExpenses => TypeExpenses,
        TypePayments => TypePayments,
        _ => TypeAll
    };

    private static TransactionKind? TypeToKind(string type) => type switch
    {
        TypeExpenses => TransactionKind.Expense,
        TypePayments => TransactionKind.Payment,
        _ => null
    };
}
