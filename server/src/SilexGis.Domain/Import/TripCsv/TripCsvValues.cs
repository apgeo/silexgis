// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.Import.TripCsv;

/// <summary>The values a cell held, and the pieces of it that did not survive.</summary>
/// <param name="Values">The kept values, in the order they were written, without repeats.</param>
/// <param name="Dropped">Pieces that carried no letter or digit in any script.</param>
public readonly record struct TripCsvCellValues(
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Dropped);

/// <summary>
/// Splitting one cell into the several values it holds, and tidying a single-valued one.
/// </summary>
public static class TripCsvValues
{
    /// <summary>
    /// Whitespace collapsed to single spaces and the result trimmed. A byte-order mark is
    /// stripped here as well as at the start of the file: .NET does not count it as whitespace,
    /// so left in place it becomes an invisible first character of a name that then matches
    /// nothing and looks identical to the name that would have.
    /// </summary>
    public static string Tidy(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (ch == '\uFEFF')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when a value carries something a person could have meant, in any script. Asking only
    /// about the Latin alphabet is what silently deletes a Bulgarian or a Greek cave name, and a
    /// deletion nobody is told about is worse than an unrecognised name.
    /// </summary>
    public static bool CarriesMeaning(string value) => value.Any(char.IsLetterOrDigit);

    /// <summary>
    /// Splits a cell on the given separators. The skip-token set is matched against the whole
    /// cell before anything is split, and again against each piece afterwards. Matching only the
    /// pieces is what turns <c>n/a</c> into two people called <c>n</c> and <c>a</c> in any column
    /// where a slash separates values: nothing ever sees the cell whole, so the set that would
    /// have recognised it never gets the chance.
    /// </summary>
    public static TripCsvCellValues Split(
        string? cell,
        IReadOnlyList<char> separators,
        IReadOnlySet<string> skipTokens)
    {
        // The whole cell first. A cell that only ever said "nothing here" has to be recognised
        // before a separator inside it turns it into several values that each mean something.
        var whole = Tidy(cell);
        if (whole.Length == 0 || skipTokens.Contains(FoldedText.Of(whole).Value))
        {
            return new TripCsvCellValues([], []);
        }

        string[] pieces = separators.Count == 0
            ? [whole]
            : whole.Split([.. separators], StringSplitOptions.None);

        var values = new List<string>();
        var dropped = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var piece in pieces)
        {
            var tidy = Tidy(piece);
            if (tidy.Length == 0)
            {
                continue;
            }

            if (skipTokens.Contains(FoldedText.Of(tidy).Value))
            {
                continue;
            }

            if (!CarriesMeaning(tidy))
            {
                dropped.Add(tidy);
                continue;
            }

            if (seen.Add(FoldedText.Of(tidy).Value))
            {
                values.Add(tidy);
            }
        }

        return new TripCsvCellValues(values, dropped);
    }

    /// <summary>A single-valued cell: tidied, and empty where it only ever said "nothing here".</summary>
    public static string? Single(string? cell, IReadOnlySet<string> skipTokens)
    {
        var tidy = Tidy(cell);
        if (tidy.Length == 0 || skipTokens.Contains(FoldedText.Of(tidy).Value))
        {
            return null;
        }

        return tidy;
    }
}
