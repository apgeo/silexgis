// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>
/// The words a sheet writes "went in", "came out" and "a note" in.
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

    /// <summary>Words meaning the row is a note about the person and claims no place and no standing.</summary>
    /// <remarks>
    /// A row with a note and nothing else has always been read as a note, so a sheet typed by hand
    /// needs none of these. They exist for the two rows that reading cannot express: a note report
    /// that carries no text at all, and one whose text is a mark this reader takes for an empty
    /// cell. A log written out as a sheet holds both, and has to come back as what it was.
    /// </remarks>
    public IReadOnlyList<string> Noted { get; init; } = ["nota", "notă", "note", "observatie", "observație"];

    /// <summary>
    /// What a sheet written out of a log puts in the state column of a row whose place was kept
    /// back from whoever took the sheet.
    /// </summary>
    /// <remarks>
    /// Never a standing and never a note, whatever the lists above are replaced with: such a row
    /// is a report that has a place the sheet does not say, and reading it as anything at all
    /// would, on a re-import that replaces, write "nowhere" over the place the log holds. It is
    /// reported as a word nobody listed and the row is sent back.
    /// </remarks>
    public const string Withheld = "retinut";

    /// <summary>The defaults: Romanian and English, which are the languages this application speaks.</summary>
    public static TrackingCsvStateWords Default { get; } = new();

    /// <summary>
    /// What a state column's text says the row is, or null when it is no listed word.
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
        if (folded == TripImportNames.Key(Withheld))
        {
            return null;
        }

        if (CameOut.Any(word => TripImportNames.Key(word) == folded))
        {
            return TripPositionEventKind.Exited;
        }

        if (WentIn.Any(word => TripImportNames.Key(word) == folded))
        {
            return TripPositionEventKind.Entered;
        }

        if (Noted.Any(word => TripImportNames.Key(word) == folded))
        {
            return TripPositionEventKind.Note;
        }

        return null;
    }
}
