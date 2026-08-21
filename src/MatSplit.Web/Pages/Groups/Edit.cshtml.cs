using System.ComponentModel.DataAnnotations;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Create (no id) and edit (?id=...) a group on one page, plus the soft delete
/// handler. The invite options are only shown when link invites are switched on
/// (progressive disclosure).
/// </summary>
public class EditModel(
    CurrentUserService currentUser,
    GroupService groups,
    HistoryService history,
    AppConfigService appConfig) : PageModel
{
    private static readonly string[] KnownCurrencies =
        ["EUR", "CHF", "USD", "GBP", "DKK", "SEK", "NOK", "PLN", "CZK", "HUF"];

    /// <summary>Group id, missing or 0 means "new group".</summary>
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsNew => (Id ?? 0) <= 0;

    /// <summary>Absolute invite url, only set for an existing group.</summary>
    public string? InviteUrl { get; private set; }

    /// <summary>Read-only creation timestamp shown on the manage page.</summary>
    public string? CreatedDisplay { get; private set; }

    /// <summary>Read-only last-change timestamp, null when never changed.</summary>
    public string? UpdatedDisplay { get; private set; }

    public IReadOnlyList<SelectListItem> CurrencyOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await PrepareAsync(userId, cancellationToken);

        if (IsNew)
        {
            Input = new InputModel
            {
                Currency = appConfig.Current.DefaultCurrency,
                InviteEnabled = true
            };

            CurrencyOptions = BuildCurrencyOptions(Input.Currency);
            SetChrome(null);
            return Page();
        }

        var group = await groups.GetGroupAsync(Id!.Value, cancellationToken);

        if (group is null)
        {
            this.FlashError("Die Gruppe wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        if (!await currentUser.CanManageGroupAsync(group.Id, cancellationToken))
        {
            return Forbid();
        }

        Input = new InputModel
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            Currency = group.Currency,
            InviteEnabled = group.InviteEnabled
        };

        CurrencyOptions = BuildCurrencyOptions(Input.Currency);
        InviteUrl = BuildInviteUrl(group);
        CreatedDisplay = IndexModel.FormatMoment(group.CreateDate);
        UpdatedDisplay = group.UpdateDate <= group.CreateDate ? null : IndexModel.FormatMoment(group.UpdateDate);
        SetChrome(group);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var groupId = Id ?? 0;

        await PrepareAsync(userId, cancellationToken);
        CurrencyOptions = BuildCurrencyOptions(Input.Currency);

        Group? group = null;

        if (groupId > 0)
        {
            group = await groups.GetGroupAsync(groupId, cancellationToken);

            if (group is null)
            {
                this.FlashError("Die Gruppe wurde nicht gefunden.");
                return RedirectToPage("./Index");
            }

            if (!await currentUser.CanManageGroupAsync(groupId, cancellationToken))
            {
                return Forbid();
            }

            InviteUrl = BuildInviteUrl(group);
        }

        if (!ModelState.IsValid)
        {
            SetChrome(group);
            return Page();
        }

        if (group is null)
        {
            var created = await groups.CreateGroupAsync(
                Input.Name, Input.Description, Input.Currency, userId, cancellationToken);

            if (created.IsFailure)
            {
                ModelState.AddModelError(string.Empty, created.Error!);
                SetChrome(null);
                return Page();
            }

            var newGroup = created.Value;

            if (!Input.InviteEnabled)
            {
                await ApplyInviteEnabledAsync(newGroup.Id, false, userId, cancellationToken);
            }

            this.Flash($"Gruppe \"{newGroup.Name}\" wurde angelegt.");
            return RedirectToPage("./Details", new { groupId = newGroup.Id });
        }

        var updated = await groups.UpdateGroupAsync(
            group.Id, Input.Name, Input.Description, Input.Currency, cancellationToken);

        if (updated.IsFailure)
        {
            ModelState.AddModelError(string.Empty, updated.Error!);
            SetChrome(group);
            return Page();
        }

        if (group.InviteEnabled != Input.InviteEnabled)
        {
            await ApplyInviteEnabledAsync(group.Id, Input.InviteEnabled, userId, cancellationToken);
        }

        if (Input.InviteEnabled && Input.RegenerateInvite)
        {
            var regenerated = await groups.RegenerateInviteTokenAsync(group.Id, cancellationToken);

            if (regenerated.IsFailure)
            {
                ModelState.AddModelError(string.Empty, regenerated.Error!);
                SetChrome(group);
                return Page();
            }
        }

        this.Flash("Die Gruppe wurde gespeichert.");
        return RedirectToPage("./Details", new { groupId = group.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var groupId = Id ?? Input.Id;

        if (groupId <= 0)
        {
            this.FlashError("Die Gruppe wurde nicht gefunden.");
            return RedirectToPage("./Index");
        }

        if (!await currentUser.CanManageGroupAsync(groupId, cancellationToken))
        {
            return Forbid();
        }

        var result = await groups.SoftDeleteGroupAsync(groupId, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return RedirectToPage("./Edit", new { id = groupId });
        }

        this.Flash("Die Gruppe wurde gelöscht.");
        return RedirectToPage("./Index");
    }

    private async Task ApplyInviteEnabledAsync(
        long groupId,
        bool enabled,
        long userId,
        CancellationToken cancellationToken)
    {
        var result = await groups.SetInviteEnabledAsync(groupId, enabled, cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return;
        }

        await history.LogAsync(
            groupId,
            userId,
            HistoryService.EntityTypes.Group,
            groupId,
            HistoryService.Actions.Updated,
            enabled ? "Einladung per Link wurde aktiviert." : "Einladung per Link wurde deaktiviert.",
            cancellationToken: cancellationToken);
    }

    private async Task PrepareAsync(long userId, CancellationToken cancellationToken)
    {
        var menu = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        this.SetMenuGroups(menu, Id is > 0 ? Id : null);
    }

    private void SetChrome(Group? group)
    {
        var title = group is null ? "Neue Gruppe" : "Gruppe bearbeiten";
        this.SetTitle(title, group?.Name ?? "Gemeinsame Kasse anlegen", "group");

        this.SetBreadcrumb(
            new BreadcrumbItem("Gruppen", "/Groups"),
            group is null
                ? new BreadcrumbItem("Neue Gruppe")
                : new BreadcrumbItem(group.Name, $"/Groups/Details?groupId={group.Id}"),
            new BreadcrumbItem(title));
    }

    private string BuildInviteUrl(Group group)
        => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/Join?token={group.InviteToken}";

    private static List<SelectListItem> BuildCurrencyOptions(string? current)
    {
        var value = string.IsNullOrWhiteSpace(current) ? "EUR" : current.Trim().ToUpperInvariant();
        var codes = KnownCurrencies.Contains(value) ? KnownCurrencies : [.. KnownCurrencies, value];

        return [.. codes.Select(code => new SelectListItem(code, code, code == value))];
    }

    /// <summary>Form model of the group editor.</summary>
    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Bitte einen Namen für die Gruppe angeben.")]
        [StringLength(120, ErrorMessage = "Maximal 120 Zeichen.")]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000, ErrorMessage = "Maximal 1000 Zeichen.")]
        [Display(Name = "Beschreibung")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Bitte eine Währung auswählen.")]
        [StringLength(3, MinimumLength = 3, ErrorMessage = "Der Währungscode besteht aus drei Zeichen.")]
        [Display(Name = "Währung")]
        public string Currency { get; set; } = "EUR";

        [Display(Name = "Einladung per Link erlauben")]
        public bool InviteEnabled { get; set; } = true;

        [Display(Name = "Einladungslink beim Speichern neu erzeugen")]
        public bool RegenerateInvite { get; set; }
    }
}
