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
/// at all, and its search folds none of them together. Measured against it on one afternoon:
/// </para>
/// <code>
///   Padis → 1 cave     Padiș → 0 caves      Padiş → 2 caves
///   Topolnita → 0      Topolnița → 2        Topolniţa → 1
///   Scarisoara → 0     (the cave is filed as Scărișoara)
/// </code>
/// <para>
/// So a single literal query does not return part of the answer — it frequently returns none of
/// it, and looks exactly like a cave that is not in the register. The expansion below runs in
/// <b>both</b> directions, and the direction that matters most is the one a keyboard forces:
/// almost nobody types <c>urșilor</c>, and <c>ursilor</c> alone finds nothing at all.
/// </para>
/// </summary>
public static class RomanianText
{
    /// <summary>
    /// How many spellings one term is expanded into. They travel as aliased fields of a
    /// <b>single</b> request, so this is the width of one query rather than a number of round
    /// trips: twenty-four aliases were measured against the live catalogue answering in 1.9
    /// seconds with a 399-byte body, which is no more than six of them cost.
    /// </summary>
    /// <remarks>
    /// It has to be this wide to be useful. The famous ice cave is filed as
    /// <c>Scărișoara</c>: reaching it from the <c>scarisoara</c> somebody types needs
    /// <b>two</b> substitutions in different letters, and a narrower expansion returns nothing
    /// while looking exactly like a cave the register does not hold.
    /// </remarks>
    public const int MaxSpellings = 24;

    /// <summary>What each letter might have been written as instead, most likely first.</summary>
    private static readonly IReadOnlyDictionary<char, string> Alternatives = new Dictionary<char, string>
    {
        ['s'] = "șş",
        ['ș'] = "sş",
        ['ş'] = "sș",
        ['t'] = "țţ",
        ['ț'] = "tţ",
        ['ţ'] = "tț",
        ['a'] = "ăâ",
        ['ă'] = "aâ",
        ['â'] = "aă",
        ['i'] = "î",
        ['î'] = "i",
        ['S'] = "ȘŞ",
        ['Ș'] = "SŞ",
        ['Ş'] = "SȘ",
        ['T'] = "ȚŢ",
        ['Ț'] = "TŢ",
        ['Ţ'] = "TȚ",
        ['A'] = "ĂÂ",
        ['Ă'] = "AÂ",
        ['Â'] = "AĂ",
        ['I'] = "Î",
        ['Î'] = "I",
    };

    /// <summary>
    /// What one substitution costs, so the expansion spends its width where it pays.
    ///
    /// <para>
    /// An <c>s</c> or a <c>t</c> is cheap: that is the ambiguity which actually splits this
    /// catalogue, and a word containing one has only a handful of alternatives. A vowel is dear:
    /// almost every word has several, most of them are not accented, and letting them compete
    /// evenly would fill the whole expansion with spellings nobody has ever used before the
    /// second consonant was tried at all.
    /// </para>
    /// </summary>
    private static int CostOf(char c) => char.ToLowerInvariant(c) switch
    {
        's' or 'ș' or 'ş' or 't' or 'ț' or 'ţ' => 1,
        _ => 2,
    };

    /// <summary>
    /// The spellings of <paramref name="term"/> worth searching for: what was typed, the wholly
    /// unaccented form, and then the accented spellings in increasing order of how far they are
    /// from what was typed, up to <see cref="MaxSpellings"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cheapest-first rather than fewest-substitutions-first, and the difference is the whole
    /// point. <c>cetatile</c> reaches <c>cetățile</c> only by changing a <c>t</c> <b>and</b> an
    /// <c>a</c>; counting substitutions alone would rank that behind every single-vowel spelling
    /// of the word, of which there are many and none of which is a cave.
    /// </para>
    /// <para>
    /// A term with no substitutable letter yields exactly one spelling and costs nothing extra.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SearchSpellings(string? term)
    {
        var trimmed = term?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { trimmed };
        var spellings = new List<string> { trimmed };

        // The wholly unaccented form is worth having before anything is expanded: a large part of
        // this catalogue was typed without diacritics, so for many caves it is not a fallback but
        // the only spelling that matches at all.
        var ascii = ToAscii(trimmed);
        if (seen.Add(ascii))
        {
            spellings.Add(ascii);
        }

        // Cheapest first; ties broken by the order they were generated in, which walks the word
        // left to right and takes each letter's alternatives in the order they are declared.
        var queue = new PriorityQueue<string, (int Cost, int Seq)>();
        var sequence = 0;
        queue.Enqueue(trimmed, (0, sequence++));

        while (queue.Count > 0 && spellings.Count < MaxSpellings)
        {
            queue.TryDequeue(out var word, out var priority);

            for (var i = 0; i < word!.Length; i++)
            {
                if (!Alternatives.TryGetValue(word![i], out var options))
                {
                    continue;
                }

                var cost = priority.Cost + CostOf(word![i]);

                foreach (var option in options)
                {
                    var chars = word.ToCharArray();
                    chars[i] = option;
                    var candidate = new string(chars);

                    if (!seen.Add(candidate))
                    {
                        continue;
                    }

                    spellings.Add(candidate);
                    if (spellings.Count >= MaxSpellings)
                    {
                        return spellings;
                    }

                    queue.Enqueue(candidate, (cost, sequence++));
                }
            }
        }

        return spellings;
    }

    /// <summary>Rewrites cedilla s and t as the comma-below letters Romanian actually uses.</summary>
    public static string ToCommaBelow(string value) => Map(value, c => c switch
    {
        'ş' => 'ș',
        'Ş' => 'Ș',
        'ţ' => 'ț',
        'Ţ' => 'Ț',
        _ => c,
    });

    /// <summary>Rewrites comma-below s and t as the cedilla forms much older Romanian text uses.</summary>
    public static string ToCedilla(string value) => Map(value, c => c switch
    {
        'ș' => 'ş',
        'Ș' => 'Ş',
        'ț' => 'ţ',
        'Ț' => 'Ţ',
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
        'ș' or 'ş' => 's',
        'Ș' or 'Ş' => 'S',
        'ț' or 'ţ' => 't',
        'Ț' or 'Ţ' => 'T',
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
