// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;

namespace SilexGis.Domain.Documents;

/// <summary>
/// Decides which language a document's text is written in, from the text itself.
/// <para>
/// This installation's corpus is Romanian and English, and that makes the question a two-way
/// one that two cheap signals settle: the letters Romanian uses and English does not
/// (a-breve, a-circumflex, i-circumflex, s-comma and t-comma), and the handful of words each
/// language cannot write a paragraph without. Nothing here tries to recognise a third
/// language; a text that is neither answers "nobody has said", which is the same answer as
/// no text at all and costs stemming quality rather than findability.
/// </para>
/// <para>
/// Deliberately conservative. Two thresholds have to be cleared before an answer is given —
/// enough evidence, and a clear enough margin between the two candidates — because a wrong
/// code makes a document harder to find than no code at all: the neutral configuration at
/// least matches the words as written, while the wrong stemmer rewrites them into forms the
/// query will never produce.
/// </para>
/// </summary>
public static class LanguageDetection
{
    /// <summary>Primary subtag written when the text reads as Romanian.</summary>
    public const string Romanian = "ro";

    /// <summary>Primary subtag written when the text reads as English.</summary>
    public const string English = "en";

    /// <summary>
    /// How much of a document is read before deciding. A page or two settles the question and
    /// a survey report can run to a million characters, so the rest is only cost. Counted in
    /// characters rather than pages because a page is a division of the format, not of the
    /// prose: one spreadsheet "page" can hold the whole workbook.
    /// <para>
    /// Public because a caller that has to fetch the text before handing it over should fetch
    /// only this much: the bound is useless if the text is already in memory by the time it
    /// applies, and two copies of the number would drift.
    /// </para>
    /// </summary>
    public const int SampleCharacters = 40_000;

    /// <summary>
    /// Weight of a word carrying a letter only Romanian writes. Worth more than a common word
    /// because it cannot be a coincidence of vocabulary, and heavy enough that a single
    /// properly-typed Romanian sentence decides on its own.
    /// </summary>
    private const int DiacriticWeight = 3;

    /// <summary>
    /// Evidence needed before any answer is given. Below this the sample is a filename, a
    /// column header or a page number, and guessing from it would be guessing.
    /// </summary>
    private const int MinimumScore = 8;

    /// <summary>
    /// Share of the total evidence the winner must hold. A bilingual abstract, or an English
    /// report quoting Romanian cave names, lands under this and is left unanswered rather
    /// than stemmed with whichever language happened to be ahead.
    /// </summary>
    private const double WinningShare = 0.65;

    /// <summary>Longest token considered a candidate marker; the markers are all short.</summary>
    private const int LongestMarker = 8;

    /// <summary>
    /// The letters Romanian orthography uses that English does not. Both the comma-below
    /// forms of the standard and the cedilla forms older documents were typed with are
    /// listed, because a file written in 1998 will carry the latter and is no less Romanian
    /// for it.
    /// </summary>
    private static readonly SearchValues<char> RomanianLetters =
        SearchValues.Create("ăâîșțşţ");

    /// <summary>
    /// Romanian words frequent enough to appear in any paragraph and absent from English.
    /// Only the diacritic-free spellings are listed, and deliberately so: a word carrying a
    /// Romanian letter has already been credited by the letter signal and never reaches this
    /// list, while material typed without diacritics — common in older material here, and
    /// exactly the case the letter signal cannot reach — is what this list is for.
    /// </summary>
    private static readonly HashSet<string> RomanianMarkers = new(StringComparer.Ordinal)
    {
        "si", "in", "este", "sunt", "pentru", "din", "dintre", "catre", "dupa", "prin",
        "peste", "fara", "acest", "aceasta", "acesta", "cu", "de", "la", "pe", "ca", "nu",
        "se", "au", "era", "fost", "mai", "dar", "sau", "pana", "unde", "cand", "cum",
        "asupra", "ale", "lui", "lor", "foarte", "mare", "mic", "intre", "sub", "spre",
    };

    /// <summary>
    /// English words frequent enough to appear in any paragraph and absent from Romanian.
    /// Short ones that collide with a Romanian word are left out rather than weighted down:
    /// a marker that means two things is not a marker.
    /// </summary>
    private static readonly HashSet<string> EnglishMarkers = new(StringComparer.Ordinal)
    {
        "the", "and", "of", "to", "is", "that", "was", "for", "with", "this", "are", "from",
        "have", "which", "been", "were", "not", "but", "they", "their", "has", "had", "also",
        "its", "between", "through", "after", "before", "during", "about", "into", "over",
        "under", "where", "when", "how", "than", "then", "these", "those", "such", "other",
        "more", "most", "some", "there", "would", "could", "should", "only", "each",
    };

    /// <summary>
    /// The language a run of pages reads as, or null when the text does not say clearly.
    /// Null pages are skipped, which is what an image page inside a paged document looks like.
    /// </summary>
    public static string? Detect(IEnumerable<string?> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var romanian = 0;
        var english = 0;
        var read = 0;

        foreach (var page in pages)
        {
            if (read >= SampleCharacters)
            {
                break;
            }

            if (string.IsNullOrEmpty(page))
            {
                continue;
            }

            var take = Math.Min(page.Length, SampleCharacters - read);
            Score(page.AsSpan(0, take), ref romanian, ref english);
            read += take;
        }

        return Decide(romanian, english);
    }

    /// <summary>The language a single run of text reads as, or null when it does not say clearly.</summary>
    public static string? Detect(string? text) => Detect([text]);

    /// <summary>
    /// Walks the sample word by word, crediting each recognised marker to its language. Words
    /// are cut on anything that is not a letter, so hyphenation, punctuation and numbers all
    /// end a word without needing a rule of their own.
    /// </summary>
    private static void Score(ReadOnlySpan<char> text, ref int romanian, ref int english)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isLetter = i < text.Length && char.IsLetter(text[i]);
            if (isLetter)
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                ScoreWord(text[start..i], ref romanian, ref english);
                start = -1;
            }
        }
    }

    private static void ScoreWord(ReadOnlySpan<char> word, ref int romanian, ref int english)
    {
        if (word.Length > LongestMarker)
        {
            // Still worth reading for its letters: "peșterile" is decisive and is not a marker.
            if (ContainsRomanianLetter(word))
            {
                romanian += DiacriticWeight;
            }

            return;
        }

        Span<char> lowered = stackalloc char[LongestMarker];
        var length = word.ToLowerInvariant(lowered);
        if (length < 0)
        {
            return;
        }

        var candidate = lowered[..length];
        if (ContainsRomanianLetter(candidate))
        {
            romanian += DiacriticWeight;
            return;
        }

        var token = candidate.ToString();
        if (RomanianMarkers.Contains(token))
        {
            romanian++;
        }
        else if (EnglishMarkers.Contains(token))
        {
            english++;
        }
    }

    private static bool ContainsRomanianLetter(ReadOnlySpan<char> word)
    {
        foreach (var c in word)
        {
            if (RomanianLetters.Contains(char.ToLowerInvariant(c)))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Decide(int romanian, int english)
    {
        var total = romanian + english;
        if (total < MinimumScore)
        {
            return null;
        }

        if (romanian >= total * WinningShare)
        {
            return Romanian;
        }

        return english >= total * WinningShare ? English : null;
    }
}
