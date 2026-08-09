// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A large upload in progress: bytes arriving a piece at a time into one partial blob, with
/// enough recorded that an interrupted transfer can pick up where it stopped instead of
/// starting over.
///
/// <para>
/// This exists because of where the files come from. A survey scan or a stitched panorama is
/// tens or hundreds of megabytes, and the connection it travels over is frequently a phone
/// on a hillside. A single request that has to succeed whole is, at that size and over that
/// link, a request that often does not — and every retry pays for the bytes that already
/// arrived.
/// </para>
/// <para>
/// The session records how much has landed rather than which pieces have: bytes are appended
/// in order, so "how far did we get" is one number and resuming is one offset. Out-of-order
/// pieces would need a map of holes, a reassembly step and a way to be told the map is
/// complete — all to serve a client that can simply send the next piece.
/// </para>
/// <para>
/// A session owns its partial blob and is the only thing that knows the path. Nothing points
/// at it until it completes, at which point it becomes an ordinary stored file and the
/// session is gone; an abandoned one is swept up with its bytes by the expiry pass, because
/// a half-sent panorama nobody came back for is otherwise permanent.
/// </para>
/// </summary>
public class UploadSession : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Whose upload this is. Only they may append to it or finish it.</summary>
    public Guid UserId { get; set; }

    /// <summary>The name the client gave the file; it becomes the stored file's own.</summary>
    public required string OriginalName { get; set; }

    /// <summary>
    /// The size the client said the file is. Checked against the installation's limit before
    /// a single byte is accepted — refusing a 900 MB upload after receiving it is the failure
    /// this whole mechanism exists to avoid — and checked again against what actually
    /// arrived, because a declaration is a claim and the bytes are the fact.
    /// </summary>
    public long DeclaredSizeBytes { get; set; }

    /// <summary>How much has actually landed. The offset a resuming client asks for.</summary>
    public long ReceivedBytes { get; set; }

    /// <summary>Where the partial content sits in the file store, relative to its root.</summary>
    public required string StoragePath { get; set; }

    /// <summary>
    /// The cabinet the finished file should be filed into, carried from the request that
    /// opened the session so a resumed upload lands where the original one was aimed.
    /// </summary>
    public Guid? CabinetId { get; set; }

    /// <summary>The batch this upload belongs to, when it was opened as part of one.</summary>
    public Guid? UploadBatchId { get; set; }

    /// <summary>
    /// The object the finished file should be attached to, as the polymorphic pair the
    /// attachment table uses, or a feature id. Both are carried so that "upload onto this
    /// cave" survives a dropped connection exactly as "upload into this cabinet" does.
    /// </summary>
    public AttachedEntityType? AttachEntityType { get; set; }

    public Guid? AttachEntityId { get; set; }

    public Guid? AttachFeatureId { get; set; }

    /// <summary>
    /// The folder path the browser reported for this file, when a folder was dropped. The
    /// finished upload is filed under the mirrored subtree this names.
    /// </summary>
    public string? RelativePath { get; set; }

    /// <summary>
    /// When this session stops being resumable and its bytes become sweepable. Extended by
    /// every accepted piece, so a slow upload that is still moving is never collected out
    /// from under itself.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A record that somebody uploaded content the installation already held, where they were
/// not allowed to know that.
///
/// <para>
/// The uploader is told nothing and gets their own copy, which is the only answer that does
/// not turn the deduplicator into an oracle: "this file already exists" said to somebody who
/// may not read the existing one tells them it exists, who has it, and — for a document
/// whose very presence is the sensitive part — rather more than that. So the disclosure is
/// refused and the storage cost is paid instead.
/// </para>
/// <para>
/// An administrator, who may see both, is the one person for whom the pair is useful: it is
/// how two copies of the same survey sitting under two clubs is noticed, and it is the only
/// signal that the store is holding duplicates it could not tell anyone about.
/// </para>
/// </summary>
public class DuplicateUploadRecord
{
    public long Id { get; set; }

    /// <summary>The content hash both documents share.</summary>
    public required string Sha256 { get; set; }

    /// <summary>The document the upload created — the second copy.</summary>
    public Guid NewDocumentId { get; set; }

    /// <summary>
    /// A document that already held these bytes and that the uploader could not see. One of
    /// possibly several; the earliest is recorded, because it is the copy the rest duplicate.
    /// </summary>
    public Guid ExistingDocumentId { get; set; }

    public Guid UploadedByUserId { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
