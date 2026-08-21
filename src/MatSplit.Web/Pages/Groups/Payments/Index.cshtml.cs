using MatSplit.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Groups.Payments;

/// <summary>
/// The standalone payment list has been replaced by the "Transaktionen" tab of
/// the group detail hub. This page only keeps deep links alive by redirecting to
/// that tab (payments filter preselected). Authorization is still enforced first.
/// </summary>
public sealed class IndexModel(CurrentUserService currentUser) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

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

        return RedirectToPage("/Groups/Details", new { groupId = GroupId, tab = "transaktionen", type = "payments" });
    }
}
