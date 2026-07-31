// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Messaging;

/// <summary>
/// Substitutes <c>{placeholder}</c> values into template text, and reports which placeholders a
/// piece of text uses so the admin editor can refuse one the message will never be given.
/// </summary>
public static partial class MessageTemplateRenderer
{
    /// <summary>
    /// A placeholder is a brace-delimited run of letters and digits. Anything else between braces
    /// is left exactly as written, so prose containing a brace survives unharmed.
    /// </summary>
    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// Renders <paramref name="text"/> against <paramref name="values"/>. A placeholder with no
    /// value renders as nothing rather than throwing: a template edited into a broken state must
    /// still let someone reset their password, and the empty gap is visible in the result.
    /// </summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return PlaceholderPattern().Replace(
            text,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : string.Empty);
    }

    /// <summary>The distinct placeholder names used in a piece of text, in first-seen order.</summary>
    public static IReadOnlyList<string> PlaceholdersIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var seen = new List<string>();
        foreach (Match match in PlaceholderPattern().Matches(text))
        {
            var name = match.Groups[1].Value;
            if (!seen.Contains(name, StringComparer.Ordinal))
            {
                seen.Add(name);
            }
        }

        return seen;
    }

    /// <summary>
    /// Placeholders used by subject or body that the template is not allowed to use. Empty when
    /// the text is valid.
    /// </summary>
    public static IReadOnlyList<string> UnknownPlaceholders(
        MessageTemplateDefinition definition, string? subject, string body)
    {
        var allowed = definition.Placeholders;
        return [.. PlaceholdersIn(subject).Concat(PlaceholdersIn(body))
            .Distinct(StringComparer.Ordinal)
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))];
    }

    /// <summary>
    /// Collapses the blank line that a removed placeholder can leave behind, so a message with an
    /// unsupplied value does not arrive with a hole in the middle of it.
    /// </summary>
    public static string Tidy(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder();
        var blankRun = 0;
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                blankRun++;
                if (blankRun > 1)
                {
                    continue;
                }
            }
            else
            {
                blankRun = 0;
            }

            builder.Append(trimmed).Append('\n');
        }

        return builder.ToString().Trim('\n');
    }
}
