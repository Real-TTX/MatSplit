using MatPlatform;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

// The factories hand their settings to Program through environment variables (Program reads them before the host is built),
// so the test classes run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MatSplit.Tests;

/// <summary>
/// The whole app in memory on its own empty data folder, in Development. <see cref="ApiKey"/> is <c>MAT_API_KEY</c> unless
/// <c>withKey</c> is false; with an <c>authority</c> the sign-in through MatIdentity is on, with a fixed OpenID configuration so no
/// MatIdentity has to run (the tests look at the redirects only).
/// </summary>
public class SplitFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-key-0123456789";

    /// <summary>The key "MatIdentity" signs with (in the fixed OpenID configuration): the tests sign logout tokens with it.</summary>
    public static readonly SigningCredentials IdentitySigning = new(
        new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "test-identity-key" },
        SecurityAlgorithms.RsaSha256);

    private readonly string _data = Path.Combine(Path.GetTempPath(), "matsplit-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _authority;
    private readonly bool _reachable;

    public SplitFactory()
        : this(withKey: true, authority: null)
    {
    }

    protected SplitFactory(bool withKey, string? authority, bool reachable = true)
    {
        _authority = authority;
        _reachable = reachable;
        Environment.SetEnvironmentVariable("MATSPLIT_DATA_DIR", _data);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ApiKeyVariable, withKey ? ApiKey : null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.AuthorityVariable, authority);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ClientIdVariable, null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ClientSecretVariable, authority is null ? null : "test-secret");
        Environment.SetEnvironmentVariable(MatPlatformSettings.InternalUrlVariable, null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_authority is not null && _reachable)
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = _authority,
                AuthorizationEndpoint = _authority + "/connect/authorize",
                TokenEndpoint = _authority + "/connect/token",
                EndSessionEndpoint = _authority + "/connect/logout",
            };
            configuration.SigningKeys.Add(IdentitySigning.Key);

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<OpenIdConnectOptions>(MatIdentitySignIn.Scheme, options =>
                {
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
                services.PostConfigure<JwtBearerOptions>(MatApiAccess.BearerScheme, options =>
                {
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        }
    }

    /// <summary>A client that keeps cookies and does not follow redirects (the tests look at each step).</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public HttpClient Api()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file for a moment; the temp folder is cleaned by the system later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Without <c>MAT_API_KEY</c>: the protected endpoints answer 401 to everyone.</summary>
public sealed class NoKeyFactory() : SplitFactory(withKey: false, authority: null);

/// <summary>With <c>MATIDENTITY_AUTHORITY</c> (a MatIdentity that does not need to run).</summary>
public sealed class IdentityFactory() : SplitFactory(withKey: true, authority: IdentityFactory.Authority)
{
    public const string Authority = "http://identity.test";
}

/// <summary>With <c>MATIDENTITY_AUTHORITY</c> pointing at an address where nothing answers (MatIdentity is down).</summary>
public sealed class UnreachableIdentityFactory() : SplitFactory(withKey: true, authority: "http://127.0.0.1:9", reachable: false);
