// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// How alike two names are, 0 to 1, folded the same way terms are. Used by duplicate
/// detection: proximity alone cannot tell a re-import of last year's file from a genuinely
/// new hole thirty metres from an old one, and the name usually can.
/// </summary>
public static class NameSimilarity
{
    /// <summary>
    /// Similarity of two names after folding: 1 for identical, 0 for nothing in common.
    /// Empty on either side scores 0 rather than 1 — an unnamed candidate tells us nothing,
    /// and scoring it as a perfect match would make every nameless waypoint a duplicate of
    /// every nameless feature nearby.
    /// </summary>
    public static double Of(string? left, string? right)
    {
        var a = FoldedText.Of(left).Value.Trim();
        var b = FoldedText.Of(right).Value.Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1;
        }

        var distance = Levenshtein(a, b);
        var longest = Math.Max(a.Length, b.Length);
        return 1d - ((double)distance / longest);
    }

    /// <summary>
    /// Edit distance, computed over two rows rather than a full matrix — the comparison runs
    /// once per candidate per nearby feature, so an import of a few thousand waypoints does
    /// it tens of thousands of times.
    /// </summary>
    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
