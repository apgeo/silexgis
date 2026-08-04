// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.Documents;

/// <summary>
/// The shape extracted page text is stored in, whatever produced it.
/// <para>
/// Every reader emits something slightly different — a word processor's page break, a
/// spreadsheet's cell separators, the line wrapping a text file was typed with — and the
/// differences are formatting rather than content. Normalising here means two readings of the
/// same page compare as equal when the text has not changed, which is what makes a
/// re-extraction detectable as a no-op rather than as a rewrite of everything downstream.
/// </para>
/// </summary>
public static class PageText
{
    /// <summary>
    /// Most characters kept for one page. Generous enough that no real page is cut, and finite
    /// so that a file built to expand without limit cannot decide how much memory is used.
    /// </summary>
    public const int MaxCharacters = 2_000_000;

    /// <summary>
    /// The storable form of a page's text: line endings unified, trailing space on each line
    /// dropped, runs of blank lines reduced to one, and the whole trimmed. Null when the page
    /// holds nothing readable, which is the same value a page nothing has read yet carries —
    /// the two are told apart by whether a reader recorded itself against the page.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var result = new StringBuilder(Math.Min(text.Length, MaxCharacters));
        var lineStart = result.Length;
        var newlines = 0;

        foreach (var c in text)
        {
            if (result.Length >= MaxCharacters)
            {
                break;
            }

            if (c is '\r')
            {
                continue;
            }

            if (c is '\n')
            {
                // Trailing horizontal space is invisible in the source and would otherwise
                // make two identical-looking pages compare as different.
                while (result.Length > lineStart && result[^1] is ' ' or '\t')
                {
                    result.Length--;
                }

                newlines++;
                if (newlines <= 2)
                {
                    result.Append('\n');
                    lineStart = result.Length;
                }

                continue;
            }

            // Control characters other than a tab are markup that escaped its reader.
            if (char.IsControl(c) && c is not '\t')
            {
                continue;
            }

            newlines = 0;
            result.Append(c);
        }

        while (result.Length > 0 && char.IsWhiteSpace(result[^1]))
        {
            result.Length--;
        }

        var start = 0;
        while (start < result.Length && char.IsWhiteSpace(result[start]))
        {
            start++;
        }

        return result.Length - start == 0 ? null : result.ToString(start, result.Length - start);
    }
}
