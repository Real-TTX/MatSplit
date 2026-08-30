using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using MatSplit.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Pages.Groups;

/// <summary>
/// Add (no userId) and edit page of a single group membership: share factor
/// (family = 3) and the group admin flag. The membership list stays read only,
/// as the ui guideline demands a separate sub page for create and edit.
/// </summary>
public class MemberEditModel(
    CurrentUserService currentUser,
    GroupService groups,
    UserService users,
    HistoryService history) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "groupId")]
    public long GroupId { get; set; }

    /// <summary>Member being edited; null or 0 adds an existing user.</summary>
    [BindProperty(SupportsGet = true, Name = "userId")]
    public long? UserId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Group Group { get; private set; } = new();

    /// <summary>Groups of the current user, rendered in the left menu.</summary>
    private IReadOnlyList<MenuGroupEntry> MenuGroups { get; set; } = [];

    /// <summary>Display name of the edited member, empty when adding.</summary>
    public string MemberName { get; private set; } = string.Empty;

    /// <summary>Active users that are not a member of this group yet.</summary>
    public IReadOnlyList<SelectListItem> CandidateOptions { get; private set; } = [];

    public bool IsExisting => UserId is > 0;

    /// <summary>Always true here: the page is only reachable for group admins.</summary>
    public bool CanManage => true;

    /// <summary>True when the membership may be removed.</summary>
    public bool CanRemove { get; private set; }

    public string ListUrl =>
        "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture) + "&tab=mitglieder";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (IsExisting)
        {
            var member = await groups.GetMemberAsync(GroupId, UserId!.Value, cancellationToken);
            if (member is null)
            {
                this.FlashError("Die Mitgliedschaft wurde nicht gefunden.");
                return LocalRedirect(ListUrl);
            }

            MemberName = member.User?.DisplayName ?? "Unbekannt";
            CanRemove = true;

            Input = new InputModel
            {
                UserId = member.UserId,
                ShareFactor = member.ShareFactor,
                IsGroupAdmin = member.IsGroupAdmin
            };
        }
        else
        {
            Input = new InputModel();
        }

        ApplyLayout();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (IsExisting)
        {
            return await SaveExistingAsync(cancellationToken);
        }

        var newName = Input.NewName?.Trim();
        var hasNewName = !string.IsNullOrWhiteSpace(newName);

        // "Add by name" and "add existing user" are alternatives, so only one of
        // the two inputs has to validate; drop the other one's model error.
        if (hasNewName)
        {
            ModelState.Remove("Input.UserId");
        }
        else if (Input.UserId <= 0)
        {
            ModelState.Remove("Input.UserId");
            ModelState.AddModelError("Input.NewName",
                "Bitte einen Namen eingeben oder einen bestehenden Benutzer auswählen.");
        }

        if (!ModelState.IsValid)
        {
            ApplyLayout();
            return Page();
        }

        if (hasNewName)
        {
            // A managed member has no own login: create an anonymous user (name
            // only, no password) and add it straight to this group. One person
            // then books everything on their behalf.
            var managed = await users.CreateAnonymousUserAsync(newName!, cancellationToken);

            var addedManaged = await groups.AddMemberAsync(
                GroupId, managed.Id, Input.ShareFactor, Input.IsGroupAdmin, cancellationToken);

            if (addedManaged.IsFailure)
            {
                ModelState.AddModelError(string.Empty, addedManaged.Error!);
                ApplyLayout();
                return Page();
            }

            this.Flash($"\"{managed.DisplayName}\" wurde als verwaltetes Mitglied hinzugefügt.");
            return LocalRedirect(ListUrl);
        }

        var added = await groups.AddMemberAsync(
            GroupId, Input.UserId, Input.ShareFactor, Input.IsGroupAdmin, cancellationToken);

        if (added.IsFailure)
        {
            ModelState.AddModelError(string.Empty, added.Error!);
            ApplyLayout();
            return Page();
        }

        var user = await users.GetByIdAsync(Input.UserId, cancellationToken);
        this.Flash($"\"{user?.DisplayName ?? "Der Benutzer"}\" ist jetzt Mitglied der Gruppe.");
        return LocalRedirect(ListUrl);
    }

    /// <summary>Removes the membership (soft delete of the assignment).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var guard = await LoadContextAsync(cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        if (!IsExisting)
        {
            return NotFound();
        }

        var result = await groups.RemoveMemberAsync(GroupId, UserId!.Value, cancellationToken);

        if (result.IsFailure)
        {
            this.FlashError(result.Error!);
            return LocalRedirect(SelfUrl());
        }

        this.Flash("Das Mitglied wurde aus der Gruppe entfernt.");
        return LocalRedirect(ListUrl);
    }

    private async Task<IActionResult> SaveExistingAsync(CancellationToken cancellationToken)
    {
        var member = await groups.GetMemberAsync(GroupId, UserId!.Value, cancellationToken);
        if (member is null)
        {
            this.FlashError("Die Mitgliedschaft wurde nicht gefunden.");
            return LocalRedirect(ListUrl);
        }

        MemberName = member.User?.DisplayName ?? "Unbekannt";
        CanRemove = true;
        Input.UserId = member.UserId;

        // The user select is not rendered when editing, so its Range rule must
        // not block the save.
        ModelState.Remove("Input.UserId");

        if (!ModelState.IsValid)
        {
            ApplyLayout();
            return Page();
        }

        if (member.ShareFactor != Input.ShareFactor)
        {
            var factorResult = await groups.SetShareFactorAsync(
                GroupId, member.UserId, Input.ShareFactor, cancellationToken);

            if (factorResult.IsFailure)
            {
                ModelState.AddModelError(string.Empty, factorResult.Error!);
                ApplyLayout();
                return Page();
            }

            await history.LogAsync(
                GroupId,
                member.UserId,
                HistoryService.EntityTypes.GroupMember,
                member.UserId,
                HistoryService.Actions.Updated,
                $"Personenzahl von \"{MemberName}\" wurde auf {Math.Clamp(Input.ShareFactor, 1, 100)} gesetzt.",
                cancellationToken: cancellationToken);
        }

        if (member.IsGroupAdmin != Input.IsGroupAdmin)
        {
            var adminResult = await groups.SetGroupAdminAsync(
                GroupId, member.UserId, Input.IsGroupAdmin, cancellationToken);

            if (adminResult.IsFailure)
            {
                ModelState.AddModelError(string.Empty, adminResult.Error!);
                ApplyLayout();
                return Page();
            }

            await history.LogAsync(
                GroupId,
                member.UserId,
                HistoryService.EntityTypes.GroupMember,
                member.UserId,
                HistoryService.Actions.Updated,
                Input.IsGroupAdmin
                    ? $"\"{MemberName}\" ist jetzt Gruppen-Administrator."
                    : $"\"{MemberName}\" ist kein Gruppen-Administrator mehr.",
                cancellationToken: cancellationToken);
        }

        this.Flash($"Die Mitgliedschaft von \"{MemberName}\" wurde gespeichert.");
        return LocalRedirect(ListUrl);
    }

    /// <summary>
    /// Loads group and lookups. Returns a result when the group is gone or the
    /// user may not manage it, otherwise null.
    /// </summary>
    private async Task<IActionResult?> LoadContextAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

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

        Group = group;

        if (UserId is not > 0)
        {
            UserId = null;

            var members = await groups.ListMembersAsync(GroupId, cancellationToken);
            var memberIds = members.Select(x => x.UserId).ToHashSet();
            var candidates = await users.ListAllActiveUsersAsync(cancellationToken);

            CandidateOptions =
            [
                .. candidates
                    .Where(x => !memberIds.Contains(x.Id))
                    .Select(x => new SelectListItem(
                        BuildUserLabel(x),
                        x.Id.ToString(CultureInfo.InvariantCulture),
                        x.Id == Input.UserId))
            ];
        }

        MenuGroups = await GroupMenu.BuildAsync(groups, currentUser, userId, cancellationToken);
        return null;
    }

    private string SelfUrl()
    {
        var url = "/Groups/MemberEdit?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture);
        return IsExisting
            ? url + "&userId=" + UserId!.Value.ToString(CultureInfo.InvariantCulture)
            : url;
    }

    private void ApplyLayout()
    {
        var title = IsExisting ? "Mitglied bearbeiten" : "Mitglied hinzufügen";

        this.SetTitle(title, Group.Name, "users");
        this.SetBreadcrumb(
            new BreadcrumbItem(Group.Name, "/Groups/Details?groupId=" + GroupId.ToString(CultureInfo.InvariantCulture)),
            new BreadcrumbItem("Mitglieder", ListUrl),
            new BreadcrumbItem(title));

        this.SetMenuGroups(MenuGroups, GroupId);
    }

    private static string BuildUserLabel(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return $"{user.DisplayName} ({user.Email})";
        }

        return user.IsAnonymous ? $"{user.DisplayName} (anonym)" : user.DisplayName;
    }

    /// <summary>Form model of the membership editor.</summary>
    public sealed class InputModel
    {
        /// <summary>
        /// Name of a new managed member without an own login. When set it takes
        /// precedence over <see cref="UserId"/>.
        /// </summary>
        [StringLength(200, ErrorMessage = "Maximal 200 Zeichen.")]
        [Display(Name = "Name")]
        public string? NewName { get; set; }

        [Range(1, long.MaxValue, ErrorMessage = "Bitte einen Benutzer auswählen.")]
        [Display(Name = "Benutzer")]
        public long UserId { get; set; }

        [Required(ErrorMessage = "Bitte die Anzahl Personen angeben.")]
        [Range(1, 100, ErrorMessage = "Die Personenzahl liegt zwischen 1 und 100.")]
        [Display(Name = "Personen")]
        public int ShareFactor { get; set; } = 1;

        [Display(Name = "Darf die Gruppe verwalten")]
        public bool IsGroupAdmin { get; set; }
    }
}
