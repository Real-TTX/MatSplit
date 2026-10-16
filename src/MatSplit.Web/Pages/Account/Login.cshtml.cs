using System.ComponentModel.DataAnnotations;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Account;

/// <summary>
/// Local sign in with e-mail address or display name plus password. The page is
/// anonymous (see the razor page conventions in Program.cs) and hands the
/// session creation over to <see cref="CurrentUserService.SignInAsync"/>.
/// With MatIdentity configured (MATIDENTITY_AUTHORITY) a visit goes straight on
/// to MatIdentity; the local form stays reachable with <c>?local=1</c> (a way in
/// for the administrator when MatIdentity is down).
/// </summary>
public class LoginModel(
    UserService users,
    CurrentUserService currentUser,
    HistoryService history,
    AppConfigService appConfig,
    IWebHostEnvironment environment,
    MatPlatformSettings platform,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Local target the user wanted to reach before the redirect.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Shows the seed credentials, development environment only.</summary>
    public bool ShowDevelopmentHint { get; private set; }

    public string AppName { get; private set; } = "MatSplit";

    /// <summary>True when invite links are enabled in appconfig.json.</summary>
    public bool AllowAnonymousJoin { get; private set; } = true;

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte E-Mail-Adresse oder Benutzername angeben.")]
        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "E-Mail oder Benutzername")]
        public string EmailOrName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bitte das Passwort angeben.")]
        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "Passwort")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Angemeldet bleiben")]
        public bool RememberMe { get; set; } = true;
    }

    /// <summary><c>?local=1</c>: the local form even when MatIdentity is configured.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Local { get; set; }

    /// <summary><c>?signedOut=1</c>: back from the sign-out at MatIdentity, no automatic sign-in.</summary>
    [BindProperty(SupportsGet = true)]
    public bool SignedOut { get; set; }

    /// <summary><c>?ssoError=1</c>: the sign-in at MatIdentity failed.</summary>
    [BindProperty(SupportsGet = true)]
    public bool SsoError { get; set; }

    /// <summary>True when the sign-in through MatIdentity is configured.</summary>
    public bool IdentityEnabled => platform.IdentityEnabled;

    /// <summary>The address that starts the sign-in at MatIdentity and comes back to <see cref="ReturnUrl"/>.</summary>
    public string IdentitySignInUrl => string.IsNullOrWhiteSpace(ReturnUrl)
        ? "/Account/Login"
        : "/Account/Login?returnUrl=" + Uri.EscapeDataString(ReturnUrl);

    /// <summary>The address of the local form, with the same <see cref="ReturnUrl"/>.</summary>
    public string LocalSignInUrl => string.IsNullOrWhiteSpace(ReturnUrl)
        ? "/Account/Login?local=1"
        : "/Account/Login?local=1&returnUrl=" + Uri.EscapeDataString(ReturnUrl);

    /// <summary>True when the page shows the local form (always without MatIdentity).</summary>
    public bool ShowLocalForm => !IdentityEnabled || Local;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(cancellationToken);

        if (currentUser.IsAuthenticated)
        {
            return Redirect(ResolveReturnUrl());
        }

        // Auto login: straight on to MatIdentity, no local form in the way.
        if (IdentityEnabled && !Local && !SignedOut && !SsoError)
        {
            return Challenge(
                new AuthenticationProperties { RedirectUri = ResolveReturnUrl() },
                MatIdentityAuthentication.Scheme);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // A posted form is the local sign-in; on an error it shows the form again.
        Local = true;
        await PreparePageAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.ValidatePasswordAsync(Input.EmailOrName, Input.Password, cancellationToken);

        if (user is null)
        {
            // Deliberately vague: never reveal whether the account exists.
            logger.LogInformation("Rejected sign in attempt for {Login}", Input.EmailOrName);
            ModelState.AddModelError(
                string.Empty,
                "Anmeldung fehlgeschlagen. Bitte Benutzername und Passwort pr\u00fcfen.");
            return Page();
        }

        await currentUser.SignInAsync(user, Input.RememberMe, cancellationToken);

        await history.LogAsync(
            null,
            user.Id,
            HistoryService.EntityTypes.User,
            user.Id,
            HistoryService.Actions.SignedIn,
            $"\"{user.DisplayName}\" hat sich angemeldet.",
            cancellationToken: cancellationToken);

        this.Flash($"Willkommen zur\u00fcck, {user.DisplayName}!");
        return Redirect(ResolveReturnUrl());
    }

    private async Task PreparePageAsync(CancellationToken cancellationToken)
    {
        var config = await appConfig.GetAsync(cancellationToken);

        AppName = config.AppName;
        AllowAnonymousJoin = config.AllowAnonymousJoin;
        ShowDevelopmentHint = environment.IsDevelopment();

        this.SetTitle("Anmelden", "Melde dich mit E-Mail-Adresse oder Benutzername an.", "user");
        ViewData[LayoutKeys.HideMenu] = true;
    }

    /// <summary>Only local urls are accepted, otherwise back to the dashboard.</summary>
    private string ResolveReturnUrl()
    {
        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return ReturnUrl!;
        }

        return "/";
    }
}
