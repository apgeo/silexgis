// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.SurveyUploads;

public static partial class SurveyUploadEndpoints
{
    public const string SizeMismatchCode = "survey_upload.size_mismatch";
    public const string ContentMismatchCode = "survey_upload.content_mismatch";
    public const string FileUnknownCode = "survey_upload.file_unknown";

    /// <summary>
    /// Receives one declared file and archives it before answering: a Therion file, a Survex file,
    /// a log or the survey archive becomes a survey source of the cave; anything else a document
    /// attached to it. Either way the document carries the upload's batch, and the survey source
    /// carries the upload and its survey.
    /// </summary>
    private static async Task<Results<Ok<SurveyUploadFileReceivedDto>, UnauthorizedHttpResult, ProblemHttpResult>> PutFileAsync(
        Guid id,
        string name,
        HttpRequest http,
        SilexGisDbContext db,
        ContentIntake intake,
        DocumentWriteService documents,
        UploadBatchService batches,
        IFileStore fileStore,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var upload = found.Upload;
        var file = await db.SurveyUploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.SurveyUploadId == upload.Id && f.Name == name, ct);
        if (file is null)
        {
            return ApiProblems.NotFound(FileUnknownCode);
        }

        // A repeat for a file already archived — the answer to the first was lost — gets the first
        // answer, whatever the upload has moved on to since.
        if (file.Status == SurveyUploadFileStatus.Stored)
        {
            return TypedResults.Ok(Received(file));
        }

        if (upload.Status != SurveyUploadStatus.Receiving)
        {
            return ApiProblems.Conflict(NotReceivingCode, "This upload is no longer receiving files.");
        }

        if (clock.GetUtcNow() >= upload.ExpiresAt)
        {
            return Expired();
        }

        if (http.ContentLength is { } announced && announced != file.DeclaredSize)
        {
            return SizeMismatch(file.DeclaredSize);
        }

        // A file with no survey format becomes an ordinary document, on the questions any other
        // document is created on. Asked before the bytes are read, and again here although the
        // open asked it too: a right can be taken away while a session waits.
        SurveyCaveAudience? documentAudience = null;
        if (SurveySourceFormats.ClaimedKind(name) is null)
        {
            documentAudience = DocumentAudience(ctx, found.Cave, upload.DocumentsVisibility);
            if (documentAudience.RefusalCode is { } refused)
            {
                return DocumentAudienceProblem(refused);
            }
        }

        // Read no further than one byte past the declaration: a body that keeps going is a size
        // mismatch either way, and the store need not hold the rest to know it.
        var content = await intake.FromStreamAsync(
            new CappedStream(http.Body, file.DeclaredSize + 1), name, http.ContentType, ct);
        if (content.SizeBytes != file.DeclaredSize)
        {
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return SizeMismatch(file.DeclaredSize);
        }

        var kind = SurveySourceFormats.ClaimedKind(name);
        if (kind is { } claimed
            && !SurveySourceFormats.ContentMatches(claimed, await HeaderAsync(fileStore, content.StoragePath, ct)))
        {
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            await RefuseAsync(db, file.Id, ContentMismatchCode, clock.GetUtcNow(), ct);
            return ApiProblems.BadRequest(
                ContentMismatchCode,
                $"This file is not what its name says: it was expected to be {SurveySourceFormats.ExpectedContent(claimed)}.",
                "file",
                name);
        }

        var archived = await ArchiveAsync(
            db, documents, ctx.UserId, upload, found.Cave, name, kind, documentAudience, content, file, clock, ct);
        if (archived is null)
        {
            // Another request for the same file archived it first; this copy of the bytes is not needed.
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            var first = await db.SurveyUploadFiles.AsNoTracking().FirstAsync(f => f.Id == file.Id, ct);
            return TypedResults.Ok(Received(first));
        }

        // The batch's own report of what it holds. Written after the archive is committed, so a
        // report that fails never takes an archived file back with it.
        await batches.RecordAsync(
            upload.UploadBatchId, name, content.SizeBytes,
            new IngestOutcome(UploadItemOutcome.Stored, DocumentId: archived.DocumentId), ct);

        return TypedResults.Ok(Received(archived));
    }

    /// <summary>
    /// Writes the archive rows and marks the file stored, in one transaction. Returns null when
    /// another request marked it stored first: the rows written here are then rolled back.
    /// </summary>
    private static async Task<SurveyUploadFile?> ArchiveAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        Guid userId,
        SurveyUpload upload,
        Feature cave,
        string name,
        SurveySourceKind? kind,
        SurveyCaveAudience? documentAudience,
        StoredContent content,
        SurveyUploadFile file,
        TimeProvider clock,
        CancellationToken ct)
    {
        var title = Path.GetFileNameWithoutExtension(name);
        DocumentFile stored;
        Guid? sourceId = null;

        if (kind is { } sourceKind)
        {
            // Stored as survey material under the format's own media type, never as a text
            // document — which is what would send it to be read for words. It stays the
            // uploader's own document: who else reads it is the cave's survey rule, asked on every
            // route that serves it.
            stored = documents.Create(
                content with { MimeType = SurveySourceFormats.MediaTypeFor(sourceKind), Kind = FileKind.Survey },
                title, userId, userId);

            var source = new SurveySource
            {
                CaveFeatureId = cave.Id,
                DocumentId = stored.Version.DocumentId,
                Kind = sourceKind,
                Name = name,
                OriginalFileName = name,
                CaveSurveyId = upload.CaveSurveyId,
                SurveyUploadId = upload.Id,
            };
            db.SurveySources.Add(source);
            sourceId = source.Id;

            // A compiler log says how the survey closes; the reading is queued in the same save,
            // naming the exact revision it reads, exactly as for a log archived on the web.
            if (sourceKind == SurveySourceKind.TherionLog)
            {
                var compilation = new SurveyCompilation
                {
                    CaveFeatureId = cave.Id,
                    SurveySourceId = source.Id,
                    LogFileId = stored.File.Id,
                    LogVersionNumber = stored.Version.VersionNumber,
                };
                db.SurveyCompilations.Add(compilation);
                db.ProcessingJobs.Add(new ProcessingJob
                {
                    Kind = ProcessingJobKinds.SurveyCompilation,
                    Payload = JsonSerializer.Serialize(new SurveyCompilationPayload(compilation.Id), JsonSerializerOptions.Web),
                    RequestedBy = userId,
                });
            }
        }
        else
        {
            // An export with no survey format of its own — a drawing, a track, a table — is a
            // document attached to the cave, on the terms the caller worked out before reading it.
            var audience = documentAudience
                ?? throw new InvalidOperationException("A document's audience is worked out before its bytes are archived.");
            stored = documents.Create(content, title, userId, userId, cavingGroupId: audience.CavingGroupId);
            var document = db.Documents.Local.First(d => d.Id == stored.Version.DocumentId);
            document.Visibility = audience.Visibility;
            db.Attachments.Add(new Attachment
            {
                FileId = stored.File.Id,
                FeatureId = cave.Id,
                Role = AttachmentRole.Other,
                AddedBy = userId,
            });
        }

        db.Documents.Local.First(d => d.Id == stored.Version.DocumentId).UploadBatchId = upload.UploadBatchId;

        var documentId = stored.Version.DocumentId;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        var claimed = await db.SurveyUploadFiles
            .Where(f => f.Id == file.Id && f.Status != SurveyUploadFileStatus.Stored)
            .ExecuteUpdateAsync(set => set
                .SetProperty(f => f.Status, SurveyUploadFileStatus.Stored)
                .SetProperty(f => f.ReceivedSize, content.SizeBytes)
                .SetProperty(f => f.Sha256, content.Sha256)
                .SetProperty(f => f.RefusalCode, (string?)null)
                .SetProperty(f => f.DocumentId, documentId)
                .SetProperty(f => f.SurveySourceId, sourceId)
                .SetProperty(f => f.UpdatedAt, now), ct);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return null;
        }

        await transaction.CommitAsync(ct);
        return await db.SurveyUploadFiles.AsNoTracking().FirstAsync(f => f.Id == file.Id, ct);
    }

    /// <summary>Who an attached export of this upload is open to, and the group it is filed under.</summary>
    private static SurveyCaveAudience DocumentAudience(AccessContext ctx, Feature cave, Visibility? requested) =>
        SurveyDocumentAudience.Resolve(ctx, cave.IsProtectedEffective, cave.Visibility, cave.CavingGroupId, requested);

    private static ProblemHttpResult DocumentAudienceProblem(string code) =>
        code == SurveyCaveVisibility.InvalidCode
            ? ApiProblems.BadRequest(
                code, "The documents cannot be opened to a caving group: the cave has none the account may file them under.")
            : ApiProblems.Forbidden(code);

    /// <summary>Records why a file's bytes were refused, so the status row can say; a later attempt may still succeed.</summary>
    private static Task RefuseAsync(SilexGisDbContext db, Guid fileId, string code, DateTimeOffset now, CancellationToken ct) =>
        db.SurveyUploadFiles
            .Where(f => f.Id == fileId && f.Status != SurveyUploadFileStatus.Stored)
            .ExecuteUpdateAsync(set => set
                .SetProperty(f => f.Status, SurveyUploadFileStatus.Refused)
                .SetProperty(f => f.RefusalCode, code)
                .SetProperty(f => f.UpdatedAt, now), ct);

    private static SurveyUploadFileReceivedDto Received(SurveyUploadFile file) => new(
        file.Name, file.Status, file.Sha256 ?? "", file.ReceivedSize ?? 0, file.SurveySourceId, file.DocumentId);

    private static ProblemHttpResult SizeMismatch(long declared) =>
        ApiProblems.BadRequest(
            SizeMismatchCode, $"The body is not the {declared} bytes this file was declared with.", "declaredSize", declared);

    private static async Task<byte[]> HeaderAsync(IFileStore fileStore, string storagePath, CancellationToken ct)
    {
        var buffer = new byte[FileFormats.HeaderBytes];
        await using var saved = await fileStore.OpenReadAsync(storagePath, ct);
        var read = await saved.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return buffer[..read];
    }

    /// <summary>A read-only view of a stream that ends after a fixed number of bytes.</summary>
    private sealed class CappedStream(Stream inner, long cap) : Stream
    {
        private long remaining = cap;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken);
            remaining -= read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
