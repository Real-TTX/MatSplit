using System.Net;
using System.Text.Json;
using MatSplit.Web.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatSplit.Tests;

/// <summary>Without <c>MATIDENTITY_AUTHORITY</c> nothing changes: the local sign-in form, as before.</summary>
public sealed class LocalSignInTests(SplitFactory site) : IClassFixture<SplitFactory>
{
    [Fact]
    public async Task The_sign_in_page_is_the_local_form()
    {
        var browser = site.Browser();
        var response = await browser.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.DoesNotContain("MatIdentity", html);
    }

    [Fact]
    public async Task A_page_that_needs_a_sign_in_sends_to_the_local_form()
    {
        var response = await site.Browser().GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString.Replace("http://localhost", string.Empty));
        Assert.Equal(HttpStatusCode.NotFound, (await site.Browser().GetAsync("/signin-oidc")).StatusCode);
    }
}

/// <summary>With <c>MATIDENTITY_AUTHORITY</c>: the automatic sign-in at MatIdentity, the local way in with <c>?local=1</c>, guests stay guests.</summary>
public sealed class IdentitySignInTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    [Fact]
    public async Task The_sign_in_page_goes_straight_to_matidentity_with_code_and_pkce()
    {
        var response = await site.Browser().GetAsync("/Account/Login?returnUrl=%2FAdmin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.StartsWith(IdentityFactory.Authority + "/connect/authorize", location.OriginalString);

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("matsplit", query["client_id"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrEmpty(query["code_challenge"].ToString()));
        Assert.EndsWith("/signin-oidc", query["redirect_uri"].ToString());
        Assert.Equal(
            ["openid", "profile", "email", "roles", "offline_access"],
            query["scope"].ToString().Split(' '));
    }

    [Fact]
    public async Task A_page_that_needs_a_sign_in_ends_at_matidentity()
    {
        var browser = site.Browser();
        var first = await browser.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var login = first.Headers.Location!.OriginalString;
        Assert.Contains("/Account/Login", login);

        var second = await browser.GetAsync(login);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.StartsWith(IdentityFactory.Authority, second.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_local_form_stays_reachable_and_works()
    {
        var browser = site.Browser();
        var page = await browser.GetAsync("/Account/Login?local=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("Mit MatIdentity anmelden", html);

        var token = System.Text.RegularExpressions.Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        var signedIn = await browser.PostAsync("/Account/Login?local=1", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.EmailOrName"] = "admin",
            ["Input.Password"] = "admin",
            ["Input.RememberMe"] = "true",
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Admin")).StatusCode);
    }

    [Fact]
    public async Task After_the_sign_out_there_is_no_automatic_sign_in()
    {
        var response = await site.Browser().GetAsync("/Account/Login?signedOut=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Mit MatIdentity anmelden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invite_links_and_the_read_only_link_stay_anonymous()
    {
        string inviteToken;
        string readOnlyToken;
        using (var scope = site.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var group = await db.Groups.SingleAsync();
            group.ReadOnlyEnabled = true;
            await db.SaveChangesAsync();
            inviteToken = group.InviteToken;
            readOnlyToken = group.ReadOnlyToken;
        }

        var guest = site.Browser();
        var join = await guest.GetAsync("/Join?token=" + inviteToken);
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        Assert.Contains("Demo", await join.Content.ReadAsStringAsync());

        var view = await guest.GetAsync("/View?token=" + readOnlyToken);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains("Demo", await view.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_definition_names_matidentity_and_a_bad_token_is_401()
    {
        using var doc = JsonDocument.Parse(await site.CreateClient().GetStringAsync("/api"));
        Assert.Equal(IdentityFactory.Authority, doc.RootElement.GetProperty("auth").GetProperty("identity").GetString());

        // Not a token of MatIdentity: the bearer check refuses it.
        var bearer = site.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await site.Api().GetAsync("/api/state")).StatusCode);
    }
}

/// <summary>MatIdentity is configured but down: the sign-in page falls back to the local form instead of an error.</summary>
public sealed class UnreachableIdentityTests(UnreachableIdentityFactory site) : IClassFixture<UnreachableIdentityFactory>
{
    [Fact]
    public async Task The_sign_in_page_shows_the_local_form_with_a_message()
    {
        var response = await site.Browser().GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("id=\"login-sso-error\"", html);
    }
}
