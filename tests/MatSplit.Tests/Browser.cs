using System.Net;
using System.Text.RegularExpressions;
using MatPlatform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MatSplit.Tests;

/// <summary>A browser with its own cookies; the callback of MatIdentity is run in the app's services and its cookies are handed over.</summary>
internal sealed class Browser
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
