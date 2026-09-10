// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// How many people a review still has to deal with, and how many it would add.
/// </summary>
/// <param name="WillCreate">Distinct names a confirmation would turn into roster entries.</param>
/// <param name="Ambiguous">
/// Distinct names more than one roster entry answers to. Settled by saying which — never by
/// picking the oldest, which is right at a keyboard and wrong in a bulk import.
/// </param>
/// <param name="Uncreatable">
/// Distinct names nothing answered to that no person can be made from under the choices in
/// force. Not ambiguous at all, and yet these are the ones that quietly cost a trip its roster:
/// they become no link whatever the roster switch says, so they are counted without reference
/// to it.
/// </param>
public readonly record struct TripImportPeopleCounts(int WillCreate, int Ambiguous, int Uncreatable);

/// <summary>
/// What a name written on a trip becomes, and how a screenful of those names adds up.
///
/// <para>
/// Both live here, next to each other and next to the rule they share, because the figure above
/// the review and the state on each row are one fact told twice. When they were worked out in
/// two places they disagreed, and the way they disagreed was the worst one available: the
/// headline said nobody needed a decision while people were being dropped from the trips they
/// went on.
/// </para>
/// <para>
/// Nothing here reads a database or writes a row. What a name answered to is looked up by the
/// caller, under whatever the caller is allowed to be told about; this decides only what that
/// answer means.
/// </para>
/// </summary>
public static class TripImportPeople
{
    /// <summary>
    /// What one name the sheet wrote was taken for, given the roster entries it answered to.
    /// </summary>
    /// <param name="source">The name as the sheet wrote it. Blank is not a person and is skipped.</param>
    /// <param name="candidates">
    /// Every roster entry that answered to the name, already narrowed to what this reader may be
    /// told about. It is also exactly the set a reviewer's choice is honoured among, so a choice
    /// naming anybody else is no choice at all.
    /// </param>
    /// <param name="options">The choices in force, which decide what may be created and what a reviewer has settled.</param>
    public static TripImportPersonMatch? Match(
        string? source,
        IReadOnlyList<TripImportCandidate> candidates,
        TripImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        var text = source?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Asked once, here, and carried on the match. The screen that warns about the people an
        // import cannot make needs the same answer this does, and a second version of the rule
        // living in the browser is how that warning came to name people it could perfectly well
        // have made.
        var mayCreate = TripImportNames.MayCreatePerson(text, options.CreateAbbreviatedCavers);

        // A choice picks one of the people this name answered to.
        if (options.ChosenCaverId(text) is { } chosen && candidates.Any(c => c.Id == chosen))
        {
            return new TripImportPersonMatch(
                text,
                TripImportMatchState.Matched,
                chosen,
                candidates.First(c => c.Id == chosen).Name,
                candidates,
                mayCreate,
                false);
        }

        if (candidates.Count == 1)
        {
            return new TripImportPersonMatch(
                text,
                TripImportMatchState.Matched,
                candidates[0].Id,
                candidates[0].Name,
                candidates,
                mayCreate,
                false);
        }

        if (candidates.Count > 1)
        {
            // Two people share a name — the state the roster's merge exists to resolve — so the
            // row waits for somebody to say which. The rule that resolves a typed name to the
            // oldest of them is right at a keyboard, where the person typing knows who they
            // mean, and wrong here, where nobody is watching and the wrong answer becomes a
            // claim about who was underground on a day in 2014. This is untouched by whether
            // abbreviated names may be created: a name being short and a name belonging to two
            // real people are different problems, and only the second one needs a person.
            return new TripImportPersonMatch(
                text, TripImportMatchState.Ambiguous, null, null, candidates, mayCreate, false);
        }

        return new TripImportPersonMatch(
            text,
            TripImportMatchState.Unmatched,
            null,
            null,
            candidates,
            mayCreate,
            options.CreateMissingCavers && mayCreate);
    }

    /// <summary>
    /// What a whole sheet's worth of names adds up to, over the distinct names it wrote.
    ///
    /// <para>
    /// Read off the matches rather than worked out again from the names, so that the count above
    /// the table, the warning beside it and the list of names under it cannot come to disagree
    /// about which people an import is unable to make.
    /// </para>
    /// </summary>
    public static TripImportPeopleCounts Count(IReadOnlyList<TripImportPersonMatch> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        var willCreate = 0;
        var ambiguous = 0;
        var uncreatable = 0;
        foreach (var person in people)
        {
            if (person.WillCreate)
            {
                willCreate++;
            }

            if (person.State == TripImportMatchState.Ambiguous)
            {
                ambiguous++;
            }
            else if (person.State == TripImportMatchState.Unmatched && !person.MayCreate)
            {
                uncreatable++;
            }
        }

        return new TripImportPeopleCounts(willCreate, ambiguous, uncreatable);
    }
}
