// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// What a cave has said its depths mean, and how that answer relates to measuring one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A declaration outranks a measurement, and only where one exists.</b> Measuring — the nearest
/// station to the datum under the trip's filter — is the general answer and stays the answer for
/// every depth nobody has declared. Where a cave has declared a depth, that is the answer and no
/// measuring is attempted: the declaration was written by somebody who knows the cave, and the
/// reason it exists at all is that the arithmetic disagrees with the cave's shape — parallel shafts
/// share a depth, and the nearest station to a round number may be somewhere nobody goes.
/// </para>
/// <para>
/// <b>Exact on the depth, not nearest.</b> A declaration is a statement about one depth, and
/// reading it as "the nearest declared depth" would quietly re-introduce the very guessing it was
/// written to replace — a party at −402 would be filed at a declared −400 without anybody saying
/// so. A depth nobody declared falls through to measurement, which is honest about being
/// approximate and says how far off it was.
/// </para>
/// <para>
/// <b>One key for a depth, whoever writes it.</b> A depth reaches this table three ways — declared
/// on the cave page, typed into a report, written in a sheet — and two of those accept the sign
/// field notes actually use (−120 for 120 metres down) and any number of decimals. The table holds
/// a magnitude to one decimal, because that is the key it is read by. So every comparison here goes
/// through <see cref="Key"/>: a report of −400 finds the declaration at 400, and a declaration
/// written as 120.04 corrects the one at 120 rather than trying to sit beside it. Rounding to the
/// table's own precision is not the nearest-declared guessing rejected below: no declaration can
/// be made finer than a tenth of a metre, so a depth within that tenth can mean nothing else.
/// </para>
/// <para>
/// <b>Only a place that has a depth answers for one.</b> A cave may also name a station without
/// giving it a depth — a hall, a junction, a place a list names and nobody reports by a number —
/// and such a place is never one of the declarations handed to the rules below: it says nothing
/// about any depth, so it can neither be found for one nor stand in the way of measuring it. The
/// filter that keeps it out is <see cref="WithDepth"/>, and every read that feeds a placement goes
/// through it.
/// </para>
/// <para>
/// Nothing here reads a database or a survey: it is given the declarations and decides only what a
/// depth means. Whether the station it names exists on the survey in force is the caller's
/// question, and a declaration naming a station a re-exported model no longer has is a visible,
/// fixable fault rather than a silent one.
/// </para>
/// </remarks>
public static class DeclaredDepthPlaces
{
    /// <summary>One depth a cave has declared, reduced to what resolution needs.</summary>
    /// <param name="DepthM">Metres below the entrance datum, positive downwards.</param>
    /// <param name="ViewerStationName">The station it means, in the survey viewer's spelling.</param>
    /// <param name="PlaceLabel">What people call it, or null where there is no such word.</param>
    public readonly record struct Declared(decimal DepthM, string ViewerStationName, string? PlaceLabel);

    /// <summary>
    /// One place a cave has named, with or without a depth, reduced to what a list of the cave's
    /// places shows and orders it by.
    /// </summary>
    /// <param name="DepthM">
    /// Metres below the entrance datum, positive downwards; null for a place named without a depth.
    /// </param>
    /// <param name="ViewerStationName">The station it is, in the survey viewer's spelling.</param>
    /// <param name="PlaceLabel">What people call it, or null where there is no such word.</param>
    /// <param name="Note">The short text a list shows beside the name, or null.</param>
    /// <param name="IsLandmark">Whether the place is named on the drawing and not only in lists.</param>
    /// <param name="DeclaredAt">When it was first written, which orders the places without a depth.</param>
    /// <param name="Id">The row's identity, which settles two written in the same instant.</param>
    public readonly record struct Place(
        decimal? DepthM,
        string ViewerStationName,
        string? PlaceLabel,
        string? Note,
        bool IsLandmark,
        DateTimeOffset DeclaredAt,
        Guid Id);

    /// <summary>
    /// The rows that answer for a depth: those that have one. Every read that hands declarations
    /// to a placement — recording a report, correcting it, importing a sheet, previewing a depth,
    /// offering the places a report can be made at — reads through this.
    /// </summary>
    public static IQueryable<CaveDepthPlace> WithDepth(this IQueryable<CaveDepthPlace> rows) =>
        rows.Where(p => p.DepthM != null);

    /// <summary>
    /// A cave's places in the order every list of them is shown in: the places with a depth by
    /// depth, shallowest first, then the places named without one in the order they were declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Depth first because that is the order a cave is described in and the order a party goes
    /// down past its places. The places without a depth have no such order of their own, so they
    /// keep the one somebody wrote them in — which is how a club's own list of its places reads,
    /// and the one thing about them that a reader of the list can recognise.
    /// </para>
    /// <para>
    /// Total, so that two reads of one state are the same bytes: a published answer's validator
    /// is computed from them. Two rows written in the same instant are settled by their identity,
    /// which is time-ordered and so keeps the order of separate writes even then. Spelled twice,
    /// once for a query and once for rows already read, side by side here so that the two cannot
    /// drift; a test holds them to the same answer.
    /// </para>
    /// </remarks>
    public static IOrderedQueryable<CaveDepthPlace> InListOrder(this IQueryable<CaveDepthPlace> rows) =>
        rows.OrderBy(p => p.DepthM == null)
            .ThenBy(p => p.DepthM)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id);

    /// <inheritdoc cref="InListOrder(IQueryable{CaveDepthPlace})"/>
    public static IOrderedEnumerable<Place> InListOrder(this IEnumerable<Place> places) =>
        places.OrderBy(p => p.DepthM is null)
            .ThenBy(p => p.DepthM)
            .ThenBy(p => p.DeclaredAt)
            .ThenBy(p => p.Id);

    /// <summary>
    /// The station a cave has declared for this depth, or null when it has declared none.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary answer: most caves declare nothing, and a cave that declares its
    /// bivouac and its sump has said nothing about the other four hundred metres.
    /// </remarks>
    public static Declared? For(IReadOnlyCollection<Declared> declarations, decimal depthM)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var asked = Key(depthM);
        foreach (var declaration in declarations)
        {
            if (Key(declaration.DepthM) == asked)
            {
                return declaration;
            }
        }

        return null;
    }

    /// <summary>
    /// The form a depth takes as the key of a declaration: its magnitude, to one decimal, rounded
    /// half away from zero — which is also what the column that stores it keeps.
    /// </summary>
    /// <remarks>
    /// Applied before a declaration is looked up, before one is written and before a reported
    /// depth is compared with one, so that the sign a person wrote and the decimals they typed
    /// decide nothing. A write that skipped this would look up 120.04, find nothing beside the row
    /// at 120, and then be refused by the unique index for inserting the same key. The rounding is
    /// the one a report's own depth gets, taken from the same place, so a declaration and the
    /// report that meets it cannot round a value two ways.
    /// </remarks>
    public static decimal Key(decimal depthM) =>
        Math.Abs(TripTrackingRules.RecordedDepthM(depthM));

    /// <summary>
    /// The declaration people call by this name, or null when none is called that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How a sheet or a screen names a place instead of a depth or a station. Matched through the
    /// project's one name folder, so a label typed without diacritics or with a doubled space is
    /// the same place — the same reason a caver's name is matched that way.
    /// </para>
    /// <para>
    /// A name two declarations answer to is <b>no answer</b> rather than the first of them: two
    /// places a club calls the same thing is a thing to fix in the declarations, and picking one
    /// would file a report at whichever happened to be written first.
    /// </para>
    /// </remarks>
    public static Declared? ByLabel(IReadOnlyCollection<Declared> declarations, string? label)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var asked = Import.TripImportNames.Key(label);
        if (asked.Length == 0)
        {
            return null;
        }

        Declared? found = null;
        foreach (var declaration in declarations)
        {
            if (Import.TripImportNames.Key(declaration.PlaceLabel) != asked)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = declaration;
        }

        return found;
    }
}
