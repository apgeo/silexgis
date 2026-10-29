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

/// <summary>
/// A trip-spreadsheet review in progress: what one person has decided so far about one uploaded
/// file, before any trip exists.
///
/// <para>
/// Nothing parsed is copied here, and that is deliberate rather than economical. A trip sheet
/// carries no coordinates, so it cannot travel the path an uploaded vector file takes; and every
/// choice a reviewer makes about it — which header is the date column, whether a numeric date is
/// day-first, which characters separate several names in one cell — changes what the rows *are*.
/// A staged copy would have to be rewritten each time one of those moved, and a staged copy that
/// was not rewritten is a review of a file nobody has any more. So the stored file is re-read on
/// every preview, and what is kept here is only what would otherwise be lost: the options and the
/// per-row decisions.
/// </para>
/// <para>
/// One session per person per file. Two people reviewing the same upload keep their own decisions
/// rather than overwriting each other, and neither has created anything.
/// </para>
/// </summary>
public class TripImportSession : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The uploaded spreadsheet being reviewed.</summary>
    public Guid StoredFileId { get; set; }

    /// <summary>Whose review this is.</summary>
    public Guid UserId { get; set; }

    /// <summary>The whole-file choices (jsonb), in the shape of <c>TripImportOptions</c>.</summary>
    public string Options { get; set; } = "{}";

    /// <summary>
    /// Decisions keyed by the row's physical line in the file (jsonb):
    /// <c>{"14": {"action": "skip"}}</c>. The line is the key rather than the sheet's own running
    /// number because two rows may carry the same running number — a sheet edited by hand does it
    /// often — and a key that is not unique silently makes one row's decision the other's.
    /// Untouched rows are absent, so agreeing with what is proposed costs nothing to store.
    /// </summary>
    public string Decisions { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A device-recording review in progress: what one person has decided so far about one uploaded
/// archive, before any position exists on any timeline.
///
/// <para>
/// Nothing read out of the archive is copied here, for the reason the trip sheet copies nothing:
/// every choice the reviewer makes changes what the rows <em>are</em>. Which recording out of the
/// device's whole database, which survey model the scans are placed in, which person a device
/// account stands for — each of those re-reads the archive into a different set of proposals, and
/// a staged copy would be wrong the moment one of them moved. So the archive is opened again on
/// every preview and again at the confirmation, and what is kept here is only what would otherwise
/// be lost.
/// </para>
/// <para>
/// One review per person per archive. Two people going through the same upload keep their own
/// decisions rather than overwriting each other, and neither has written anything.
/// </para>
/// </summary>
public class SpeleolocImportSession : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The uploaded archive being reviewed.</summary>
    public Guid StoredFileId { get; set; }

    /// <summary>Whose review this is.</summary>
    public Guid UserId { get; set; }

    /// <summary>The whole-recording choices (jsonb), in the shape of <c>SpeleolocImportOptions</c>.</summary>
    public string Options { get; set; } = "{}";

    /// <summary>
    /// Decisions keyed by the scanned point's own identifier (jsonb):
    /// <c>{"0199…-…": {"stationName": "cave.upper.2"}}</c>. The point's own identifier rather than
    /// its position in a listing, because the listing is rebuilt from the archive on every preview
    /// and changing which recording is being read renumbers all of it — a key that moves silently
    /// makes one scan's decision another's. Untouched points are absent, so agreeing with what is
    /// proposed costs nothing to store.
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

    /// <summary>
    /// Caves chosen from a third-party catalogue this installation reads over the network. There
    /// is no file and there is nothing to re-read: the source is somebody else's register, and
    /// what it said at the time is kept on the batch's own lines rather than fetched again.
    /// </summary>
    ExternalCatalogue = 3,

    /// <summary>
    /// A club's trip spreadsheet, read row by row and confirmed. The objects are trips rather
    /// than features, which is why a line of such a batch points at a trip; the caves and areas
    /// a confirmation created along the way are lines of the same batch, so one undo takes the
    /// whole import back.
    /// </summary>
    TripCsv = 4,

    /// <summary>
    /// A recording made on a phone underground and brought out as an export archive: the scans a
    /// party made at marked places, read back and confirmed one by one as positions on a tracked
    /// trip. The objects are position events rather than features or trips, which is why a line of
    /// such a batch points at an event; a trip the confirmation created to hold them is a line of
    /// the same batch, so one undo takes the whole import back.
    /// </summary>
    SpeleolocArchive = 5,
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
/// <para>
/// The one exception is a batch held for review: it is recorded when it is committed, with what
/// it proposes and nothing written, and it changes as a reviewer decides it.
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

    /// <summary>
    /// The rows that could not be created, as JSON, or null where none failed.
    /// </summary>
    /// <remarks>
    /// Kept on the batch because the confirmation that produces them no longer answers in the
    /// request that asked for it: the work runs on the queue, so by the time a row is refused
    /// there is nobody left to tell. A reviewer who confirmed three thousand rows and got two
    /// thousand nine hundred objects has to be able to find out which hundred did not land and
    /// why, and the batch is the thing they still have.
    /// </remarks>
    public string? Failures { get; set; }

    public DateTimeOffset? RevertedAt { get; set; }

    public Guid? RevertedByUserId { get; set; }

    /// <summary>
    /// Whether this batch waits for a reviewer, and how it was decided. Every batch of an account
    /// that is not under review is <see cref="ImportReviewState.NotHeld"/> and lands as it always
    /// did; a held batch has written nothing of what it proposes until it is accepted.
    /// </summary>
    public ImportReviewState ReviewState { get; set; } = ImportReviewState.NotHeld;

    /// <summary>When the batch was held for review; null for a batch that never was.</summary>
    public DateTimeOffset? HeldAt { get; set; }

    /// <summary>When a reviewer last decided the batch as a whole or the last of its lines.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    /// <summary>What the reviewer said about the batch as a whole, for the uploader to read.</summary>
    public string? ReviewNote { get; set; }

    /// <summary>
    /// For a held file import: the commit request exactly as the account submitted it (jsonb), so
    /// that accepting runs the source's own commit with it, the rejected candidates turned into
    /// skips. A held phone upload keeps its proposals on its lines instead, and leaves this empty.
    /// </summary>
    public string? HeldRequest { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public bool IsReverted => RevertedAt is not null;

    public bool IsHeld => ReviewState != ImportReviewState.NotHeld;
}

/// <summary>
/// Where a batch stands with review. Stored as smallint; values are part of the schema contract
/// and are never renumbered.
/// </summary>
public enum ImportReviewState : short
{
    /// <summary>The account was not under review; the batch landed when it was committed.</summary>
    NotHeld = 0,

    /// <summary>Waiting for a reviewer, wholly or in part.</summary>
    Pending = 1,

    /// <summary>Every line a reviewer decided was accepted.</summary>
    Accepted = 2,

    /// <summary>Decided, some lines accepted and some rejected.</summary>
    PartlyAccepted = 3,

    /// <summary>Decided, nothing accepted.</summary>
    Rejected = 4,
}

/// <summary>
/// What became of one held line. Null on the lines of a batch that was never held. Stored as
/// smallint; values are part of the schema contract and are never renumbered.
/// </summary>
public enum ImportReviewDecision : short
{
    /// <summary>Waiting for a reviewer.</summary>
    Pending = 0,

    /// <summary>A reviewer accepted it and it was written.</summary>
    Accepted = 1,

    /// <summary>A reviewer rejected it; nothing was written.</summary>
    Rejected = 2,

    /// <summary>
    /// The uploader sent the same row again with different content before anybody decided this
    /// one, and the newer proposal waits in its place.
    /// </summary>
    Superseded = 3,

    /// <summary>
    /// A reviewer accepted it but it could not be written — the row changed underneath it, or the
    /// uploader no longer had the right to write it.
    /// </summary>
    Failed = 4,
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

    /// <summary>
    /// The trip this line created. A line points at a trip or at a feature, never at both: one
    /// row of a spreadsheet becomes one trip, and the caves and areas that row named are lines
    /// of their own so that undo can take each of them back by the same rule.
    ///
    /// <para>
    /// It goes null rather than taking the line with it when the trip is deleted by hand
    /// afterwards, for the reason the other pointers here go null: a batch that says a trip was
    /// created and has since been removed is still the answer somebody looking for the import is
    /// asking for.
    /// </para>
    /// </summary>
    public Guid? TripLogId { get; set; }

    /// <summary>
    /// The tracking position this line recorded. A line points at an event, a trip or a feature,
    /// never at more than one: one scan of a marked place becomes one position on the timeline,
    /// and a trip the same confirmation created to hold those positions is a line of its own so
    /// that undo can take each of them back by the same rule.
    ///
    /// <para>
    /// Unlike the pointers around it this one does <em>not</em> survive its object: a position
    /// event is removed rather than stamped when it is corrected, and a line still claiming to
    /// have created one would make an undo look for a row nobody can find. It goes with the event.
    /// </para>
    /// </summary>
    public Guid? TripPositionEventId { get; set; }

    /// <summary>
    /// The phone user this line described or linked, for a phone upload that sent one — keyed by
    /// the phone's own identifier for them. Like the feature and trip pointers it goes null rather
    /// than taking the line with it when the phone user is removed, so the upload still says it
    /// sent somebody.
    /// </summary>
    public Guid? DevicePersonId { get; set; }

    /// <summary>
    /// The document this line created, edited, gave new bytes or removed, for a phone upload that
    /// sent one. Like the feature pointers it goes null rather than taking the line with it when
    /// the document is destroyed for good, so the upload still says it touched a document.
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>
    /// The revision this line made current — the first one of a document it created, or the new
    /// one it stacked onto a document's earlier bytes. Absent for a line that changed only what
    /// is said about a document or removed it. It is what an undo of new bytes reaches for: the
    /// revision to step back from, which a later upload may since have superseded in its turn.
    /// </summary>
    public Guid? DocumentVersionId { get; set; }

    /// <summary>
    /// The phone's trip recording this line created, edited or removed, for a phone upload that
    /// sent one. Goes null rather than taking the line with it when the recording is removed for
    /// good, like the trip pointer.
    /// </summary>
    public Guid? TripRecordingId { get; set; }

    /// <summary>
    /// The scan on a recording this line created, edited or removed. A line names a recording or one
    /// of its points, never both: each is arbitrated, answered and taken back on its own.
    /// </summary>
    public Guid? TripRecordingPointId { get; set; }

    /// <summary>
    /// The raster map this line created, edited or removed, for a phone upload that sent one. A line
    /// names a map or one of its pins, never both: each is arbitrated, answered and taken back on its
    /// own. Goes null when the map is removed for good, like the recording pointer.
    /// </summary>
    public Guid? RasterMapId { get; set; }

    /// <summary>
    /// The pin on a raster map this line created, edited or removed: the place-pin link, whose
    /// identifier is the one the phone gave the pin.
    /// </summary>
    public Guid? RasterPinId { get; set; }

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

    /// <summary>
    /// When an undo took this line back, wholly or in part. A phone upload can be undone a few
    /// lines at a time, so the batch alone cannot say which of its lines are still in force: the
    /// batch counts as reverted only once every line carries this, and a second undo leaves a
    /// line that already does alone instead of comparing its row against values it put back
    /// itself. A line an undo could not take back stays empty, so a later undo can try it again.
    /// </summary>
    public DateTimeOffset? UndoneAt { get; set; }

    /// <summary>
    /// What became of this line in review; null on a batch that was never held. A held line
    /// records what would be written and points at nothing written, so every reader that treats
    /// a line as "this changed something" must leave a line that is not accepted alone.
    /// </summary>
    public ImportReviewDecision? ReviewDecision { get; set; }

    /// <summary>
    /// The identity a held line is known by outside the server — the <c>reviewId</c> a phone is
    /// answered with and later finds its decision under. Opaque and stable, unlike the line's own
    /// number, which is an internal sequence.
    /// </summary>
    public Guid? ReviewId { get; set; }

    /// <summary>
    /// What the line would write, as plain values (jsonb): for a phone upload, the proposal its
    /// section's first step produced; for a file import, the candidate's decision.
    /// </summary>
    public string? Proposal { get; set; }

    /// <summary>
    /// Which part of an upload the proposal belongs to (the section's array name) — what reads
    /// the proposal back and what applies it.
    /// </summary>
    public string? ProposalSection { get; set; }

    /// <summary>
    /// The id of the row the proposal is about, in its section's own identity: what a resend of
    /// the same row is matched by, and what a later row that refers to it depends on.
    /// </summary>
    public Guid? ProposalRowId { get; set; }

    /// <summary>For an accepted update: the fields the reviewer accepted, when not all of them.</summary>
    public List<string>? AcceptedFields { get; set; }

    /// <summary>
    /// The held lines this one cannot be written without — a place under a cave that is itself
    /// waiting, a link to a held document and to the held place it hangs on, a scan on a held
    /// recording at a held place. Accepting this line accepts those with it; rejecting any of them
    /// rejects this one. Null when it depends on nothing held.
    /// </summary>
    public List<long>? DependsOnItemIds { get; set; }

    /// <summary>What the reviewer said about this line.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>When the line was decided, superseded included — the order a phone is told in.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    /// <summary>When an accepted line was written.</summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>
    /// For a line accepted that could not be written: the stable reason, the same code the upload
    /// or the import would have answered had it been written at once.
    /// </summary>
    public string? ReviewFailureCode { get; set; }

    /// <summary>The words that go with <see cref="ReviewFailureCode"/>.</summary>
    public string? ReviewFailureDetail { get; set; }

    /// <summary>
    /// The staged upload whose bytes a held line would adopt — a phone's photo sent ahead of the
    /// row naming it. Kept so that the bytes are kept for as long as the line waits and released
    /// to the staged-upload sweep once it is decided, without reading every proposal to find them.
    /// Not a foreign key: the staged upload goes when its bytes are adopted or swept, and the line
    /// outlives it.
    /// </summary>
    public Guid? HeldUploadSessionId { get; set; }
}
