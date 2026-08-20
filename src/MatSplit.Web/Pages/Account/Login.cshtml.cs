using System.ComponentModel.DataAnnotations;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Account;

/// <summary>
/// Local sign in with e-mail address or display name plus password. The page is
/// anonymous (see the razor page conventions in Program.cs) and hands the
/// session creation over to <see cref="CurrentUserService.SignInAsync"/>.
/// </summary>
public class LoginModel(
    UserService users,
    CurrentUserService currentUser,
    HistoryService history,
    AppConfigService appConfig,
    IWebHostEnvironment environment,
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

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(cancellationToken);

        if (currentUser.IsAuthenticated)
        {
            return Redirect(ResolveReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
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

        this.SetTitle("Anmelden", AppName, "user");
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
