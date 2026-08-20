using System.Globalization;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups.Payments;

/// <summary>
/// Payment list of one group: who handed how much money to whom.
/// Filtering, sorting and paging happen server side through query parameters.
/// </summary>
public class IndexModel(
    CurrentUserService currentUser,
    GroupService groups,
    PaymentService payments) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

    [BindProperty(SupportsGet = true, Name = "search")]
    public string? Search { get; set; }

    /// <summary>Filters payments where the member is payer or receiver.</summary>
    [BindProperty(SupportsGet = true, Name = "memberUserId")]
    public long? MemberUserId { get; set; }

    [BindProperty(SupportsGet = true, Name = "fromDate")]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "toDate")]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true, Name = "sort")]
    public string? Sort { get; set; }

    /// <summary>
    /// 1-based page number from <c>?page=</c>, read by hand because Razor Pages
    /// owns the route value "page" (see <see cref="MsPaging"/>).
    /// </summary>
    public int PageNumber => MsPaging.ReadPageNumber(Request);

    public Group Group { get; private set; } = new();

    public PagedResult<Payment> Result { get; private set; } = PagedResult<Payment>.Empty();

    /// <summary>Sum of all payments of the group, not only of the current page.</summary>
    public long TotalCents { get; private set; }

    public bool CanManage { get; private set; }

    public List<SelectListItem> MemberOptions { get; } = [];

    public List<SelectListItem> SortOptions { get; } = [];

    public string Currency => string.IsNullOrWhiteSpace(Group.Currency) ? "EUR" : Group.Currency;

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

        Result = await payments.ListPaymentsAsync(
            GroupId,
            Search,
            MemberUserId,
            FromDate,
            ToDate,
            PageNumber,
            Paging.DefaultPageSize,
            Sort,
            cancellationToken);

        TotalCents = await payments.GetTotalCentsAsync(GroupId, cancellationToken);

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

        if (MemberUserId is > 0)
        {
            parts.Add("memberUserId=" + MemberUserId.Value.ToString(CultureInfo.InvariantCulture));
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
        return "/Groups/Payments?" + string.Join('&', parts);
    }

    /// <summary>Display name of a member, en dash when the user is missing.</summary>
    public string MemberName(User? user)
        => string.IsNullOrWhiteSpace(user?.DisplayName) ? "\u2013" : user!.DisplayName;

    /// <summary>German date without a culture dependency (dd.MM.yyyy).</summary>
    public string FormatDate(DateTime value)
        => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private async Task BuildOptionsAsync(CancellationToken cancellationToken)
    {
        var members = await groups.ListMembersAsync(GroupId, cancellationToken);

        foreach (var member in members)
        {
            MemberOptions.Add(new SelectListItem
            {
                Value = member.UserId.ToString(CultureInfo.InvariantCulture),
                Text = member.User?.DisplayName ?? "Benutzer " + member.UserId.ToString(CultureInfo.InvariantCulture),
                Selected = MemberUserId == member.UserId
            });
        }

        AddSort("date_desc", "Datum (neueste zuerst)");
        AddSort("date", "Datum (\u00e4lteste zuerst)");
        AddSort("amount_desc", "Betrag (absteigend)");
        AddSort("amount", "Betrag (aufsteigend)");
        AddSort("from", "Von (A-Z)");
        AddSort("from_desc", "Von (Z-A)");
        AddSort("to", "An (A-Z)");
        AddSort("to_desc", "An (Z-A)");
    }

    private void AddSort(string value, string text)
    {
        var current = string.IsNullOrWhiteSpace(Sort) ? "date_desc" : Sort!;

        SortOptions.Add(new SelectListItem
        {
            Value = value,
            Text = text,
            Selected = string.Equals(current, value, StringComparison.OrdinalIgnoreCase)
        });
    }

    private async Task ApplyLayoutAsync(CancellationToken cancellationToken)
    {
        this.SetTitle("Zahlungen", Group.Name, "paypal");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(Group.Name, $"/Groups/Details?groupId={GroupId}"),
            new BreadcrumbItem("Zahlungen"));

        var userId = currentUser.RequireUserId();
        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, GroupId);
    }
}
