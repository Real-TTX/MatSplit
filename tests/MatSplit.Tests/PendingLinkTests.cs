using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using MatPlatform;
using MatSplit.Web.Data;
using MatSplit.Web.Data.Entities;
using MatSplit.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MatSplit.Tests;

/// <summary>
/// The attack this guards against: someone puts the address of another person into their own MatSplit account; at that person's first
/// sign-in through MatIdentity they would land in the attacker's account. Now the account is linked only after its own password.
/// </summary>
public sealed class PendingLinkTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    private const string LinkTarget = "/Account/Login?local=1&link=1";

    [Fact]
    public async Task A_pre_claimed_address_does_not_let_the_person_into_that_account()
    {
        var mallory = await AddUserAsync("Mallory", "victim@example.com", "mallory-password");
        var count = await CountUsersAsync();
        var browser = new Browser(site);

        var target = await browser.SignInThroughIdentityAsync(new MatIdentityPerson("sub-victim", "victim@example.com", "Victim", "victim", null, "victim.id.token", new ClaimsPrincipal()));

        Assert.Equal(LinkTarget, target);
        Assert.Null((await FindAsync(mallory.Id)).ExternalSubject);
        Assert.Equal(count, await CountUsersAsync());
        // Not signed in as anybody.
        Assert.Equal(HttpStatusCode.Redirect, (await browser.Client.GetAsync("/Admin")).StatusCode);

        var page = await browser.Client.GetStringAsync(LinkTarget);
        Assert.Contains("id=\"login-link\"", page);
        Assert.Contains("victim@example.com", page);
        Assert.Contains("id=\"login-form\"", page);
    }

    [Fact]
    public async Task The_local_sign_in_of_the_matched_account_links_it()
    {
        var vera = await AddUserAsync("Vera", "vera@example.com", "vera-password");
        var browser = new Browser(site);
        Assert.Equal(LinkTarget, await browser.SignInThroughIdentityAsync(Person("sub-vera", "vera@example.com", "admin")));

        var response = await browser.SignInLocallyAsync("vera@example.com", "vera-password");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var linked = await FindAsync(vera.Id);
        Assert.Equal("sub-vera", linked.ExternalSubject);
        Assert.Equal(UserRole.Admin, linked.Role);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/Admin")).StatusCode);
        Assert.False(browser.HasPendingLink);
        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.HistoryEntries.AnyAsync(x => x.EntityId == vera.Id && x.Summary.Contains("linked with MatIdentity")));
        }

        // The next sign-in through MatIdentity goes straight into the account.
        Assert.Equal("/", await new Browser(site).SignInThroughIdentityAsync(Person("sub-vera", "vera@example.com", null)));
    }

    [Fact]
    public async Task Another_account_signing_in_links_nothing()
    {
        var walter = await AddUserAsync("Walter", "walter@example.com", "walter-password");
        var browser = new Browser(site);
        Assert.Equal(LinkTarget, await browser.SignInThroughIdentityAsync(Person("sub-walter", "walter@example.com", "admin")));

        var response = await browser.SignInLocallyAsync("admin", "admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await FindAsync(walter.Id)).ExternalSubject);
        Assert.False(await AnyWithSubjectAsync("sub-walter"));
        Assert.False(browser.HasPendingLink);

        // The link is gone: Walter's own sign-in afterwards links nothing either.
        await new Browser(site).SignInLocallyAsync("walter@example.com", "walter-password");
        Assert.Null((await FindAsync(walter.Id)).ExternalSubject);
    }

    [Fact]
    public async Task Cancel_deletes_the_pending_link()
    {
        var clara = await AddUserAsync("Clara", "clara@example.com", "clara-password");
        var browser = new Browser(site);
        Assert.Equal(LinkTarget, await browser.SignInThroughIdentityAsync(Person("sub-clara", "clara@example.com", null)));

        var page = await browser.Client.GetStringAsync(LinkTarget);
        var cancel = await browser.Client.PostAsync("/Account/Login?handler=CancelLink", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Browser.Token(page),
        }));

        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal("/Account/Login?local=1", cancel.Headers.Location!.OriginalString);
        Assert.False(browser.HasPendingLink);
        Assert.DoesNotContain("id=\"login-link\"", await browser.Client.GetStringAsync(LinkTarget));

        await browser.SignInLocallyAsync("clara@example.com", "clara-password");
        Assert.Null((await FindAsync(clara.Id)).ExternalSubject);
    }

    [Fact]
    public async Task An_expired_or_forged_pending_link_is_ignored()
    {
        var erik = await AddUserAsync("Erik", "erik@example.com", "erik-password");
        var link = new MatIdentityPendingLink("sub-erik", "erik@example.com", "Erik", null, "admin", null, erik.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var json = JsonSerializer.Serialize(link, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var protector = site.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(MatIdentityPendingLink.Purpose).ToTimeLimitedDataProtector();

        string[] values =
        [
            protector.Protect(json, DateTimeOffset.UtcNow.AddSeconds(-1)),
            "CfDJ8forged-" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)),
            site.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Another.Purpose").ToTimeLimitedDataProtector().Protect(json, TimeSpan.FromMinutes(5)),
        ];

        foreach (var value in values)
        {
            var browser = new Browser(site);
            browser.SetPendingLink(value);

            var page = await browser.Client.GetStringAsync(LinkTarget);
            Assert.DoesNotContain("id=\"login-link\"", page);
            Assert.Contains("id=\"login-link-expired\"", page);

            Assert.Equal(HttpStatusCode.Redirect, (await browser.SignInLocallyAsync("erik@example.com", "erik-password")).StatusCode);
            Assert.Null((await FindAsync(erik.Id)).ExternalSubject);
            Assert.Equal(UserRole.User, (await FindAsync(erik.Id)).Role);
        }

        // The same link with time left does link: the values above failed for their age, forgery or purpose only.
        var fresh = new Browser(site);
        fresh.SetPendingLink(protector.Protect(json, DateTimeOffset.UtcNow.AddMinutes(5)));
        await fresh.SignInLocallyAsync("erik@example.com", "erik-password");
        Assert.Equal("sub-erik", (await FindAsync(erik.Id)).ExternalSubject);
    }

    private static MatIdentityPerson Person(string subject, string email, string? role)
        => new(subject, email, subject, null, role, subject + ".id.token", new ClaimsPrincipal());

    private async Task<User> AddUserAsync(string name, string email, string password)
    {
        using var scope = site.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Token = Guid.NewGuid().ToString(),
            DisplayName = name,
            Email = email,
            PasswordHash = PasswordHasher.Hash(password),
            Role = UserRole.User,
            UpdateState = UpdateState.Created,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<User> FindAsync(long id)
    {
        using var scope = site.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<int> CountUsersAsync()
    {
        using var scope = site.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync();
    }

    private async Task<bool> AnyWithSubjectAsync(string subject)
    {
        using var scope = site.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(x => x.ExternalSubject == subject);
    }

    /// <summary>A browser with its own cookies; the callback of MatIdentity is run in the app's services and its cookies are handed over.</summary>
    private sealed class Browser
    {
        private static readonly Uri Base = new("http://localhost");
        private readonly IdentityFactory _site;
        private readonly CookieContainer _cookies = new();

        public Browser(IdentityFactory site)
        {
            _site = site;
            Client = site.CreateDefaultClient(Base, new CookieContainerHandler(_cookies));
        }

        public HttpClient Client { get; }

        public bool HasPendingLink => _cookies.GetCookies(Base)[MatIdentityPendingLink.CookieName] is { Expired: false } cookie && cookie.Value.Length > 0;

        public void SetPendingLink(string value) => _cookies.Add(Base, new Cookie(MatIdentityPendingLink.CookieName, value, "/"));

        /// <summary>What <c>/signin-oidc</c> does after MatIdentity signed the person in; the cookies it sets go into this browser.</summary>
        public async Task<string> SignInThroughIdentityAsync(MatIdentityPerson person)
        {
            using var scope = _site.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Scheme = "http";
            http.Request.Host = new HostString("localhost");
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = http;
            try
            {
                var target = await MatIdentitySignIn.CompleteSignInAsync(http, person);
                foreach (var cookie in SetCookieHeaderValue.ParseList(http.Response.Headers.SetCookie.Select(x => x ?? string.Empty).ToList()))
                {
                    _cookies.Add(Base, new Cookie(cookie.Name.Value!, cookie.Value.Value, "/"));
                }

                return target;
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }

        public async Task<HttpResponseMessage> SignInLocallyAsync(string login, string password)
        {
            var page = await Client.GetStringAsync("/Account/Login?local=1");
            return await Client.PostAsync("/Account/Login?local=1", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Token(page),
                ["Input.EmailOrName"] = login,
                ["Input.Password"] = password,
                ["Input.RememberMe"] = "false",
            }));
        }

        public static string Token(string html)
            => Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
    }
}
