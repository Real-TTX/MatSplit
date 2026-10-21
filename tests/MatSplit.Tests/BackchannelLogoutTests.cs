using System.Net;
using System.Security.Claims;
using MatPlatform;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MatSplit.Tests;

/// <summary>
/// The back-channel logout of MatIdentity (MatPlatform 1.4.0, <c>POST /signout-backchannel</c>): a signed logout token ends the MatSplit
/// session that started with its <c>sid</c>, or every session of the person with the <c>sub</c> alone; the next request is signed out.
/// </summary>
public sealed class BackchannelLogoutTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    private const string Path = "/signout-backchannel";

    [Fact]
    public async Task A_valid_token_ends_the_session_of_its_sid_and_its_next_request_is_signed_out()
    {
        var ended = new Browser(site);
        var other = new Browser(site);
        Assert.Equal("/", await ended.SignInThroughIdentityAsync(Person("sub-bc-1", "sid-bc-1a")));
        Assert.Equal("/", await other.SignInThroughIdentityAsync(Person("sub-bc-1", "sid-bc-1b")));
        Assert.Equal(HttpStatusCode.OK, (await ended.Client.GetAsync("/Admin")).StatusCode);

        var response = await PostAsync(Token("sub-bc-1", "sid-bc-1a"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await ended.Client.GetAsync("/Admin")).StatusCode);
        // The other session of the same person (another sid) goes on.
        Assert.Equal(HttpStatusCode.OK, (await other.Client.GetAsync("/Admin")).StatusCode);

        using var scope = site.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.ExternalSubject == "sub-bc-1");
        Assert.True(await db.HistoryEntries.AnyAsync(x => x.EntityId == user.Id && x.Action == HistoryService.Actions.SignedOut && x.Summary.Contains("MatIdentity")));
    }

    [Fact]
    public async Task A_token_with_the_subject_alone_ends_every_session_of_the_person()
    {
        var first = new Browser(site);
        var second = new Browser(site);
        var someoneElse = new Browser(site);
        await first.SignInThroughIdentityAsync(Person("sub-bc-2", "sid-bc-2a"));
        await second.SignInThroughIdentityAsync(Person("sub-bc-2", "sid-bc-2b"));
        await someoneElse.SignInThroughIdentityAsync(Person("sub-bc-3", "sid-bc-3"));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Token("sub-bc-2", null))).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await first.Client.GetAsync("/Admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await second.Client.GetAsync("/Admin")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await someoneElse.Client.GetAsync("/Admin")).StatusCode);
    }

    [Fact]
    public async Task The_sid_of_a_linked_account_is_kept_with_its_session()
    {
        // Linked after its password: the session that starts then carries the sid of the pending link.
        var browser = new Browser(site);
        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User
            {
                Token = Guid.NewGuid().ToString(),
                DisplayName = "Lina",
                Email = "lina-bc@example.com",
                PasswordHash = PasswordHasher.Hash("lina-password"),
                Role = UserRole.Admin,
                UpdateState = UpdateState.Created,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal("/Account/Login?local=1&link=1", await browser.SignInThroughIdentityAsync(Person("sub-bc-lina", "sid-bc-lina", "lina-bc@example.com")));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SignInLocallyAsync("lina-bc@example.com", "lina-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/Admin")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Token(null, "sid-bc-lina"))).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await browser.Client.GetAsync("/Admin")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_subject_or_sid_is_no_error_and_a_local_session_goes_on()
    {
        var local = new Browser(site);
        Assert.Equal(HttpStatusCode.Redirect, (await local.SignInLocallyAsync("admin", "admin")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Token("sub-nobody", "sid-nobody"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(Token("sub-nobody", null))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await local.Client.GetAsync("/Admin")).StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_400_and_ends_nothing()
    {
        var browser = new Browser(site);
        await browser.SignInThroughIdentityAsync(Person("sub-bc-4", "sid-bc-4"));
        var otherKey = new SigningCredentials(new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "other" }, SecurityAlgorithms.RsaSha256);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(Token("sub-bc-4", "sid-bc-4", otherKey))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/Admin")).StatusCode);
    }

    private static MatIdentityPerson Person(string subject, string sessionId, string? email = null)
        => new(subject, email, subject, null, "admin", subject + ".id.token", new ClaimsPrincipal()) { SessionId = sessionId };

    private Task<HttpResponseMessage> PostAsync(string token)
        => site.CreateClient().PostAsync(Path, new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = token }));

    /// <summary>A logout token as MatIdentity writes it.</summary>
    private string Token(string? subject, string? sessionId, SigningCredentials? credentials = null)
    {
        var clientId = site.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(MatIdentitySignIn.Scheme).ClientId!;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = IdentityFactory.Authority,
            ["aud"] = clientId,
            ["iat"] = now,
            ["exp"] = now + 120,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["events"] = new Dictionary<string, object> { [MatIdentityLogoutToken.EventType] = new Dictionary<string, object>() },
        };
        if (subject is not null)
        {
            claims["sub"] = subject;
        }

        if (sessionId is not null)
        {
            claims["sid"] = sessionId;
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            TokenType = MatIdentityLogoutToken.TokenType,
            SigningCredentials = credentials ?? SplitFactory.IdentitySigning,
        });
    }
}

/// <summary>Without <c>MATIDENTITY_AUTHORITY</c> there is no back-channel logout.</summary>
public sealed class BackchannelLogoutWithoutIdentityTests(SplitFactory site) : IClassFixture<SplitFactory>
{
    [Fact]
    public async Task Without_matidentity_the_endpoint_is_404()
    {
        var response = await site.CreateClient().PostAsync("/signout-backchannel", new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = "x.y.z" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
