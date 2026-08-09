// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// How a batch of files reached the installation. Stored as smallint and append-only: the
/// values are part of the schema contract, so a new member takes the next free number and
/// nothing is ever renumbered or reused.
/// </summary>
public enum UploadSource : short
{
    /// <summary>Somebody dropped files (or a folder) into the browser.</summary>
    Interactive = 0,

    /// <summary>An uploaded archive was expanded on the server.</summary>
    Archive = 1,

    /// <summary>A directory the server itself can reach was walked and copied in.</summary>
    ServerDirectory = 2,
}

/// <summary>How far a batch has got.</summary>
public enum UploadBatchStatus : short
{
    /// <summary>Files are still arriving. Every interactive batch starts here.</summary>
    Open = 0,

    /// <summary>Queued work is walking the archive or the directory.</summary>
    Running = 1,

    /// <summary>Nothing more will be added, whatever the per-file outcomes were.</summary>
    Completed = 2,

    /// <summary>The batch itself failed — the archive would not open, the directory vanished.</summary>
    Failed = 3,
}

/// <summary>
/// One upload — the whole drop, not one file of it. Everything that arrived together
/// carries this id, which is what makes "the four hundred scans somebody added last
/// Tuesday" a thing you can name afterwards rather than a date range you have to guess at.
///
/// <para>
/// It is a *reference*, deliberately, rather than only a tag: a tag is a label a person
/// chose and may change or reuse, and the question a batch answers — which files arrived in
/// one act, from where, by whose hand — must survive somebody tidying up their labels. A
/// batch may also apply a tag to everything it holds (<see cref="TagId"/>), and that tag is
/// the part meant for browsing; the id is the part meant for accounting.
/// </para>
/// <para>
/// A batch is never edited after it closes. It is a record of something that happened, and
/// the counts on it are what the report is drawn from.
/// </para>
/// </summary>
public class UploadBatch : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public UploadSource Source { get; set; } = UploadSource.Interactive;

    public UploadBatchStatus Status { get; set; } = UploadBatchStatus.Open;

    /// <summary>Whose drop this was. Never null: nothing uploads without an account behind it.</summary>
    public Guid StartedByUserId { get; set; }

    /// <summary>
    /// What the uploader called this drop ("Bulletin scans 1987–1994"), or null when they
    /// called it nothing. Free text, shown wherever the batch is listed.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// The cabinet everything was filed into, when the drop named one. For a folder drop or
    /// a directory import this is the cabinet the mirrored subtree was created *under*, not
    /// the shelf each file ended on.
    /// </summary>
    public Guid? CabinetId { get; set; }

    /// <summary>
    /// A tag applied to every document the batch created, when the uploader asked for one.
    /// Null is the ordinary case — the batch id already identifies the set, and a tag is
    /// only worth minting when somebody wants the set to be browsable by name.
    /// </summary>
    public long? TagId { get; set; }

    /// <summary>
    /// Where the bytes came from, in the source's own terms: the archive's file name, or the
    /// server path that was walked. Null for an interactive drop, where the browser is the
    /// only answer and it is not a useful one. Kept as text rather than as a reference,
    /// because the point of it is to still read correctly once the archive is deleted.
    /// </summary>
    public string? SourceDescription { get; set; }

    /// <summary>How many files the batch has accounted for, whatever became of each.</summary>
    public int TotalCount { get; set; }

    /// <summary>How many became stored documents.</summary>
    public int StoredCount { get; set; }

    /// <summary>
    /// How many were passed over — a duplicate the uploader could already see, a name the
    /// filing rules refused, an entry the archive holds that is not a file.
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>How many were attempted and failed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Why the batch as a whole failed, when it did. Null in every other state.</summary>
    public string? Error { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>What became of one file in a batch.</summary>
public enum UploadItemOutcome : short
{
    /// <summary>Accounted for but not yet attempted — a queued walk records these up front.</summary>
    Pending = 0,

    /// <summary>Stored, and a document exists for it.</summary>
    Stored = 1,

    /// <summary>
    /// Deliberately not stored. The reason is on the row: most often the uploader already
    /// holds this exact content and asked not to store it twice.
    /// </summary>
    Skipped = 2,

    /// <summary>Attempted and failed. The reason is on the row.</summary>
    Failed = 3,
}

/// <summary>
/// One line of a batch: which source file became which document, or why it did not.
///
/// <para>
/// The source path is kept verbatim because it is the only thing that makes a report
/// readable: "12 of 500 failed" is useless, and "<c>1987/bulletin-3/scan-014.tif</c> —
/// unreadable image" is a thing somebody can act on. It is also what lets a failed subset be
/// retried without re-walking what already landed.
/// </para>
/// </summary>
public class UploadBatchItem
{
    public long Id { get; set; }

    public Guid UploadBatchId { get; set; }

    /// <summary>
    /// The file's path as its source named it, relative to the root that was walked
    /// (<c>1987/bulletins/march.pdf</c>). For an interactive drop it is the browser's own
    /// relative path when a folder was dropped, and the bare file name otherwise.
    /// </summary>
    public required string SourcePath { get; set; }

    public long SizeBytes { get; set; }

    public UploadItemOutcome Outcome { get; set; } = UploadItemOutcome.Pending;

    /// <summary>The document created for it, once one was. Null for every other outcome.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>The cabinet it was filed on, which for a mirrored tree is not the batch's own.</summary>
    public Guid? CabinetId { get; set; }

    /// <summary>
    /// Why it was skipped or why it failed, as a stable code rather than a sentence — the
    /// report is translated on the client like every other message. Null when it was stored.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// The document holding identical content that caused this one to be skipped. Set only
    /// when the uploader may actually see that document: a duplicate they may not read is
    /// never named, because naming it would disclose that it exists.
    /// </summary>
    public Guid? DuplicateOfDocumentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Stable reasons a batch line was skipped or failed.</summary>
public static class UploadItemReasons
{
    /// <summary>The uploader already holds a document with exactly these bytes.</summary>
    public const string Duplicate = "upload.duplicate";

    /// <summary>Storing it would put the uploader, or the installation, past its byte limit.</summary>
    public const string QuotaExceeded = "upload.quota_exceeded";

    /// <summary>Larger on its own than this installation accepts.</summary>
    public const string TooLarge = "upload.too_large";

    /// <summary>The extension is one this installation does not accept.</summary>
    public const string TypeNotAccepted = "upload.type_not_accepted";

    /// <summary>Zero bytes. Nothing to store and nothing to say about it.</summary>
    public const string Empty = "upload.empty";

    /// <summary>
    /// The path named a place outside the tree being walked — an archive entry escaping its
    /// own root, or a link pointing away from the allowed directory.
    /// </summary>
    public const string PathRefused = "upload.path_refused";

    /// <summary>The bytes could not be read.</summary>
    public const string Unreadable = "upload.unreadable";

    /// <summary>The filing tree could not take it: too deep, or a name nothing can file under.</summary>
    public const string FilingRefused = "upload.filing_refused";
}
