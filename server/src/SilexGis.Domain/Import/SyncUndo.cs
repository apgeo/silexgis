// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>What an upload from a phone did to one row, as an undo of the upload reports it.</summary>
public static class SyncUndoChanges
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
}

/// <summary>How far an undo of a phone upload got with one of the upload's rows.</summary>
public static class SyncUndoOutcomes
{
    /// <summary>Everything the upload did to the row is taken back.</summary>
    public const string Undone = "undone";

    /// <summary>
    /// Some fields are back and some were left — each left field is named with its reason; the
    /// usual one is that somebody changed it after the upload.
    /// </summary>
    public const string Partly = "partly";

    /// <summary>Nothing was taken back; the reason says why.</summary>
    public const string NotUndone = "not_undone";
}

/// <summary>Why an undo of a phone upload left one of its rows as it is.</summary>
public static class SyncUndoReasons
{
    /// <summary>An earlier undo already took this line back.</summary>
    public const string AlreadyUndone = "already_undone";

    /// <summary>The row has been removed for good since, and there is nothing left to put back.</summary>
    public const string Purged = "purged";

    /// <summary>
    /// The upload deleted the row, and the row is inside something that is deleted too: it can
    /// come back only with its container.
    /// </summary>
    public const string ContainerDeleted = "container_deleted";

    /// <summary>
    /// The row was changed after the upload — deleted again by somebody else, or every field the
    /// upload wrote has been written since — and the undo leaves the newer state alone.
    /// </summary>
    public const string Superseded = "superseded";

    /// <summary>
    /// The upload changed fields of the row and none of them could be put back; the skipped
    /// fields say why, one by one.
    /// </summary>
    public const string FieldsLeft = "fields_left";

    /// <summary>
    /// The history does not say what the upload did to the row: the upload is older than the
    /// history's record of which rows each upload wrote. Nothing of the row is taken back, and the
    /// line is closed all the same, since no later undo could do more with it.
    /// </summary>
    public const string NoHistory = "no_history";

    /// <summary>The caller may no longer change this row — or, for a row the upload deleted, may no longer delete it, which is the right that restores it.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>
    /// For the trip report made from a recording the undo took back: somebody has changed the trip
    /// since it was made — an edit, a person added, a flag cleared — so it is theirs now and is left.
    /// </summary>
    public const string TripChanged = "trip_changed";

    /// <summary>
    /// For the trip report made from a recording the undo took back: the report was not made by the
    /// conversion of one of the recording's own uploads — an archive import made it — and is left.
    /// </summary>
    public const string ConvertedElsewhere = "converted_elsewhere";
}
