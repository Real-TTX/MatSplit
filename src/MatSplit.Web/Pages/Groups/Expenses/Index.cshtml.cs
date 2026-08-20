using System.Globalization;
using System.Text;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups.Expenses;

/// <summary>
/// Expense list of a single group: filter toolbar, totals row, pagination,
/// csv export and a bulk soft delete for group admins.
/// </summary>
public sealed class IndexModel(
    CurrentUserService currentUser,
    GroupService groups,
    ExpenseService expenses) : PageModel
{
    /// <summary>Upper bound so a csv export can never run away.</summary>
    private const int ExportRowLimit = 5000;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [BindProperty(SupportsGet = true)]
    public long GroupId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? PayerUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>
    /// 1-based page number from <c>?page=</c>. Read from the query string by
    /// hand: Razor Pages owns the route value "page" (it holds the page path),
    /// so a bound property named "page" would receive that path and add a
    /// model error instead.
    /// </summary>
    public int PageNumber => MsPaging.ReadPageNumber(Request);

    public Group Group { get; private set; } = new();

    public PagedResult<Expense> Result { get; private set; } = PagedResult<Expense>.Empty();

    public IReadOnlyList<ExpenseListRow> Rows { get; private set; } = [];

    /// <summary>Sum of the rows currently visible.</summary>
    public long PageTotalCents { get; private set; }

    /// <summary>Sum of all (undeleted) expenses of the group.</summary>
    public long GroupTotalCents { get; private set; }

    public bool CanManage { get; private set; }

    public IReadOnlyList<SelectListItem> MemberOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> SortOptions { get; private set; } = [];

    public string Currency => string.IsNullOrWhiteSpace(Group.Currency) ? "EUR" : Group.Currency;

    /// <summary>Query part ("and search=...") used for pagination and export links.</summary>
    public string FilterQuery { get; private set; } = string.Empty;

    public string ListUrl => $"/Groups/Expenses?groupId={GroupId}";

    public string ExportUrl => $"{ListUrl}{FilterQuery}&handler=Export";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadAsync(cancellationToken);
        return guard ?? Page();
    }

    /// <summary>Bulk soft delete of the selected rows, group admins only.</summary>
    public async Task<IActionResult> OnPostDeleteSelectedAsync(long[] selectedIds, CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return NotFound();
        }

        if (!await currentUser.CanManageGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        if (selectedIds is null || selectedIds.Length == 0)
        {
            this.FlashError("Bitte zuerst mindestens eine Ausgabe auswählen.");
            return RedirectToFilteredList();
        }

        var deleted = 0;
        string? lastError = null;

        foreach (var id in selectedIds.Distinct())
        {
            var expense = await expenses.GetExpenseAsync(id, cancellationToken);
            if (expense is null || expense.GroupId != GroupId)
            {
                continue;
            }

            var result = await expenses.SoftDeleteExpenseAsync(id, cancellationToken);
            if (result.IsSuccess)
            {
                deleted++;
                continue;
            }

            lastError = result.Error;
        }

        if (deleted == 0)
        {
            this.FlashError(lastError ?? "Es wurde keine Ausgabe gelöscht.");
            return RedirectToFilteredList();
        }

        this.Flash(deleted == 1
            ? "Die Ausgabe wurde gelöscht."
            : $"{deleted} Ausgaben wurden gelöscht.");

        return RedirectToFilteredList();
    }

    /// <summary>Exports the current filter result as semicolon separated csv.</summary>
    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return NotFound();
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

        var items = new List<Expense>();
        var page = 1;

        while (items.Count < ExportRowLimit)
        {
            var chunk = await expenses.ListExpensesAsync(
                GroupId, Search, PayerUserId, FromDate, ToDate, page, Paging.MaxPageSize, Sort, cancellationToken);

            items.AddRange(chunk.Items);

            if (chunk.Items.Count == 0 || !chunk.HasNextPage)
            {
                break;
            }

            page++;
        }

        var rows = BuildRows(items.Take(ExportRowLimit).ToList());
        var payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(BuildCsv(rows));
        var fileName = $"ausgaben-{FileNamePart(group.Name)}-{DateTime.UtcNow:yyyyMMdd}.csv";

        return File(payload, "text/csv", fileName);
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        if (GroupId <= 0)
        {
            return NotFound();
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

        var members = await groups.ListMembersAsync(GroupId, cancellationToken);
        MemberOptions = members
            .Select(m => new SelectListItem(
                m.User?.DisplayName ?? $"#{m.UserId}",
                m.UserId.ToString(CultureInfo.InvariantCulture),
                PayerUserId == m.UserId))
            .ToList();

        SortOptions = BuildSortOptions();

        Result = await expenses.ListExpensesAsync(
            GroupId, Search, PayerUserId, FromDate, ToDate, PageNumber, Paging.DefaultPageSize, Sort, cancellationToken);

        Rows = BuildRows(Result.Items);
        PageTotalCents = Rows.Sum(x => x.AmountCents);
        GroupTotalCents = await expenses.GetTotalCentsAsync(GroupId, cancellationToken);
        FilterQuery = BuildFilterQuery();

        var menu = await GroupMenu.BuildAsync(groups, currentUser, currentUser.RequireUserId(), cancellationToken);

        this.SetTitle("Ausgaben", Group.Name, "expense");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(Group.Name, $"/Groups/Details?groupId={GroupId}"),
            new BreadcrumbItem("Ausgaben"));
        this.SetMenuGroups(menu, GroupId);

        return null;
    }

    /// <summary>
    /// Maps the loaded entities to view rows. ExpenseService.ListExpensesAsync
    /// already includes payer, shares (with user) and receipts, so this needs no
    /// further database round trip.
    /// </summary>
    private List<ExpenseListRow> BuildRows(IReadOnlyList<Expense> items)
    {
        var rows = new List<ExpenseListRow>(items.Count);

        foreach (var item in items)
        {
            var shares = item.Shares.ToList();

            rows.Add(new ExpenseListRow
            {
                Id = item.Id,
                ExpenseDate = item.ExpenseDate,
                Description = item.Description,
                Category = item.Category,
                AmountCents = item.AmountCents,
                Currency = string.IsNullOrWhiteSpace(item.Currency) ? Currency : item.Currency,
                PaidByName = item.PaidByUser?.DisplayName ?? $"#{item.PaidByUserId}",
                ParticipantCount = shares.Count,
                Participants = shares.Count == 0
                    ? "Alle Mitglieder"
                    : string.Join(", ", shares.Select(DescribeShare)),
                ReceiptCount = item.Receipts.Count
            });
        }

        return rows;
    }

    private static string DescribeShare(ExpenseShare share)
    {
        var name = share.User?.DisplayName ?? $"#{share.UserId}";

        if (share.ShareAmountCents.HasValue)
        {
            return $"{name} ({MsHtml.FormatMoney(share.ShareAmountCents.Value)})";
        }

        return share.ShareFactor > 1 ? $"{name} (Faktor {share.ShareFactor})" : name;
    }

    private IReadOnlyList<SelectListItem> BuildSortOptions()
    {
        var current = string.IsNullOrWhiteSpace(Sort) ? "date_desc" : Sort!;

        var options = new (string Value, string Text)[]
        {
            ("date_desc", "Datum (neueste zuerst)"),
            ("date", "Datum (älteste zuerst)"),
            ("amount_desc", "Betrag (absteigend)"),
            ("amount", "Betrag (aufsteigend)"),
            ("description", "Beschreibung (A-Z)"),
            ("description_desc", "Beschreibung (Z-A)"),
            ("payer", "Bezahlt von (A-Z)"),
            ("payer_desc", "Bezahlt von (Z-A)")
        };

        return options
            .Select(x => new SelectListItem(x.Text, x.Value, string.Equals(x.Value, current, StringComparison.Ordinal)))
            .ToList();
    }

    private string BuildFilterQuery()
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            builder.Append("&search=").Append(Uri.EscapeDataString(Search!.Trim()));
        }

        if (PayerUserId is > 0)
        {
            builder.Append("&payerUserId=").Append(PayerUserId.Value);
        }

        if (FromDate.HasValue)
        {
            builder.Append("&fromDate=").Append(FromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (ToDate.HasValue)
        {
            builder.Append("&toDate=").Append(ToDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            builder.Append("&sort=").Append(Uri.EscapeDataString(Sort!));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Post-redirect-get back to the filtered list. The url is built by hand
    /// because "page" is a reserved route value in razor pages - passing it to
    /// RedirectToPage would look for a page named "1".
    /// </summary>
    private IActionResult RedirectToFilteredList()
    {
        var page = PageNumber < 1 ? 1 : PageNumber;
        return LocalRedirect($"{ListUrl}{BuildFilterQuery()}&page={page.ToString(CultureInfo.InvariantCulture)}");
    }

    private string BuildCsv(IReadOnlyList<ExpenseListRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sep=;");
        builder.AppendLine(string.Join(';',
            "Datum",
            "Beschreibung",
            "Kategorie",
            "Bezahlt von",
            "Betrag",
            "Währung",
            "Beteiligte",
            "Anzahl Beteiligte",
            "Belege"));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';',
                Csv(row.ExpenseDate.ToString("dd.MM.yyyy", German)),
                Csv(row.Description),
                Csv(row.Category),
                Csv(row.PaidByName),
                Csv((row.AmountCents / 100m).ToString("0.00", German)),
                Csv(row.Currency),
                Csv(row.Participants),
                Csv(row.ParticipantCount.ToString(CultureInfo.InvariantCulture)),
                Csv(row.ReceiptCount.ToString(CultureInfo.InvariantCulture))));
        }

        builder.AppendLine(string.Join(';',
            Csv("Summe"),
            string.Empty,
            string.Empty,
            string.Empty,
            Csv((rows.Sum(x => x.AmountCents) / 100m).ToString("0.00", German)),
            Csv(Currency),
            string.Empty,
            string.Empty,
            string.Empty));

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string FileNamePart(string value)
    {
        var cleaned = new string(value
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray())
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        return cleaned.Length == 0 ? "gruppe" : cleaned;
    }
}

/// <summary>One rendered row of the expense list (and of the csv export).</summary>
public sealed class ExpenseListRow
{
    public long Id { get; init; }

    public DateTime ExpenseDate { get; init; }

    public string Description { get; init; } = string.Empty;

    public string? Category { get; init; }

    public long AmountCents { get; init; }

    public string Currency { get; init; } = "EUR";

    public string PaidByName { get; init; } = string.Empty;

    public string Participants { get; init; } = string.Empty;

    public int ParticipantCount { get; init; }

    public int ReceiptCount { get; init; }
}
