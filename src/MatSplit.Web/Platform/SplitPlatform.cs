using System.Text.Json;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;

namespace MatSplit.Web.Platform;

/// <summary>
/// MatSplit as an app of the platform contract (MatDesign docs/PLATFORM.md): how Mattiverse shows it (tiles, settings, buttons), what its
/// values are and what the buttons do. The settings are a few of /Admin/Settings, checked by the same <see cref="AppConfigRules"/>.
/// Texts are German, or English when the caller asks for it with <c>Accept-Language</c>.
/// </summary>
public sealed class SplitPlatform(
    AppConfigService appConfig,
    InstanceStatusService status,
    SessionService sessions,
    HistoryService history,
    ApiAccess access,
    MatPlatformSettings settings,
    IHttpContextAccessor httpContextAccessor) : IMatPlatformApp
{
    public const string Accent = "#bf5610";

    /// <summary>The fields of the platform form and the property of <see cref="AppConfig"/> each one is.</summary>
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["appName"] = nameof(AppConfig.AppName),
        ["defaultCurrency"] = nameof(AppConfig.DefaultCurrency),
        ["allowAnonymousJoin"] = nameof(AppConfig.AllowAnonymousJoin),
        ["sessionLifetimeDays"] = nameof(AppConfig.SessionLifetimeDays),
        ["maxReceiptSizeMb"] = nameof(AppConfig.MaxReceiptSizeMb),
    };

    public MatApiDefinition Definition(HttpContext http)
    {
        var en = IsEnglish(http);
        string T(string de, string english) => en ? english : de;

        return new MatApiDefinition
        {
            App = new MatAppInfo(
                MatPlatformSettings.AppId,
                "MatSplit",
                T("Geteilte Ausgaben für Urlaube und WGs", "Shared expenses for holidays and shared flats"),
                AppVersion.Current,
                "/img/favicon.svg",
                Accent),
            Auth = new MatAuthInfo(access.KeyEnabled, settings.Authority),
            Pages =
            [
                new MatPage("overview", T("Übersicht", "Overview"), "dashboard",
                [
                    new MatSection("tiles", ["groups", "people", "guests", "expenses30d", "openSettlements", "receipts", "lastActivity"]),
                    new MatSection("actions", ["openAdmin", "openSettings", "cleanupSessions"]),
                ]),
                new MatPage("settings", T("Einstellungen", "Settings"), "settings",
                [
                    new MatSection("form", ["appName", "defaultCurrency", "allowAnonymousJoin"], Label: T("Allgemein", "General")),
                    new MatSection("form", ["sessionLifetimeDays", "maxReceiptSizeMb"], Label: T("Grenzen", "Limits")),
                ]),
            ],
            State =
            [
                new MatStateItem("groups", T("Gruppen", "Groups"), "number") { Icon = "folder" },
                new MatStateItem("people", T("Personen mit Konto", "People with an account"), "number") { Icon = "users" },
                new MatStateItem("guests", T("Gäste über Einladungslink", "Guests by invite link"), "number") { Icon = "link" },
                new MatStateItem("expenses30d", T("Ausgaben der letzten 30 Tage", "Expenses in the last 30 days"), "number") { Icon = "dollar" },
                new MatStateItem("openSettlements", T("Offene Ausgleichszahlungen", "Open settlements"), "number") { Icon = "arrow-right" },
                new MatStateItem("receipts", T("Belege", "Receipts"), "number") { Icon = "image", Unit = "bytes" },
                new MatStateItem("lastActivity", T("Letzte Buchung", "Last entry"), "time") { Icon = "clock" },
            ],
            Configure =
            [
                new MatField("appName", T("Anwendungsname", "Application name"), "text")
                {
                    Required = true,
                    Max = AppConfigRules.AppNameMaxLength,
                    Hint = T("Erscheint im Menü und auf der Anmeldeseite.", "Shown in the menu and on the sign-in page."),
                },
                new MatField("defaultCurrency", T("Standardwährung", "Default currency"), "text")
                {
                    Required = true,
                    Min = 3,
                    Max = 3,
                    Pattern = AppConfigRules.CurrencyPattern,
                    Placeholder = "EUR",
                    Hint = T("Dreistelliger Code für neue Gruppen, z. B. EUR.", "Three-letter code for new groups, e.g. EUR."),
                },
                new MatField("allowAnonymousJoin", T("Beitritt über Einladungslinks erlauben", "Allow joining by invite link"), "bool")
                {
                    Hint = T("Aus: Einladungslinks funktionieren nicht mehr.", "Off: invite links stop working."),
                },
                new MatField("sessionLifetimeDays", T("Sitzungsdauer in Tagen", "Stay signed in (days)"), "number")
                {
                    Required = true,
                    Min = AppConfigRules.MinSessionLifetimeDays,
                    Max = AppConfigRules.MaxSessionLifetimeDays,
                    Hint = T("Gilt sofort für neue Sitzungen, für das Cookie nach einem Neustart.", "New sessions at once, the cookie after a restart."),
                    Restart = true,
                },
                new MatField("maxReceiptSizeMb", T("Maximale Beleggröße in MB", "Largest receipt (MB)"), "number")
                {
                    Required = true,
                    Min = AppConfigRules.MinReceiptSizeMb,
                    Max = AppConfigRules.MaxReceiptSizeMb,
                },
            ],
            Actions =
            [
                new MatAction("openAdmin", T("Administration öffnen", "Open admin"), "external", "link") { Url = "/Admin" },
                new MatAction("openSettings", T("Alle Einstellungen", "All settings"), "settings", "link") { Url = "/Admin/Settings" },
                new MatAction("cleanupSessions", T("Abgelaufene Sitzungen löschen", "Remove expired sessions"), "broom", "post"),
            ],
            Roles =
            [
                new MatRole("admin", "Administrator"),
                new MatRole("user", "User"),
            ],
            Endpoints = ["/api/health", "/api/state", "/api/configure", "/api/actions"],
        };
    }

    public async Task<MatHealth> HealthAsync(CancellationToken ct)
    {
        var checks = new List<MatHealthCheck>();
        var en = IsEnglish(httpContextAccessor.HttpContext);

        if (await status.CanReadDatabaseAsync(ct))
        {
            checks.Add(new MatHealthCheck("database", "ok"));

            if (await status.CountActiveAdminsAsync(ct) == 0)
            {
                checks.Add(new MatHealthCheck("administrators", "degraded", en ? "No active administrator." : "Kein aktiver Administrator."));
            }
        }
        else
        {
            checks.Add(new MatHealthCheck("database", "down", en ? "The database cannot be read." : "Die Datenbank kann nicht gelesen werden."));
        }

        var state = checks.Any(c => c.Status == "down") ? "down" : checks.Any(c => c.Status == "degraded") ? "degraded" : "ok";
        return new MatHealth(state, MatPlatformSettings.AppId, AppVersion.Current, DateTimeOffset.Now, checks);
    }

    public async Task<IReadOnlyDictionary<string, MatStateValue>> StateAsync(CancellationToken ct)
    {
        var snapshot = await status.GetSnapshotAsync(ct);
        return new Dictionary<string, MatStateValue>
        {
            ["groups"] = new(snapshot.Groups),
            ["people"] = new(snapshot.People),
            ["guests"] = new(snapshot.Guests),
            ["expenses30d"] = new(snapshot.ExpensesLast30Days),
            ["openSettlements"] = new(snapshot.OpenSettlements),
            ["receipts"] = new(snapshot.ReceiptBytes),
            ["lastActivity"] = new(snapshot.LastActivityUtc is { } last
                ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc))
                : null),
        };
    }

    public async Task<IReadOnlyDictionary<string, object?>> ConfigurationAsync(CancellationToken ct)
    {
        var config = await appConfig.GetAsync(ct);
        return new Dictionary<string, object?>
        {
            ["appName"] = config.AppName,
            ["defaultCurrency"] = config.DefaultCurrency,
            ["allowAnonymousJoin"] = config.AllowAnonymousJoin,
            ["sessionLifetimeDays"] = config.SessionLifetimeDays,
            ["maxReceiptSizeMb"] = config.MaxReceiptSizeMb,
        };
    }

    public async Task<IReadOnlyDictionary<string, string>> ConfigureAsync(IReadOnlyDictionary<string, JsonElement> values, string caller, CancellationToken ct)
    {
        var previous = await appConfig.GetAsync(ct);
        var config = Copy(previous);
        var errors = new Dictionary<string, string>();

        foreach (var (key, value) in values)
        {
            switch (key)
            {
                case "appName" when value.ValueKind == JsonValueKind.String:
                    config.AppName = value.GetString()!;
                    break;
                case "defaultCurrency" when value.ValueKind == JsonValueKind.String:
                    config.DefaultCurrency = value.GetString()!;
                    break;
                case "allowAnonymousJoin" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    config.AllowAnonymousJoin = value.GetBoolean();
                    break;
                case "sessionLifetimeDays" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var days):
                    config.SessionLifetimeDays = days;
                    break;
                case "maxReceiptSizeMb" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var megabytes):
                    config.MaxReceiptSizeMb = megabytes;
                    break;
                case "appName" or "defaultCurrency":
                    errors[key] = "Bitte einen Text angeben.";
                    break;
                case "allowAnonymousJoin":
                    errors[key] = "Bitte true oder false angeben.";
                    break;
                case "sessionLifetimeDays" or "maxReceiptSizeMb":
                    errors[key] = "Bitte eine ganze Zahl angeben.";
                    break;
                default:
                    errors[key] = "Diese Einstellung gibt es nicht.";
                    break;
            }
        }

        // The rules of the settings page, reported under the keys of the platform form.
        foreach (var (property, error) in AppConfigRules.Validate(config))
        {
            var key = Fields.FirstOrDefault(f => f.Value == property).Key ?? property;
            errors.TryAdd(key, error);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var result = await appConfig.SaveAsync(config, ct);
        if (result.IsFailure)
        {
            return new Dictionary<string, string> { [string.Empty] = result.Error! };
        }

        await history.LogAsync(
            null,
            null,
            HistoryService.EntityTypes.AppConfig,
            null,
            HistoryService.Actions.Updated,
            $"Anwendungseinstellungen wurden über die Plattform-API geändert ({caller}).",
            JsonSerializer.Serialize(new { Before = previous, After = appConfig.Current, Caller = caller }),
            cancellationToken: ct);

        return new Dictionary<string, string>();
    }

    public async Task<(bool Ok, string Message)?> RunActionAsync(string id, string caller, CancellationToken ct)
    {
        switch (id)
        {
            case "cleanupSessions":
                var removed = await sessions.CleanupExpiredAsync(ct);
                return (true, removed == 1 ? "1 abgelaufene Sitzung gelöscht." : $"{removed} abgelaufene Sitzungen gelöscht.");
            default:
                return null;
        }
    }

    private static AppConfig Copy(AppConfig config) => new()
    {
        AppName = config.AppName,
        DefaultCurrency = config.DefaultCurrency,
        AllowAnonymousJoin = config.AllowAnonymousJoin,
        SessionLifetimeDays = config.SessionLifetimeDays,
        MaxReceiptSizeMb = config.MaxReceiptSizeMb,
        CompressReceipts = config.CompressReceipts,
        ReceiptTargetKb = config.ReceiptTargetKb,
    };

    private static bool IsEnglish(HttpContext? http)
        => http?.Request.Headers.AcceptLanguage.ToString().TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
}
