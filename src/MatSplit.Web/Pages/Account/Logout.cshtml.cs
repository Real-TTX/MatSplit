using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Account;

/// <summary>
/// Confirms and performs the sign out. The confirmation step keeps the sign out
/// a POST (no CSRF driven logout through a prefetched link) and is the last
/// chance to warn link guests that their account is only reachable through the
/// invite link.
/// </summary>
public class LogoutModel(
    CurrentUserService currentUser,
    GroupService groups,
    HistoryService history) : PageModel
{
    /// <summary>Name shown in the confirmation text.</summary>
    public string DisplayName { get; private set; } = "Gast";

    /// <summary>True for link guests without password.</summary>
    public bool IsAnonymousUser { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return RedirectToPage("/Account/Login");
        }

        await PreparePageAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return RedirectToPage("/Account/Login");
        }

        var userId = currentUser.UserId;
        var name = currentUser.DisplayName;

        await history.LogAsync(
            null,
            userId,
            HistoryService.EntityTypes.User,
            userId,
            HistoryService.Actions.SignedOut,
            $"\"{name}\" hat sich abgemeldet.",
            cancellationToken: cancellationToken);

        await currentUser.SignOutAsync(cancellationToken);

        this.Flash("Du bist jetzt abgemeldet.");
        return RedirectToPage("/Account/Login");
    }

    private async Task PreparePageAsync(CancellationToken cancellationToken)
    {
        DisplayName = currentUser.DisplayName;
        IsAnonymousUser = currentUser.IsAnonymousUser;

        var userId = currentUser.UserId;
        if (userId is not null)
        {
            var memberships = await groups.ListGroupsForUserAsync(userId.Value, cancellationToken: cancellationToken);
            this.SetMenuGroups(memberships.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        }

        this.SetTitle("Abmelden", DisplayName, "logout");
        this.SetBreadcrumb(
            new BreadcrumbItem("Konto", "/Account/Profile"),
            new BreadcrumbItem("Abmelden"));
    }
}
