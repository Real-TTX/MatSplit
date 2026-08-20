using System.Globalization;
using MatSplit.Web.Data;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Pages.Admin.Users;

/// <summary>
/// Admin user list with search, role filter, account type filter, sorting and
/// paging. Creating, merging and deleting happens on the sub pages.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    UserService users,
    GroupService groups,
    CurrentUserService currentUser) : PageModel
{
    public const string KindLocal = "local";

    public const string KindAnonymous = "anonymous";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public UserRole? Role { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

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

    public PagedResult<UserRow> Result { get; private set; } = PagedResult<UserRow>.Empty();

    public long CurrentUserId { get; private set; }

    public IReadOnlyList<SelectListItem> RoleOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> KindOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> SortOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken cancellationToken)
    {
        PageNumber = ReadPageNumber();

        if (id == currentUser.UserId)
        {
            this.FlashError("Das eigene Konto kann hier nicht gelöscht werden.");
            return RedirectToList();
        }

        var result = await users.SoftDeleteUserAsync(id, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToList();
        }

        this.Flash("Der Benutzer wurde gelöscht.");
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

        if (Role.HasValue)
        {
            parts.Add("role=" + Role.Value);
        }

        if (!string.IsNullOrWhiteSpace(Kind))
        {
            parts.Add("kind=" + Uri.EscapeDataString(Kind));
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

        return "/Admin/Users?" + string.Join("&", parts);
    }

    public static string RoleText(UserRole role) => role switch
    {
        UserRole.Admin => "Administrator",
        UserRole.User => "Benutzer",
        _ => "Anonym"
    };

    private IActionResult RedirectToList() => Redirect(BuildListUrl(PageNumber));

    private int ReadPageNumber() => MsPaging.ReadPageNumber(Request);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CurrentUserId = currentUser.UserId ?? 0L;
        PageNumber = ReadPageNumber();

        var (page, pageSize) = Paging.Normalize(PageNumber, Paging.DefaultPageSize);
        PageNumber = page;

        var query = db.Users.AsNoTracking().AsQueryable();

        if (!IncludeDeleted)
        {
            query = query.Where(x => x.UpdateState != UpdateState.Deleted);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.DisplayName, "%" + term + "%")
                || (x.Email != null && EF.Functions.Like(x.Email, "%" + term + "%")));
        }

        if (Role.HasValue)
        {
            var role = Role.Value;
            query = query.Where(x => x.Role == role);
        }

        if (string.Equals(Kind, KindAnonymous, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.IsAnonymous);
        }
        else if (string.Equals(Kind, KindLocal, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => !x.IsAnonymous);
        }

        query = Sort switch
        {
            "name_desc" => query.OrderByDescending(x => x.DisplayName),
            "email" => query.OrderBy(x => x.Email).ThenBy(x => x.DisplayName),
            "email_desc" => query.OrderByDescending(x => x.Email).ThenBy(x => x.DisplayName),
            "role" => query.OrderBy(x => x.Role).ThenBy(x => x.DisplayName),
            "role_desc" => query.OrderByDescending(x => x.Role).ThenBy(x => x.DisplayName),
            "created" => query.OrderBy(x => x.CreateDate),
            "created_desc" => query.OrderByDescending(x => x.CreateDate),
            _ => query.OrderBy(x => x.DisplayName)
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
            .Select(x => new UserRow
            {
                Id = x.Id,
                DisplayName = x.DisplayName,
                Email = x.Email,
                Role = x.Role,
                IsAnonymous = x.IsAnonymous,
                HasPassword = x.PasswordHash != null,
                HasPayPal = x.PayPalAddress != null,
                IsDeleted = x.UpdateState == UpdateState.Deleted,
                MergedIntoUserId = x.MergedIntoUserId,
                CreateDate = x.CreateDate
            })
            .ToListAsync(cancellationToken);

        await FillGroupCountsAsync(rows, cancellationToken);

        Result = new PagedResult<UserRow>(rows, page, pageSize, total);

        BuildFilterOptions();

        var myGroups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId(), cancellationToken: cancellationToken);
        this.SetMenuGroups(myGroups.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        this.SetTitle("Benutzer", "Konten, Rollen und Zusammenführungen", "users");
        this.SetBreadcrumb(
            new BreadcrumbItem("Administration", "/Admin"),
            new BreadcrumbItem("Benutzer"));
    }

    private async Task FillGroupCountsAsync(List<UserRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var ids = rows.Select(x => x.Id).ToList();

        var counts = await db.GroupMembers
            .AsNoTracking()
            .Where(x => ids.Contains(x.UserId) && x.UpdateState != UpdateState.Deleted)
            .GroupBy(x => x.UserId)
            .Select(x => new { UserId = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var lookup = counts.ToDictionary(x => x.UserId, x => x.Count);

        foreach (var row in rows)
        {
            row.GroupCount = lookup.TryGetValue(row.Id, out var count) ? count : 0;
        }
    }

    private void BuildFilterOptions()
    {
        RoleOptions =
        [
            new SelectListItem("Administrator", nameof(UserRole.Admin), Role == UserRole.Admin),
            new SelectListItem("Benutzer", nameof(UserRole.User), Role == UserRole.User),
            new SelectListItem("Anonym", nameof(UserRole.Anonymous), Role == UserRole.Anonymous)
        ];

        KindOptions =
        [
            new SelectListItem("Lokales Konto", KindLocal, string.Equals(Kind, KindLocal, StringComparison.OrdinalIgnoreCase)),
            new SelectListItem("Anonymer Zugang", KindAnonymous, string.Equals(Kind, KindAnonymous, StringComparison.OrdinalIgnoreCase))
        ];

        var sort = string.IsNullOrWhiteSpace(Sort) ? "name" : Sort;

        SortOptions =
        [
            new SelectListItem("Name (A-Z)", "name", sort == "name"),
            new SelectListItem("Name (Z-A)", "name_desc", sort == "name_desc"),
            new SelectListItem("E-Mail (A-Z)", "email", sort == "email"),
            new SelectListItem("E-Mail (Z-A)", "email_desc", sort == "email_desc"),
            new SelectListItem("Rolle (aufsteigend)", "role", sort == "role"),
            new SelectListItem("Rolle (absteigend)", "role_desc", sort == "role_desc"),
            new SelectListItem("Neueste zuerst", "created_desc", sort == "created_desc"),
            new SelectListItem("Älteste zuerst", "created", sort == "created")
        ];
    }

    /// <summary>One row of the admin user list.</summary>
    public sealed class UserRow
    {
        public long Id { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public UserRole Role { get; init; }

        public bool IsAnonymous { get; init; }

        public bool HasPassword { get; init; }

        public bool HasPayPal { get; init; }

        public bool IsDeleted { get; init; }

        public long? MergedIntoUserId { get; init; }

        public DateTime CreateDate { get; init; }

        public int GroupCount { get; set; }

        public string RoleLabel => RoleText(Role);

        public string KindLabel => IsAnonymous ? "Anonym" : "Lokal";
    }
}
