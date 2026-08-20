using System.Globalization;
using System.Text.RegularExpressions;

namespace MatSplit.Web.Infrastructure;

/// <summary>
/// Builds paypal.me deep links for settlement suggestions.
/// Accepts a full profile URL (https://paypal.me/horst), a "paypal.me/horst"
/// fragment or a plain handle. E-mail addresses cannot be expressed as a
/// paypal.me handle and therefore yield null.
/// </summary>
public static partial class PayPalLinkBuilder
{
    private const string BaseUrl = "https://paypal.me/";

    /// <summary>
    /// Returns the payment link or null when no usable handle can be derived.
    /// </summary>
    public static string? BuildLink(string? payPalAddress, long amountCents, string? currency)
    {
        var handle = ExtractHandle(payPalAddress);
        if (handle is null || amountCents <= 0)
        {
            return null;
        }

        var amount = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var currencyCode = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();

        return $"{BaseUrl}{handle}/{amount}{currencyCode}";
    }

    /// <summary>
    /// Normalises whatever the user typed into a bare paypal.me handle.
    /// </summary>
    public static string? ExtractHandle(string? payPalAddress)
    {
        if (string.IsNullOrWhiteSpace(payPalAddress))
        {
            return null;
        }

        var value = payPalAddress.Trim();

        // E-mail addresses are not supported by paypal.me.
        if (value.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        var markerIndex = value.IndexOf("paypal.me/", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            value = value[(markerIndex + "paypal.me/".Length)..];
        }
        else
        {
            markerIndex = value.IndexOf("paypal.com/paypalme/", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                value = value[(markerIndex + "paypal.com/paypalme/".Length)..];
            }
        }

        // Strip query string, fragment and any trailing path segments.
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        value = value.Trim('/', ' ');
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            value = value[..slash];
        }

        if (value.Length == 0 || !HandlePattern().IsMatch(value))
        {
            return null;
        }

        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HandlePattern();
}
