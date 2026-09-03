// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// How a name written in a spreadsheet is compared with a name already here, and when it is
/// enough of a name to make a person out of.
/// </summary>
public static class TripImportNames
{
    /// <summary>
    /// The form two names are compared in: folded to lower case with the diacritics removed, and
    /// with runs of whitespace collapsed to one space so that a name typed with two spaces is the
    /// same name. Folding is done by the one folder this project has rather than a second one —
    /// two folders that disagree by a single character silently stop matching, and the symptom is
    /// an importer that proposes nothing while looking like it is working.
    /// </summary>
    public static string Key(string? value)
    {
        var folded = FoldedText.Of(value?.Trim()).Value;
        if (folded.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(folded.Length);
        var wasSpace = false;
        foreach (var ch in folded)
        {
            if (char.IsWhiteSpace(ch))
            {
                wasSpace = true;
                continue;
            }

            if (wasSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            wasSpace = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether a person may be created from this name, as opposed to only matched against one.
    ///
    /// <para>
    /// Matching happens for every name whatever this answers: somebody already in the roster is
    /// found by their initials as readily as by their full name. Creating is the asymmetric half,
    /// because the two mistakes are not each other's mirror. A duplicate roster entry is one
    /// merge to fix; a person invented from an initial is a row that can never be confidently
    /// merged into anybody, because nothing in it says who it was. A club sheet naming
    /// <c>Ion A.</c> on forty trips would otherwise leave forty half-people behind, and the next
    /// import would leave forty more.
    /// </para>
    /// <para>
    /// The bar is two words of two letters or more. That refuses an initial in either position
    /// and refuses a lone given name, which identifies nobody in a club that has two of them.
    /// A name below the bar is reported unresolved and carried into the trip's own words, so the
    /// person is still recorded as having been there — just not as a roster row nobody can place.
    /// </para>
    /// </summary>
    public static bool MayCreatePerson(string? value)
    {
        var key = Key(value);
        if (key.Length == 0)
        {
            return false;
        }

        var words = 0;
        foreach (var token in key.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var letters = token.Count(char.IsLetterOrDigit);
            if (letters >= 2)
            {
                words++;
            }
        }

        return words >= 2;
    }
}
