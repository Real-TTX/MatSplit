using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Server side session store backing the auth cookie. The cookie only carries
/// the session token, so a session can be revoked by deleting its row.
/// </summary>
public sealed class SessionService(AppDbContext db, AppConfigService appConfig, ILogger<SessionService> logger)
{
    /// <summary>
    /// Creates a new session for the user and returns its token, which belongs
    /// into the auth cookie via <see cref="MatSplitClaims.CreatePrincipal"/>.
    /// </summary>
    public async Task<string> CreateSessionAsync(long userId, string? userAgent, CancellationToken cancellationToken = default)
    {
        var config = await appConfig.GetAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var session = new UserSession
        {
            Token = PasswordHasher.CreateRandomToken(),
            UserId = userId,
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(config.SessionLifetimeDays),
            LastSeenUtc = now,
            UserAgent = Truncate(userAgent, 512),
            UpdateState = UpdateState.Created
        };

        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Created session for user {UserId}", userId);
        return session.Token;
    }

    /// <summary>
    /// Loads a live session including its user. Returns null when the token is
    /// unknown, the session expired or user/session were soft deleted.
    /// </summary>
    public async Task<UserSession?> ResolveSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var now = DateTime.UtcNow;

        return await db.UserSessions
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.Token == token
                     && x.UpdateState != UpdateState.Deleted
                     && x.ExpiresUtc > now
                     && x.User != null
                     && x.User.UpdateState != UpdateState.Deleted
                     && x.User.MergedIntoUserId == null,
                cancellationToken);
    }

    /// <summary>
    /// Refreshes LastSeenUtc and slides the expiry window forward.
    /// </summary>
    public async Task TouchAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(
            x => x.Token == token && x.UpdateState != UpdateState.Deleted,
            cancellationToken);

        if (session is null)
        {
            return;
        }

        var config = await appConfig.GetAsync(cancellationToken);
        var now = DateTime.UtcNow;

        session.LastSeenUtc = now;
        session.ExpiresUtc = now.AddDays(config.SessionLifetimeDays);
        session.UpdateState = UpdateState.Updated;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Soft deletes a single session (logout).</summary>
    public async Task EndSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(x => x.Token == token, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.UpdateState = UpdateState.Deleted;
        session.ExpiresUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Soft deletes every session of a user (kick from all devices).</summary>
    public async Task EndAllSessionsForUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var sessions = await db.UserSessions
            .Where(x => x.UserId == userId && x.UpdateState != UpdateState.Deleted)
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var session in sessions)
        {
            session.UpdateState = UpdateState.Deleted;
            session.ExpiresUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Hard deletes expired session rows. Called once at startup.
    /// </summary>
    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var removed = await db.UserSessions
            .Where(x => x.ExpiresUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} expired sessions", removed);
        }

        return removed;
    }

    /// <summary>Active sessions of a user, newest first (profile page).</summary>
    public async Task<IReadOnlyList<UserSession>> ListSessionsForUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await db.UserSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.UpdateState != UpdateState.Deleted && x.ExpiresUtc > now)
            .OrderByDescending(x => x.LastSeenUtc)
            .ToListAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
