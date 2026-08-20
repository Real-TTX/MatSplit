using System.ComponentModel.DataAnnotations;
using MatSplit.Web.Data;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Pages.Admin.Users;

/// <summary>
/// Create (id missing) or edit (id given) a single account. Role, anonymous
/// flag, PayPal handle and an optional new password are maintained here.
/// </summary>
public sealed class EditModel(
    AppDbContext db,
    UserService users,
    GroupService groups,
    HistoryService history,
    CurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsNew => Input.Id <= 0;

    public bool IsSelf { get; private set; }

    public bool IsDeletedAccount { get; private set; }

    public int GroupCount { get; private set; }

    public int ExpenseCount { get; private set; }

    public int PaymentCount { get; private set; }

    public int ActiveSessionCount { get; private set; }

    public string? Token { get; private set; }

    public DateTime? CreateDate { get; private set; }

    public DateTime? UpdateDate { get; private set; }

    public string? MergedIntoName { get; private set; }

    public IReadOnlyList<SelectListItem> RoleOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is > 0)
        {
            var user = await users.GetByIdAsync(Id.Value, cancellationToken);

            if (user is null)
            {
                this.FlashError("Der Benutzer wurde nicht gefunden.");
                return RedirectToPage("./Index");
            }

            Input = new InputModel
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                Email = user.Email,
                Role = user.Role,
                PayPalAddress = user.PayPalAddress,
                IsAnonymous = user.IsAnonymous,
                SetPassword = false
            };
        }
        else
        {
            Input = new InputModel
            {
                Id = 0,
                Role = UserRole.User,
                SetPassword = true
            };
        }

        await PreparePageAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.SetPassword && string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            ModelState.AddModelError("Input.NewPassword", "Bitte ein Passwort angeben.");
        }
        else if (Input.SetPassword && Input.NewPassword!.Length < 4)
        {
            ModelState.AddModelError("Input.NewPassword", "Das Passwort muss mindestens 4 Zeichen haben.");
        }

        if (!ModelState.IsValid)
        {
            await PreparePageAsync(cancellationToken);
            return Page();
        }

        var password = Input.SetPassword ? Input.NewPassword : null;
        var role = Input.IsAnonymous ? UserRole.Anonymous : Input.Role;

        return Input.Id <= 0
            ? await CreateAsync(role, password, cancellationToken)
            : await UpdateAsync(Input.Id, role, password, cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var userId = Input.Id > 0 ? Input.Id : Id ?? 0L;

        if (userId <= 0)
        {
            this.FlashError("Der Benutzer wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        if (userId == currentUser.UserId)
        {
            this.FlashError("Das eigene Konto kann nicht gelöscht werden.");
            return RedirectToPage("./Edit", new { id = userId });
        }

        var result = await users.SoftDeleteUserAsync(userId, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToPage("./Edit", new { id = userId });
        }

        this.Flash("Der Benutzer wurde gelöscht.");
        return RedirectToPage("./Index");
    }

    private async Task<IActionResult> CreateAsync(UserRole role, string? password, CancellationToken cancellationToken)
    {
        var created = await users.CreateLocalUserAsync(Input.DisplayName, Input.Email, password, role, cancellationToken);

        if (created.IsFailure)
        {
            ModelState.AddModelError(string.Empty, created.Error!);
            await PreparePageAsync(cancellationToken);
            return Page();
        }

        var user = created.Value;

        // CreateLocalUserAsync always creates a password account; SetRoleAsync is
        // the only place that maintains the IsAnonymous flag.
        if (role == UserRole.Anonymous)
        {
            await users.SetRoleAsync(user.Id, UserRole.Anonymous, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(Input.PayPalAddress))
        {
            var profile = await users.UpdateProfileAsync(
                user.Id, Input.DisplayName, Input.Email, Input.PayPalAddress, null, cancellationToken);

            if (profile.IsFailure)
            {
                ModelState.AddModelError(string.Empty, profile.Error!);
                Input.Id = user.Id;
                await PreparePageAsync(cancellationToken);
                return Page();
            }
        }

        this.Flash($"Der Benutzer „{user.DisplayName}“ wurde angelegt.");
        return RedirectToPage("./Index");
    }

    private async Task<IActionResult> UpdateAsync(long userId, UserRole role, string? password, CancellationToken cancellationToken)
    {
        var existing = await users.GetByIdAsync(userId, cancellationToken);

        if (existing is null)
        {
            this.FlashError("Der Benutzer wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        var profile = await users.UpdateProfileAsync(
            userId, Input.DisplayName, Input.Email, Input.PayPalAddress, password, cancellationToken);

        if (profile.IsFailure)
        {
            ModelState.AddModelError(string.Empty, profile.Error!);
            await PreparePageAsync(cancellationToken);
            return Page();
        }

        var roleChanged = existing.Role != role || existing.IsAnonymous != (role == UserRole.Anonymous);

        if (roleChanged)
        {
            var roleResult = await users.SetRoleAsync(userId, role, cancellationToken);

            if (roleResult.IsFailure)
            {
                ModelState.AddModelError(string.Empty, roleResult.Error!);
                await PreparePageAsync(cancellationToken);
                return Page();
            }

            await history.LogAsync(
                null,
                currentUser.UserId,
                HistoryService.EntityTypes.User,
                userId,
                HistoryService.Actions.Updated,
                $"Rolle von „{Input.DisplayName}“ wurde auf „{RoleText(role)}“ gesetzt.",
                cancellationToken: cancellationToken);
        }

        if (userId == currentUser.UserId)
        {
            await currentUser.RefreshSignInAsync(cancellationToken);
        }

        this.Flash($"Der Benutzer „{Input.DisplayName}“ wurde gespeichert.");
        return RedirectToPage("./Index");
    }

    private static string RoleText(UserRole role) => role switch
    {
        UserRole.Admin => "Administrator",
        UserRole.User => "Benutzer",
        _ => "Anonym"
    };

    private async Task PreparePageAsync(CancellationToken cancellationToken)
    {
        RoleOptions =
        [
            new SelectListItem("Administrator", nameof(UserRole.Admin)),
            new SelectListItem("Benutzer", nameof(UserRole.User)),
            new SelectListItem("Anonym", nameof(UserRole.Anonymous))
        ];

        IsSelf = Input.Id > 0 && Input.Id == currentUser.UserId;

        if (Input.Id > 0)
        {
            await LoadDetailsAsync(Input.Id, cancellationToken);
        }

        var myGroups = await groups.ListGroupsForUserAsync(currentUser.RequireUserId(), cancellationToken: cancellationToken);
        this.SetMenuGroups(myGroups.Select(x => new MenuGroupEntry(x.Id, x.Name)));

        var title = IsNew ? "Neuer Benutzer" : Input.DisplayName;
        this.SetTitle(title, IsNew ? "Konto anlegen" : "Konto bearbeiten", "user");
        this.SetBreadcrumb(
            new BreadcrumbItem("Administration", "/Admin"),
            new BreadcrumbItem("Benutzer", "/Admin/Users"),
            new BreadcrumbItem(title));
    }

    private async Task LoadDetailsAsync(long userId, CancellationToken cancellationToken)
    {
        var info = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Token,
                x.CreateDate,
                x.UpdateDate,
                x.MergedIntoUserId,
                IsDeleted = x.UpdateState == UpdateState.Deleted
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            return;
        }

        Token = info.Token;
        CreateDate = info.CreateDate;
        UpdateDate = info.UpdateDate;
        IsDeletedAccount = info.IsDeleted;

        if (info.MergedIntoUserId is { } mergedInto)
        {
            MergedIntoName = await db.Users
                .AsNoTracking()
                .Where(x => x.Id == mergedInto)
                .Select(x => x.DisplayName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        GroupCount = await db.GroupMembers.CountAsync(
            x => x.UserId == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        ExpenseCount = await db.Expenses.CountAsync(
            x => x.PaidByUserId == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        PaymentCount = await db.Payments.CountAsync(
            x => (x.FromUserId == userId || x.ToUserId == userId) && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        var now = DateTime.UtcNow;
        ActiveSessionCount = await db.UserSessions.CountAsync(
            x => x.UserId == userId && x.UpdateState != UpdateState.Deleted && x.ExpiresUtc > now,
            cancellationToken);
    }

    /// <summary>Form values of the user editor.</summary>
    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Bitte einen Anzeigenamen angeben.")]
        [StringLength(80, ErrorMessage = "Maximal 80 Zeichen.")]
        [Display(Name = "Anzeigename")]
        public string DisplayName { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Bitte eine gültige E-Mail-Adresse angeben.")]
        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "E-Mail")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Bitte eine Rolle auswählen.")]
        [Display(Name = "Rolle")]
        public UserRole Role { get; set; } = UserRole.User;

        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "PayPal-Adresse")]
        public string? PayPalAddress { get; set; }

        [Display(Name = "Anonymer Zugang (Einladungslink, kein Passwort)")]
        public bool IsAnonymous { get; set; }

        [Display(Name = "Passwort setzen")]
        public bool SetPassword { get; set; }

        [StringLength(100, ErrorMessage = "Maximal 100 Zeichen.")]
        [DataType(DataType.Password)]
        [Display(Name = "Neues Passwort")]
        public string? NewPassword { get; set; }
    }
}
