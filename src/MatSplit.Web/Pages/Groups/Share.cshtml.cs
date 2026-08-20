using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Focused share screen for one group: shows the anonymous invite link big and
/// centred, with copy and messenger shortcuts (WhatsApp, Telegram, native web
/// share). Read only – regenerating or switching the link off stays on the
/// member management page. Visible to everyone who may view the group.
/// </summary>
public class ShareModel(
    CurrentUserService currentUser,
    GroupService groups,
    AppConfigService appConfig) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long GroupId { get; set; }

    public Group Group { get; private set; } = null!;

    public bool CanManage { get; private set; }

    /// <summary>False when link invites are switched off for the whole installation.</summary>
    public bool AllowAnonymousJoin { get; private set; } = true;

    /// <summary>Anonymous invite link for this group (/Join?token=...).</summary>
    public string InviteUrl { get; private set; } = string.Empty;

    /// <summary>Prefilled message when sharing the anonymous invite link.</summary>
    public string ShareText => $"Tritt unserer MatSplit-Gruppe »{Group?.Name}« bei:";

    /// <summary>Direct WhatsApp share link – works even without JavaScript.</summary>
    public string WhatsAppUrl => "https://wa.me/?text=" + Uri.EscapeDataString($"{ShareText} {InviteUrl}");

    /// <summary>Direct Telegram share link – works even without JavaScript.</summary>
    public string TelegramUrl =>
        "https://t.me/share/url?url=" + Uri.EscapeDataString(InviteUrl)
        + "&text=" + Uri.EscapeDataString(ShareText);

    /// <summary>True only when the link may actually be used to join right now.</summary>
    public bool CanShare => AllowAnonymousJoin && Group?.InviteEnabled == true;

    /// <summary>Link to the member management, where the link can be toggled or renewed.</summary>
    public string MembersUrl => $"/Groups/Members?groupId={GroupId}";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        if (GroupId <= 0)
        {
            return RedirectToPage("./Index");
        }

        if (!await currentUser.CanViewGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);

        if (group is null)
        {
            this.FlashError("Die Gruppe wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        Group = group;
        CanManage = await currentUser.CanManageGroupAsync(GroupId, cancellationToken);
        AllowAnonymousJoin = (await appConfig.GetAsync(cancellationToken)).AllowAnonymousJoin;
        InviteUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/Join?token={group.InviteToken}";

        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, GroupId);
        this.SetTitle("Gruppe teilen", group.Name, "share");
        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            new BreadcrumbItem(group.Name, $"/Groups/Details?groupId={GroupId}"),
            new BreadcrumbItem("Teilen"));

        return Page();
    }
}
