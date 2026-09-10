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
    /// merged into anybody, because nothing in it says who it was.
    /// </para>
    /// <para>
    /// The ordinary bar is two words of two letters or more. That refuses an initial in either
    /// position and refuses a lone given name, which identifies nobody in a club that has two of
    /// them. A name below the bar is reported unresolved and carried into the trip's own words,
    /// so the person is still recorded as having been there — just not as a roster row nobody
    /// can place.
    /// </para>
    /// <para>
    /// <paramref name="allowAbbreviated"/> lowers that bar to any name at all, and exists because
    /// whole clubs write their sheets in exactly the refused form — a given name and an initial,
    /// or a given name alone. For those sheets the strict bar does not protect a roster, it
    /// prevents one: nearly every person on every trip is refused, and the import records a
    /// history with almost nobody in it. Lowering the bar is a decision about one club's sheet
    /// and belongs to whoever is reading it, so it is a choice made per import and off unless it
    /// is made. It lowers the bar for both refused shapes at once, because they are one question:
    /// a club that writes <c>Given I.</c> writes <c>Given</c> on the next line. What it never
    /// touches is a name that answers to more than one person already here — that is ambiguity
    /// between real people, settled by saying which, and a short name is not the same thing.
    /// </para>
    /// </summary>
    public static bool MayCreatePerson(string? value, bool allowAbbreviated)
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

        // Something was written and it is not only punctuation: below the ordinary bar, but a
        // person the reviewer has said may be made.
        if (allowAbbreviated)
        {
            return key.Any(char.IsLetterOrDigit);
        }

        return words >= 2;
    }
}
