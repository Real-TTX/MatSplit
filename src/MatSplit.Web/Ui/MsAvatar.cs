using System.Globalization;

namespace MatSplit.Web.Ui;

/// <summary>
/// Pure helpers for the <see cref="MsAvatarTagHelper"/>: initials from a display
/// name and a deterministic background colour derived from a stable string hash.
/// No <c>Random</c> / <c>DateTime</c> is used, so the same name always yields the
/// same avatar across requests, servers and reloads.
/// </summary>
internal static class MsAvatar
{
    /// <summary>
    /// Returns one or two uppercase initials for a display name. Parenthetical
    /// suffixes such as "(du)" are ignored. Two words yield the first letter of
    /// the first and last word; a single word yields its first (up to two)
    /// letters. Falls back to "?" when nothing usable is found.
    /// </summary>
    public static string Initials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var cleaned = StripParentheticals(displayName);

        var words = cleaned.Split(
            [' ', '\t', '\n', '\r', '-', '_', '.', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var letters = new List<string>();
        foreach (var word in words)
        {
            var first = FirstLetterOrDigit(word);
            if (first is not null)
            {
                letters.Add(first);
            }
        }

        if (letters.Count == 0)
        {
            var single = FirstLetterOrDigit(cleaned);
            return single ?? "?";
        }

        if (letters.Count == 1)
        {
            // Single word: use up to the first two letter/digit characters.
            var glyphs = words[0].Where(char.IsLetterOrDigit).ToArray();
            if (glyphs.Length >= 2)
            {
                return string.Concat(
                    char.ToUpperInvariant(glyphs[0]),
                    char.ToUpperInvariant(glyphs[1]));
            }

            return letters[0];
        }

        return (letters[0] + letters[^1]).ToUpperInvariant();
    }

    /// <summary>
    /// Deterministic background colour as an <c>hsl(...)</c> string derived from a
    /// stable FNV-1a hash of the trimmed, lower-cased name. Saturation and
    /// lightness are fixed to values that read well with white text in both the
    /// light and the dark theme.
    /// </summary>
    public static string Background(string? name)
    {
        var hue = Hue(name);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"hsl({hue}, 62%, 45%)");
    }

    /// <summary>Hue 0..359 from a stable hash of the name.</summary>
    public static int Hue(string? name)
    {
        var key = (name ?? string.Empty).Trim().ToLowerInvariant();

        // FNV-1a 32 bit: deterministic, no allocation, stable across runtimes.
        uint hash = 2166136261u;
        foreach (var character in key)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return (int)(hash % 360u);
    }

    private static string StripParentheticals(string value)
    {
        if (value.IndexOf('(') < 0)
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var depth = 0;
        foreach (var character in value)
        {
            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }

                continue;
            }

            if (depth == 0)
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? value : result;
    }

    private static string? FirstLetterOrDigit(string word)
    {
        foreach (var character in word)
        {
            if (char.IsLetterOrDigit(character))
            {
                return char.ToUpperInvariant(character).ToString();
            }
        }

        return null;
    }
}
