using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatSplit.Web.Ui;

/// <summary>
/// Small html helpers shared by all ms-* controls. Money formatting uses a hand
/// built NumberFormatInfo so the output stays German even when the process runs
/// with invariant globalization.
/// </summary>
internal static class MsHtml
{
    private static readonly NumberFormatInfo MoneyFormat = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NumberDecimalDigits = 2,
        NegativeSign = "-"
    };

    /// <summary>Formats cents as 1.234,56 EUR using the currency symbol.</summary>
    public static string FormatMoney(long cents, string? currency = "EUR")
    {
        var text = (cents / 100m).ToString("N2", MoneyFormat);
        var symbol = CurrencySymbol(currency);
        return symbol is null ? text : text + "\u00a0" + symbol;
    }

    /// <summary>Formats cents as an invariant decimal, used for input values.</summary>
    public static string FormatDecimalInvariant(long cents)
        => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static string? CurrencySymbol(string? currency) => currency?.ToUpperInvariant() switch
    {
        null or "" => null,
        "EUR" => "\u20ac",
        "USD" => "$",
        "GBP" => "\u00a3",
        "CHF" => "CHF",
        _ => currency
    };

    /// <summary>Joins css class names, skipping empty entries.</summary>
    public static string Classes(params string?[] parts)
        => string.Join(' ', parts.Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>Adds a css class without losing the classes written by the page.</summary>
    public static void AddClass(TagHelperOutput output, string? cssClass)
    {
        if (string.IsNullOrWhiteSpace(cssClass))
        {
            return;
        }

        if (output.Attributes.TryGetAttribute("class", out var existing))
        {
            var current = existing.Value?.ToString();
            output.Attributes.SetAttribute("class", Classes(cssClass, current));
            return;
        }

        output.Attributes.SetAttribute("class", cssClass);
    }

    /// <summary>Renders html content into a string.</summary>
    public static string Render(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    /// <summary>True when the child content contains more than whitespace.</summary>
    public static bool HasContent(TagHelperContent? content)
        => content is not null && !content.IsEmptyOrWhiteSpace;

    /// <summary>
    /// Icons of the old MatSplit sprite that the toolkit sprite (matui-icons.svg) knows under another name.
    /// A name that is in neither place is looked up in the app sprite (<c>_AppIcons.cshtml</c>).
    /// </summary>
    private static readonly Dictionary<string, string> ToolkitIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["group"] = "users",
        ["receipt"] = "file-text",
        ["history"] = "clock",
        ["admin"] = "shield",
        ["forward"] = "arrow-right",
        ["close"] = "x",
        ["warning"] = "alert",
        ["empty"] = "box",
        ["arrow-left"] = "back",
        ["chart"] = "bar-chart",
        ["profile"] = "user-circle",
        ["cog"] = "settings",
        ["dots"] = "more-horizontal"
    };

    /// <summary>Icons that exist in the toolkit sprite under their own name.</summary>
    private static readonly HashSet<string> ToolkitNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "users", "user", "plus", "edit", "trash", "save", "back", "search", "filter", "sort", "menu",
        "sun", "moon", "monitor", "logout", "link", "settings", "info", "check", "chevron-down", "chevron-up",
        "chevron-left", "chevron-right", "copy", "calendar", "download", "upload", "activity", "x", "x-circle",
        "check-circle", "alert", "clock", "dollar", "file-text", "shield", "arrow-right", "box", "bar-chart",
        "user-circle", "more-horizontal", "eye", "eye-off", "lock", "key", "image", "refresh", "smartphone",
        "globe", "help-circle", "star", "list", "bell", "tag", "mail", "sliders"
    };

    /// <summary>The id of the symbol an icon name refers to: the toolkit sprite first, the app sprite for what only MatSplit needs.</summary>
    public static string IconId(string name)
    {
        var key = name.Trim();

        if (ToolkitIcons.TryGetValue(key, out var mapped))
        {
            return "mat-i-" + mapped;
        }

        return ToolkitNames.Contains(key) ? "mat-i-" + key : "ms-i-" + key;
    }

    /// <summary>Builds an inline svg icon that references the sprite symbol; the toolkit sizes it (<c>mat-icon</c>).</summary>
    public static IHtmlContent Icon(string? name, string? extraClass = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return HtmlString.Empty;
        }

        var svg = new TagBuilder("svg");
        svg.AddCssClass(Classes("mat-icon", extraClass));
        svg.Attributes["aria-hidden"] = "true";
        svg.Attributes["focusable"] = "false";

        var use = new TagBuilder("use")
        {
            TagRenderMode = TagRenderMode.Normal
        };
        use.Attributes["href"] = "#" + IconId(name);
        svg.InnerHtml.AppendHtml(use);

        return svg;
    }

    /// <summary>Adds the current query values of the given keys as hidden inputs.</summary>
    public static void AppendPreservedQuery(TagBuilder target, ViewContext viewContext, string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
        {
            return;
        }

        var query = viewContext.HttpContext.Request.Query;

        foreach (var key in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!query.TryGetValue(key, out var values))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (value is null)
                {
                    continue;
                }

                var hidden = new TagBuilder("input")
                {
                    TagRenderMode = TagRenderMode.SelfClosing
                };
                hidden.Attributes["type"] = "hidden";
                hidden.Attributes["name"] = key;
                hidden.Attributes["value"] = value;
                target.InnerHtml.AppendHtml(hidden);
            }
        }
    }

    /// <summary>
    /// Copies the attributes the page wrote on a ms-* element (data-*, aria-*,
    /// title, ...) onto the element the control renders. Needed for every control
    /// that replaces its host element with a TagBuilder, otherwise those
    /// attributes would be dropped silently.
    /// </summary>
    public static void CopyAttributes(TagHelperOutput output, TagBuilder target)
    {
        foreach (var attribute in output.Attributes)
        {
            var name = attribute.Name;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var value = AttributeText(attribute.Value);

            if (string.Equals(name, "class", StringComparison.OrdinalIgnoreCase))
            {
                target.AddCssClass(value);
                continue;
            }

            target.Attributes[name] = value;
        }

        output.Attributes.Clear();
    }

    /// <summary>
    /// Turns a tag helper attribute value into the raw text a TagBuilder expects.
    /// Values that razor already html encoded are decoded once so the TagBuilder
    /// does not encode them twice.
    /// </summary>
    private static string AttributeText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        HtmlString html => System.Net.WebUtility.HtmlDecode(html.Value ?? string.Empty),
        IHtmlContent content => System.Net.WebUtility.HtmlDecode(Render(content)),
        _ => value.ToString() ?? string.Empty
    };
}
