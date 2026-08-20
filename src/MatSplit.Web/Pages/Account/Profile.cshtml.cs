using System.ComponentModel.DataAnnotations;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Account;

/// <summary>
/// Own profile: display name, e-mail, PayPal handle, password, colour scheme,
/// upgrade of a link guest to a real account and the list of active sessions.
/// </summary>
public class ProfileModel(
    UserService users,
    SessionService sessions,
    GroupService groups,
    CurrentUserService currentUser,
    HistoryService history) : PageModel
{
    /// <summary>Url the theme switcher in the left menu posts to.</summary>
    public const string ThemeSaveUrl = "/Account/Profile?handler=Theme";

    private const string PayPalBase = "https://paypal.me/";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Keeps the typed e-mail when the upgrade form is redisplayed.</summary>
    public string? UpgradeEmail { get; private set; }

    public string DisplayName { get; private set; } = "Gast";

    public string? UserToken { get; private set; }

    public UserRole Role { get; private set; } = UserRole.Anonymous;

    /// <summary>True for link guests: no password yet.</summary>
    public bool IsAnonymousAccount { get; private set; }

    /// <summary>False when the account never had a password (link guest).</summary>
    public bool HasPassword { get; private set; }

    /// <summary>Preview of the paypal.me profile link, null when not derivable.</summary>
    public string? PayPalPreview { get; private set; }

    public IReadOnlyList<SessionRow> SessionRows { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ThemeOptions { get; } =
    [
        new SelectListItem("System folgen", nameof(ThemeMode.System)),
        new SelectListItem("Immer hell", nameof(ThemeMode.Light)),
        new SelectListItem("Immer dunkel", nameof(ThemeMode.Dark))
    ];

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte einen Anzeigenamen angeben.")]
        [StringLength(120, ErrorMessage = "Maximal 120 Zeichen.")]
        [Display(Name = "Anzeigename")]
        public string DisplayName { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Bitte eine g\u00fcltige E-Mail-Adresse angeben.")]
        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "E-Mail")]
        public string? Email { get; set; }

        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "PayPal-Adresse")]
        public string? PayPalAddress { get; set; }

        [Required(ErrorMessage = "Bitte ein Farbschema w\u00e4hlen.")]
        [Display(Name = "Farbschema")]
        public ThemeMode Theme { get; set; } = ThemeMode.System;

        [Display(Name = "Passwort \u00e4ndern")]
        public bool ChangePassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Aktuelles Passwort")]
        public string? CurrentPassword { get; set; }

        [StringLength(200, MinimumLength = 6, ErrorMessage = "Das Passwort muss mindestens 6 Zeichen lang sein.")]
        [DataType(DataType.Password)]
        [Display(Name = "Neues Passwort")]
        public string? NewPassword { get; set; }

        [Compare(nameof(NewPassword), ErrorMessage = "Die Passw\u00f6rter stimmen nicht \u00fcberein.")]
        [DataType(DataType.Password)]
        [Display(Name = "Neues Passwort wiederholen")]
        public string? ConfirmPassword { get; set; }
    }

    /// <summary>One row of the session table.</summary>
    public sealed record SessionRow(
        long Id,
        string Device,
        DateTime CreatedUtc,
        DateTime LastSeenUtc,
        DateTime ExpiresUtc,
        bool IsCurrent);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await LoadAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        ResetInput(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await LoadAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var payPal = string.IsNullOrWhiteSpace(Input.PayPalAddress) ? null : Input.PayPalAddress!.Trim();

        if (payPal is not null
            && !payPal.Contains('@', StringComparison.Ordinal)
            && PayPalLinkBuilder.ExtractHandle(payPal) is null)
        {
            ModelState.AddModelError(
                "Input.PayPalAddress",
                "Bitte einen paypal.me-Namen, eine paypal.me-Adresse oder eine PayPal-E-Mail-Adresse angeben.");
        }

        if (Input.ChangePassword)
        {
            if (string.IsNullOrEmpty(Input.NewPassword))
            {
                ModelState.AddModelError("Input.NewPassword", "Bitte ein neues Passwort angeben.");
            }

            if (HasPassword && !PasswordHasher.Verify(Input.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError("Input.CurrentPassword", "Das aktuelle Passwort ist nicht korrekt.");
            }
        }

        if (!ModelState.IsValid)
        {
            PayPalPreview = BuildPreview(Input.PayPalAddress);
            return Page();
        }

        var newPassword = Input.ChangePassword ? Input.NewPassword : null;

        var result = await users.UpdateProfileAsync(
            user.Id,
            Input.DisplayName,
            Input.Email,
            payPal,
            newPassword,
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            PayPalPreview = BuildPreview(Input.PayPalAddress);
            return Page();
        }

        if (Input.Theme != user.ThemePreference)
        {
            var themeResult = await users.SetThemeAsync(user.Id, Input.Theme, cancellationToken);
            if (themeResult.IsFailure)
            {
                ModelState.AddModelError(string.Empty, themeResult.Error!);
                PayPalPreview = BuildPreview(Input.PayPalAddress);
                return Page();
            }
        }

        if (!string.IsNullOrEmpty(newPassword))
        {
            await history.LogAsync(
                null,
                user.Id,
                HistoryService.EntityTypes.User,
                user.Id,
                HistoryService.Actions.Updated,
                $"\"{Input.DisplayName}\" hat das Passwort ge\u00e4ndert.",
                cancellationToken: cancellationToken);
        }

        await currentUser.RefreshSignInAsync(cancellationToken);

        this.Flash("Profil gespeichert.");
        return RedirectToPage();
    }

    /// <summary>
    /// Turns a link guest into a real account by setting e-mail and password.
    /// </summary>
    public async Task<IActionResult> OnPostUpgradeAsync(
        string? email,
        string? password,
        string? confirmPassword,
        CancellationToken cancellationToken)
    {
        // The main profile form is not part of this post, so its binding errors
        // would be misleading.
        ModelState.Clear();

        var user = await LoadAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        ResetInput(user);
        UpgradeEmail = email?.Trim();

        if (!IsAnonymousAccount)
        {
            this.FlashError("Dieses Konto ist bereits ein vollwertiges Konto.");
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(UpgradeEmail))
        {
            ModelState.AddModelError("email", "Bitte eine E-Mail-Adresse angeben.");
        }
        else if (!new EmailAddressAttribute().IsValid(UpgradeEmail))
        {
            ModelState.AddModelError("email", "Bitte eine g\u00fcltige E-Mail-Adresse angeben.");
        }

        if (string.IsNullOrEmpty(password) || password.Length < 6)
        {
            ModelState.AddModelError("password", "Das Passwort muss mindestens 6 Zeichen lang sein.");
        }
        else if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError("confirmPassword", "Die Passw\u00f6rter stimmen nicht \u00fcberein.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await users.UpdateProfileAsync(
            user.Id,
            user.DisplayName,
            UpgradeEmail,
            user.PayPalAddress,
            password,
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError("email", result.Error!);
            return Page();
        }

        var roleResult = await users.SetRoleAsync(user.Id, UserRole.User, cancellationToken);
        if (roleResult.IsFailure)
        {
            ModelState.AddModelError(string.Empty, roleResult.Error!);
            return Page();
        }

        await history.LogAsync(
            null,
            user.Id,
            HistoryService.EntityTypes.User,
            user.Id,
            HistoryService.Actions.Updated,
            $"Gastkonto \"{user.DisplayName}\" wurde zu einem Konto mit Passwort erweitert.",
            cancellationToken: cancellationToken);

        await currentUser.RefreshSignInAsync(cancellationToken);

        this.Flash("Dein Konto ist jetzt mit E-Mail-Adresse und Passwort gesichert.");
        return RedirectToPage();
    }

    /// <summary>Ends one of the own sessions ("abmelden" per device).</summary>
    public async Task<IActionResult> OnPostEndSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        ModelState.Clear();

        var userId = currentUser.UserId;
        if (userId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        // Only sessions of the signed in user are listed, so this is the
        // ownership check as well.
        var own = await sessions.ListSessionsForUserAsync(userId.Value, cancellationToken);
        var target = own.FirstOrDefault(x => x.Id == sessionId);

        if (target is null)
        {
            this.FlashError("Die Sitzung wurde nicht gefunden oder ist bereits beendet.");
            return RedirectToPage();
        }

        var isCurrent = string.Equals(target.Token, currentUser.SessionToken, StringComparison.Ordinal);

        await sessions.EndSessionAsync(target.Token, cancellationToken);

        await history.LogAsync(
            null,
            userId,
            HistoryService.EntityTypes.User,
            userId.Value,
            HistoryService.Actions.Deleted,
            $"Eine Sitzung von \"{DisplayName}\" wurde abgemeldet.",
            cancellationToken: cancellationToken);

        if (isCurrent)
        {
            await currentUser.SignOutAsync(cancellationToken);
            this.Flash("Du bist jetzt abgemeldet.");
            return RedirectToPage("/Account/Login");
        }

        this.Flash("Die Sitzung wurde beendet.");
        return RedirectToPage();
    }

    /// <summary>Ends every session except the current one.</summary>
    public async Task<IActionResult> OnPostEndOtherSessionsAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();

        var userId = currentUser.UserId;
        if (userId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var own = await sessions.ListSessionsForUserAsync(userId.Value, cancellationToken);
        var current = currentUser.SessionToken;
        var ended = 0;

        foreach (var session in own)
        {
            if (string.Equals(session.Token, current, StringComparison.Ordinal))
            {
                continue;
            }

            await sessions.EndSessionAsync(session.Token, cancellationToken);
            ended++;
        }

        if (ended == 0)
        {
            this.Flash("Es gibt keine weiteren Sitzungen.");
            return RedirectToPage();
        }

        await history.LogAsync(
            null,
            userId,
            HistoryService.EntityTypes.User,
            userId.Value,
            HistoryService.Actions.Deleted,
            $"{ended} weitere Sitzung(en) von \"{DisplayName}\" wurden abgemeldet.",
            cancellationToken: cancellationToken);

        this.Flash($"{ended} weitere Sitzung(en) wurden beendet.");
        return RedirectToPage();
    }

    /// <summary>
    /// Endpoint of the theme switcher in the left menu. Answers without a body,
    /// the client already applied the theme optimistically.
    /// </summary>
    public async Task<IActionResult> OnPostThemeAsync(string? theme, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return new StatusCodeResult(StatusCodes.Status401Unauthorized);
        }

        var mode = (theme ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dark" => ThemeMode.Dark,
            "light" => ThemeMode.Light,
            "system" => ThemeMode.System,
            _ => (ThemeMode?)null
        };

        if (mode is null)
        {
            return BadRequest();
        }

        var result = await users.SetThemeAsync(userId.Value, mode.Value, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(result.Error ?? "Das Farbschema konnte nicht gespeichert werden.");
        }

        await currentUser.RefreshSignInAsync(cancellationToken);
        return new StatusCodeResult(StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Loads the signed in user plus everything the view needs. Returns null
    /// when there is no usable session anymore.
    /// </summary>
    private async Task<User?> LoadAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return null;
        }

        var user = await users.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return null;
        }

        DisplayName = user.DisplayName;
        UserToken = user.Token;
        Role = user.Role;
        IsAnonymousAccount = user.IsAnonymous || user.PasswordHash is null;
        HasPassword = user.PasswordHash is not null;
        PayPalPreview = BuildPreview(user.PayPalAddress);

        var currentToken = currentUser.SessionToken;
        var list = await sessions.ListSessionsForUserAsync(user.Id, cancellationToken);

        SessionRows = list
            .Select(x => new SessionRow(
                x.Id,
                DescribeUserAgent(x.UserAgent),
                ToLocal(x.CreatedUtc),
                ToLocal(x.LastSeenUtc),
                ToLocal(x.ExpiresUtc),
                string.Equals(x.Token, currentToken, StringComparison.Ordinal)))
            .ToList();

        var memberships = await groups.ListGroupsForUserAsync(user.Id, cancellationToken: cancellationToken);

        this.SetTitle("Konto", user.DisplayName, "user");
        this.SetBreadcrumb(new BreadcrumbItem("Konto"));
        this.SetMenuGroups(memberships.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        ViewData["ThemeSaveUrl"] = ThemeSaveUrl;

        return user;
    }

    /// <summary>Fills the form from the stored record.</summary>
    private void ResetInput(User user)
    {
        Input = new InputModel
        {
            DisplayName = user.DisplayName,
            Email = user.Email,
            PayPalAddress = user.PayPalAddress,
            Theme = user.ThemePreference
        };
    }

    /// <summary>Session timestamps are stored in UTC but shown in server local time.</summary>
    private static DateTime ToLocal(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static string? BuildPreview(string? payPalAddress)
    {
        var handle = PayPalLinkBuilder.ExtractHandle(payPalAddress);
        return handle is null ? null : PayPalBase + handle;
    }

    /// <summary>
    /// Very small user agent summary - enough to recognise your own devices
    /// without shipping a parser library.
    /// </summary>
    private static string DescribeUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unbekanntes Ger\u00e4t";
        }

        var platform = userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
            : userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
            : userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "Mac"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : "Ger\u00e4t";

        var browser = userAgent.Contains("Edg", StringComparison.Ordinal) ? "Edge"
            : userAgent.Contains("OPR", StringComparison.Ordinal) ? "Opera"
            : userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Browser";

        return $"{platform} \u00b7 {browser}";
    }
}
