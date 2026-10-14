using System.Reflection;
using Microsoft.AspNetCore.Html;

namespace MatSplit.Web.Ui;

/// <summary>
/// What the layout needs from the MatUi toolkit that is not markup of its own: the icon sprite that is
/// written inline once per page, the German texts of the toolkit and the version shown at the foot of the menu.
/// </summary>
public static class MsUi
{
    /// <summary>The toolkit files are copies under wwwroot/matui (CODE-03: an app carries them, it never loads them from elsewhere).</summary>
    public const string ToolkitPath = "/matui";

    private static string? _sprite;

    /// <summary>
    /// The texts the toolkit writes itself (dialog buttons, the tip after Copy), in German. They go on the
    /// <c>html</c> element; the markup of a page keeps its own words.
    /// </summary>
    public static IHtmlContent ToolkitTexts { get; } = new HtmlString(
        "data-dlg-ok=\"OK\" data-dlg-cancel=\"Abbrechen\" data-dlg-close=\"Schließen\" " +
        "data-dlg-confirm=\"Bitte bestätigen\" data-dlg-notice=\"Hinweis\" data-dlg-input=\"Eingabe\" " +
        "data-copied=\"Kopiert\" data-theme-label=\"Design: {0}. Klicken für {1}.\" " +
        "data-theme-name-system=\"System\" data-theme-name-light=\"Hell\" data-theme-name-dark=\"Dunkel\" " +
        "data-msg-required=\"Bitte fülle dieses Feld aus.\" data-msg-type=\"Bitte gib einen gültigen Wert ein.\" " +
        "data-msg-pattern=\"Das Format passt nicht.\" data-week-start=\"1\"");

    /// <summary>The icon sprite of the toolkit, read once and placed inline at the top of the body.</summary>
    public static IHtmlContent Sprite(string webRootPath)
    {
        _sprite ??= File.ReadAllText(Path.Combine(webRootPath, "matui", "matui-icons.svg"));
        return new HtmlString(_sprite);
    }

    /// <summary>The version of this build, as the container image carries it (for example 0.5.123-20261014).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(MsUi).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(info))
        {
            return "local";
        }

        // The SDK appends the source revision after a plus sign.
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? info[..plus] : info;
    }
}
