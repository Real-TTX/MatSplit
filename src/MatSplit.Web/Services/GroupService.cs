using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Groups and their membership, including the anonymous invite-link flow.
/// </summary>
public sealed class GroupService(
    AppDbContext db,
    UserService users,
    HistoryService history,
    AppConfigService appConfig,
    ILogger<GroupService> logger)
{
    /// <summary>Groups the user is a member of, plus every group for admins.</summary>
    public async Task<IReadOnlyList<Group>> ListGroupsForUserAsync(long userId, bool includeAll = false, CancellationToken cancellationToken = default)
    {
        var query = db.Groups.AsNoTracking().Where(x => x.UpdateState != UpdateState.Deleted);

        if (!includeAll)
        {
            query = query.Where(g => db.GroupMembers.Any(
                m => m.GroupId == g.Id && m.UserId == userId && m.UpdateState != UpdateState.Deleted));
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    /// <summary>Paged group list for /Admin/Groups. Sort: name, name_desc, created, created_desc.</summary>
    public async Task<PagedResult<Group>> ListGroupsAsync(
        string? search = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        string? sort = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var query = db.Groups.AsNoTracking().AsQueryable();

        if (!includeDeleted)
        {
            query = query.Where(x => x.UpdateState != UpdateState.Deleted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.Name, "%" + term + "%")
                || (x.Description != null && EF.Functions.Like(x.Description, "%" + term + "%")));
        }

        query = sort switch
        {
            "name_desc" => query.OrderByDescending(x => x.Name),
            "created" => query.OrderBy(x => x.CreateDate),
            "created_desc" => query.OrderByDescending(x => x.CreateDate),
            _ => query.OrderBy(x => x.Name)
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(Paging.Skip(safePage, safeSize))
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Group>(items, safePage, safeSize, total);
    }

    public async Task<Group?> GetGroupAsync(long groupId, CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
        {
            return null;
        }

        return await db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);
    }

    public async Task<Group?> GetGroupByTokenAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Token == token && x.UpdateState != UpdateState.Deleted, cancellationToken);
    }

    /// <summary>
    /// Resolves an invite link. Returns null when the token is unknown, the
    /// group was deleted or invites are switched off.
    /// </summary>
    public async Task<Group?> GetGroupByInviteTokenAsync(string? inviteToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inviteToken))
        {
            return null;
        }

        var config = await appConfig.GetAsync(cancellationToken);
        if (!config.AllowAnonymousJoin)
        {
            return null;
        }

        return await db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.InviteToken == inviteToken && x.InviteEnabled && x.UpdateState != UpdateState.Deleted,
                cancellationToken);
    }

    /// <summary>Members of a group including the user record, ordered by name.</summary>
    public async Task<IReadOnlyList<GroupMember>> ListMembersAsync(long groupId, CancellationToken cancellationToken = default)
    {
        return await db.GroupMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted)
            .OrderBy(x => x.User!.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<GroupMember?> GetMemberAsync(long groupId, long userId, CancellationToken cancellationToken = default)
    {
        return await db.GroupMembers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.GroupId == groupId && x.UserId == userId && x.UpdateState != UpdateState.Deleted,
                cancellationToken);
    }

    public async Task<bool> IsMemberAsync(long groupId, long userId, CancellationToken cancellationToken = default)
    {
        return await db.GroupMembers.AnyAsync(
            x => x.GroupId == groupId && x.UserId == userId && x.UpdateState != UpdateState.Deleted,
            cancellationToken);
    }

    public async Task<bool> IsGroupAdminAsync(long groupId, long userId, CancellationToken cancellationToken = default)
    {
        return await db.GroupMembers.AnyAsync(
            x => x.GroupId == groupId
                 && x.UserId == userId
                 && x.IsGroupAdmin
                 && x.UpdateState != UpdateState.Deleted,
            cancellationToken);
    }

    /// <summary>
    /// Creates a group and makes <paramref name="ownerUserId"/> its group admin.
    /// </summary>
    public async Task<Result<Group>> CreateGroupAsync(
        string name,
        string? description,
        string? currency,
        long ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail<Group>("Der Gruppenname darf nicht leer sein.");
        }

        var config = await appConfig.GetAsync(cancellationToken);

        var group = new Group
        {
            Token = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Currency = NormalizeCurrency(currency, config.DefaultCurrency),
            InviteToken = Guid.NewGuid().ToString(),
            InviteEnabled = true,
            UpdateState = UpdateState.Created
        };

        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken);

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = ownerUserId,
            ShareFactor = 1,
            IsGroupAdmin = true,
            UpdateState = UpdateState.Created
        });

        await history.LogAsync(
            group.Id,
            ownerUserId,
            HistoryService.EntityTypes.Group,
            group.Id,
            HistoryService.Actions.Created,
            $"Gruppe \"{group.Name}\" wurde angelegt.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok(group);
    }

    public async Task<Result<Group>> UpdateGroupAsync(
        long groupId,
        string name,
        string? description,
        string? currency,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail<Group>("Der Gruppenname darf nicht leer sein.");
        }

        var group = await db.Groups.FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail<Group>("Die Gruppe wurde nicht gefunden.");
        }

        var config = await appConfig.GetAsync(cancellationToken);

        group.Name = name.Trim();
        group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        group.Currency = NormalizeCurrency(currency, config.DefaultCurrency);
        group.UpdateState = UpdateState.Updated;

        await history.LogAsync(
            group.Id,
            null,
            HistoryService.EntityTypes.Group,
            group.Id,
            HistoryService.Actions.Updated,
            $"Gruppe \"{group.Name}\" wurde geändert.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok(group);
    }

    /// <summary>Soft deletes the group and all of its child records.</summary>
    public async Task<Result> SoftDeleteGroupAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail("Die Gruppe wurde nicht gefunden.");
        }

        group.UpdateState = UpdateState.Deleted;

        var members = await db.GroupMembers.Where(x => x.GroupId == groupId).ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            member.UpdateState = UpdateState.Deleted;
        }

        var expenses = await db.Expenses.Where(x => x.GroupId == groupId).ToListAsync(cancellationToken);
        foreach (var expense in expenses)
        {
            expense.UpdateState = UpdateState.Deleted;
        }

        var payments = await db.Payments.Where(x => x.GroupId == groupId).ToListAsync(cancellationToken);
        foreach (var payment in payments)
        {
            payment.UpdateState = UpdateState.Deleted;
        }

        await history.LogAsync(
            groupId,
            null,
            HistoryService.EntityTypes.Group,
            groupId,
            HistoryService.Actions.Deleted,
            $"Gruppe \"{group.Name}\" wurde gelöscht.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Adds a member. Re-activates a previously removed membership instead of
    /// creating a duplicate row.
    /// </summary>
    public async Task<Result<GroupMember>> AddMemberAsync(
        long groupId,
        long userId,
        int shareFactor = 1,
        bool isGroupAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail<GroupMember>("Die Gruppe wurde nicht gefunden.");
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Fail<GroupMember>("Der Benutzer wurde nicht gefunden.");
        }

        var existing = await db.GroupMembers.FirstOrDefaultAsync(
            x => x.GroupId == groupId && x.UserId == userId, cancellationToken);

        if (existing is not null && existing.UpdateState != UpdateState.Deleted)
        {
            return Result.Fail<GroupMember>("Der Benutzer ist bereits Mitglied dieser Gruppe.");
        }

        var member = existing ?? new GroupMember { GroupId = groupId, UserId = userId };
        member.ShareFactor = NormalizeShareFactor(shareFactor);
        member.IsGroupAdmin = isGroupAdmin;
        member.UpdateState = existing is null ? UpdateState.Created : UpdateState.Updated;

        if (existing is null)
        {
            db.GroupMembers.Add(member);
        }

        await history.LogAsync(
            groupId,
            userId,
            HistoryService.EntityTypes.GroupMember,
            userId,
            HistoryService.Actions.Joined,
            $"\"{user.DisplayName}\" wurde zur Gruppe hinzugefügt.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(member);
    }

    /// <summary>
    /// Soft deletes a membership. Refuses when the member still appears in
    /// expenses or payments, because that would break the balance.
    /// </summary>
    public async Task<Result> RemoveMemberAsync(long groupId, long userId, CancellationToken cancellationToken = default)
    {
        var member = await db.GroupMembers
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.GroupId == groupId && x.UserId == userId && x.UpdateState != UpdateState.Deleted,
                cancellationToken);

        if (member is null)
        {
            return Result.Fail("Die Mitgliedschaft wurde nicht gefunden.");
        }

        var hasExpenses = await db.Expenses.AnyAsync(
            x => x.GroupId == groupId && x.UpdateState != UpdateState.Deleted && x.PaidByUserId == userId,
            cancellationToken);

        var hasShares = await db.ExpenseShares.AnyAsync(
            x => x.UserId == userId
                 && x.UpdateState != UpdateState.Deleted
                 && x.Expense != null
                 && x.Expense.GroupId == groupId
                 && x.Expense.UpdateState != UpdateState.Deleted,
            cancellationToken);

        var hasPayments = await db.Payments.AnyAsync(
            x => x.GroupId == groupId
                 && x.UpdateState != UpdateState.Deleted
                 && (x.FromUserId == userId || x.ToUserId == userId),
            cancellationToken);

        if (hasExpenses || hasShares || hasPayments)
        {
            return Result.Fail(
                "Das Mitglied ist noch in Ausgaben oder Zahlungen enthalten und kann nicht entfernt werden. "
                + "Doppelte Konten lassen sich unter Administration > Benutzer > Zusammenführen vereinen.");
        }

        if (member.IsGroupAdmin && await IsLastGroupAdminAsync(groupId, userId, cancellationToken))
        {
            return Result.Fail("Der letzte Gruppen-Administrator kann nicht entfernt werden.");
        }

        member.UpdateState = UpdateState.Deleted;

        await history.LogAsync(
            groupId,
            userId,
            HistoryService.EntityTypes.GroupMember,
            userId,
            HistoryService.Actions.Left,
            $"\"{member.User?.DisplayName ?? "Unbekannt"}\" wurde aus der Gruppe entfernt.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Sets the share weight of a member (family = 3, ...).</summary>
    public async Task<Result> SetShareFactorAsync(long groupId, long userId, int shareFactor, CancellationToken cancellationToken = default)
    {
        var member = await db.GroupMembers.FirstOrDefaultAsync(
            x => x.GroupId == groupId && x.UserId == userId && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        if (member is null)
        {
            return Result.Fail("Die Mitgliedschaft wurde nicht gefunden.");
        }

        member.ShareFactor = NormalizeShareFactor(shareFactor);
        member.UpdateState = UpdateState.Updated;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    public async Task<Result> SetGroupAdminAsync(long groupId, long userId, bool isGroupAdmin, CancellationToken cancellationToken = default)
    {
        var member = await db.GroupMembers.FirstOrDefaultAsync(
            x => x.GroupId == groupId && x.UserId == userId && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        if (member is null)
        {
            return Result.Fail("Die Mitgliedschaft wurde nicht gefunden.");
        }

        if (!isGroupAdmin && member.IsGroupAdmin && await IsLastGroupAdminAsync(groupId, userId, cancellationToken))
        {
            return Result.Fail("Die Gruppe braucht mindestens einen Administrator.");
        }

        member.IsGroupAdmin = isGroupAdmin;
        member.UpdateState = UpdateState.Updated;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>Invalidates the current invite link and returns the new token.</summary>
    public async Task<Result<string>> RegenerateInviteTokenAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail<string>("Die Gruppe wurde nicht gefunden.");
        }

        group.InviteToken = Guid.NewGuid().ToString();
        group.UpdateState = UpdateState.Updated;

        await history.LogAsync(
            groupId,
            null,
            HistoryService.EntityTypes.Group,
            groupId,
            HistoryService.Actions.Updated,
            "Der Einladungslink wurde neu erzeugt.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(group.InviteToken);
    }

    public async Task<Result> SetInviteEnabledAsync(long groupId, bool enabled, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail("Die Gruppe wurde nicht gefunden.");
        }

        group.InviteEnabled = enabled;
        group.UpdateState = UpdateState.Updated;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>Resolves a group from its read-only token, only when the link is enabled.</summary>
    public async Task<Group?> GetGroupByReadOnlyTokenAsync(string? readOnlyToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(readOnlyToken))
        {
            return null;
        }

        return await db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ReadOnlyToken == readOnlyToken && x.ReadOnlyEnabled && x.UpdateState != UpdateState.Deleted,
                cancellationToken);
    }

    public async Task<Result<string>> RegenerateReadOnlyTokenAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail<string>("Die Gruppe wurde nicht gefunden.");
        }

        group.ReadOnlyToken = Guid.NewGuid().ToString();
        group.UpdateState = UpdateState.Updated;

        await history.LogAsync(
            groupId,
            null,
            HistoryService.EntityTypes.Group,
            groupId,
            HistoryService.Actions.Updated,
            "Der Nur-Lese-Link wurde neu erzeugt.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(group.ReadOnlyToken);
    }

    public async Task<Result> SetReadOnlyEnabledAsync(long groupId, bool enabled, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(
            x => x.Id == groupId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (group is null)
        {
            return Result.Fail("Die Gruppe wurde nicht gefunden.");
        }

        group.ReadOnlyEnabled = enabled;
        group.UpdateState = UpdateState.Updated;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>
    /// Invite-link entry point: creates an anonymous user with the given display
    /// name and adds it to the group. The Join page then signs that user in.
    /// </summary>
    public async Task<Result<GroupJoinResult>> JoinByInviteTokenAsync(
        string? inviteToken,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Fail<GroupJoinResult>("Bitte einen Namen angeben.");
        }

        var group = await GetGroupByInviteTokenAsync(inviteToken, cancellationToken);
        if (group is null)
        {
            return Result.Fail<GroupJoinResult>("Der Einladungslink ist ungültig oder wurde deaktiviert.");
        }

        var user = await users.CreateAnonymousUserAsync(displayName, cancellationToken);

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = user.Id,
            ShareFactor = 1,
            IsGroupAdmin = false,
            UpdateState = UpdateState.Created
        });

        await history.LogAsync(
            group.Id,
            user.Id,
            HistoryService.EntityTypes.GroupMember,
            user.Id,
            HistoryService.Actions.Joined,
            $"\"{user.DisplayName}\" ist über den Einladungslink beigetreten.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} joined group {GroupId} via invite", user.Id, group.Id);

        return Result.Ok(new GroupJoinResult
        {
            Group = group,
            User = user,
            CreatedNewMembership = true
        });
    }

    /// <summary>
    /// Adds an already signed in user to a group by invite token, without
    /// creating a new anonymous identity.
    /// </summary>
    public async Task<Result<GroupJoinResult>> JoinExistingUserByInviteTokenAsync(
        string? inviteToken,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var group = await GetGroupByInviteTokenAsync(inviteToken, cancellationToken);
        if (group is null)
        {
            return Result.Fail<GroupJoinResult>("Der Einladungslink ist ungültig oder wurde deaktiviert.");
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Fail<GroupJoinResult>("Der Benutzer wurde nicht gefunden.");
        }

        if (await IsMemberAsync(group.Id, userId, cancellationToken))
        {
            return Result.Ok(new GroupJoinResult { Group = group, User = user, CreatedNewMembership = false });
        }

        var added = await AddMemberAsync(group.Id, userId, 1, false, cancellationToken);
        if (added.IsFailure)
        {
            return Result.Fail<GroupJoinResult>(added.Error!);
        }

        return Result.Ok(new GroupJoinResult { Group = group, User = user, CreatedNewMembership = true });
    }

    private async Task<bool> IsLastGroupAdminAsync(long groupId, long userId, CancellationToken cancellationToken)
    {
        var otherAdmins = await db.GroupMembers.CountAsync(
            x => x.GroupId == groupId
                 && x.UserId != userId
                 && x.IsGroupAdmin
                 && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        return otherAdmins == 0;
    }

    private static int NormalizeShareFactor(int shareFactor) => Math.Clamp(shareFactor, 1, 100);

    private static string NormalizeCurrency(string? currency, string fallback)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return string.IsNullOrWhiteSpace(fallback) ? "EUR" : fallback.Trim().ToUpperInvariant();
        }

        var value = currency.Trim().ToUpperInvariant();
        return value.Length > 3 ? value[..3] : value;
    }
}
