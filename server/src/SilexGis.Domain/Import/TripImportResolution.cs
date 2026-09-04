// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>What became of one name the sheet wrote.</summary>
public enum TripImportMatchState
{
    /// <summary>Exactly one thing here answers to that name.</summary>
    Matched = 0,

    /// <summary>Nothing here answers to it. Whether that becomes a new row is a toggle's business.</summary>
    Unmatched = 1,

    /// <summary>
    /// More than one thing answers to it, so nothing is chosen. Guessing is the one failure this
    /// importer cannot take back: linking a trip to the wrong person or the wrong cave is a
    /// statement about who was where, and there is no merge record to unpick it with afterwards.
    /// </summary>
    Ambiguous = 2,
}

/// <summary>A vocabulary value the sheet wrote, and the term it was taken for.</summary>
/// <param name="Source">The text as the sheet wrote it.</param>
/// <param name="State">Whether it was matched, missed or left for a person to settle.</param>
/// <param name="Id">The term, when exactly one answered. Installation-local, never exchanged.</param>
/// <param name="Name">That term's name, so the reviewer sees what they are agreeing to.</param>
/// <param name="WillCreate">
/// True when nothing matched and the choices in force say a term should be added for it. False
/// while the toggle is off, and false for anything that matched: a toggle decides only what
/// happens to what did not match.
/// </param>
public sealed record TripImportTermMatch(
    string Source,
    TripImportMatchState State,
    long? Id,
    string? Name,
    bool WillCreate);

/// <summary>
/// One of the things a name answered to, named so that somebody can say which was meant.
/// </summary>
/// <remarks>
/// Only ever the things this caller was already going to be told about: the set is the one the
/// name matched against, which every visibility and disclosure gate has already narrowed. It is
/// also exactly the set a choice is honoured among, so a reviewer is never offered an option
/// that would be silently discarded.
/// </remarks>
/// <param name="Id">The thing chosen, when this candidate is the one meant.</param>
/// <param name="Name">Its name as this installation holds it, which is what distinguishes it.</param>
public sealed record TripImportCandidate(Guid Id, string Name);

/// <summary>A person the sheet named, and the roster entry they were taken for.</summary>
/// <param name="Candidates">
/// The roster entries that answered to the name. Two or more is what makes the row ambiguous, and
/// they are listed rather than counted because a count states that a decision is needed while
/// leaving no way to make it: settling the name means picking one of exactly these.
/// </param>
/// <param name="MayCreate">
/// Whether a person could be made from this name at all — that is, whether it is a name and not a
/// bare initial or a lone given word. Stated on its own, separately from <paramref name="WillCreate"/>,
/// because the two answer different questions and confusing them is how a screen comes to list a
/// perfectly creatable person among the ones nothing can be done about: with the toggle off,
/// nothing will be created, and that says nothing about what could be.
/// </param>
/// <param name="WillCreate">
/// True only when nothing matched, the toggle is on, and the name is one a person can be created
/// from. A name that is an initial or a single word creates nobody however the toggle stands.
/// </param>
public sealed record TripImportPersonMatch(
    string Source,
    TripImportMatchState State,
    Guid? CaverId,
    string? Name,
    IReadOnlyList<TripImportCandidate> Candidates,
    bool MayCreate,
    bool WillCreate);

/// <summary>A place the sheet named, and the feature it was taken for.</summary>
/// <remarks>
/// A feature the caller may not be told about at all is not a candidate here, and its absence is
/// not reported: an answer that differs from "nothing matched" is itself a disclosure, and one
/// anybody could ask for by uploading a sheet with a name in it. The visible cost is accepted —
/// a caller who may not be told about a cave will be offered the chance to create a second one.
/// </remarks>
/// <param name="Candidates">
/// The features that answered to the name, for the same reason the people are listed: a place two
/// caves answer to is settled by saying which, and only these can be said.
/// </param>
public sealed record TripImportFeatureMatch(
    string Source,
    TripImportMatchState State,
    Guid? FeatureId,
    string? Name,
    IReadOnlyList<TripImportCandidate> Candidates,
    bool WillCreate);

/// <summary>What one row of the sheet was taken to mean.</summary>
/// <param name="Line">The row's physical line in the file — the same number its problems carry.</param>
/// <param name="LocationNote">
/// Everything the row said about where it went that did not become a link: names nothing here
/// answered to, names left ambiguous, and places a switched-off toggle declined to create. It is
/// carried into the trip's own words so that nothing written in the sheet is silently lost, which
/// is the whole reason a switched-off toggle is safe to leave off.
/// </param>
public sealed record TripImportRowResolution(
    int Line,
    TripImportTermMatch? TripType,
    IReadOnlyList<TripImportFeatureMatch> Caves,
    TripImportFeatureMatch? Massif,
    TripImportFeatureMatch? SubArea,
    IReadOnlyList<TripImportPersonMatch> Proposers,
    IReadOnlyList<TripImportPersonMatch> Participants,
    string? LocationNote);

/// <summary>
/// What a whole sheet was taken to mean, under one set of choices and for one caller.
/// </summary>
/// <param name="ParticipantRole">The shipped role a plain name on the participants list is recorded under.</param>
/// <param name="ProposerRole">The shipped role a name on the proposers list is recorded under.</param>
/// <param name="NewTripTypes">
/// The distinct type values nothing answered to, in the order the sheet first wrote them. Nothing
/// is created from these until a person confirms the import: an append-only vocabulary grown by a
/// preview would collect a row for every misspelling anybody ever previewed.
/// </param>
/// <param name="TripTypes">
/// Every distinct value of the type column with what it was taken for, in the order the sheet
/// first wrote them. The row table answers "what does this trip become"; this answers "what does
/// this sheet do to the installation", which is the question somebody is really being asked
/// before they confirm — and it is the only form in which a value written on four hundred rows
/// is one line rather than four hundred.
/// </param>
/// <param name="People">Every distinct name the sheet put on a trip, with what it was taken for.</param>
/// <param name="Caves">Every distinct cave name the sheet wrote, with what it was taken for.</param>
/// <param name="Areas">Every distinct massif or sub-area the sheet wrote, with what it was taken for.</param>
public sealed record TripImportResolutionSet(
    IReadOnlyDictionary<int, TripImportRowResolution> Rows,
    TripImportTermMatch? ParticipantRole,
    TripImportTermMatch? ProposerRole,
    IReadOnlyList<string> NewTripTypes,
    IReadOnlyList<string> NewCavers,
    IReadOnlyList<string> NewCaves,
    IReadOnlyList<string> NewAreas,
    IReadOnlyList<TripImportTermMatch> TripTypes,
    IReadOnlyList<TripImportPersonMatch> People,
    IReadOnlyList<TripImportFeatureMatch> Caves,
    IReadOnlyList<TripImportFeatureMatch> Areas);
