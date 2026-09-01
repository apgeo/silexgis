// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.Catalogue;

/// <summary>
/// The spellings a Romanian search term has to be tried in, because the catalogue stores all of
/// them and matches none of them against each other.
///
/// <para>
/// Romanian s-comma and t-comma (<c>ș</c> U+0219, <c>ț</c> U+021B) are the correct letters, but
/// for years the widely-deployed fonts and keyboards produced the Turkish cedilla forms
/// (<c>ş</c> U+015F, <c>ţ</c> U+0163) instead, and a great deal of Romanian text was written
/// with them. The catalogue contains both, alongside plenty of entries typed with no diacritics
/// at all. Its search does not fold any of these together — measured, a search for
/// <c>urșilor</c> and a search for <c>urşilor</c> return entirely disjoint sets of caves.
/// </para>
/// <para>
/// So a single literal query silently returns part of the answer. This produces the small set of
/// spellings worth asking about; the caller sends them together and merges what comes back.
/// </para>
/// </summary>
public static class RomanianText
{
    private const char SCommaLower = 'ș';
    private const char SCommaUpper = 'Ș';
    private const char TCommaLower = 'ț';
    private const char TCommaUpper = 'Ț';

    private const char SCedillaLower = 'ş';
    private const char SCedillaUpper = 'Ş';
    private const char TCedillaLower = 'ţ';
    private const char TCedillaUpper = 'Ţ';

    /// <summary>
    /// The spellings of <paramref name="term"/> worth searching for, most faithful first: what
    /// was typed, then the same word in each of the two diacritic conventions, then with the
    /// diacritics dropped entirely. Duplicates are removed, so a term with no Romanian letters in
    /// it yields exactly one spelling and costs nothing.
    /// </summary>
    public static IReadOnlyList<string> SearchSpellings(string? term)
    {
        var trimmed = term?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        var spellings = new List<string>(4) { trimmed };

        foreach (var candidate in new[] { ToCommaBelow(trimmed), ToCedilla(trimmed), ToAscii(trimmed) })
        {
            if (!spellings.Contains(candidate, StringComparer.Ordinal))
            {
                spellings.Add(candidate);
            }
        }

        return spellings;
    }

    /// <summary>Rewrites cedilla s and t as the comma-below letters Romanian actually uses.</summary>
    public static string ToCommaBelow(string value) => Map(value, c => c switch
    {
        SCedillaLower => SCommaLower,
        SCedillaUpper => SCommaUpper,
        TCedillaLower => TCommaLower,
        TCedillaUpper => TCommaUpper,
        _ => c,
    });

    /// <summary>Rewrites comma-below s and t as the cedilla forms much older Romanian text uses.</summary>
    public static string ToCedilla(string value) => Map(value, c => c switch
    {
        SCommaLower => SCedillaLower,
        SCommaUpper => SCedillaUpper,
        TCommaLower => TCedillaLower,
        TCommaUpper => TCedillaUpper,
        _ => c,
    });

    /// <summary>
    /// Drops Romanian diacritics, both conventions, leaving the plain letters underneath. This is
    /// how a good deal of the catalogue was typed, so it is a spelling worth asking about rather
    /// than merely a normalisation.
    /// </summary>
    public static string ToAscii(string value) => Map(value, c => c switch
    {
        'ă' or 'â' => 'a',
        'Ă' or 'Â' => 'A',
        'î' => 'i',
        'Î' => 'I',
        SCommaLower or SCedillaLower => 's',
        SCommaUpper or SCedillaUpper => 'S',
        TCommaLower or TCedillaLower => 't',
        TCommaUpper or TCedillaUpper => 'T',
        _ => c,
    });

    private static string Map(string value, Func<char, char> map)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(map(c));
        }

        return builder.ToString();
    }
}
