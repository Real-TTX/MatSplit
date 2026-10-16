using System.Text.RegularExpressions;
using MatSplit.Web.Services.Models;

namespace MatSplit.Web.Services;

/// <summary>
/// The rules of the settings in appconfig.json, shared by the settings page (/Admin/Settings) and the
/// platform API (<c>PUT /api/configure</c>), so both accept exactly the same values. The keys of
/// <see cref="Validate"/> are the property names of <see cref="AppConfig"/>.
/// </summary>
public static partial class AppConfigRules
{
    public const int AppNameMaxLength = 60;

    public const string CurrencyPattern = "^[A-Za-z]{3}$";

    public const int MinSessionLifetimeDays = 1;
    public const int MaxSessionLifetimeDays = 365;

    public const int MinReceiptSizeMb = 1;
    public const int MaxReceiptSizeMb = 100;

    public const int MinReceiptTargetKb = 50;
    public const int MaxReceiptTargetKb = 5000;

    public const string AppNameRequired = "Bitte einen Namen angeben.";
    public const string AppNameTooLong = "Maximal 60 Zeichen.";
    public const string CurrencyRequired = "Bitte eine Währung angeben.";
    public const string CurrencyInvalid = "Bitte einen dreistelligen Währungscode angeben, z. B. EUR.";
    public const string SessionLifetimeRange = "Bitte einen Wert zwischen 1 und 365 Tagen angeben.";
    public const string ReceiptSizeRange = "Bitte einen Wert zwischen 1 und 100 MB angeben.";
    public const string ReceiptTargetRange = "Bitte einen Wert zwischen 50 und 5000 KB angeben.";

    /// <summary>The errors per property of <paramref name="config"/>; empty when every value is valid.</summary>
    public static IReadOnlyDictionary<string, string> Validate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(config.AppName))
        {
            errors[nameof(AppConfig.AppName)] = AppNameRequired;
        }
        else if (config.AppName.Trim().Length > AppNameMaxLength)
        {
            errors[nameof(AppConfig.AppName)] = AppNameTooLong;
        }

        if (string.IsNullOrWhiteSpace(config.DefaultCurrency))
        {
            errors[nameof(AppConfig.DefaultCurrency)] = CurrencyRequired;
        }
        else if (!CurrencyRegex().IsMatch(config.DefaultCurrency.Trim()))
        {
            errors[nameof(AppConfig.DefaultCurrency)] = CurrencyInvalid;
        }

        if (config.SessionLifetimeDays is < MinSessionLifetimeDays or > MaxSessionLifetimeDays)
        {
            errors[nameof(AppConfig.SessionLifetimeDays)] = SessionLifetimeRange;
        }

        if (config.MaxReceiptSizeMb is < MinReceiptSizeMb or > MaxReceiptSizeMb)
        {
            errors[nameof(AppConfig.MaxReceiptSizeMb)] = ReceiptSizeRange;
        }

        if (config.ReceiptTargetKb is < MinReceiptTargetKb or > MaxReceiptTargetKb)
        {
            errors[nameof(AppConfig.ReceiptTargetKb)] = ReceiptTargetRange;
        }

        return errors;
    }

    [GeneratedRegex(CurrencyPattern)]
    private static partial Regex CurrencyRegex();
}
