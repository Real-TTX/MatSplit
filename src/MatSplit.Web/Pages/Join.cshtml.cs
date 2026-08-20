using System.ComponentModel.DataAnnotations;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages;

/// <summary>
/// Entry point of an invite link (/Join?token=...). Shows the group, asks for a
/// display name only and creates an anonymous member through
/// <see cref="GroupService.JoinByInviteTokenAsync"/>. Signed in users can join
/// with their existing account instead.
/// </summary>
public class JoinModel(
    GroupService groups,
    CurrentUserService currentUser,
    AppConfigService appConfig,
    ILogger<JoinModel> logger) : PageModel
{
    /// <summary>Invite token from the link.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The invited group, null when the link is not usable.</summary>
    public Group? Group { get; private set; }

    public bool IsSignedIn { get; private set; }

    public string SignedInName { get; private set; } = "Gast";

    /// <summary>Reason shown when <see cref="Group"/> is null.</summary>
    public string ErrorText { get; private set; } = string.Empty;

    /// <summary>Login link that comes back to this invite afterwards.</summary>
    public string LoginUrl { get; private set; } = "/Account/Login";

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte einen Anzeigenamen angeben.")]
        [StringLength(120, ErrorMessage = "Maximal 120 Zeichen.")]
        [Display(Name = "Dein Name in der Gruppe")]
        public string DisplayName { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync(cancellationToken);

        if (Group is null)
        {
            return Page();
        }

        // Already a member: straight into the group instead of a second identity.
        if (IsSignedIn && await currentUser.IsMemberAsync(Group.Id, cancellationToken))
        {
            this.Flash($"Du bist bereits Mitglied von \"{Group.Name}\".");
            return Redirect($"/Groups/Details?groupId={Group.Id}");
        }

        return Page();
    }

    /// <summary>Creates a new link guest and signs it in.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync(cancellationToken);

        if (Group is null)
        {
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await groups.JoinByInviteTokenAsync(Token, Input.DisplayName, cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }

        var join = result.Value;
        await currentUser.SignInAsync(join.User, isPersistent: true, cancellationToken);

        logger.LogInformation(
            "Guest {UserId} joined group {GroupId} through an invite link",
            join.User.Id,
            join.Group.Id);

        this.Flash($"Willkommen in \"{join.Group.Name}\", {join.User.DisplayName}!");
        return Redirect($"/Groups/Details?groupId={join.Group.Id}");
    }

    /// <summary>Adds the already signed in user to the group.</summary>
    public async Task<IActionResult> OnPostExistingAsync(CancellationToken cancellationToken)
    {
        // The guest form is not part of this post.
        ModelState.Clear();

        await PrepareAsync(cancellationToken);

        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Redirect(LoginUrl);
        }

        if (Group is null)
        {
            return Page();
        }

        var result = await groups.JoinExistingUserByInviteTokenAsync(Token, userId.Value, cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }

        var join = result.Value;

        this.Flash(join.CreatedNewMembership
            ? $"Du bist der Gruppe \"{join.Group.Name}\" beigetreten."
            : $"Du bist bereits Mitglied von \"{join.Group.Name}\".");

        return Redirect($"/Groups/Details?groupId={join.Group.Id}");
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var config = await appConfig.GetAsync(cancellationToken);

        IsSignedIn = currentUser.IsAuthenticated;
        SignedInName = currentUser.DisplayName;
        Token = string.IsNullOrWhiteSpace(Token) ? null : Token!.Trim();

        LoginUrl = Token is null
            ? "/Account/Login"
            : "/Account/Login?returnUrl=" + Uri.EscapeDataString("/Join?token=" + Token);

        if (Token is null)
        {
            ErrorText = "Der Einladungslink ist unvollst\u00e4ndig. Bitte den kompletten Link aus der Einladung verwenden.";
        }
        else if (!config.AllowAnonymousJoin)
        {
            ErrorText = "Einladungslinks sind auf diesem Server deaktiviert. Wende dich bitte an die Administration.";
        }
        else
        {
            Group = await groups.GetGroupByInviteTokenAsync(Token, cancellationToken);

            if (Group is null)
            {
                ErrorText = "Der Einladungslink ist ung\u00fcltig oder wurde deaktiviert.";
            }
        }

        // The centred hero on the page itself carries the big "Gruppe beitreten"
        // heading, so the top bar shows the group as context instead of echoing it.
        this.SetTitle(Group?.Name ?? "Gruppe beitreten", Group is null ? null : "Einladung", "link");
        ViewData[LayoutKeys.HideMenu] = true;
    }
}
