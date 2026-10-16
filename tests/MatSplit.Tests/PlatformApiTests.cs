using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MatSplit.Tests;

/// <summary>The platform contract of MatDesign docs/PLATFORM.md as MatSplit answers it: shapes, status codes, access, the rules of a definition.</summary>
public sealed partial class PlatformApiTests(SplitFactory site) : IClassFixture<SplitFactory>
{
    private static readonly string[] Protected = ["/api/state", "/api/state/groups", "/api/configure"];

    [Fact]
    public async Task Health_answers_without_a_key()
    {
        var response = await site.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("matsplit", doc.RootElement.GetProperty("app").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("version").GetString()));
        Assert.Contains(doc.RootElement.GetProperty("checks").EnumerateArray(), c => c.GetProperty("name").GetString() == "database" && c.GetProperty("status").GetString() == "ok");
    }

    [Fact]
    public async Task The_old_health_endpoint_still_answers()
    {
        var response = await site.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_definition_answers_without_a_key_and_with_an_etag()
    {
        var client = site.CreateClient();
        var response = await client.GetAsync("/api");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var etag = response.Headers.ETag!.Tag;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(request)).StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("contract").GetInt32());
        var app = root.GetProperty("app");
        Assert.Equal("matsplit", app.GetProperty("id").GetString());
        Assert.Equal("MatSplit", app.GetProperty("name").GetString());
        Assert.Equal("#bf5610", app.GetProperty("accent").GetString());
        Assert.Equal("/", app.GetProperty("home").GetString());
        Assert.True(root.GetProperty("auth").GetProperty("apiKey").GetBoolean());
        Assert.False(root.GetProperty("auth").TryGetProperty("identity", out _));

        Assert.Equal(["overview", "settings"], root.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(
            [("admin", "Administrator"), ("user", "User")],
            root.GetProperty("roles").EnumerateArray().Select(r => (r.GetProperty("key").GetString(), r.GetProperty("label").GetString())));

        var openAdmin = root.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "openAdmin");
        Assert.Equal("link", openAdmin.GetProperty("kind").GetString());
        Assert.Equal("/Admin", openAdmin.GetProperty("url").GetString());
    }

    [Fact]
    public async Task The_definition_follows_accept_language()
    {
        var client = site.CreateClient();
        using var german = new HttpRequestMessage(HttpMethod.Get, "/api");
        german.Headers.TryAddWithoutValidation("Accept-Language", "de-DE");
        using var english = new HttpRequestMessage(HttpMethod.Get, "/api");
        english.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        using var de = JsonDocument.Parse(await (await client.SendAsync(german)).Content.ReadAsStringAsync());
        using var en = JsonDocument.Parse(await (await client.SendAsync(english)).Content.ReadAsStringAsync());
        Assert.Equal("Übersicht", de.RootElement.GetProperty("pages")[0].GetProperty("label").GetString());
        Assert.Equal("Overview", en.RootElement.GetProperty("pages")[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task The_definition_is_consistent_and_names_only_icons_of_the_sprite()
    {
        using var doc = JsonDocument.Parse(await site.CreateClient().GetStringAsync("/api"));
        var root = doc.RootElement;
        var state = root.GetProperty("state").EnumerateArray().Select(s => s.GetProperty("key").GetString()!).ToHashSet();
        var fields = root.GetProperty("configure").EnumerateArray().Select(f => f.GetProperty("key").GetString()!).ToHashSet();
        var actions = root.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToHashSet();

        foreach (var section in root.GetProperty("pages").EnumerateArray().SelectMany(p => p.GetProperty("sections").EnumerateArray()))
        {
            var kind = section.GetProperty("kind").GetString();
            var known = kind switch { "tiles" => state, "form" => fields, "actions" => actions, _ => [] };
            foreach (var item in section.GetProperty("items").EnumerateArray().Select(i => i.GetString()!))
            {
                Assert.True(known.Contains(item), $"The {kind} section names {item}, which the definition does not have.");
            }
        }

        foreach (var field in root.GetProperty("configure").EnumerateArray().Where(f => f.TryGetProperty("showIf", out _)))
        {
            Assert.All(field.GetProperty("showIf").EnumerateObject(), named => Assert.Contains(named.Name, fields));
        }

        Assert.True(fields.Count <= 10, "The platform form is for the few settings a fresh install needs.");

        var sprite = SymbolRegex().Matches(File.ReadAllText(Path.Combine(WebRoot(), "matui", "matui-icons.svg"))).Select(m => m.Groups[1].Value).ToHashSet();
        var icons = root.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("icon").GetString()!)
            .Concat(root.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("icon").GetString()!))
            .Concat(root.GetProperty("state").EnumerateArray().Where(s => s.TryGetProperty("icon", out _)).Select(s => s.GetProperty("icon").GetString()!));
        Assert.All(icons, icon => Assert.True(sprite.Contains(icon), $"{icon} is not in the sprite"));

        Assert.True(File.Exists(Path.Combine(WebRoot(), root.GetProperty("app").GetProperty("icon").GetString()!.TrimStart('/'))));
    }

    [Fact]
    public async Task State_configure_and_actions_need_the_key()
    {
        var anonymous = site.CreateClient();
        foreach (var path in Protected)
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/configure", new { values = new { appName = "x" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/actions/cleanupSessions", null)).StatusCode);

        var wrong = site.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/api/state")).StatusCode);

        // A bearer token is no way in while MatIdentity is not configured.
        var bearer = site.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "abc.def.ghi");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/state")).StatusCode);
    }

    [Fact]
    public async Task State_answers_every_tile_and_one_value_by_key()
    {
        var api = site.Api();
        using var definition = JsonDocument.Parse(await api.GetStringAsync("/api"));
        using var state = JsonDocument.Parse(await api.GetStringAsync("/api/state"));
        var values = state.RootElement.GetProperty("values");
        Assert.True(state.RootElement.TryGetProperty("time", out _));

        foreach (var key in definition.RootElement.GetProperty("state").EnumerateArray().Select(s => s.GetProperty("key").GetString()!))
        {
            Assert.True(values.TryGetProperty(key, out var value), $"No value for {key}");
            Assert.True(value.TryGetProperty("value", out _), $"{key} has no value field");
        }

        // The seed: the demo group with four members, three expenses within the last week.
        Assert.Equal(1, values.GetProperty("groups").GetProperty("value").GetInt32());
        Assert.Equal(3, values.GetProperty("expenses30d").GetProperty("value").GetInt32());
        Assert.True(values.GetProperty("openSettlements").GetProperty("value").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.String, values.GetProperty("lastActivity").GetProperty("value").ValueKind);

        var one = await api.GetAsync("/api/state/groups");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        using (var doc = JsonDocument.Parse(await one.Content.ReadAsStringAsync()))
        {
            Assert.Equal("groups", doc.RootElement.GetProperty("key").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("value").GetInt32());
        }

        var unknown = await api.GetAsync("/api/state/nothing");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using (var doc = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("nothing", out _));
        }
    }

    [Fact]
    public async Task Configure_checks_every_value_with_the_rules_of_the_settings_page_and_saves_nothing_when_one_is_wrong()
    {
        var api = site.Api();

        var bad = await api.PutAsJsonAsync("/api/configure", new { values = new { appName = "Urlaubskasse", sessionLifetimeDays = 0, nothing = 1 } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using (var errors = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()))
        {
            var byKey = errors.RootElement.GetProperty("errors");
            Assert.True(byKey.TryGetProperty("nothing", out _));
            Assert.Equal("Bitte einen Wert zwischen 1 und 365 Tagen angeben.", byKey.GetProperty("sessionLifetimeDays").GetString());
        }

        using (var unchanged = JsonDocument.Parse(await api.GetStringAsync("/api/configure")))
        {
            Assert.Equal("MatSplit", unchanged.RootElement.GetProperty("values").GetProperty("appName").GetString());
        }

        foreach (var wrong in new object[]
                 {
                     new { defaultCurrency = "EURO" },
                     new { defaultCurrency = "E1R" },
                     new { appName = "" },
                     new { appName = new string('x', 61) },
                     new { maxReceiptSizeMb = 101 },
                     new { allowAnonymousJoin = "yes" },
                     new { sessionLifetimeDays = 1.5 },
                 })
        {
            var response = await api.PutAsJsonAsync("/api/configure", new { values = wrong });
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(wrong)}: {(int)response.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await api.PutAsync("/api/configure", JsonContent.Create(new { nothing = 1 }))).StatusCode);

        var good = await api.PutAsJsonAsync("/api/configure", new { values = new { appName = "Urlaubskasse", defaultCurrency = "chf", allowAnonymousJoin = false, maxReceiptSizeMb = 20 } });
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        using (var saved = JsonDocument.Parse(await good.Content.ReadAsStringAsync()))
        {
            var values = saved.RootElement.GetProperty("values");
            Assert.Equal("Urlaubskasse", values.GetProperty("appName").GetString());
            Assert.Equal("CHF", values.GetProperty("defaultCurrency").GetString());
            Assert.False(values.GetProperty("allowAnonymousJoin").GetBoolean());
            Assert.Equal(20, values.GetProperty("maxReceiptSizeMb").GetInt32());
            Assert.Equal(30, values.GetProperty("sessionLifetimeDays").GetInt32());
        }

        // Back to the defaults, so the other tests see the usual installation.
        var reset = await api.PutAsJsonAsync("/api/configure", new { values = new { appName = "MatSplit", defaultCurrency = "EUR", allowAnonymousJoin = true, maxReceiptSizeMb = 10 } });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    [Fact]
    public async Task Configure_answers_every_field_and_never_a_secret()
    {
        var api = site.Api();
        using var definition = JsonDocument.Parse(await api.GetStringAsync("/api"));
        using var configuration = JsonDocument.Parse(await api.GetStringAsync("/api/configure"));
        var values = configuration.RootElement.GetProperty("values");

        foreach (var field in definition.RootElement.GetProperty("configure").EnumerateArray())
        {
            var key = field.GetProperty("key").GetString()!;
            Assert.True(values.TryGetProperty(key, out var value), $"No value for {key}");
            if (field.GetProperty("type").GetString() == "secret")
            {
                Assert.Equal(JsonValueKind.Object, value.ValueKind);
                Assert.Equal(["isSet"], value.EnumerateObject().Select(p => p.Name));
            }
        }

        Assert.DoesNotContain(SplitFactory.ApiKey, configuration.RootElement.GetRawText());
        Assert.DoesNotContain(SplitFactory.ApiKey, definition.RootElement.GetRawText());
    }

    [Fact]
    public async Task Actions_run_and_an_unknown_one_is_404()
    {
        var api = site.Api();
        var cleanup = await api.PostAsync("/api/actions/cleanupSessions", null);
        Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
        using (var doc = JsonDocument.Parse(await cleanup.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync("/api/actions/nothing", null)).StatusCode);
    }

    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MatSplit.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "MatSplit.Web", "wwwroot");
    }

    [GeneratedRegex(@"<symbol id=""mat-i-([a-z0-9-]+)""")]
    private static partial Regex SymbolRegex();
}

/// <summary>Without <c>MAT_API_KEY</c> the key is off: the protected endpoints answer 401 to every key.</summary>
public sealed class PlatformWithoutKeyTests(NoKeyFactory site) : IClassFixture<NoKeyFactory>
{
    [Fact]
    public async Task Every_protected_endpoint_is_401_and_the_definition_says_so()
    {
        var client = site.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", SplitFactory.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/configure")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/actions/cleanupSessions", null)).StatusCode);

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api"));
        Assert.False(doc.RootElement.GetProperty("auth").GetProperty("apiKey").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }
}
