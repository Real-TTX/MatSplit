using System.Security.Claims;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Services;
using Microsoft.AspNetCore.Authentication;

namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Keys used to cache per-request state in <c>HttpContext.Items</c>.
/// </summary>
public static class MatSplitHttpItems
{
    public const string CurrentUser = "MatSplit.CurrentUser";

    public const string CurrentSession = "MatSplit.CurrentSession";
}

/// <summary>
/// Validates the cookie principal against the UserSessions table on every
/// request. A cookie whose session was deleted or has expired is signed out
/// immediately, so revoking a session takes effect at once.
/// Runs after <c>UseAuthentication</c> and before <c>UseAuthorization</c>.
/// </summary>
public sealed class SessionAuthenticationMiddleware(RequestDelegate next, ILogger<SessionAuthenticationMiddleware> logger)
{
    /// <summary>Do not hammer the database with LastSeenUtc updates.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(2);

    public async Task InvokeAsync(HttpContext context, SessionService sessionService)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var sessionToken = MatSplitClaims.GetSessionToken(context.User);
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            await SignOutAsync(context, "Cookie ohne Session-Token.");
            await next(context);
            return;
        }

        var session = await sessionService.ResolveSessionAsync(sessionToken);
        if (session?.User is null)
        {
            await SignOutAsync(context, "Session ist abgelaufen oder wurde beendet.");
            await next(context);
            return;
        }

        // Re-project the principal from the database so role, display name and
        // theme changes are picked up without forcing a new login.
        context.User = MatSplitClaims.CreatePrincipal(session.User, sessionToken);
        context.Items[MatSplitHttpItems.CurrentUser] = session.User;
        context.Items[MatSplitHttpItems.CurrentSession] = session;

        if (DateTime.UtcNow - session.LastSeenUtc > TouchInterval)
        {
            await sessionService.TouchAsync(sessionToken);
        }

        await next(context);
    }

    private async Task SignOutAsync(HttpContext context, string reason)
    {
        logger.LogDebug("Signing out request to {Path}: {Reason}", context.Request.Path, reason);

        await context.SignOutAsync(MatSplitClaims.AuthenticationScheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Items.Remove(MatSplitHttpItems.CurrentUser);
        context.Items.Remove(MatSplitHttpItems.CurrentSession);
    }
}

/// <summary>
/// Registration helpers for the session middleware.
/// </summary>
public static class SessionAuthenticationMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="SessionAuthenticationMiddleware"/> to the pipeline.
    /// Must be called between UseAuthentication and UseAuthorization.
    /// </summary>
    public static IApplicationBuilder UseMatSplitSessionValidation(this IApplicationBuilder app)
        => app.UseMiddleware<SessionAuthenticationMiddleware>();

    /// <summary>Reads the cached user of the current request, if any.</summary>
    public static User? GetCachedUser(this HttpContext context)
        => context.Items.TryGetValue(MatSplitHttpItems.CurrentUser, out var value) ? value as User : null;
}
