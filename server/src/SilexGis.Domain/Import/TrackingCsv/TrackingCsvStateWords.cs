// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// The words a sheet writes "went in" and "came out" in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configured rather than guessed, and that is the whole reason this exists.</b> A club's sheet
/// is written in its own language and its own habits — <c>intrare</c>, <c>ieșire</c>, <c>in</c>,
/// <c>out</c>, <c>plecare</c> — and a list of every spelling anybody might use is a list that grows
/// forever and is wrong for the next club. Two defaults are carried because they cover the two
/// languages this application speaks; anything else is named by whoever brings the sheet.
/// </para>
/// <para>
/// <b>Why the alternative was refused.</b> Reading a standing off a depth of zero would make one
/// number mean two things — the entrance is a real place a party passes through and is reported
/// from, and a row at zero metres that meant "came out" would turn every report from the entrance
/// into somebody leaving. The sheet says which, in its own column.
/// </para>
/// <para>
/// Compared in the folded form names are compared in, so <c>Ieșire</c>, <c>iesire</c> and
/// <c>IEȘIRE</c> are one word.
/// </para>
/// </remarks>
public sealed record TrackingCsvStateWords
{
    /// <summary>Words meaning the person went into the cave.</summary>
    public IReadOnlyList<string> WentIn { get; init; } = ["intrare", "intrat", "went in", "in", "entered"];

    /// <summary>Words meaning the person came out of it.</summary>
    public IReadOnlyList<string> CameOut { get; init; } = ["iesire", "ieșire", "ieșit", "iesit", "went out", "out", "exited"];

    /// <summary>The defaults: Romanian and English, which are the languages this application speaks.</summary>
    public static TrackingCsvStateWords Default { get; } = new();

    /// <summary>
    /// The standing a state column's text names, or null when it names neither.
    /// </summary>
    /// <remarks>
    /// Null is an ordinary answer and not a fault: most rows of a tracking sheet are places inside
    /// the cave and leave the column empty. A word that is neither is left alone rather than
    /// guessed at — the row is then read as whatever its place columns say, and the unrecognised
    /// word is reported so that somebody can add it to the two lists above.
    /// </remarks>
    public TripPositionEventKind? KindOf(string? text)
    {
        var folded = TripImportNames.Key(text);
        if (folded.Length == 0)
        {
            return null;
        }

        // Out before in, deliberately. Several of these words contain another — "went in" contains
        // "in", and a club writing "intrat"/"iesit" has one word inside neither — so the test is
        // equality on the folded word rather than containment, and the order then decides nothing.
        // It is written out because a later reader reaching for `Contains` would make "went out"
        // match the in-list.
        if (CameOut.Any(word => TripImportNames.Key(word) == folded))
        {
            return TripPositionEventKind.Exited;
        }

        if (WentIn.Any(word => TripImportNames.Key(word) == folded))
        {
            return TripPositionEventKind.Entered;
        }

        return null;
    }
}
