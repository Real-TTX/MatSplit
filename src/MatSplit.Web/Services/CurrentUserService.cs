using System.Security.Claims;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace MatSplit.Web.Services;

/// <summary>
/// Per-request access to the signed in user. Razor Pages inject this instead of
/// digging through claims. The user record itself is cached in
/// HttpContext.Items by <see cref="SessionAuthenticationMiddleware"/>, so
/// <see cref="CurrentUser"/> is free of database round trips.
/// Also owns cookie sign in / sign out so Login, Logout and Join share one code path.
/// </summary>
public sealed class CurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db,
    SessionService sessions)
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    public ClaimsPrincipal? Principal => Context?.User;

    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true;

    /// <summary>Id of the signed in user, null when anonymous visitor.</summary>
    public long? UserId => MatSplitClaims.GetUserId(Context?.User);

    /// <summary>The signed in user record, null when not signed in.</summary>
    public User? CurrentUser => Context?.GetCachedUser();

    public string DisplayName => CurrentUser?.DisplayName
        ?? Context?.User.Identity?.Name
        ?? "Gast";

    public UserRole Role => CurrentUser?.Role ?? MatSplitClaims.GetRole(Context?.User);

    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>True for link-invited users without a password.</summary>
    public bool IsAnonymousUser => CurrentUser?.IsAnonymous
        ?? Context?.User.FindFirst(MatSplitClaims.IsAnonymousClaim)?.Value == "1";

    public ThemeMode Theme
    {
        get
        {
            if (CurrentUser is not null)
            {
                return CurrentUser.ThemePreference;
            }

            var raw = Context?.User.FindFirst(MatSplitClaims.ThemeClaim)?.Value;
            return Enum.TryParse<ThemeMode>(raw, out var theme) ? theme : ThemeMode.System;
        }
    }

    /// <summary>Token of the active session, used for logout.</summary>
    public string? SessionToken => MatSplitClaims.GetSessionToken(Context?.User);

    /// <summary>
    /// Id of the signed in user or an exception. Use in page handlers that are
    /// already protected by an authorization policy.
    /// </summary>
    public long RequireUserId()
        => UserId ?? throw new InvalidOperationException("No user is signed in.");

    /// <summary>True when the user is an (active) member of the group.</summary>
    public async Task<bool> IsMemberAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        if (userId is null || groupId <= 0)
        {
            return false;
        }

        return await db.GroupMembers.AnyAsync(
            x => x.GroupId == groupId && x.UserId == userId.Value && x.UpdateState != UpdateState.Deleted,
            cancellationToken);
    }

    /// <summary>
    /// True when the user carries the IsGroupAdmin flag in that group.
    /// Global admins are group admins everywhere.
    /// </summary>
    public async Task<bool> IsGroupAdminAsync(long groupId, CancellationToken cancellationToken = default)
    {
        if (IsAdmin)
        {
            return true;
        }

        var userId = UserId;
        if (userId is null || groupId <= 0)
        {
            return false;
        }

        return await db.GroupMembers.AnyAsync(
            x => x.GroupId == groupId
                 && x.UserId == userId.Value
                 && x.IsGroupAdmin
                 && x.UpdateState != UpdateState.Deleted,
            cancellationToken);
    }

    /// <summary>Read access to a group: member or global admin.</summary>
    public async Task<bool> CanViewGroupAsync(long groupId, CancellationToken cancellationToken = default)
        => IsAdmin || await IsMemberAsync(groupId, cancellationToken);

    /// <summary>Write access to group settings and membership.</summary>
    public async Task<bool> CanManageGroupAsync(long groupId, CancellationToken cancellationToken = default)
        => await IsGroupAdminAsync(groupId, cancellationToken);

    /// <summary>
    /// Creates a session row and issues the auth cookie. Used by
    /// /Account/Login and /Join.
    /// </summary>
    public async Task SignInAsync(User user, bool isPersistent = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var context = Context ?? throw new InvalidOperationException("No HttpContext available for sign in.");

        var userAgent = context.Request.Headers.UserAgent.ToString();
        var sessionToken = await sessions.CreateSessionAsync(user.Id, userAgent, cancellationToken);
        var principal = MatSplitClaims.CreatePrincipal(user, sessionToken);

        var properties = new AuthenticationProperties
        {
            IsPersistent = isPersistent,
            IssuedUtc = DateTimeOffset.UtcNow
        };

        await context.SignInAsync(MatSplitClaims.AuthenticationScheme, principal, properties);

        context.User = principal;
        context.Items[MatSplitHttpItems.CurrentUser] = user;
    }

    /// <summary>Ends the session and clears the cookie.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var context = Context;
        if (context is null)
        {
            return;
        }

        await sessions.EndSessionAsync(SessionToken, cancellationToken);
        await context.SignOutAsync(MatSplitClaims.AuthenticationScheme);

        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Items.Remove(MatSplitHttpItems.CurrentUser);
        context.Items.Remove(MatSplitHttpItems.CurrentSession);
    }

    /// <summary>
    /// Re-issues the cookie after the profile changed (name, role, theme), so
    /// the UI updates without a new login.
    /// </summary>
    public async Task RefreshSignInAsync(CancellationToken cancellationToken = default)
    {
        var context = Context;
        var userId = UserId;
        var sessionToken = SessionToken;

        if (context is null || userId is null || string.IsNullOrWhiteSpace(sessionToken))
        {
            return;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == userId.Value && x.UpdateState != UpdateState.Deleted, cancellationToken);

        if (user is null)
        {
            return;
        }

        var principal = MatSplitClaims.CreatePrincipal(user, sessionToken);
        await context.SignInAsync(MatSplitClaims.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = DateTimeOffset.UtcNow
        });

        context.User = principal;
        context.Items[MatSplitHttpItems.CurrentUser] = user;
    }

    /// <summary>Convenience for the theme switcher in the left menu.</summary>
    public static string ToThemeAttribute(ThemeMode theme) => theme switch
    {
        ThemeMode.Dark => "dark",
        ThemeMode.Light => "light",
        _ => "system"
    };
}
