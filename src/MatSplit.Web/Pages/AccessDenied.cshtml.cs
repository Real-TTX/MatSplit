using MatSplit.Web.Data;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages;

/// <summary>
/// Friendly 403 page for signed in users that lack a permission (foreign group,
/// admin area). Pages redirect here instead of returning a bare Forbid() when a
/// readable explanation helps.
/// </summary>
public class AccessDeniedModel(CurrentUserService currentUser, GroupService groups) : PageModel
{
    /// <summary>Local page the user came from, offered as a back link.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Optional explanation supplied by the redirecting page.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Reason { get; set; }

    public string DisplayName { get; private set; } = "Gast";

    public bool IsSignedIn { get; private set; }

    /// <summary>True for link guests, they get an extra hint.</summary>
    public bool IsAnonymousUser { get; private set; }

    /// <summary>Back link, only set when it is a local url.</summary>
    public string? SafeReturnUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = currentUser.IsAuthenticated;
        DisplayName = currentUser.DisplayName;
        IsAnonymousUser = currentUser.IsAnonymousUser;

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            SafeReturnUrl = ReturnUrl;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            Reason = currentUser.Role == UserRole.Anonymous
                ? "Gastzug\u00e4nge sehen nur die Gruppen, zu denen sie eingeladen wurden."
                : "F\u00fcr diesen Bereich fehlen dir die Rechte.";
        }

        var userId = currentUser.UserId;
        if (userId is not null)
        {
            var memberships = await groups.ListGroupsForUserAsync(userId.Value, cancellationToken: cancellationToken);
            this.SetMenuGroups(memberships.Select(x => new MenuGroupEntry(x.Id, x.Name)));
        }

        this.SetTitle("Kein Zugriff", DisplayName, "warning");
        this.SetBreadcrumb(new BreadcrumbItem("Kein Zugriff"));

        Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
