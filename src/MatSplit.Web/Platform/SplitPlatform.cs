using System.Text.Json;
using MatPlatform;
using MatSplit.Web.Infrastructure;
using MatSplit.Web.Services;
using MatSplit.Web.Services.Models;

namespace MatSplit.Web.Platform;

/// <summary>
/// MatSplit as an app of the platform contract (MatDesign docs/PLATFORM.md), on the shared package MatPlatform (the folder <c>MatPlatform/</c>, a copy):
/// how Mattiverse shows it (<see cref="Describe"/>: tiles, settings, buttons), what its values are and what the buttons do. The settings are a few of
/// /Admin/Settings, checked by the same <see cref="AppConfigRules"/>. Texts are German, or English when the caller asks for it with <c>Accept-Language</c>.
/// </summary>
public sealed class SplitPlatform(
    AppConfigService appConfig,
    InstanceStatusService status,
    SessionService sessions,
    HistoryService history) : IMatPlatformState, IMatPlatformConfiguration, IMatPlatformActions, IMatPlatformHealth
{
    /// <summary>The id of MatSplit in the platform (and the default client id in MatIdentity).</summary>
    public const string AppId = "matsplit";

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

    /// <summary>The MatApiDefinition of MatSplit (<c>GET /api</c>); <c>auth</c> and <c>endpoints</c> are filled in by MatPlatform.</summary>
    public static void Describe(MatPlatformBuilder o)
    {
        var T = o.T;

        o.App(
            AppId,
            "MatSplit",
            T("Geteilte Ausgaben für Urlaube und WGs", "Shared expenses for holidays and shared flats"),
            AppVersion.Current,
            "/img/favicon.svg",
            Accent);

        o.Page("overview", T("Übersicht", "Overview"), "dashboard",
            MatSection.Tiles("groups", "people", "guests", "expenses30d", "openSettlements", "receipts", "lastActivity"),
            MatSection.Actions("openAdmin", "openSettings", "cleanupSessions"));
        o.Page("settings", T("Einstellungen", "Settings"), "settings",
            MatSection.Form(T("Allgemein", "General"), "appName", "defaultCurrency", "allowAnonymousJoin"),
            MatSection.Form(T("Grenzen", "Limits"), "sessionLifetimeDays", "maxReceiptSizeMb"));

        o.Tile("groups", T("Gruppen", "Groups"), icon: "folder");
        o.Tile("people", T("Personen mit Konto", "People with an account"), icon: "users");
        o.Tile("guests", T("Gäste über Einladungslink", "Guests by invite link"), icon: "link");
        o.Tile("expenses30d", T("Ausgaben der letzten 30 Tage", "Expenses in the last 30 days"), icon: "dollar");
        o.Tile("openSettlements", T("Offene Ausgleichszahlungen", "Open settlements"), icon: "arrow-right");
        o.Tile("receipts", T("Belege", "Receipts"), icon: "image", unit: "bytes");
        o.Tile("lastActivity", T("Letzte Buchung", "Last entry"), "time", icon: "clock");

        o.Field(new MatField("appName", T("Anwendungsname", "Application name"), "text")
        {
            Required = true,
            Max = AppConfigRules.AppNameMaxLength,
            Hint = T("Erscheint im Menü und auf der Anmeldeseite.", "Shown in the menu and on the sign-in page."),
        });
        o.Field(new MatField("defaultCurrency", T("Standardwährung", "Default currency"), "text")
        {
            Required = true,
            Min = 3,
            Max = 3,
            Pattern = AppConfigRules.CurrencyPattern,
            Placeholder = "EUR",
            Hint = T("Dreistelliger Code für neue Gruppen, z. B. EUR.", "Three-letter code for new groups, e.g. EUR."),
        });
        o.Field(new MatField("allowAnonymousJoin", T("Beitritt über Einladungslinks erlauben", "Allow joining by invite link"), "bool")
        {
            Hint = T("Aus: Einladungslinks funktionieren nicht mehr.", "Off: invite links stop working."),
        });
        o.Field(new MatField("sessionLifetimeDays", T("Sitzungsdauer in Tagen", "Stay signed in (days)"), "number")
        {
            Required = true,
            Min = AppConfigRules.MinSessionLifetimeDays,
            Max = AppConfigRules.MaxSessionLifetimeDays,
            Hint = T("Gilt sofort für neue Sitzungen, für das Cookie nach einem Neustart.", "New sessions at once, the cookie after a restart."),
            Restart = true,
        });
        o.Field(new MatField("maxReceiptSizeMb", T("Maximale Beleggröße in MB", "Largest receipt (MB)"), "number")
        {
            Required = true,
            Min = AppConfigRules.MinReceiptSizeMb,
            Max = AppConfigRules.MaxReceiptSizeMb,
        });

        o.LinkAction("openAdmin", T("Administration öffnen", "Open admin"), "external", "/Admin");
        o.LinkAction("openSettings", T("Alle Einstellungen", "All settings"), "settings", "/Admin/Settings");
        o.PostAction("cleanupSessions", T("Abgelaufene Sitzungen löschen", "Remove expired sessions"), "broom");

        o.Role("admin", "Administrator");
        o.Role("user", "User");
    }

    public async Task<IReadOnlyList<MatHealthCheck>> CheckHealthAsync(MatPlatformCall call, CancellationToken ct)
    {
        if (!await status.CanReadDatabaseAsync(ct))
        {
            return [new MatHealthCheck("database", "down", call.T("Die Datenbank kann nicht gelesen werden.", "The database cannot be read."))];
        }

        var checks = new List<MatHealthCheck> { new("database", "ok") };
        if (await status.CountActiveAdminsAsync(ct) == 0)
        {
            checks.Add(new MatHealthCheck("administrators", "degraded", call.T("Kein aktiver Administrator.", "No active administrator.")));
        }

        return checks;
    }

    public async Task<IReadOnlyDictionary<string, MatStateValue>> GetStateAsync(MatPlatformCall call, CancellationToken ct)
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

    public async Task<IReadOnlyDictionary<string, object?>> GetConfigurationAsync(MatPlatformCall call, CancellationToken ct)
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

    public async Task<IReadOnlyDictionary<string, string>> ConfigureAsync(IReadOnlyDictionary<string, JsonElement> values, MatPlatformCall call, CancellationToken ct)
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
            $"Anwendungseinstellungen wurden über die Plattform-API geändert ({call.Caller}).",
            JsonSerializer.Serialize(new { Before = previous, After = appConfig.Current, call.Caller }),
            cancellationToken: ct);

        return new Dictionary<string, string>();
    }

    public async Task<MatActionResult?> RunActionAsync(string id, MatPlatformCall call, CancellationToken ct)
    {
        switch (id)
        {
            case "cleanupSessions":
                var removed = await sessions.CleanupExpiredAsync(ct);
                return new MatActionResult(true, removed == 1 ? "1 abgelaufene Sitzung gelöscht." : $"{removed} abgelaufene Sitzungen gelöscht.");
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
}
