// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// Turns a source label into the name the registry keeps. The term that identified a
/// candidate is almost never wanted in its name — <c>P. Ursilor</c> is the cave <c>Ursilor</c>,
/// filed under caves — so a rule may ask for it to be taken back out.
/// </summary>
public static class ImportNameCleaner
{
    /// <summary>
    /// The name with the matched term removed per the rule's strip mode, tidied so no
    /// separator is left dangling. Returns the name unchanged when nothing should be
    /// stripped, and null when stripping would leave nothing at all — a candidate named
    /// exactly <c>Peștera</c> keeps its only label rather than becoming nameless.
    /// </summary>
    public static string? Apply(string? name, TermMatch match, TermStripMode mode)
    {
        if (mode == TermStripMode.None || string.IsNullOrEmpty(name) || match.Length <= 0)
        {
            return name;
        }

        if (match.Start < 0 || match.End > name.Length)
        {
            return name;
        }

        var leading = name[..match.Start].AsSpan().Trim().Length == 0;
        var trailing = name[match.End..].AsSpan().Trim().Length == 0;
        var strip = mode switch
        {
            TermStripMode.Leading => leading,
            TermStripMode.Trailing => trailing,
            _ => true,
        };

        if (!strip)
        {
            return name;
        }

        var stripped = Tidy(string.Concat(name.AsSpan(0, match.Start), name.AsSpan(match.End)));
        return stripped.Length == 0 ? name : stripped;
    }

    /// <summary>
    /// A prefix applied to a whole selection, kept out of the caller so the space between
    /// prefix and name is decided once. An empty prefix is not an empty name.
    /// </summary>
    public static string? WithPrefix(string? prefix, string? name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(name) ? prefix.Trim() : $"{prefix.Trim()} {name.Trim()}";
    }

    /// <summary>
    /// Removes the punctuation and whitespace a removed term leaves behind: a name that was
    /// <c>P. Ursilor</c> must come out as <c>Ursilor</c>, not as <c>. Ursilor</c> or <c> Ursilor</c>,
    /// and one that was <c>Ursilor (peșteră)</c> must not keep an empty pair of brackets.
    /// </summary>
    private static string Tidy(string value)
    {
        var trimmed = value.Trim().Trim('-', '–', '—', '.', ',', ':', ';', '/', '\\', '_').Trim();
        trimmed = RemoveEmptyBrackets(trimmed);

        // Collapse the double space left where a term was lifted out of the middle.
        var builder = new System.Text.StringBuilder(trimmed.Length);
        var previousWasSpace = false;
        foreach (var ch in trimmed)
        {
            var isSpace = char.IsWhiteSpace(ch);
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : ch);
            previousWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    private static string RemoveEmptyBrackets(string value)
    {
        var result = value;
        foreach (var (open, close) in new[] { ('(', ')'), ('[', ']'), ('{', '}') })
        {
            var index = result.IndexOf(open);
            while (index >= 0)
            {
                var end = result.IndexOf(close, index + 1);
                if (end < 0)
                {
                    break;
                }

                if (result.AsSpan(index + 1, end - index - 1).Trim().Length == 0)
                {
                    result = string.Concat(result.AsSpan(0, index), result.AsSpan(end + 1));
                    index = result.IndexOf(open);
                }
                else
                {
                    index = result.IndexOf(open, end + 1);
                }
            }
        }

        return result.Trim();
    }
}
