using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;

namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Who may call the protected part of <c>/api</c> (MatDesign docs/PLATFORM.md, "Access"): a caller with the key <c>MAT_API_KEY</c> in the
/// header <c>X-Api-Key</c> (compared in constant time; no key configured = the key is off), or, when MatIdentity is configured, a caller
/// with an access token of MatIdentity for the audience of this app (the client id), checked by <see cref="BearerScheme"/>.
/// </summary>
public sealed class ApiAccess
{
    public const string HeaderName = "X-Api-Key";

    public const string CallerKey = "matsplit:api-caller";

    /// <summary>The JWT bearer scheme that validates tokens of MatIdentity (registered only when MatIdentity is configured).</summary>
    public const string BearerScheme = "MatIdentityBearer";

    private readonly byte[]? _key;
    private readonly bool _bearer;

    public ApiAccess(MatPlatformSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _key = settings.ApiKey is null ? null : Encoding.UTF8.GetBytes(settings.ApiKey);
        _bearer = settings.IdentityEnabled;
    }

    public bool KeyEnabled => _key is not null;

    /// <summary>The name of the caller for the history, or null when it may not call.</summary>
    public async Task<string?> AuthenticateAsync(HttpContext http)
    {
        if (_key is not null && http.Request.Headers.TryGetValue(HeaderName, out var sent) && sent.Count == 1)
        {
            var given = Encoding.UTF8.GetBytes(sent.ToString());
            if (CryptographicOperations.FixedTimeEquals(given, _key))
            {
                return "api-key";
            }
        }

        if (_bearer && http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // Issuer, signature (JWKS of MatIdentity), lifetime and audience are checked by the scheme.
            var result = await http.AuthenticateAsync(BearerScheme);
            if (result.Succeeded)
            {
                return result.Principal.FindFirst("sub")?.Value
                    ?? result.Principal.FindFirst("client_id")?.Value
                    ?? "token";
            }
        }

        return null;
    }

    /// <summary>An endpoint filter: <c>401</c> unless <see cref="AuthenticateAsync"/> knows the caller; the name lands in <c>HttpContext.Items</c>.</summary>
    public static async ValueTask<object?> RequireAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var caller = await http.RequestServices.GetRequiredService<ApiAccess>().AuthenticateAsync(http);
        if (caller is null)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return Results.Json(
                new { errors = new Dictionary<string, string> { [string.Empty] = "A valid X-Api-Key or bearer token is required." } },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        http.Items[CallerKey] = caller;
        return await next(context);
    }
}
