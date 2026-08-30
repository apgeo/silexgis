// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>What became of the links over a body that was replaced.</summary>
/// <param name="Unmoved">Passages whose offsets still named them: nothing above them changed.</param>
/// <param name="Moved">Passages found again elsewhere and re-measured.</param>
/// <param name="Lost">Passages no longer in the text. Their anchors are left exactly as
/// written and read as degraded from here on.</param>
public readonly record struct ReanchorReport(int Unmoved, int Moved, int Lost)
{
    public int Total => this.Unmoved + this.Moved + this.Lost;
}

/// <summary>The document a write produced, with what happened to the links over it.</summary>
public sealed record AnnotatedTextWrite(DocumentFile Content, ReanchorReport Reanchoring);

/// <summary>
/// Writing and reading link-annotated text bodies.
///
/// <para>
/// A body is stored as an ordinary document — one revision carrying one file — rather than as
/// a table of its own, and that is the whole design. Versioning, the read audience and the club
/// binding, filing into cabinets, the discussion thread, full-text search, the audit trail and
/// every existing rule about who may have a file all apply to it already, because it *is* one.
/// Editing the text is uploading a new revision, which is why a correction never destroys what
/// the previous revision said and why a link measured against the old bytes can still be told
/// apart from one measured against the new.
/// </para>
///
/// <para>
/// The bytes are described here rather than sniffed. Content that arrives from outside is
/// judged by its own bytes, because what an uploader claims is not evidence; this content did
/// not arrive from outside — it was serialised two statements ago from a body these rules had
/// already validated. Handing it to the sniffer would have it come back as generic JSON, which
/// is the one answer that is definitely wrong: the viewer would show a reader the source of
/// their own document, and the plain-text reader would put its punctuation in the search index.
/// </para>
/// </summary>
public sealed class AnnotatedTextService(
    SilexGisDbContext db, DocumentWriteService documents, IFileStore fileStore)
{
    /// <summary>The stored body is not a link-annotated text document.</summary>
    public const string NotAnnotatedTextCode = "annotatedtext.not_annotated_text";

    /// <summary>The document serves no file, so there is nothing to read or replace.</summary>
    public const string NoContentCode = "annotatedtext.no_content";

    /// <summary>
    /// Records a body as a brand-new document. Tracked, not saved — the caller commits, so a
    /// document and whatever else the same request creates land together or not at all.
    /// </summary>
    public async Task<DocumentFile> CreateAsync(
        AnnotatedTextBody body,
        string title,
        Guid ownerUserId,
        Visibility visibility,
        Guid? cavingGroupId,
        CancellationToken ct)
    {
        var content = await StoreAsync(body, title, ct);
        var stored = documents.Create(
            content, title, ownerUserId: ownerUserId, uploadedBy: ownerUserId, cavingGroupId: cavingGroupId);

        // Read out of the change tracker: the write service leaves the document tracked and
        // unsaved, so at this point it exists nowhere else.
        var document = db.Documents.Local.First(d => d.Id == stored.Version.DocumentId);
        document.Visibility = visibility;
        return stored;
    }

    /// <summary>
    /// Replaces a document's body with a new revision, and re-measures every text-range link
    /// over it against the new words.
    ///
    /// <para>
    /// The re-measuring is the point, and it is done here rather than left to a reader because
    /// the two facts it needs — the old text and the new — exist together only during this
    /// call. A passage found again is re-measured and re-pinned to the new file, so it goes on
    /// reading as exact, which it is. A passage that is genuinely gone keeps the offsets and the
    /// pin its author wrote, so it reads as degraded against a superseded file: the link is
    /// still there, still says what it said, and now says honestly that the words it pointed at
    /// are not in the current revision. Silently re-aiming it at the nearest surviving sentence
    /// would be the one outcome no reader could detect.
    /// </para>
    ///
    /// <para>
    /// Saves before returning, because <see cref="DocumentWriteService.AddVersionAsync"/> owns
    /// its own transaction for the current-revision flag: the anchors are updated inside the
    /// same unit of work as the version that moved them.
    /// </para>
    /// </summary>
    public async Task<AnnotatedTextWrite> ReplaceBodyAsync(
        Guid documentId, AnnotatedTextBody body, Guid? uploadedBy, CancellationToken ct)
    {
        var current = await DocumentQueries.CurrentFileAsync(db, documentId, ct)
            ?? throw new DocumentWriteException(NoContentCode, "The document serves no file.");

        if (!string.Equals(current.File.MimeType, AnnotatedText.MediaType, StringComparison.Ordinal))
        {
            throw new DocumentWriteException(
                NotAnnotatedTextCode, "The document is not a link-annotated text document.");
        }

        var title = await db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => d.Title)
            .FirstAsync(ct);

        var content = await StoreAsync(body, title, ct);
        var stored = await documents.AddVersionAsync(current.Version.Id, content, uploadedBy, ct);

        var report = await ReanchorAsync(documentId, body, current.File.Id, stored.File.Id, ct);
        await db.SaveChangesAsync(ct);
        return new AnnotatedTextWrite(stored, report);
    }

    /// <summary>
    /// The body a file holds, or null when the bytes are not one. Null rather than an
    /// exception: this is read on the way to drawing a page, and a body that will not parse is
    /// a document that says so rather than a request that fails.
    /// </summary>
    public async Task<AnnotatedTextBody?> ReadBodyAsync(StoredFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!string.Equals(file.MimeType, AnnotatedText.MediaType, StringComparison.Ordinal))
        {
            return null;
        }

        await using var content = await fileStore.OpenReadAsync(file.StoragePath, ct);
        return await AnnotatedText.ReadAsync(content, ct);
    }

    private async Task<StoredContent> StoreAsync(AnnotatedTextBody body, string title, CancellationToken ct)
    {
        var problem = AnnotatedText.BodyProblem(body);
        if (problem is not null)
        {
            throw new DocumentWriteException(AnnotatedText.BodyInvalidCode, problem);
        }

        var bytes = AnnotatedText.Serialize(body);
        string storagePath;
        using (var buffer = new MemoryStream(bytes, writable: false))
        {
            storagePath = await fileStore.SaveAsync(buffer, AnnotatedText.FileExtension, ct);
        }

        return new StoredContent(
            storagePath,
            FileNameFor(title),
            AnnotatedText.MediaType,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            FileKind.Document);
    }

    /// <summary>
    /// A file name for a body that was never a file anybody chose a name for. It is only ever
    /// seen on a download, so it carries the document's title with everything a filesystem
    /// might object to replaced — never the title verbatim, because a title is free text and a
    /// name is written into a header.
    /// </summary>
    private static string FileNameFor(string title)
    {
        var name = new string([.. title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)]).Trim();
        return (name.Length == 0 ? "text" : name[..Math.Min(name.Length, 80)]) + AnnotatedText.FileExtension;
    }

    /// <summary>
    /// Re-measures every text-range member pointing at this document against the new body.
    /// </summary>
    private async Task<ReanchorReport> ReanchorAsync(
        Guid documentId, AnnotatedTextBody body, Guid previousFileId, Guid newFileId, CancellationToken ct)
    {
        var members = await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Document
                && m.EntityId == documentId
                && m.AnchorKind == AnchorKind.TextRange)
            .ToListAsync(ct);

        if (members.Count == 0)
        {
            return default;
        }

        var stream = AnnotatedText.CanonicalText(body.Blocks);
        var unmoved = 0;
        var moved = 0;
        var lost = 0;

        foreach (var member in members)
        {
            var anchor = TextAnchorReanchoring.Read(member.Anchor);
            if (anchor is null)
            {
                // A payload this cannot read is left untouched. It was written by something,
                // and rewriting what is not understood is how data is destroyed quietly.
                lost++;
                continue;
            }

            var located = TextAnchorReanchoring.Locate(anchor.Value, stream);
            if (located.Outcome == ReanchorOutcome.Lost)
            {
                lost++;

                // A member that was never pinned has nothing to degrade against, so pin it to
                // the file it was last known good on. Without this a lost passage would go on
                // reading as exact against a revision that no longer contains it.
                member.AnchorFileId ??= previousFileId;
                continue;
            }

            member.Anchor = TextAnchorReanchoring.Write(anchor.Value, located.Start, located.End);
            member.AnchorFileId = newFileId;
            if (located.Outcome == ReanchorOutcome.Unmoved)
            {
                unmoved++;
            }
            else
            {
                moved++;
            }
        }

        return new ReanchorReport(unmoved, moved, lost);
    }
}
