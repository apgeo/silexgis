// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Where a change recorded in the history came from: the kind of logical action that wrote it.
/// Stored on every history row beside the change set it belongs to, so a timeline can say "this
/// came from a phone upload" or "this was put back by an undo" and so the rows one upload wrote
/// can be found again and taken back.
/// </summary>
/// <remarks>
/// The numbers are stored, so they never move; a new source takes the next free value.
/// </remarks>
public enum ChangeSource : short
{
    /// <summary>A person editing through the web application (any ordinary request).</summary>
    Web = 0,

    /// <summary>The confirmation of a file import: a vector file, photographs, a spreadsheet, a catalogue selection.</summary>
    FileImport = 1,

    /// <summary>Rows a mobile device pushed up in one upload.</summary>
    DeviceSync = 2,

    /// <summary>A recording brought out of a cave as an export archive and confirmed.</summary>
    SpeleolocArchive = 3,

    /// <summary>A value put back from the history.</summary>
    Restore = 4,

    /// <summary>An upload or an import taken back.</summary>
    Undo = 5,

    /// <summary>A reviewer accepting or rejecting what an account uploaded.</summary>
    ReviewDecision = 6,

    /// <summary>Work done by the background queue, with no request behind it.</summary>
    Job = 7,

    /// <summary>A trip made automatically from a recording.</summary>
    TripConversion = 8,
}
