using MatSplit.Web.Data;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Pages.Admin.Users;

/// <summary>
/// Merges a duplicated account into the surviving one (the "Horst / Horsti"
/// case). Reachable from the member list of a group as
/// /Admin/Users/Merge?sourceId=..&amp;targetId=..
/// </summary>
public sealed class MergeModel(
    AppDbContext db,
    UserService users,
    GroupService groups,
    AppConfigService appConfig,
    CurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? SourceId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? TargetId { get; set; }

    public IReadOnlyList<SelectListItem> SourceOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> TargetOptions { get; private set; } = [];

    public MergePreview? Source { get; private set; }

    public MergePreview? Target { get; private set; }

    public string? Problem { get; private set; }

    public string Currency => appConfig.Current.DefaultCurrency;

    public bool CanMerge => Source is not null && Target is not null && Problem is null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        if (!CanMerge)
        {
            this.FlashError(Problem ?? "Bitte Quelle und Ziel auswählen.");
            return Page();
        }

        var result = await users.MergeUsersAsync(SourceId!.Value, TargetId!.Value, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return Page();
        }

        this.Flash($"„{Source!.DisplayName}“ wurde in „{Target!.DisplayName}“ zusammengeführt.");
        return RedirectToPage("./Index");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var all = await users.ListAllActiveUsersAsync(cancellationToken);

        SourceOptions = all
            .Select(x => new SelectListItem(Describe(x.DisplayName, x.Email, x.IsAnonymous), x.Id.ToString(), x.Id == SourceId))
            .ToList();

        TargetOptions = all
            .Select(x => new SelectListItem(Describe(x.DisplayName, x.Email, x.IsAnonymous), x.Id.ToString(), x.Id == TargetId))
            .ToList();

        if (SourceId is > 0)
        {
            Source = await BuildPreviewAsync(SourceId.Value, cancellationToken);
        }

        if (TargetId is > 0)
        {
            Target = await BuildPreviewAsync(TargetId.Value, cancellationToken);
        }

        Problem = ResolveProblem();

        var myGroups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId(), cancellationToken: cancellationToken);
        this.SetMenuGroups(myGroups.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        this.SetTitle("Benutzer zusammenführen", "Doppelte Konten zu einem verschmelzen", "merge");
        this.SetBreadcrumb(
            new BreadcrumbItem("Administration", "/Admin"),
            new BreadcrumbItem("Benutzer", "/Admin/Users"),
            new BreadcrumbItem("Zusammenführen"));
    }

    private string? ResolveProblem()
    {
        if (SourceId is null or 0 || TargetId is null or 0)
        {
            return null;
        }

        if (SourceId == TargetId)
        {
            return "Quelle und Ziel dürfen nicht identisch sein.";
        }

        if (Source is null)
        {
            return "Der Quell-Benutzer wurde nicht gefunden.";
        }

        if (Target is null)
        {
            return "Der Ziel-Benutzer wurde nicht gefunden.";
        }

        if (SourceId == currentUser.UserId)
        {
            return "Das eigene Konto kann nicht als Quelle verwendet werden.";
        }

        return null;
    }

    private async Task<MergePreview?> BuildPreviewAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var groupNames = await db.GroupMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.UpdateState != UpdateState.Deleted)
            .OrderBy(x => x.Group!.Name)
            .Select(x => x.Group!.Name)
            .ToListAsync(cancellationToken);

        var expenseCount = await db.Expenses.CountAsync(
            x => x.PaidByUserId == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        var shareCount = await db.ExpenseShares.CountAsync(
            x => x.UserId == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        var paymentCount = await db.Payments.CountAsync(
            x => (x.FromUserId == userId || x.ToUserId == userId) && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        var expenseTotalCents = await db.Expenses
            .Where(x => x.PaidByUserId == userId && x.UpdateState != UpdateState.Deleted)
            .SumAsync(x => (long?)x.AmountCents, cancellationToken) ?? 0L;

        return new MergePreview
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            IsAnonymous = user.IsAnonymous,
            PayPalAddress = user.PayPalAddress,
            GroupNames = groupNames,
            ExpenseCount = expenseCount,
            ExpenseTotalCents = expenseTotalCents,
            ShareCount = shareCount,
            PaymentCount = paymentCount
        };
    }

    private static string Describe(string displayName, string? email, bool isAnonymous)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return displayName + " (" + email + ")";
        }

        return isAnonymous ? displayName + " (anonym)" : displayName;
    }

    /// <summary>Everything that would move during a merge.</summary>
    public sealed class MergePreview
    {
        public long UserId { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public UserRole Role { get; init; }

        public bool IsAnonymous { get; init; }

        public string? PayPalAddress { get; init; }

        public IReadOnlyList<string> GroupNames { get; init; } = [];

        public int GroupCount => GroupNames.Count;

        public int ExpenseCount { get; init; }

        public long ExpenseTotalCents { get; init; }

        public int ShareCount { get; init; }

        public int PaymentCount { get; init; }

        public string RoleLabel => Role switch
        {
            UserRole.Admin => "Administrator",
            UserRole.User => "Benutzer",
            _ => "Anonym"
        };

        public string GroupsText => GroupNames.Count == 0 ? "–" : string.Join(", ", GroupNames);
    }
}
