using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Focused share screen for one group: shows the anonymous invite link big and
/// centred, with copy and messenger shortcuts (WhatsApp, Telegram, native web
/// share). Group administrators additionally manage the link here – switch it
/// off/on and regenerate it. Visible to everyone who may view the group.
/// </summary>
public class ShareModel(
    CurrentUserService currentUser,
    GroupService groups,
    HistoryService history,
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

    /// <summary>False for link guests, who may not hand the invite link on.</summary>
    public bool CanInvite => currentUser.CanInvite;

    /// <summary>PRG target for the invite management forms on this page.</summary>
    public string SelfUrl => $"/Groups/Share?groupId={GroupId}";

    /// <summary>Whether the public read-only link currently resolves.</summary>
    public bool ReadOnlyEnabled { get; private set; }

    /// <summary>Absolute read-only view link (/View?token=...).</summary>
    public string ReadOnlyUrl { get; private set; } = string.Empty;

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

        // Link guests joined by invite themselves and may not hand the link on.
        // The entry points are already hidden for them; this blocks direct URLs.
        if (!currentUser.CanInvite)
        {
            this.Flash("Einladungen können nur registrierte Mitglieder teilen.");
            return RedirectToPage("./Details", new { groupId = GroupId });
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
        ReadOnlyEnabled = group.ReadOnlyEnabled;
        ReadOnlyUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/View?token={group.ReadOnlyToken}";

        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, GroupId);
        this.SetTitle("Gruppe teilen", group.Name, "share");
        this.SetBreadcrumb(
            new BreadcrumbItem(group.Name, $"/Groups/Details?groupId={GroupId}"),
            new BreadcrumbItem("Teilen"));

        return Page();
    }

    public async Task<IActionResult> OnPostToggleInviteAsync(bool enabled, CancellationToken cancellationToken)
    {
        var guard = await GuardManageAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var result = await groups.SetInviteEnabledAsync(GroupId, enabled, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToSelf();
        }

        await history.LogAsync(
            GroupId,
            currentUser.UserId,
            HistoryService.EntityTypes.Group,
            GroupId,
            HistoryService.Actions.Updated,
            enabled ? "Einladung per Link wurde aktiviert." : "Einladung per Link wurde deaktiviert.",
            cancellationToken: cancellationToken);

        this.Flash(enabled ? "Einladung per Link ist aktiv." : "Einladung per Link ist deaktiviert.");
        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostRegenerateInviteAsync(CancellationToken cancellationToken)
    {
        var guard = await GuardManageAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var result = await groups.RegenerateInviteTokenAsync(GroupId, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToSelf();
        }

        this.Flash("Der Einladungslink wurde neu erzeugt, der alte Link ist ungültig.");
        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostToggleReadOnlyAsync(bool enabled, CancellationToken cancellationToken)
    {
        var guard = await GuardManageAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var result = await groups.SetReadOnlyEnabledAsync(GroupId, enabled, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToSelf();
        }

        this.Flash(enabled ? "Der Nur-Lese-Link ist aktiv." : "Der Nur-Lese-Link ist deaktiviert.");
        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostRegenerateReadOnlyAsync(CancellationToken cancellationToken)
    {
        var guard = await GuardManageAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        var result = await groups.RegenerateReadOnlyTokenAsync(GroupId, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToSelf();
        }

        this.Flash("Der Nur-Lese-Link wurde neu erzeugt, der alte Link ist ungültig.");
        return RedirectToSelf();
    }

    /// <summary>Returns a redirect/forbid result when the user may not manage the group.</summary>
    private async Task<IActionResult?> GuardManageAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();

        if (GroupId <= 0)
        {
            return RedirectToPage("./Index");
        }

        if (!await currentUser.CanManageGroupAsync(GroupId, cancellationToken))
        {
            return Forbid();
        }

        var group = await groups.GetGroupAsync(GroupId, cancellationToken);

        if (group is null)
        {
            this.FlashError("Die Gruppe wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        return null;
    }

    private IActionResult RedirectToSelf() => LocalRedirect($"/Groups/Share?groupId={GroupId}");
}
