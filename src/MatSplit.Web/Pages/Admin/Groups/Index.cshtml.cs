using System.Globalization;
using MatSplit.Web.Data;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Pages.Admin.Groups;

/// <summary>
/// Admin view over every group of the installation including owner, member
/// count and expense totals.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    GroupService groups,
    CurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>
    /// Current page. "page" is a reserved Razor Pages route value (it carries
    /// the page path), so model binding cannot be used for it - the value comes
    /// straight from the query string.
    /// </summary>
    public int PageNumber { get; private set; } = 1;

    [BindProperty(SupportsGet = true)]
    public bool IncludeDeleted { get; set; }

    public PagedResult<GroupRow> Result { get; private set; } = PagedResult<GroupRow>.Empty();

    public IReadOnlyList<SelectListItem> SortOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken cancellationToken)
    {
        PageNumber = ReadPageNumber();

        var result = await groups.SoftDeleteGroupAsync(id, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToList();
        }

        this.Flash("Die Gruppe wurde gelöscht.");
        return RedirectToList();
    }

    /// <summary>Url of the list including the current filter, {0} = page placeholder.</summary>
    public string BuildListUrl(int? page = null)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            parts.Add("search=" + Uri.EscapeDataString(Search));
        }

        if (!string.IsNullOrWhiteSpace(Sort))
        {
            parts.Add("sort=" + Uri.EscapeDataString(Sort));
        }

        if (IncludeDeleted)
        {
            parts.Add("includeDeleted=true");
        }

        parts.Add("page=" + (page?.ToString(CultureInfo.InvariantCulture) ?? "{0}"));

        return "/Admin/Groups?" + string.Join("&", parts);
    }

    private IActionResult RedirectToList() => Redirect(BuildListUrl(PageNumber));

    private int ReadPageNumber() => MsPaging.ReadPageNumber(Request);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        PageNumber = ReadPageNumber();

        var (page, pageSize) = Paging.Normalize(PageNumber, Paging.DefaultPageSize);
        PageNumber = page;

        var query = db.Groups.AsNoTracking().AsQueryable();

        if (!IncludeDeleted)
        {
            query = query.Where(x => x.UpdateState != UpdateState.Deleted);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.Name, "%" + term + "%")
                || (x.Description != null && EF.Functions.Like(x.Description, "%" + term + "%")));
        }

        query = Sort switch
        {
            "name_desc" => query.OrderByDescending(x => x.Name),
            "created" => query.OrderBy(x => x.CreateDate),
            "created_desc" => query.OrderByDescending(x => x.CreateDate),
            _ => query.OrderBy(x => x.Name)
        };

        var total = await query.CountAsync(cancellationToken);

        // Never show an empty page behind the last one (bookmarked page numbers).
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        if (page > totalPages)
        {
            page = totalPages;
            PageNumber = page;
        }

        var rows = await query
            .Skip(Paging.Skip(page, pageSize))
            .Take(pageSize)
            .Select(x => new GroupRow
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                Currency = x.Currency,
                InviteEnabled = x.InviteEnabled,
                IsDeleted = x.UpdateState == UpdateState.Deleted,
                CreateDate = x.CreateDate,
                CreatorName = db.Users
                    .Where(u => u.Id == x.CreateUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        await FillStatisticsAsync(rows, cancellationToken);

        Result = new PagedResult<GroupRow>(rows, page, pageSize, total);

        var sort = string.IsNullOrWhiteSpace(Sort) ? "name" : Sort;

        SortOptions =
        [
            new SelectListItem("Name (A-Z)", "name", sort == "name"),
            new SelectListItem("Name (Z-A)", "name_desc", sort == "name_desc"),
            new SelectListItem("Neueste zuerst", "created_desc", sort == "created_desc"),
            new SelectListItem("Älteste zuerst", "created", sort == "created")
        ];

        var myGroups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId(), cancellationToken: cancellationToken);
        this.SetMenuGroups(myGroups.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        this.SetTitle("Gruppen", "Alle Gruppen dieser Installation", "group");
        this.SetBreadcrumb(
            new BreadcrumbItem("Administration", "/Admin"),
            new BreadcrumbItem("Gruppen"));
    }

    private async Task FillStatisticsAsync(List<GroupRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var ids = rows.Select(x => x.Id).ToList();

        var members = await db.GroupMembers
            .AsNoTracking()
            .Where(x => ids.Contains(x.GroupId) && x.UpdateState != UpdateState.Deleted)
            .GroupBy(x => x.GroupId)
            .Select(x => new { GroupId = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(x => ids.Contains(x.GroupId) && x.UpdateState != UpdateState.Deleted)
            .GroupBy(x => x.GroupId)
            .Select(x => new { GroupId = x.Key, Count = x.Count(), Total = x.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);

        var payments = await db.Payments
            .AsNoTracking()
            .Where(x => ids.Contains(x.GroupId) && x.UpdateState != UpdateState.Deleted)
            .GroupBy(x => x.GroupId)
            .Select(x => new { GroupId = x.Key, Count = x.Count(), Total = x.Sum(p => p.AmountCents) })
            .ToListAsync(cancellationToken);

        var admins = await db.GroupMembers
            .AsNoTracking()
            .Where(x => ids.Contains(x.GroupId) && x.IsGroupAdmin && x.UpdateState != UpdateState.Deleted)
            .OrderBy(x => x.Id)
            .Select(x => new { x.GroupId, DisplayName = x.User!.DisplayName })
            .ToListAsync(cancellationToken);

        var memberLookup = members.ToDictionary(x => x.GroupId, x => x.Count);
        var expenseLookup = expenses.ToDictionary(x => x.GroupId, x => x);
        var paymentLookup = payments.ToDictionary(x => x.GroupId, x => x);
        var adminLookup = admins
            .GroupBy(x => x.GroupId)
            .ToDictionary(x => x.Key, x => x.Select(entry => entry.DisplayName).ToList());

        foreach (var row in rows)
        {
            row.MemberCount = memberLookup.TryGetValue(row.Id, out var memberCount) ? memberCount : 0;

            if (expenseLookup.TryGetValue(row.Id, out var expense))
            {
                row.ExpenseCount = expense.Count;
                row.ExpenseTotalCents = expense.Total;
            }

            if (paymentLookup.TryGetValue(row.Id, out var payment))
            {
                row.PaymentCount = payment.Count;
                row.PaymentTotalCents = payment.Total;
            }

            if (adminLookup.TryGetValue(row.Id, out var adminNames))
            {
                row.AdminNames = adminNames;
            }
        }
    }

    /// <summary>One row of the admin group list.</summary>
    public sealed class GroupRow
    {
        public long Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string Currency { get; init; } = "EUR";

        public bool InviteEnabled { get; init; }

        public bool IsDeleted { get; init; }

        public DateTime CreateDate { get; init; }

        public string? CreatorName { get; init; }

        public IReadOnlyList<string> AdminNames { get; set; } = [];

        public int MemberCount { get; set; }

        public int ExpenseCount { get; set; }

        public long ExpenseTotalCents { get; set; }

        public int PaymentCount { get; set; }

        public long PaymentTotalCents { get; set; }

        /// <summary>Creator of the group, falling back to the first group admin.</summary>
        public string OwnerText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CreatorName))
                {
                    return CreatorName;
                }

                return AdminNames.Count > 0 ? AdminNames[0] : "–";
            }
        }

        public string AdminsText => AdminNames.Count == 0 ? "–" : string.Join(", ", AdminNames);
    }
}
