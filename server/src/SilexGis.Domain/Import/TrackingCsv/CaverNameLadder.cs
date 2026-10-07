// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// How a name written on a tracking sheet is matched against the roster: by the whole name as the
/// roster has it, then by the whole name in another order, then by a given name and a surname
/// initial, then by an initial and a surname, then by the given name alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a ladder rather than one comparison.</b> A tracking sheet is written while somebody is
/// on the phone. What gets typed is whatever is quickest and still unambiguous to the person
/// typing: <c>Ion</c> when only one Ion is underground, <c>Ion P.</c> when two Ions are, and the
/// whole name when the sheet is being written up properly afterwards — surname first as often as
/// not, because that is how a register is kept, or as <c>I. Popescu</c>, because that is how a
/// name is signed. All of them are the same person, and an importer that only understood the
/// roster's own spelling would refuse most of a real sheet.
/// </para>
/// <para>
/// <b>Narrowest rung wins, and a rung that answers with several people stops the walk.</b> The
/// rungs are tried from the most specific spelling the written name can be read as, downwards.
/// The first rung that answers with exactly one person is the answer; a rung that answers with
/// several is <em>ambiguous and is not passed over</em> — falling through to a looser rung would
/// turn "two people answer to this" into "one person answers to a shorter version of it", which
/// is how an import quietly files a report against the wrong caver.
/// </para>
/// <para>
/// <b>Nothing here reads a database.</b> It is given the roster to match against and decides only
/// what a name means; who may be seen, and what a reviewer has chosen instead, are the caller's.
/// Matching folds through <see cref="TripImportNames.Key"/> — the one folder this project has — so
/// a diacritic or a doubled space cannot be what stops a name matching.
/// </para>
/// </remarks>
public static class CaverNameLadder
{
    /// <summary>Which spelling of a name found the person, for telling somebody how sure it is.</summary>
    public enum Rung
    {
        /// <summary>The whole name, as written, matched a whole name on the roster.</summary>
        FullName,

        /// <summary>
        /// The whole name, with its words in another order than the roster keeps them — "Popescu
        /// Ion" for Ion Popescu — matched a whole name on the roster.
        /// </summary>
        FullNameAnyOrder,

        /// <summary>A given name and a surname initial — "Ion P." — matched one person.</summary>
        GivenNameAndInitial,

        /// <summary>An initial and a whole surname — "I. Popescu" — matched one person.</summary>
        InitialAndSurname,

        /// <summary>A given name alone matched exactly one person on the roster.</summary>
        GivenName,
    }

    /// <summary>One person a written name was taken for, and how.</summary>
    public readonly record struct Hit<T>(T Key, string Name, Rung By);

    /// <summary>
    /// Who a written name answers to, and by which rung.
    /// </summary>
    /// <param name="written">The name as the sheet wrote it.</param>
    /// <param name="roster">
    /// Everybody this reader may be matched against, as (key, full name). The caller decides who is
    /// in it — a roster narrowed to the trip's own participants matches only them, and one holding
    /// the whole instance matches anybody. A person who goes by two names is given once under
    /// each, with one key, and is found by either.
    /// </param>
    /// <returns>
    /// Every person the winning rung found. Empty is nobody; one is a match; more than one is
    /// ambiguous and must be settled by a person rather than by taking the first.
    /// </returns>
    public static IReadOnlyList<Hit<T>> Match<T>(string? written, IReadOnlyList<(T Key, string? Name)> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var asked = Words(written);
        if (asked.Length == 0)
        {
            return [];
        }

        // One entry per person and spelling, whatever the roster repeats. A trip's roster is one
        // row per person per job, so somebody who both led and surveyed arrives here twice under
        // one key; and one person may be handed in under two names — the roster's entry and the
        // name their account goes by — which are two spellings to be found by and still one
        // person. Either way two hits with one key are not two people to choose between, so every
        // rung answers through Found, which gives each person once. Collapsed here rather than
        // left to every caller, because a caller that forgot would refuse the trip leader as
        // ambiguous with a message naming them against themselves.
        var people = roster
            .Select(person => (person.Key, Display: person.Name ?? string.Empty, Words: Words(person.Name)))
            .Where(person => person.Words.Length > 0)
            .DistinctBy(person => (person.Key, Spelling: string.Join(' ', person.Words)))
            .ToList();

        IReadOnlyList<Hit<T>> Found(IEnumerable<(T Key, string Display, string[] Words)> hits, Rung by) =>
            [.. hits.DistinctBy(p => p.Key).Select(p => new Hit<T>(p.Key, p.Display, by))];

        // Rung one: the whole thing, which is also what a sheet written up properly carries.
        var whole = people.Where(person => person.Words.SequenceEqual(asked)).ToList();
        if (whole.Count > 0)
        {
            return Found(whole, Rung.FullName);
        }

        // Rung two: the whole thing again, in whatever order its words were written. A register
        // is kept surname first and a roster given name first, and both are the whole name. It
        // comes after the rung above and never beside it: where one person is Ion Popescu and
        // another Popescu Ion, each is found by their own spelling, and only a spelling that is
        // neither's asks which of them was meant. One word has one order, so this starts at two.
        if (asked.Length >= 2)
        {
            var sorted = Sorted(asked);
            var reordered = people.Where(person => Sorted(person.Words).SequenceEqual(sorted)).ToList();
            if (reordered.Count > 0)
            {
                return Found(reordered, Rung.FullNameAnyOrder);
            }
        }

        var given = asked[0];

        // Rung three: a given name and an initial — "Ion P." Recognised by the written second word
        // being one letter once its punctuation is off, which is what the stop in "P." is: the
        // shared folder lowercases and removes diacritics and deliberately keeps everything else,
        // so the stop is still there to be dealt with here.
        if (asked.Length == 2 && asked[1].Length == 1)
        {
            var initial = asked[1][0];
            var byInitial = people
                .Where(person => person.Words.Length >= 2
                    && person.Words[0] == given
                    && person.Words[^1][0] == initial)
                .ToList();

            if (byInitial.Count > 0)
            {
                return Found(byInitial, Rung.GivenNameAndInitial);
            }
        }

        // Rung four: an initial and a surname — "I. Popescu". The mirror of the rung above, and
        // told from it by which of the two words is the single letter, so one written name is
        // never read both ways. The surname has to be the whole of the person's last word: an
        // initial narrows a surname down to somebody, it does not stand in for one, and "I. Pop"
        // is not Ion Popescu. Everybody the initial and the surname both fit is an answer — Ion
        // and Ioana Popescu alike — and two answers are a question, as on every other rung.
        if (asked.Length == 2 && asked[0].Length == 1 && asked[1].Length > 1)
        {
            var bySurname = people
                .Where(person => person.Words.Length >= 2
                    && person.Words[^1] == asked[1]
                    && person.Words[0][0] == asked[0][0])
                .ToList();

            if (bySurname.Count > 0)
            {
                return Found(bySurname, Rung.InitialAndSurname);
            }
        }

        // Rung five: a given name alone, and only when the sheet wrote nothing else. A written
        // "Ion Vasilescu" that matched nobody whole is not then matched on "Ion": the sheet was
        // specific and the roster disagreed, which is a name to report rather than to resolve.
        if (asked.Length == 1)
        {
            var byGiven = people.Where(person => person.Words[0] == given).ToList();

            if (byGiven.Count > 0)
            {
                return Found(byGiven, Rung.GivenName);
            }
        }

        return [];
    }

    /// <summary>
    /// A name as the words it is made of: folded by the project's one folder, then with anything
    /// that is not a letter or a digit dropped.
    /// </summary>
    /// <remarks>
    /// The second half is this rule's own and is why it does not simply compare folded strings. The
    /// shared folder lowercases and removes diacritics and keeps punctuation, correctly — a term
    /// list needs the stops. Here a stop is noise in two ways: it is what tells an initial apart
    /// from a word ("P." against "Popescu"), and a stray one typed after a surname would otherwise
    /// stop a whole name matching. Dropping it makes "Ion P.", "Ion P" and "ION p." one written
    /// name, which is three ways a sheet writes the same thing. A stop also ends a word, so that
    /// "I.Popescu", typed without the space, is the two words it was meant as; no other mark
    /// does, because a hyphen joins the halves of one name where a stop abbreviates one.
    /// </remarks>
    private static string[] Words(string? value)
    {
        var folded = TripImportNames.Key(value);
        if (folded.Length == 0)
        {
            return [];
        }

        return [.. folded
            .Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string([.. word.Where(char.IsLetterOrDigit)]))
            .Where(word => word.Length > 0)];
    }

    /// <summary>The words of a name in one fixed order, so two orders of one name compare equal.</summary>
    private static string[] Sorted(string[] words)
    {
        var copy = (string[])words.Clone();
        Array.Sort(copy, StringComparer.Ordinal);
        return copy;
    }
}
