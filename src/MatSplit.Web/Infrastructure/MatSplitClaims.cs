using System.Security.Claims;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;

namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Claim type constants and the factory that turns a <see cref="User"/> plus a
/// session token into the cookie principal.
/// </summary>
public static class MatSplitClaims
{
    /// <summary>Authentication + cookie scheme name.</summary>
    public const string AuthenticationScheme = "MatSplit";

    /// <summary>Authorization policy allowing Admin only.</summary>
    public const string AdminOnlyPolicy = "AdminOnly";

    /// <summary>Authorization policy allowing Admin, User and Anonymous.</summary>
    public const string AuthenticatedUserPolicy = "AuthenticatedUser";

    public const string SessionTokenClaim = "matsplit:session";

    public const string UserTokenClaim = "matsplit:usertoken";

    public const string ThemeClaim = "matsplit:theme";

    public const string IsAnonymousClaim = "matsplit:anonymous";

    /// <summary>
    /// Builds the principal that is stored in the auth cookie. Only the session
    /// token is security relevant; everything else is a display convenience and
    /// is re-read from the database on every request.
    /// </summary>
    public static ClaimsPrincipal CreatePrincipal(User user, string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(SessionTokenClaim, sessionToken),
            new(UserTokenClaim, user.Token),
            new(ThemeClaim, user.ThemePreference.ToString()),
            new(IsAnonymousClaim, user.IsAnonymous ? "1" : "0")
        };

        var identity = new ClaimsIdentity(claims, AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Reads the user id from the principal, null when not signed in.</summary>
    public static long? GetUserId(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Reads the session token from the principal.</summary>
    public static string? GetSessionToken(ClaimsPrincipal? principal)
        => principal?.FindFirst(SessionTokenClaim)?.Value;

    /// <summary>Reads the role from the principal, defaults to Anonymous.</summary>
    public static UserRole GetRole(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(ClaimTypes.Role)?.Value;
        return Enum.TryParse<UserRole>(raw, out var role) ? role : UserRole.Anonymous;
    }
}
