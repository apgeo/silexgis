// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A review in progress: what one person has decided so far about one uploaded file, before
/// anything is created.
///
/// <para>
/// The candidates themselves are not copied here — they are the file's already-parsed
/// <see cref="GeofileFeature"/> rows, which is what makes staging free and re-importable. What
/// is stored is only the part that would otherwise be lost: the options chosen for the import
/// and the per-candidate decisions, keyed by source row. Four hundred waypoints take a while
/// to go through, and a review that a closed tab throws away is a review nobody finishes.
/// </para>
/// <para>
/// One session per person per file. Two people reviewing the same upload keep their own
/// decisions rather than overwriting each other, and neither has committed anything.
/// </para>
/// </summary>
public class GeofileImportSession : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GeofileId { get; set; }

    /// <summary>Whose review this is.</summary>
    public Guid UserId { get; set; }

    /// <summary>The whole-file choices (jsonb), in the shape of <see cref="ImportOptions"/>.</summary>
    public string Options { get; set; } = "{}";

    /// <summary>
    /// Decisions keyed by source row id (jsonb): <c>{"12345": {"action": "skip"}}</c>. Rows the
    /// reviewer has not touched are absent, so agreeing with the rules costs nothing to store.
    /// </summary>
    public string Decisions { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A photo review in progress: what one person has decided so far about one drop of pictures,
/// before anything is created.
///
/// <para>
/// The candidates are not stored — they are a reading of the files themselves, grouped into
/// places by distance, which is what lets the clustering radius be changed without anything
/// being rewritten. What is kept is only the part that would otherwise be lost: which pictures
/// are being reviewed, the whole-drop options, and the per-candidate decisions.
/// </para>
/// <para>
/// One review per person. A drop is a sitting rather than a document — starting a new one
/// replaces the last, because a half-finished review of photographs somebody has moved on from
/// is worth less than a clear surface.
/// </para>
/// </summary>
public class PhotoImportSession : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Whose review this is; there is at most one per person.</summary>
    public Guid UserId { get; set; }

    /// <summary>The files being reviewed (jsonb array of ids), in the order they were dropped.</summary>
    public string FileIds { get; set; } = "[]";

    /// <summary>The whole-drop choices (jsonb), in the shape of <see cref="PhotoImportOptions"/>.</summary>
    public string Options { get; set; } = "{}";

    /// <summary>
    /// Decisions keyed by candidate (jsonb), where a candidate is named by the lowest file id
    /// among its pictures. Untouched candidates are absent, so agreeing costs nothing to store.
    /// </summary>
    public string Decisions { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>What a batch was made from.</summary>
public enum ImportSource : short
{
    /// <summary>An uploaded vector file: waypoints, tracks, a shapefile.</summary>
    VectorFile = 0,

    /// <summary>Photographs, placed by what the camera recorded or by the person reviewing them.</summary>
    Photos = 1,

    /// <summary>
    /// Rows a mobile device pushed up. There is no file and nobody reviewed the candidates one
    /// by one: a batch is one upload request, recorded here so that a device which sent the
    /// wrong survey can have it taken back in a single act, the same way a bad file can.
    /// </summary>
    DeviceSync = 2,
}

/// <summary>How the objects in a batch came to exist.</summary>
public enum ImportBatchMode : short
{
    /// <summary>Somebody went through the candidates and confirmed them.</summary>
    Reviewed = 0,

    /// <summary>
    /// The rules were trusted to create without review. Recorded rather than inferred: it is
    /// the first thing worth knowing about a batch that turned out wrong.
    /// </summary>
    AutoCreated = 1,
}

/// <summary>
/// One confirmation: everything created in a single press of the button, with what it was
/// created from.
///
/// <para>
/// This is what makes a bad import findable months later, and revertible as a unit. A batch
/// is never edited after it lands — the only change it takes is being reverted, which
/// soft-deletes every object it created and stamps who did it.
/// </para>
/// </summary>
public class ImportBatch : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// What this batch was made from. Both kinds revert the same way and appear in the same
    /// list; what differs is only what a line of it points back at — a row of a vector file, or
    /// a photograph.
    /// </summary>
    public ImportSource Source { get; set; } = ImportSource.VectorFile;

    /// <summary>
    /// The file this came from, while it is still there. A batch outlives the upload it was
    /// made from: "this cave came from a GPX somebody deleted in March" is still the answer
    /// somebody needs, and the options snapshot and the per-item source properties below are
    /// what keeps the answer useful once the file is gone. Null for a batch made from
    /// photographs, where the source is the pictures themselves.
    /// </summary>
    public Guid? GeofileId { get; set; }

    /// <summary>The trip a photo drop was filed under, when it named one.</summary>
    public Guid? TripLogId { get; set; }

    /// <summary>
    /// The identifier the device minted for the upload this batch records. Null for every batch
    /// that did not come from a device.
    /// </summary>
    /// <remarks>
    /// A phone in a cave sends a batch, loses signal before the answer arrives, and sends the
    /// same batch again when it surfaces. Without this the second send is a second import, and
    /// the caver ends up with every cave twice. It is minted by the device rather than by the
    /// server because only the device knows that the two requests are the same intention; it is
    /// unique per account rather than globally so that one account cannot probe for another's.
    /// </remarks>
    public Guid? SyncBatchId { get; set; }

    /// <summary>
    /// What the device was told the first time it sent this batch (jsonb), so that a resend is
    /// answered instead of applied. Null for every batch that did not come from a device.
    /// </summary>
    /// <remarks>
    /// Decisions only — which rows were written, which were refused and why. Deliberately not
    /// the rows themselves: a stored position would be handed back on every later resend under
    /// whatever rights the account has by then, and an account's right to see a position can be
    /// taken away. Anything the answer needs to say about a row as it stands now is read from
    /// the row when the resend arrives.
    /// </remarks>
    public string? SyncResult { get; set; }

    /// <summary>The rule set that ran. Null when the set has since been deleted, or none was used.</summary>
    public Guid? TermRuleSetId { get; set; }

    /// <summary>Kept as text as well, so a deleted set does not erase what a batch was made with.</summary>
    public string? TermRuleSetName { get; set; }

    public Guid ConfirmedByUserId { get; set; }

    public ImportBatchMode Mode { get; set; } = ImportBatchMode.Reviewed;

    /// <summary>The options as confirmed (jsonb) — a snapshot, not a reference.</summary>
    public string Options { get; set; } = "{}";

    public int CreatedCount { get; set; }

    public int AttachedCount { get; set; }

    public int SkippedCount { get; set; }

    public DateTimeOffset? RevertedAt { get; set; }

    public Guid? RevertedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public bool IsReverted => RevertedAt is not null;
}

/// <summary>
/// One line of a batch: which source row became which object, under which rule, with the
/// source's own attributes kept verbatim.
///
/// <para>
/// The raw properties are the recoverable part of a bad attribute mapping. If the wrong
/// column was mapped to the name, the right one is still here, on the row that names the
/// feature it went to — without it, recovering the import means re-uploading the file and
/// hoping it is the same one.
/// </para>
/// </summary>
public class ImportBatchItem
{
    public long Id { get; set; }

    public Guid ImportBatchId { get; set; }

    /// <summary>The created feature. Null for a skipped or attached candidate.</summary>
    public Guid? FeatureId { get; set; }

    /// <summary>The existing feature an attached candidate was recognised as.</summary>
    public Guid? AttachedToFeatureId { get; set; }

    /// <summary>The geofile row this came from. Kept even after a re-import replaces those rows.</summary>
    public long? SourceFeatureId { get; set; }

    /// <summary>
    /// The photograph this came from, for a batch made of pictures. It is the first of the
    /// candidate's pictures — the one the created object is named and placed by — and the rest
    /// are found through the attachments the same confirmation made.
    /// </summary>
    public Guid? SourceFileId { get; set; }

    /// <summary>
    /// The attachments this line hung, as a jsonb array of ids. Recorded because undoing a
    /// batch has to take them down, and soft-deleting the created objects would not: a picture
    /// hung on somebody else's existing cave, or filed under a trip, survives its own object
    /// being deleted — which is exactly the case undo exists for.
    /// </summary>
    public string AttachmentIds { get; set; } = "[]";

    public string? RuleId { get; set; }

    public string? RuleName { get; set; }

    public ImportDecisionAction Action { get; set; }

    /// <summary>The source row's own attributes (jsonb), exactly as the file wrote them.</summary>
    public string SourceProperties { get; set; } = "{}";
}
