using Microsoft.AspNetCore.Mvc.Rendering;

namespace MatSplit.Web.Ui;

/// <summary>
/// Produces stable, request unique fallback ids so that the same control can be
/// used several times on one page even when the page does not supply an id.
/// </summary>
internal static class MsControlIds
{
    private const string CounterKey = "MatSplit.Ui.ControlCounter";

    public static string Next(ViewContext? viewContext, string controlName)
    {
        var items = viewContext?.HttpContext?.Items;
        var next = 1;

        if (items is not null)
        {
            if (items.TryGetValue(CounterKey, out var raw) && raw is int current)
            {
                next = current + 1;
            }

            items[CounterKey] = next;
        }

        return $"ms-{controlName}-{next}";
    }

    /// <summary>Turns arbitrary text into a token usable inside an html id.</summary>
    public static string Slug(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var buffer = new char[text.Length];
        var length = 0;

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                continue;
            }

            if (length > 0 && buffer[length - 1] != '-')
            {
                buffer[length++] = '-';
            }
        }

        var slug = new string(buffer, 0, length).Trim('-');
        return slug.Length == 0 ? fallback : slug;
    }
}
