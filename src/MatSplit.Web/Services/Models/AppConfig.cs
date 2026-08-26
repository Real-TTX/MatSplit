namespace MatSplit.Web.Services.Models;

/// <summary>
/// Content of /data/config/appconfig.json. Edited by admins under
/// /Admin/Settings and loaded by <see cref="AppConfigService"/>.
/// </summary>
public sealed class AppConfig
{
    public string AppName { get; set; } = "MatSplit";

    public string DefaultCurrency { get; set; } = "EUR";

    /// <summary>Global kill switch for invite links.</summary>
    public bool AllowAnonymousJoin { get; set; } = true;

    public int SessionLifetimeDays { get; set; } = 30;

    public int MaxReceiptSizeMb { get; set; } = 10;

    /// <summary>Shrink/re-encode receipt photos in the browser before upload.</summary>
    public bool CompressReceipts { get; set; } = true;

    /// <summary>Target size in KB the client compression aims for (best effort).</summary>
    public int ReceiptTargetKb { get; set; } = 500;

    /// <summary>Clamps every value into a sane range.</summary>
    public AppConfig Normalized()
    {
        return new AppConfig
        {
            AppName = string.IsNullOrWhiteSpace(AppName) ? "MatSplit" : AppName.Trim(),
            DefaultCurrency = string.IsNullOrWhiteSpace(DefaultCurrency)
                ? "EUR"
                : DefaultCurrency.Trim().ToUpperInvariant(),
            AllowAnonymousJoin = AllowAnonymousJoin,
            SessionLifetimeDays = Math.Clamp(SessionLifetimeDays, 1, 365),
            MaxReceiptSizeMb = Math.Clamp(MaxReceiptSizeMb, 1, 100),
            CompressReceipts = CompressReceipts,
            ReceiptTargetKb = Math.Clamp(ReceiptTargetKb, 50, 5000)
        };
    }
}
