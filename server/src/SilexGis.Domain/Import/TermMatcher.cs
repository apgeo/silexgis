// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Import;

/// <summary>Where a term was found, in coordinates of the original (unfolded) text.</summary>
public readonly record struct TermMatch(string Term, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Compares terms against a candidate's text, case- and diacritic-blind in every mode —
/// field GPS units mangle both, and a term list that had to enumerate the manglings would
/// be unusable.
/// </summary>
public static class TermMatcher
{
    /// <summary>
    /// A ceiling on one regular-expression evaluation. Rules are user-written and shared
    /// between installations, so a pattern that backtracks catastrophically is a thing a
    /// club can be sent rather than only a thing it can write; a preview over a few thousand
    /// candidates must fail that rule rather than the request.
    /// </summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The first place any of the rule's terms matches the text, or null. Terms are tried in
    /// the order the rule lists them and the earliest position wins, so a set holding both
    /// <c>pestera</c> and <c>pesteră</c> cannot depend on which was typed first.
    /// </summary>
    public static TermMatch? Match(
        string? text, IEnumerable<string> terms, TermMatchMode mode)
    {
        var folded = FoldedText.Of(text);
        if (folded.Value.Length == 0)
        {
            return null;
        }

        TermMatch? best = null;
        foreach (var term in terms)
        {
            var candidate = MatchOne(folded, term, mode);
            if (candidate is not null && (best is null || candidate.Value.Start < best.Value.Start))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Whether a pattern can be used as a rule term — checked when a set is saved, not when it runs.</summary>
    public static bool IsValidRegex(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            _ = Regex.Match(string.Empty, pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static TermMatch? MatchOne(FoldedText folded, string term, TermMatchMode mode)
    {
        if (mode == TermMatchMode.Regex)
        {
            return MatchRegex(folded, term);
        }

        var needle = FoldedText.Of(term).Value.Trim();
        if (needle.Length == 0)
        {
            return null;
        }

        var haystack = folded.Value;
        return mode switch
        {
            TermMatchMode.Prefix => haystack.StartsWith(needle, StringComparison.Ordinal)
                ? ToSource(folded, 0, needle.Length, term)
                : null,
            TermMatchMode.WholeWord => MatchWholeWord(folded, needle, term),
            _ => haystack.IndexOf(needle, StringComparison.Ordinal) is var index and >= 0
                ? ToSource(folded, index, needle.Length, term)
                : null,
        };
    }

    private static TermMatch? MatchWholeWord(FoldedText folded, string needle, string term)
    {
        // A term may itself end in punctuation ("p." is how half of Romania labels a cave on
        // a GPS), so the boundary is asked of the term's own edge characters: a term that
        // ends in a dot needs nothing but the dot on its right, while one that ends in a
        // letter needs a non-word character there.
        var haystack = folded.Value;
        var startsWithWord = char.IsLetterOrDigit(needle[0]) || needle[0] == '_';
        var endsWithWord = char.IsLetterOrDigit(needle[^1]) || needle[^1] == '_';

        var from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, from, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            var leftClear = !startsWithWord || !folded.IsWordCharacter(index - 1);
            var rightClear = !endsWithWord || !folded.IsWordCharacter(index + needle.Length);
            if (leftClear && rightClear)
            {
                return ToSource(folded, index, needle.Length, term);
            }

            from = index + 1;
        }

        return null;
    }

    private static TermMatch? MatchRegex(FoldedText folded, string pattern)
    {
        try
        {
            // Applied to the folded text, so a pattern written with plain ASCII letters
            // matches an accented name exactly as the literal modes do.
            var match = Regex.Match(folded.Value, pattern, RegexOptions.CultureInvariant, RegexTimeout);
            return match.Success && match.Length > 0
                ? ToSource(folded, match.Index, match.Length, match.Value)
                : null;
        }
        catch (ArgumentException)
        {
            return null; // an unparseable pattern claims nothing; saving one is refused
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static TermMatch ToSource(FoldedText folded, int start, int length, string term)
    {
        var (from, to) = folded.SourceRange(start, length);
        return new TermMatch(term, from, to - from);
    }
}
