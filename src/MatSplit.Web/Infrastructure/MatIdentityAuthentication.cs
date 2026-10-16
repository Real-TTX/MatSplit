using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MatSplit.Web.Infrastructure;

/// <summary>
/// The sign-in through MatIdentity (MatDesign docs/PLATFORM.md, "Sign-in with MatIdentity"), on only when
/// <c>MATIDENTITY_AUTHORITY</c> is set: OpenID Connect with the authorization code and PKCE for people, and a JWT bearer scheme
/// for the tokens of other apps on the protected part of <c>/api</c>. The person is mapped to a local user by
/// <see cref="UserService.SignInExternalAsync"/> and then signed in with MatSplit's own cookie and session, like a local sign-in.
/// </summary>
public static class MatIdentityAuthentication
{
    /// <summary>The OpenID Connect scheme.</summary>
    public const string Scheme = "MatIdentity";

    public const string CallbackPath = "/signin-oidc";

    public const string SignedOutCallbackPath = "/signout-callback-oidc";

    /// <summary>Where the browser lands after the sign-out at MatIdentity: the sign-in page, without the automatic sign-in.</summary>
    public const string SignedOutRedirect = "/Account/Login?signedOut=1";

    /// <summary>Where a failed sign-in at MatIdentity ends: the local sign-in with a message.</summary>
    public const string FailedRedirect = "/Account/Login?local=1&ssoError=1";

    public static AuthenticationBuilder AddMatIdentity(this AuthenticationBuilder builder, MatPlatformSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IdentityEnabled)
        {
            return builder;
        }

        var authority = new Uri(settings.Authority!);
        // http only for a MatIdentity on http (development); a public one is https and must stay so.
        var requireHttps = !string.Equals(authority.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        builder.AddOpenIdConnect(Scheme, "MatIdentity", options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.RequireHttpsMetadata = requireHttps;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.CallbackPath = CallbackPath;
            options.SignedOutCallbackPath = SignedOutCallbackPath;
            options.SignedOutRedirectUri = SignedOutRedirect;
            // The ticket is never stored by this handler (OnTicketReceived signs in with the session cookie instead), but it needs a
            // scheme that can sign in; the sign-out reads the ID token (id_token_hint) from the session cookie.
            options.SignInScheme = MatSplitClaims.AuthenticationScheme;
            options.SignOutScheme = MatSplitClaims.AuthenticationScheme;

            options.Scope.Clear();
            foreach (var scope in new[] { "openid", "profile", "email", "roles", "offline_access" })
            {
                options.Scope.Add(scope);
            }

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "role";

            if (!requireHttps)
            {
                // A development MatIdentity on http: the correlation and nonce cookies cannot be "Secure; SameSite=None" there.
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
            }

            if (Backchannel(settings) is { } handler)
            {
                options.BackchannelHttpHandler = handler;
            }

            // SaveTokens is off; only the ID token is kept (in the session cookie), as the id_token_hint of the sign-out.
            options.Events.OnTokenValidated = context =>
            {
                context.HttpContext.Items[IdTokenItem] = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage?.IdToken;
                return Task.CompletedTask;
            };
            options.Events.OnTicketReceived = SignInLocallyAsync;
            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
            {
                // The session cookie is already gone at this point; Logout hands the ID token over explicitly.
                if (context.Properties.Items.TryGetValue(IdTokenHintProperty, out var hint) && !string.IsNullOrEmpty(hint))
                {
                    context.ProtocolMessage.IdTokenHint = hint;
                }

                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(MatIdentityAuthentication))
                    .LogWarning(context.Failure, "Sign-in through MatIdentity failed");
                context.HandleResponse();
                context.Response.Redirect(FailedRedirect);
                return Task.CompletedTask;
            };
        });

        builder.AddJwtBearer(ApiAccess.BearerScheme, options =>
        {
            options.Authority = settings.Authority;
            options.Audience = settings.ClientId;
            options.RequireHttpsMetadata = requireHttps;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "role";

            if (Backchannel(settings) is { } handler)
            {
                options.BackchannelHttpHandler = handler;
            }
        });

        return builder;
    }

    /// <summary>The rewriting handler for <c>MATIDENTITY_INTERNAL_URL</c>, or null without it.</summary>
    private static MatIdentityBackchannelHandler? Backchannel(MatPlatformSettings settings)
        => settings.InternalUrl is null
            ? null
            : new MatIdentityBackchannelHandler(new Uri(settings.Authority!), new Uri(settings.InternalUrl));

    /// <summary>MatIdentity has signed the person in: find or create the local user and sign in with the session cookie.</summary>
    private static async Task SignInLocallyAsync(TicketReceivedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var principal = context.Principal;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MatIdentityAuthentication));

        string? Claim(string type) => principal?.FindFirst(type)?.Value;

        var identity = new ExternalIdentity(
            Claim("sub") ?? string.Empty,
            Claim("email"),
            Claim("name"),
            Claim("preferred_username"),
            Claim("role"));

        var result = await services.GetRequiredService<UserService>().SignInExternalAsync(identity, context.HttpContext.RequestAborted);

        context.HandleResponse();

        if (result.IsFailure)
        {
            logger.LogWarning("Sign-in through MatIdentity refused for {Subject}: {Error}", identity.Subject, result.Error);
            context.Response.Redirect("/AccessDenied");
            return;
        }

        var user = result.Value;
        var idToken = context.Properties?.GetTokenValue("id_token")
            ?? context.HttpContext.Items[IdTokenItem] as string;

        await services.GetRequiredService<CurrentUserService>().SignInAsync(user, true, context.HttpContext.RequestAborted, idToken);

        await services.GetRequiredService<HistoryService>().LogAsync(
            null,
            user.Id,
            HistoryService.EntityTypes.User,
            user.Id,
            HistoryService.Actions.SignedIn,
            $"\"{user.DisplayName}\" hat sich über MatIdentity angemeldet.",
            cancellationToken: context.HttpContext.RequestAborted);

        logger.LogInformation("User {UserId} signed in through MatIdentity", user.Id);

        var returnUrl = context.ReturnUri;
        context.Response.Redirect(!string.IsNullOrEmpty(returnUrl) && IsLocal(returnUrl) ? returnUrl : "/");
    }

    /// <summary>
    /// The result of the sign-out page for a person who signed in through MatIdentity: ends the session at MatIdentity too
    /// (end-session endpoint, <c>id_token_hint</c>, back to <see cref="SignedOutCallbackPath"/> and then <see cref="SignedOutRedirect"/>).
    /// </summary>
    public static SignOutResult SignOutAtIdentity(string identityToken)
    {
        var properties = new AuthenticationProperties { RedirectUri = SignedOutRedirect };
        properties.Items[IdTokenHintProperty] = identityToken;
        return new SignOutResult([Scheme], properties);
    }

    private const string IdTokenHintProperty = "matsplit:id_token_hint";

    /// <summary>Where the ID token of the current callback is kept between token validation and the local sign-in.</summary>
    internal const string IdTokenItem = "matsplit:id-token";

    private static bool IsLocal(string url)
        => url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
}
