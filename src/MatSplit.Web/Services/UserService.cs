using System.Text.Json;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Everything around users: login validation, profile, anonymous invite users
/// and the merge of a duplicated anonymous user into a real one
/// (the "Horst / Horsti" case).
/// </summary>
public sealed class UserService(AppDbContext db, HistoryService history, ILogger<UserService> logger)
{
    public async Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UpdateState != UpdateState.Deleted, cancellationToken);
    }

    public async Task<User?> GetByTokenAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Token == token && x.UpdateState != UpdateState.Deleted, cancellationToken);
    }

    /// <summary>
    /// Paged user list for /Admin/Users. Supported sort keys:
    /// name, name_desc, email, email_desc, role, role_desc, created, created_desc.
    /// </summary>
    public async Task<PagedResult<User>> ListUsersAsync(
        string? search = null,
        UserRole? role = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        string? sort = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = Paging.Normalize(page, pageSize);

        var query = db.Users.AsNoTracking().AsQueryable();

        if (!includeDeleted)
        {
            query = query.Where(x => x.UpdateState != UpdateState.Deleted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.Like(x.DisplayName, "%" + term + "%")
                || (x.Email != null && EF.Functions.Like(x.Email, "%" + term + "%")));
        }

        if (role.HasValue)
        {
            query = query.Where(x => x.Role == role.Value);
        }

        query = sort switch
        {
            "name_desc" => query.OrderByDescending(x => x.DisplayName),
            "email" => query.OrderBy(x => x.Email).ThenBy(x => x.DisplayName),
            "email_desc" => query.OrderByDescending(x => x.Email).ThenBy(x => x.DisplayName),
            "role" => query.OrderBy(x => x.Role).ThenBy(x => x.DisplayName),
            "role_desc" => query.OrderByDescending(x => x.Role).ThenBy(x => x.DisplayName),
            "created" => query.OrderBy(x => x.CreateDate),
            "created_desc" => query.OrderByDescending(x => x.CreateDate),
            _ => query.OrderBy(x => x.DisplayName)
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(Paging.Skip(safePage, safeSize))
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<User>(items, safePage, safeSize, total);
    }

    /// <summary>All active users ordered by name, for select boxes.</summary>
    public async Task<IReadOnlyList<User>> ListAllActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted && x.MergedIntoUserId == null)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a login capable user. Password may be null for accounts that are
    /// only used as expense participants.
    /// </summary>
    public async Task<Result<User>> CreateLocalUserAsync(
        string displayName,
        string? email,
        string? password,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Fail<User>("Der Anzeigename darf nicht leer sein.");
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is not null && await EmailExistsAsync(normalizedEmail, null, cancellationToken))
        {
            return Result.Fail<User>("Diese E-Mail-Adresse ist bereits vergeben.");
        }

        var user = new User
        {
            Token = Guid.NewGuid().ToString(),
            DisplayName = displayName.Trim(),
            Email = normalizedEmail,
            PasswordHash = string.IsNullOrEmpty(password) ? null : PasswordHasher.Hash(password),
            Role = role,
            IsAnonymous = false,
            ThemePreference = ThemeMode.System,
            UpdateState = UpdateState.Created
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await history.LogAsync(
            null,
            user.Id,
            HistoryService.EntityTypes.User,
            user.Id,
            HistoryService.Actions.Created,
            $"Benutzer \"{user.DisplayName}\" wurde angelegt.",
            cancellationToken: cancellationToken);

        return Result.Ok(user);
    }

    /// <summary>
    /// Creates a nameless invite user. Anonymous users cannot log in with a
    /// password; they are bound to their session cookie.
    /// </summary>
    public async Task<User> CreateAnonymousUserAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Gast" : displayName.Trim();

        var user = new User
        {
            Token = Guid.NewGuid().ToString(),
            DisplayName = name,
            Role = UserRole.Anonymous,
            IsAnonymous = true,
            ThemePreference = ThemeMode.System,
            UpdateState = UpdateState.Created
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return user;
    }

    /// <summary>
    /// Validates a login. <paramref name="emailOrName"/> matches either the
    /// e-mail address or the display name. Returns null on failure.
    /// </summary>
    public async Task<User?> ValidatePasswordAsync(string? emailOrName, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailOrName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var login = emailOrName.Trim();
        var normalized = login.ToLowerInvariant();

        var user = await db.Users
            .AsNoTracking()
            .Where(x => x.UpdateState != UpdateState.Deleted
                        && x.MergedIntoUserId == null
                        && x.PasswordHash != null
                        // SQLite compares strings byte wise, so both sides are
                        // lower cased: "Admin" has to log in just like "admin".
                        && ((x.Email != null && x.Email.ToLower() == normalized)
                            || x.DisplayName.ToLower() == normalized))
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            logger.LogInformation("Login failed: unknown account {Login}", login);
            return null;
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            logger.LogInformation("Login failed: wrong password for user {UserId}", user.Id);
            return null;
        }

        return user;
    }

    /// <summary>
    /// Updates the own profile. Pass <paramref name="newPassword"/> as null to
    /// keep the current password.
    /// </summary>
    public async Task<Result> UpdateProfileAsync(
        long userId,
        string displayName,
        string? email,
        string? payPalAddress,
        string? newPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Fail("Der Anzeigename darf nicht leer sein.");
        }

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (user is null)
        {
            return Result.Fail("Benutzer wurde nicht gefunden.");
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is not null && await EmailExistsAsync(normalizedEmail, userId, cancellationToken))
        {
            return Result.Fail("Diese E-Mail-Adresse ist bereits vergeben.");
        }

        user.DisplayName = displayName.Trim();
        user.Email = normalizedEmail;
        user.PayPalAddress = string.IsNullOrWhiteSpace(payPalAddress) ? null : payPalAddress.Trim();
        user.UpdateState = UpdateState.Updated;

        if (!string.IsNullOrEmpty(newPassword))
        {
            user.PasswordHash = PasswordHasher.Hash(newPassword);
        }

        await db.SaveChangesAsync(cancellationToken);

        await history.LogAsync(
            null,
            userId,
            HistoryService.EntityTypes.User,
            userId,
            HistoryService.Actions.Updated,
            $"Profil von \"{user.DisplayName}\" wurde geändert.",
            cancellationToken: cancellationToken);

        return Result.Ok();
    }

    /// <summary>Admin only: changes the global role of a user.</summary>
    public async Task<Result> SetRoleAsync(long userId, UserRole role, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (user is null)
        {
            return Result.Fail("Benutzer wurde nicht gefunden.");
        }

        if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastAdminAsync(userId, cancellationToken))
        {
            return Result.Fail("Der letzte Administrator kann nicht herabgestuft werden.");
        }

        user.Role = role;
        user.IsAnonymous = role == UserRole.Anonymous;
        user.UpdateState = UpdateState.Updated;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>Stores the colour scheme preference.</summary>
    public async Task<Result> SetThemeAsync(long userId, ThemeMode theme, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (user is null)
        {
            return Result.Fail("Benutzer wurde nicht gefunden.");
        }

        user.ThemePreference = theme;
        user.UpdateState = UpdateState.Updated;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>Soft deletes a user and ends all of their sessions.</summary>
    public async Task<Result> SoftDeleteUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (user is null)
        {
            return Result.Fail("Benutzer wurde nicht gefunden.");
        }

        if (user.Role == UserRole.Admin && await IsLastAdminAsync(userId, cancellationToken))
        {
            return Result.Fail("Der letzte Administrator kann nicht gelöscht werden.");
        }

        user.UpdateState = UpdateState.Deleted;

        var sessions = await db.UserSessions.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.UpdateState = UpdateState.Deleted;
            session.ExpiresUtc = DateTime.UtcNow;
        }

        var memberships = await db.GroupMembers
            .Where(x => x.UserId == userId && x.UpdateState != UpdateState.Deleted)
            .ToListAsync(cancellationToken);

        foreach (var membership in memberships)
        {
            membership.UpdateState = UpdateState.Deleted;
        }

        await history.LogAsync(
            null,
            userId,
            HistoryService.EntityTypes.User,
            userId,
            HistoryService.Actions.Deleted,
            $"Benutzer \"{user.DisplayName}\" wurde gelöscht.",
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Merges <paramref name="sourceUserId"/> into <paramref name="targetUserId"/>:
    /// moves memberships, expenses, shares and payments, then soft deletes the
    /// source and records MergedIntoUserId. Solves duplicated invite users.
    /// </summary>
    public async Task<Result> MergeUsersAsync(long sourceUserId, long targetUserId, CancellationToken cancellationToken = default)
    {
        if (sourceUserId == targetUserId)
        {
            return Result.Fail("Quelle und Ziel dürfen nicht identisch sein.");
        }

        var source = await db.Users.FirstOrDefaultAsync(
            x => x.Id == sourceUserId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        var target = await db.Users.FirstOrDefaultAsync(
            x => x.Id == targetUserId && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (source is null)
        {
            return Result.Fail("Der Quell-Benutzer wurde nicht gefunden.");
        }

        if (target is null)
        {
            return Result.Fail("Der Ziel-Benutzer wurde nicht gefunden.");
        }

        if (source.Role == UserRole.Admin && await IsLastAdminAsync(sourceUserId, cancellationToken))
        {
            return Result.Fail("Der letzte Administrator kann nicht zusammengefuehrt werden.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var movedMemberships = await MoveMembershipsAsync(sourceUserId, targetUserId, cancellationToken);
        var movedExpenses = await MoveExpensesAsync(sourceUserId, targetUserId, cancellationToken);
        var movedShares = await MoveSharesAsync(sourceUserId, targetUserId, cancellationToken);
        var movedPayments = await MovePaymentsAsync(sourceUserId, targetUserId, cancellationToken);

        // Take over data the target does not have yet.
        target.PayPalAddress ??= source.PayPalAddress;
        target.Email ??= source.Email;
        target.UpdateState = UpdateState.Updated;

        source.MergedIntoUserId = targetUserId;
        source.UpdateState = UpdateState.Deleted;

        var sessions = await db.UserSessions.Where(x => x.UserId == sourceUserId).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.UpdateState = UpdateState.Deleted;
            session.ExpiresUtc = DateTime.UtcNow;
        }

        var details = JsonSerializer.Serialize(new
        {
            SourceUserId = sourceUserId,
            SourceDisplayName = source.DisplayName,
            TargetUserId = targetUserId,
            TargetDisplayName = target.DisplayName,
            MovedMemberships = movedMemberships,
            MovedExpenses = movedExpenses,
            MovedShares = movedShares,
            MovedPayments = movedPayments
        });

        await history.LogAsync(
            null,
            targetUserId,
            HistoryService.EntityTypes.User,
            targetUserId,
            HistoryService.Actions.Merged,
            $"\"{source.DisplayName}\" wurde in \"{target.DisplayName}\" zusammengefuehrt.",
            details,
            saveChanges: false,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Merged user {Source} into {Target}", sourceUserId, targetUserId);
        return Result.Ok();
    }

    /// <summary>
    /// Finds or creates the local user of a person that MatIdentity signed in (single sign-on):
    /// (1) the user with this <c>sub</c> (a merged one leads to the user it was merged into),
    /// (2) else the one active user with the same e-mail address, which is linked to the <c>sub</c>,
    /// (3) else a new local user without password. A deleted (disabled) user stays out: the result is a failure.
    /// When <see cref="ExternalIdentity.Role"/> is <c>admin</c> or <c>user</c> the global role follows it at every sign-in;
    /// without it the local role stays (the app manages its roles). A link guest becomes a regular user.
    /// </summary>
    public async Task<Result<User>> SignInExternalAsync(ExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(identity.Subject))
        {
            return Result.Fail<User>("MatIdentity hat keine Kennung (sub) geliefert.");
        }

        var subject = identity.Subject.Trim();
        var email = NormalizeEmail(identity.Email);
        var disabled = Result.Fail<User>("Dein Konto ist in MatSplit gesperrt. Bitte wende dich an einen Administrator.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.ExternalSubject == subject, cancellationToken);

        if (user?.MergedIntoUserId is not null)
        {
            var effective = await ResolveEffectiveUserAsync(user.Id, cancellationToken);
            user = effective is null ? null : await db.Users.FirstOrDefaultAsync(x => x.Id == effective.Id, cancellationToken);
            if (user is null || user.UpdateState == UpdateState.Deleted)
            {
                return disabled;
            }
        }
        else if (user is not null && user.UpdateState == UpdateState.Deleted)
        {
            return disabled;
        }

        var created = false;
        var linked = false;

        if (user is null && email is not null)
        {
            var sameEmail = await db.Users
                .Where(x => x.Email != null && x.Email.ToLower() == email && x.MergedIntoUserId == null)
                .ToListAsync(cancellationToken);

            var active = sameEmail.Where(x => x.UpdateState != UpdateState.Deleted).ToList();

            if (active.Count == 1 && active[0].ExternalSubject is null)
            {
                user = active[0];
                user.ExternalSubject = subject;
                linked = true;
            }
            else if (active.Count == 0 && sameEmail.Count > 0)
            {
                // The only account with this address was deleted by an administrator: no new one behind its back.
                return disabled;
            }
        }

        if (user is null)
        {
            var emailTaken = email is not null && await EmailExistsAsync(email, null, cancellationToken);
            var name = FirstNonEmpty(identity.Name, identity.PreferredUsername, email) ?? "MatIdentity";

            user = new User
            {
                Token = Guid.NewGuid().ToString(),
                DisplayName = name.Length > 200 ? name[..200] : name,
                // Two accounts with one address would break the sign-in by e-mail; the address stays with the existing one.
                Email = emailTaken ? null : email,
                Role = UserRole.User,
                IsAnonymous = false,
                ThemePreference = ThemeMode.System,
                ExternalSubject = subject,
                UpdateState = UpdateState.Created
            };

            db.Users.Add(user);
            created = true;
        }

        var role = identity.Role?.Trim().ToLowerInvariant() switch
        {
            "admin" => UserRole.Admin,
            "user" => UserRole.User,
            _ => (UserRole?)null
        };

        if (role is { } wanted && user.Role != wanted)
        {
            if (!created && user.Role == UserRole.Admin && await IsLastAdminAsync(user.Id, cancellationToken))
            {
                logger.LogWarning("MatIdentity asks for role {Role} for user {UserId}, who is the last administrator; the role stays", wanted, user.Id);
            }
            else
            {
                user.Role = wanted;
            }
        }

        if (user.Role == UserRole.Anonymous || user.IsAnonymous)
        {
            // Whoever comes through MatIdentity is a real person, not a link guest.
            user.Role = user.Role == UserRole.Anonymous ? UserRole.User : user.Role;
            user.IsAnonymous = false;
        }

        if (user.Email is null && email is not null && !created && !await EmailExistsAsync(email, user.Id, cancellationToken))
        {
            user.Email = email;
        }

        if (!created && db.Entry(user).State == EntityState.Modified)
        {
            user.UpdateState = UpdateState.Updated;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (created || linked)
        {
            await history.LogAsync(
                null,
                user.Id,
                HistoryService.EntityTypes.User,
                user.Id,
                created ? HistoryService.Actions.Created : HistoryService.Actions.Updated,
                created
                    ? $"Benutzer \"{user.DisplayName}\" wurde bei der ersten Anmeldung über MatIdentity angelegt."
                    : $"Benutzer \"{user.DisplayName}\" wurde über die E-Mail-Adresse mit MatIdentity verknüpft.",
                cancellationToken: cancellationToken);
        }

        return Result.Ok(user);
    }

    /// <summary>
    /// Follows the MergedIntoUserId chain to the surviving user.
    /// </summary>
    public async Task<User?> ResolveEffectiveUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var current = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        var guard = 0;
        while (current?.MergedIntoUserId is { } next && guard++ < 20)
        {
            current = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == next, cancellationToken);
        }

        return current;
    }

    private async Task<int> MoveMembershipsAsync(long sourceUserId, long targetUserId, CancellationToken cancellationToken)
    {
        var sourceMemberships = await db.GroupMembers
            .Where(x => x.UserId == sourceUserId)
            .ToListAsync(cancellationToken);

        if (sourceMemberships.Count == 0)
        {
            return 0;
        }

        var groupIds = sourceMemberships.Select(x => x.GroupId).Distinct().ToList();

        var targetMemberships = await db.GroupMembers
            .Where(x => x.UserId == targetUserId && groupIds.Contains(x.GroupId))
            .ToListAsync(cancellationToken);

        var moved = 0;
        foreach (var membership in sourceMemberships)
        {
            var existing = targetMemberships.FirstOrDefault(x => x.GroupId == membership.GroupId);
            if (existing is null)
            {
                membership.UserId = targetUserId;
                membership.UpdateState = UpdateState.Updated;
                moved++;
                continue;
            }

            // Target is already a member: keep the stronger rights and drop the duplicate.
            existing.IsGroupAdmin = existing.IsGroupAdmin || membership.IsGroupAdmin;
            existing.ShareFactor = Math.Max(existing.ShareFactor, membership.ShareFactor);
            existing.UpdateState = UpdateState.Updated;
            db.GroupMembers.Remove(membership);
        }

        return moved;
    }

    private async Task<int> MoveExpensesAsync(long sourceUserId, long targetUserId, CancellationToken cancellationToken)
    {
        var expenses = await db.Expenses.Where(x => x.PaidByUserId == sourceUserId).ToListAsync(cancellationToken);
        foreach (var expense in expenses)
        {
            expense.PaidByUserId = targetUserId;
            if (expense.UpdateState != UpdateState.Deleted)
            {
                expense.UpdateState = UpdateState.Updated;
            }
        }

        return expenses.Count;
    }

    private async Task<int> MoveSharesAsync(long sourceUserId, long targetUserId, CancellationToken cancellationToken)
    {
        var shares = await db.ExpenseShares.Where(x => x.UserId == sourceUserId).ToListAsync(cancellationToken);

        var expenseIds = shares.Select(x => x.ExpenseId).Distinct().ToList();
        var targetShares = await db.ExpenseShares
            .Where(x => x.UserId == targetUserId && expenseIds.Contains(x.ExpenseId))
            .ToListAsync(cancellationToken);

        var moved = 0;
        foreach (var share in shares)
        {
            var existing = targetShares.FirstOrDefault(x => x.ExpenseId == share.ExpenseId);
            if (existing is null)
            {
                share.UserId = targetUserId;
                if (share.UpdateState != UpdateState.Deleted)
                {
                    share.UpdateState = UpdateState.Updated;
                }

                moved++;
                continue;
            }

            // Both users participated: add the factors / fixed amounts up.
            existing.ShareFactor += share.ShareFactor;
            if (share.ShareAmountCents.HasValue)
            {
                existing.ShareAmountCents = (existing.ShareAmountCents ?? 0) + share.ShareAmountCents.Value;
            }

            existing.UpdateState = UpdateState.Updated;
            db.ExpenseShares.Remove(share);
        }

        return moved;
    }

    private async Task<int> MovePaymentsAsync(long sourceUserId, long targetUserId, CancellationToken cancellationToken)
    {
        var payments = await db.Payments
            .Where(x => x.FromUserId == sourceUserId || x.ToUserId == sourceUserId)
            .ToListAsync(cancellationToken);

        foreach (var payment in payments)
        {
            if (payment.FromUserId == sourceUserId)
            {
                payment.FromUserId = targetUserId;
            }

            if (payment.ToUserId == sourceUserId)
            {
                payment.ToUserId = targetUserId;
            }

            // A payment to oneself is meaningless after the merge.
            if (payment.FromUserId == payment.ToUserId)
            {
                payment.UpdateState = UpdateState.Deleted;
                continue;
            }

            if (payment.UpdateState != UpdateState.Deleted)
            {
                payment.UpdateState = UpdateState.Updated;
            }
        }

        return payments.Count;
    }

    private async Task<bool> IsLastAdminAsync(long userId, CancellationToken cancellationToken)
    {
        var otherAdmins = await db.Users.CountAsync(
            x => x.Id != userId
                 && x.Role == UserRole.Admin
                 && x.UpdateState != UpdateState.Deleted
                 && x.MergedIntoUserId == null,
            cancellationToken);

        return otherAdmins == 0;
    }

    private async Task<bool> EmailExistsAsync(string email, long? exceptUserId, CancellationToken cancellationToken)
    {
        return await db.Users.AnyAsync(
            x => x.Email != null
                 && x.Email.ToLower() == email
                 && x.UpdateState != UpdateState.Deleted
                 && (exceptUserId == null || x.Id != exceptUserId),
            cancellationToken);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.Select(x => x?.Trim()).FirstOrDefault(x => !string.IsNullOrEmpty(x));

    private static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
