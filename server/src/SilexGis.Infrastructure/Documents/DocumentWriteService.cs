// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents.Extraction;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>A rejected document write, carrying the stable error code the API surfaces.</summary>
public sealed class DocumentWriteException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Bytes that are already in the file store, described well enough to record them.
/// Callers write the content first (uploads can be large; nothing is buffered), then hand
/// the resulting facts over.
/// </summary>
public sealed record StoredContent(
    string StoragePath,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string Sha256,
    FileKind Kind,
    Point? Geom = null,
    ContentFacts? Facts = null);

/// <summary>
/// The editable, document-level facts of a document. A null <see cref="Metadata"/> means
/// "leave what is stored alone" rather than "clear it" — which is what lets a title be
/// corrected on a document whose kind has since tightened its schema.
/// <para>
/// <see cref="Visibility"/> and <see cref="CavingGroupId"/> are the read audience and the
/// club binding: the facts the access rule reads when no entry has an opinion. They are
/// carried here rather than edited row-side so that every write of them travels the one
/// path, and so the audit trail records them like any other document change. Whether the
/// caller may bind to the named club is decided before this is called — that guard needs
/// the caller, which this service deliberately does not have.
/// </para>
/// </summary>
/// <param name="Language">
/// The document's language code, or null to leave whatever is stored alone — the same carve-out
/// <see cref="Metadata"/> has, and for the same reason: the language is detected from the text,
/// and a caller correcting a title must not silently undo that detection by not mentioning it.
/// Clearing it is still possible and still explicit: any value that is not a language subtag —
/// an empty string is the obvious one — normalises to "nobody has said".
/// </param>
public sealed record DocumentUpdate(
    string Title,
    long? DocumentTypeId,
    string? Metadata,
    Visibility Visibility,
    Guid? CavingGroupId,
    string? Language = null);

/// <summary>
/// The single mutator of a document's derived state: which revision is current, what
/// number the next one takes, and the attachments and taggings that follow the current
/// revision when it moves. Every path that stores a file goes through here, so "what is
/// the current version" has exactly one answer — the database enforces the same thing
/// with a partial unique index, and the integrity verifier re-derives it on a schedule.
///
/// Sequence semantics live in <see cref="DocumentVersionRules"/> (pure, unit-tested);
/// this class loads state, delegates and persists.
///
/// Creating rows leaves them tracked but unsaved, because callers legitimately batch a
/// document with the entity that owns it — a geofile, a raster, a survey model — into one
/// unit of work. Superseding a revision is the exception: it owns its own transaction,
/// because a demotion that committed without its replacement would leave a document with
/// no current version at all.
/// </summary>
public sealed class DocumentWriteService(
    SilexGisDbContext db, ITypedPropertiesValidator metadataValidator, IDocumentConverter converter)
{
    /// <summary>A document's metadata does not conform to its kind's schema.</summary>
    public const string MetadataInvalidCode = "document.metadata_invalid";

    /// <summary>The requested document kind does not exist.</summary>
    public const string UnknownTypeCode = "document.type_unknown";

    private const string NotHeadCode = "file.not_head";
    private const string NotHeadMessage = "A newer version already exists; upload onto the current version.";
    private const string VersionInUseCode = "file.version_in_use";
    private const int TitleMaxLength = 300;

    /// <summary>
    /// Column widths for the facts a file states about itself. Author and producer share
    /// one width because they are the same kind of free text out of the same tags.
    /// </summary>
    private const int NameFactMaxLength = 255;
    private const int CodecMaxLength = 64;
    private const int TextExtractionErrorMaxLength = 1000;

    /// <summary>Last-resort title when the upload supplied nothing usable to name it by.</summary>
    private const string UntitledTitle = "Untitled";

    /// <summary>Unique indexes whose violation means someone else moved the version sequence.</summary>
    private const string CurrentVersionIndex = "ix_document_versions_current";
    private const string VersionNumberIndex = "ix_document_versions_document_id_version_number";

    /// <summary>
    /// Records new content as a brand-new document: one revision, current from the start,
    /// carrying one file. Tracked, not saved — the caller commits it.
    /// </summary>
    /// <param name="cavingGroupId">
    /// The club the new document belongs to, when the upload named one. Whether the
    /// uploader may bind to it is settled before this is reached — that guard needs the
    /// caller, which this service deliberately does not have.
    /// </param>
    public DocumentFile Create(
        StoredContent content,
        string? title,
        Guid ownerUserId,
        Guid? uploadedBy,
        DateOnly? documentDate = null,
        Guid? cavingGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var document = new Document
        {
            Title = TitleOf(title, content.OriginalName),
            OwnerUserId = ownerUserId,
            CavingGroupId = cavingGroupId,
        };
        db.Documents.Add(document);

        var version = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = DocumentVersionRules.FirstVersionNumber,
            IsCurrent = true,
            UploadedBy = uploadedBy,
            DocumentDate = documentDate,
        };
        db.DocumentVersions.Add(version);

        return new DocumentFile(AddFile(version.Id, content), version);
    }

    /// <summary>
    /// Adds another physical file to an existing revision — a rendition derived from what
    /// is already there, such as a cloud-optimized copy of an uploaded raster. It is the
    /// same content in another encoding, not a new revision, so nothing about the version
    /// sequence moves. Tracked, not saved.
    /// </summary>
    /// <param name="convertedFromFileId">
    /// The uploaded file this one is a converted copy of, when that is what it is. Its text is
    /// read like any other portable document's, because that reading is what counts the pages
    /// and puts each page's words on the page they are actually on — which is the whole reason
    /// the copy exists. What the copy is deliberately kept out of is content search: whether a
    /// document can be found must not depend on whether an optional service happens to be
    /// deployed here, so searching keeps looking at the words read out of the upload itself,
    /// which every installation reads the same way.
    /// </param>
    public StoredFile AddFile(
        Guid documentVersionId, StoredContent content, Guid? convertedFromFileId = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Facts the file states about itself get columns of their own rather than a place in
        // the metadata bag, because document lists filter and order on them. A format that
        // states none of this leaves them null, which is the honest answer.
        var facts = content.Facts ?? ContentFacts.None;
        var file = new StoredFile
        {
            DocumentVersionId = documentVersionId,
            StoragePath = content.StoragePath,
            OriginalName = content.OriginalName,
            MimeType = Fit(content.MimeType, FileFormats.MaxMediaTypeLength),
            SizeBytes = content.SizeBytes,
            Sha256 = content.Sha256,
            Kind = content.Kind,
            Geom = content.Geom,
            Author = Trim(facts.Author, NameFactMaxLength),
            Producer = Trim(facts.Producer, NameFactMaxLength),
            ContentCreatedAt = facts.ContentCreatedAt,
            ContentModifiedAt = facts.ContentModifiedAt,
            DurationSeconds = facts.DurationSeconds >= 0 ? facts.DurationSeconds : null,
            Codec = Trim(facts.Codec, CodecMaxLength),
            ConvertedFromFileId = convertedFromFileId,
        };
        db.StoredFiles.Add(file);

        if (content.Kind == FileKind.Image)
        {
            // An image is one page by definition. Paged formats get their page rows from
            // text extraction, which is the only thing that knows the real count — and the
            // count column is written here alongside the row so the two cannot disagree
            // about a file whose pages nothing has read yet.
            db.DocumentPages.Add(new DocumentPage { FileId = file.Id, PageNumber = 1 });
            file.PageCount = 1;
        }

        // A format that carries words is pending from the instant it is recorded, and the row
        // that says so is written in the same breath as the queue row that will answer it.
        // Marking the file without queuing it would leave it waiting forever; queuing without
        // marking it would let a reader look at the file in the meantime and report, wrongly,
        // that there is nothing in it. Both go in the caller's unit of work, so a rejected
        // upload takes the job with it.
        if (TextExtractionFormats.CarriesText(file.MimeType))
        {
            file.TextExtraction = TextExtractionState.Pending;
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.TextExtraction,
                Payload = JsonSerializer.Serialize(
                    new TextExtractionPayload(file.Id), JsonSerializerOptions.Web),

                // Deliberately nobody: the queue emails its requester on every outcome, and an
                // upload is not a request for a mail saying a background task finished. Where
                // the reading got to is a fact about the document, and it is on the document.
                RequestedBy = null,
            });
        }

        QueueConversion(file, convertedFromFileId);
        return file;
    }

    /// <summary>
    /// Marks a format that has no pages of its own for conversion into one that does, and
    /// queues the work — or records, without queuing anything, that this installation has no
    /// converter.
    /// <para>
    /// The state is written either way, and that is the point of it. "Nothing here can lay this
    /// document out" is a fact about the installation, not about the document, and an interface
    /// that cannot tell the two apart shows a perfectly good file as a broken one.
    /// </para>
    /// </summary>
    private void QueueConversion(StoredFile file, Guid? convertedFromFileId)
    {
        // A converted copy is never itself converted, and a format that paginates itself has
        // nothing to gain.
        if (convertedFromFileId is not null || !ConvertibleFormats.CanConvert(file.MimeType))
        {
            return;
        }

        if (!converter.IsConfigured)
        {
            file.Conversion = ConversionState.Unavailable;
            return;
        }

        file.Conversion = ConversionState.Pending;
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.DocumentConversion,
            Payload = JsonSerializer.Serialize(
                new DocumentConversionPayload(file.Id), JsonSerializerOptions.Web),

            // Deliberately nobody, for the same reason a reading names nobody: an upload is not
            // a request for a mail saying a background task finished.
            RequestedBy = null,
        });
    }

    /// <summary>
    /// Records how converting a file ended, and — when it produced one — the copy it produced.
    /// Saves. Returns false when the file has since been deleted, which is an ordinary outcome
    /// for queued work rather than a failure of anything.
    /// </summary>
    public async Task<bool> RecordConversionOutcomeAsync(
        Guid fileId,
        ConversionState state,
        StoredContent? converted,
        CancellationToken ct)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return false;
        }

        if (converted is not null)
        {
            AddFile(file.DocumentVersionId, converted, convertedFromFileId: file.Id);
        }

        file.Conversion = state;
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Records what a reader made of a file: the pages it found, and the reader and version
    /// that found them. Saves. Returns false when the file has since been deleted, which is an
    /// ordinary outcome for a queued reading and not a failure of anything.
    /// </summary>
    /// <remarks>
    /// Written to be run again over a file it has already read. Rows are matched by page
    /// number and updated in place rather than replaced, so nothing that points at a page
    /// loses its target to a re-reading; pages past the end of the new reading are removed,
    /// because a file that turned out to have fewer pages than a previous reader thought must
    /// not keep the surplus. The page count on the file moves in the same unit of work as the
    /// rows, so the two cannot disagree.
    /// <para>
    /// The upload itself is never touched. Everything written here is derived data hanging off
    /// the file — the stored bytes, their hash, their recorded format and their name stay
    /// exactly as they were received.
    /// </para>
    /// </remarks>
    public async Task<bool> RecordPageTextAsync(
        Guid fileId,
        string extractor,
        int version,
        IReadOnlyList<ExtractedPage> pages,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return false;
        }

        var existing = await db.DocumentPages.Where(p => p.FileId == fileId).ToListAsync(ct);
        var byNumber = existing.ToDictionary(p => p.PageNumber);

        foreach (var page in pages)
        {
            if (byNumber.TryGetValue(page.PageNumber, out var row))
            {
                row.Text = page.Text;
                row.Extractor = extractor;
                row.ExtractorVersion = version;
                continue;
            }

            db.DocumentPages.Add(new DocumentPage
            {
                FileId = fileId,
                PageNumber = page.PageNumber,
                Text = page.Text,
                Extractor = extractor,
                ExtractorVersion = version,
            });
        }

        var highest = pages.Count == 0 ? 0 : pages.Max(p => p.PageNumber);
        foreach (var stale in existing.Where(p => p.PageNumber > highest))
        {
            db.DocumentPages.Remove(stale);
        }

        file.PageCount = pages.Count;

        // A file that was read in full and holds no words is a different answer from one
        // nothing has looked at, and the interface has to be able to say which — a scanned
        // page will not become readable by waiting.
        file.TextExtraction = pages.Any(p => !string.IsNullOrEmpty(p.Text))
            ? TextExtractionState.Extracted
            : TextExtractionState.NoText;
        file.TextExtractionError = null;

        await DetectLanguageAsync(file.DocumentVersionId, pages.Select(p => p.Text), ct);

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Gives the document behind a revision a language, when the text just read says clearly
    /// what it is and nothing has said before. Tracked, not saved — the caller commits it with
    /// the pages, so a document never carries a language for text that was not stored.
    /// </summary>
    /// <remarks>
    /// Reading the whole document is the expensive part and it has just happened, so this is
    /// where the question is cheapest to ask — and asking it here means the answer is in place
    /// before anything is searched.
    /// <para>
    /// Only a document that has no language is given one. A code already on the row is either a
    /// correction somebody made or the answer of an earlier reading of the same words, and
    /// neither is improved by overwriting it from a re-reading — a correction especially, since
    /// the maintenance sweep re-reads files and would otherwise undo every correction it passed.
    /// The cost of that rule is that clearing the language deliberately looks exactly like never
    /// having had one, so a later re-reading will fill it in again.
    /// </para>
    /// </remarks>
    private async Task DetectLanguageAsync(
        Guid documentVersionId, IEnumerable<string?> pages, CancellationToken ct)
    {
        var documentId = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.Id == documentVersionId)
            .Select(v => v.DocumentId)
            .FirstOrDefaultAsync(ct);
        if (documentId == Guid.Empty)
        {
            return;
        }

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null || document.Language is not null)
        {
            return;
        }

        document.Language = LanguageDetection.Detect(pages);
    }

    /// <summary>
    /// Records that a reading ended without pages — nothing could read the format, or the
    /// bytes would not open. Saves. Returns false when the file has since been deleted.
    /// </summary>
    public async Task<bool> RecordTextExtractionOutcomeAsync(
        Guid fileId,
        TextExtractionState state,
        string? error,
        CancellationToken ct)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return false;
        }

        file.TextExtraction = state;
        file.TextExtractionError = Trim(error, TextExtractionErrorMaxLength);
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Applies the editable document-level fields, validating metadata against the kind's
    /// schema and stamping the schema version it was checked against. Saves.
    /// </summary>
    /// <remarks>
    /// Which schema a document is measured against is the load-bearing part. Metadata the
    /// caller actually supplies is measured against the kind's *current* schema, so a
    /// tightened schema takes effect for everything written from then on. Metadata the
    /// caller left alone is measured against the version already stamped on the row — the
    /// text of which is kept precisely so this is possible — because a document that was
    /// valid when it was written stays valid, and otherwise tightening a schema would lock
    /// every older document out of even a title correction. Metadata the caller left alone on
    /// a row carrying no stamp was never measured against anything, because its kind had no
    /// schema when it was written; that stays true rather than being decided retroactively by
    /// a schema that arrived later.
    /// </remarks>
    /// <exception cref="DocumentWriteException">
    /// <c>document.not_found</c>, <c>document.type_unknown</c>,
    /// <c>document.metadata_invalid</c>.
    /// </exception>
    public async Task<Document> UpdateAsync(Guid documentId, DocumentUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new DocumentWriteException("document.not_found", "The document no longer exists.");

        DocumentType? type = null;
        if (update.DocumentTypeId is { } typeId)
        {
            type = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct)
                ?? throw new DocumentWriteException(UnknownTypeCode, "The requested document type does not exist.");
        }

        var typeChanged = document.DocumentTypeId != update.DocumentTypeId;
        var metadata = update.Metadata ?? document.Metadata;
        var rewritten = update.Metadata is not null || typeChanged;

        document.Title = TitleOf(update.Title, null);
        document.DocumentTypeId = update.DocumentTypeId;
        document.Visibility = update.Visibility;
        document.CavingGroupId = update.CavingGroupId;
        document.Metadata = metadata;

        // Mentioned or not mentioned, never "mentioned as nothing": a caller that says nothing
        // about the language leaves the detected one standing, and one that says something has
        // it normalised to the stored form. The database re-derives every page's search vector
        // from the language whenever this column actually changes, so nothing here has to ask
        // for a reindex - but it does mean an accidental clearing would quietly re-index a
        // whole document language-neutrally, which is why absence cannot mean clearing.
        if (update.Language is not null)
        {
            document.Language = DocumentLanguage.Normalize(update.Language);
        }

        document.MetadataSchemaVersion =
            await ValidateMetadataAsync(document, type, metadata, rewritten, ct);

        await db.SaveChangesAsync(ct);
        return document;
    }

    /// <summary>
    /// The schema version to stamp on the row, having checked the metadata against it.
    /// Null when there is nothing to check against — the kind publishes no schema, or the
    /// caller supplied no metadata and the row was never measured against one — so the stamp
    /// is cleared rather than left pointing at a version that does not describe the row.
    /// </summary>
    private async Task<int?> ValidateMetadataAsync(
        Document document, DocumentType? type, string metadata, bool rewritten, CancellationToken ct)
    {
        if (type?.MetadataSchema is null)
        {
            return null;
        }

        var version = type.MetadataSchemaVersion;
        var schema = type.MetadataSchema;
        if (!rewritten)
        {
            if (document.MetadataSchemaVersion is not { } stamped)
            {
                // No stamp at all means this metadata was never measured against anything —
                // the kind published no schema when it was written, which is how most kinds
                // ship. Measuring it now, against a schema that arrived afterwards, is the
                // same lock-out the stamped-version fallback below exists to prevent: a title
                // correction would be refused for a requirement the document predates. It
                // stays unmeasured, and the empty stamp keeps saying so, until a caller
                // actually supplies metadata — which is then measured against the current
                // schema like any other write.
                return null;
            }

            if (stamped != version)
            {
                // Fall forward to the current schema only when the stamped version's text is
                // missing, which means the history lost a row rather than that the document is
                // stale — failing the write instead would strand the document permanently.
                var published = await DocumentQueries.TypeSchemaOfVersionAsync(db, type.Id, stamped, ct);
                if (published is not null)
                {
                    version = stamped;
                    schema = published;
                }
            }
        }

        var errors = metadataValidator.Validate(schema, metadata);
        return errors.Count == 0
            ? version
            : throw new DocumentWriteException(MetadataInvalidCode, string.Join(" ", errors));
    }

    /// <summary>
    /// Supersedes a document's current revision with a new one carrying
    /// <paramref name="content"/>, and moves everything that pointed at the old revision's
    /// file onto the new one. Owns its transaction; returns the new file.
    /// </summary>
    /// <exception cref="DocumentWriteException">
    /// <c>file.not_head</c> when <paramref name="supersededVersionId"/> is not the current
    /// revision — including the case where a concurrent upload got there first.
    /// </exception>
    public async Task<DocumentFile> AddVersionAsync(
        Guid supersededVersionId, StoredContent content, Guid? uploadedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var superseded = await db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == supersededVersionId, ct);
        if (superseded is null
            || !DocumentVersionRules.MayStackOnto(
                new DocumentVersionRules.VersionState(superseded.Id, superseded.VersionNumber, superseded.IsCurrent)))
        {
            throw new DocumentWriteException(NotHeadCode, NotHeadMessage);
        }

        // Demote exactly the revision the caller claimed to be stacking onto, and treat "no
        // row changed" as the conflict. That makes the head check and the demotion one
        // indivisible act: the read above is only a fast path, because between it and this
        // statement another upload may already have taken the current flag. Demoting whatever
        // happens to be current instead would silently discard that upload and strand its
        // attachments on a revision no longer served.
        //
        // The row lock this takes is also what serialises two uploads racing onto the same
        // revision: the loser blocks here, then re-evaluates the predicate against the
        // committed row and finds nothing to do.
        //
        // A statement of its own is required regardless: a partial unique index is checked per
        // statement, so the flag has to be free before it is taken, and leaving the demotion
        // and the insert to one flush leaves their order to the change tracker, which sorts by
        // key rather than by intent. The surrounding transaction is what keeps the gap
        // invisible — a demotion that committed without its replacement would leave the
        // document with no current version at all.
        var demoted = await db.DocumentVersions
            .Where(v => v.Id == superseded.Id && v.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsCurrent, false), ct);
        if (demoted == 0)
        {
            throw new DocumentWriteException(NotHeadCode, NotHeadMessage);
        }

        // Read after the demotion, never before. The next version number and the set of rows
        // to repoint must describe the document as it stands now that this call owns the
        // current flag; numbers derived from an older snapshot can skip a revision, and a
        // repoint set derived from one can miss rows another upload has already moved.
        var sequence = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == superseded.DocumentId)
            .Select(v => new DocumentVersionRules.VersionState(v.Id, v.VersionNumber, v.IsCurrent))
            .ToListAsync(ct);
        var supersededFileIds = await db.StoredFiles.AsNoTracking()
            .Where(f => f.DocumentVersionId == superseded.Id)
            .Select(f => f.Id)
            .ToListAsync(ct);

        var version = new DocumentVersion
        {
            DocumentId = superseded.DocumentId,
            VersionNumber = DocumentVersionRules.NextVersionNumber(sequence),
            IsCurrent = true,
            UploadedBy = uploadedBy,
            // Version detail carries across: a re-scan of a 1974 survey is still from 1974,
            // and the uploader corrects the date afterwards if this revision is genuinely
            // from another day.
            DocumentDate = superseded.DocumentDate,
            Label = superseded.Label,
        };
        db.DocumentVersions.Add(version);
        var file = AddFile(version.Id, content);

        // Repoint attachments from the superseded revision to the new one, tracked so the
        // change is audited — entity timelines get a "document updated" event from the
        // Attachment FileId diff. Selecting by FileId moves feature-targeted rows and
        // polymorphic-pair rows alike; the target side of each row is untouched.
        //
        // A revision may hold several files (a scan and the rendition derived from it), and
        // they all collapse onto the single file of the new revision. Two rows that attached
        // two of those files to the same object would become the same attachment twice, so
        // the later ones are dropped instead of duplicated — the earliest row, which carries
        // the caption and sort order the object has been showing, is the one kept.
        var attachments = await db.Attachments
            .Where(a => supersededFileIds.Contains(a.FileId))
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(ct);
        var attachedTargets = new HashSet<(Guid?, AttachedEntityType?, Guid?)>();
        foreach (var attachment in attachments)
        {
            if (attachedTargets.Add((attachment.FeatureId, attachment.EntityType, attachment.EntityId)))
            {
                attachment.FileId = file.Id;
            }
            else
            {
                db.Attachments.Remove(attachment);
            }
        }

        // Tags belong to the document, not a revision of it. A file is only ever a
        // polymorphic-pair target (never a feature), so the pair filter reaches every tagging.
        // The same collapse applies, and here it is not merely untidy: one tag may sit on one
        // target only once, and that is a unique index — reassigning the second row would
        // violate it and fail the whole upload.
        var taggings = await db.Taggings
            .Where(t => t.EntityType == AttachedEntityType.StoredFile
                && t.EntityId != null && supersededFileIds.Contains(t.EntityId.Value))
            .OrderBy(t => t.Id)
            .ToListAsync(ct);
        var movedTagIds = new HashSet<long>();
        foreach (var tagging in taggings)
        {
            if (movedTagIds.Add(tagging.TagId))
            {
                tagging.EntityId = file.Id;
            }
            else
            {
                db.Taggings.Remove(tagging);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsVersionSequenceViolation(e))
        {
            // Something else claimed this document's current flag or the version number this
            // call computed. The scoped demotion above makes that unreachable through this
            // method, so this is the backstop for a version written by any other route.
            // Deliberately narrow: a unique violation on an unrelated index is a different
            // failure, and reporting it as "a newer version already exists" would send the
            // caller to re-upload onto the revision they already used.
            throw new DocumentWriteException(NotHeadCode, NotHeadMessage);
        }

        await transaction.CommitAsync(ct);
        return new DocumentFile(file, version);
    }

    /// <summary>
    /// Deletes a superseded revision whole — its files and their pages go with it. This is
    /// the "version 1 held something that had to go" purge path; the current revision is
    /// never deletable, because a document with no current version has nothing to serve.
    /// Returns the removed files so the caller can drop their bytes and cached derivatives.
    /// </summary>
    /// <exception cref="DocumentWriteException">
    /// <c>file.head_undeletable</c> for the current revision; <c>file.version_in_use</c>
    /// when something still points at one of its files.
    /// </exception>
    public async Task<IReadOnlyList<StoredFile>> DeleteVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new DocumentWriteException("file.not_found", "The version no longer exists.");

        if (version.IsCurrent)
        {
            throw new DocumentWriteException(
                "file.head_undeletable",
                "The current version cannot be deleted; upload a new version instead.");
        }

        // A revision goes whole, so every file under it has to be free — not just the one the
        // caller named. A revision holding a server-derived rendition an entity still points
        // at is the common case: the entity's foreign key restricts, so deleting would fail
        // the statement outright rather than report anything useful.
        var files = await db.StoredFiles.Where(f => f.DocumentVersionId == versionId).ToListAsync(ct);
        var fileIds = files.Select(f => f.Id).ToList();
        if (await AnythingPointsAtAsync(fileIds, ct))
        {
            throw new DocumentWriteException(
                VersionInUseCode,
                "This version is still in use and cannot be deleted.");
        }

        // Removed through the change tracker rather than left to the cascade: a deleted file
        // is a forensic event the timeline keeps, and a database cascade writes no audit row.
        db.StoredFiles.RemoveRange(files);
        db.DocumentVersions.Remove(version);
        await db.SaveChangesAsync(ct);
        return files;
    }

    /// <summary>
    /// Deletes a whole document — every revision, file and page under it. For the entities
    /// that own their upload outright (a geofile, a raster map, an avatar), where deleting
    /// the entity means the content is gone. Returns the removed files so the caller can
    /// drop their bytes, or nothing when the document has outgrown that ownership.
    /// </summary>
    /// <remarks>
    /// The caller's claim on the document rests entirely on <paramref name="fileId"/>: that
    /// is the file its entity holds. Everything else under the same document may have grown
    /// a life of its own — a later revision someone attached elsewhere, a rendition another
    /// entity points at — and taking those with it would either drop attachment rows through
    /// a cascade that writes no audit trail, or fail the statement against a restricting
    /// foreign key. When that has happened nothing is deleted and the content stays reachable
    /// through its uploader, the same "it belongs to that other thing now, leave it alone"
    /// answer the avatar path already gives for a single file.
    /// </remarks>
    public async Task<IReadOnlyList<StoredFile>> DeleteDocumentOfFileAsync(Guid fileId, CancellationToken ct = default)
    {
        var documentId = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.DocumentId)
            .FirstOrDefaultAsync(ct);
        if (documentId == Guid.Empty)
        {
            return [];
        }

        var versionIds = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .Select(v => v.Id)
            .ToListAsync(ct);
        var files = await db.StoredFiles.Where(f => versionIds.Contains(f.DocumentVersionId)).ToListAsync(ct);

        var siblingIds = files.Where(f => f.Id != fileId).Select(f => f.Id).ToList();
        if (await AnythingPointsAtAsync(siblingIds, ct))
        {
            return [];
        }

        var versions = await db.DocumentVersions.Where(v => v.DocumentId == documentId).ToListAsync(ct);
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);

        db.StoredFiles.RemoveRange(files);
        db.DocumentVersions.RemoveRange(versions);
        if (document is not null)
        {
            db.Documents.Remove(document);
        }

        return files;
    }

    /// <summary>
    /// Whether anything still points at one of these files. Deleting a referenced file is
    /// never harmless: the attachment foreign key cascades, so the row disappears with no
    /// audit trail; the entity foreign keys (geofile, raster map, survey model) restrict, so
    /// the statement fails and the caller sees an unhandled database error instead of a
    /// stable code; taggings and avatars carry no foreign key at all and would simply be left
    /// pointing at nothing.
    /// </summary>
    private async Task<bool> AnythingPointsAtAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct)
    {
        if (fileIds.Count == 0)
        {
            return false;
        }

        return await db.Attachments.AsNoTracking().AnyAsync(a => fileIds.Contains(a.FileId), ct)
            || await db.Taggings.AsNoTracking().AnyAsync(
                t => t.EntityType == AttachedEntityType.StoredFile
                    && t.EntityId != null && fileIds.Contains(t.EntityId.Value), ct)
            || await db.Geofiles.AsNoTracking().AnyAsync(g => fileIds.Contains(g.FileId), ct)
            || await db.GeoreferencedMaps.AsNoTracking().AnyAsync(m => fileIds.Contains(m.FileId), ct)
            || await db.SurveyModels.AsNoTracking().AnyAsync(s => fileIds.Contains(s.FileId), ct)
            || await db.Users.AsNoTracking().AnyAsync(
                u => u.AvatarFileId != null && fileIds.Contains(u.AvatarFileId.Value), ct);
    }

    /// <summary>
    /// Whether a failed save is the version sequence being taken by someone else, rather
    /// than any other write failure that happens to reach the database as a conflict.
    /// </summary>
    private static bool IsVersionSequenceViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
        && violation.ConstraintName is CurrentVersionIndex or VersionNumberIndex;

    /// <summary>
    /// A document always has a readable title, and a client-supplied file name is not a
    /// reliable source of one: a multipart part may carry a blank name, and a name that is
    /// nothing but an extension leaves nothing behind once the extension is stripped. Callers
    /// derive their titles from those names, so the fallback lives here rather than being
    /// re-checked at every upload site — and rejecting the write at this point would strand
    /// bytes that are already in the file store.
    /// </summary>
    private static string TitleOf(string? title, string? originalName)
    {
        var candidate = string.IsNullOrWhiteSpace(title) ? originalName : title;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = UntitledTitle;
        }

        candidate = candidate.Trim();
        return candidate.Length > TitleMaxLength ? candidate[..TitleMaxLength] : candidate;
    }

    /// <summary>
    /// Fits a media type into its column. Every path that stores bytes routes through here, and
    /// several of them pass a value nothing bounds — a request header the client wrote, or the
    /// declaration a container carries inside itself. An over-long one is not a media type at
    /// all, so it is recorded as the unknown type rather than as a truncated prefix that would
    /// read like a real format; either way the upload succeeds instead of failing against the
    /// column width with the bytes already in the store and no row pointing at them.
    /// </summary>
    private static string Fit(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length > maxLength
            ? FileFormats.UnknownMimeType
            : value;

    /// <summary>
    /// Fits a value a file stated about itself into its column. These come from the file's own
    /// bytes, so nothing bounds them: a damaged tag can hold anything, and truncating it keeps
    /// an unusable value from failing an upload that is otherwise fine.
    /// </summary>
    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
