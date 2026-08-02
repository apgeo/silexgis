// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// A stored file together with the revision it belongs to — the pair every file response
/// needs, because version number and document date are version detail rather than
/// properties of the bytes.
/// </summary>
public sealed record DocumentFile(StoredFile File, DocumentVersion Version);

/// <summary>
/// Reads over the document → version → file structure that several slices need. They live
/// together so "the file a document currently serves" is spelled the same way everywhere;
/// writing that walk out by hand is how a read path ends up disagreeing with the write
/// service about which version is current.
/// </summary>
public static class DocumentQueries
{
    /// <summary>A file and its revision in one round trip; null when the file is gone.</summary>
    public static Task<DocumentFile?> FileWithVersionAsync(
        SilexGisDbContext db, Guid fileId, CancellationToken ct = default) =>
        (from file in db.StoredFiles.AsNoTracking()
         join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
         where file.Id == fileId
         select new DocumentFile(file, version)).FirstOrDefaultAsync(ct);

    /// <summary>Who created the revision a file belongs to (null when that account is gone).</summary>
    public static Task<Guid?> UploaderOfFileAsync(
        SilexGisDbContext db, Guid fileId, CancellationToken ct = default) =>
        db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == fileId)
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id, (f, v) => v.UploadedBy)
            .FirstOrDefaultAsync(ct);

    /// <summary>The revision a document currently serves.</summary>
    public static Task<DocumentVersion?> CurrentVersionAsync(
        SilexGisDbContext db, Guid documentId, CancellationToken ct = default) =>
        db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.IsCurrent, ct);

    /// <summary>
    /// The file the current revision of this file's document serves — the row attachments
    /// and taggings point at, and the one every access rule is evaluated against. Returns
    /// the given file itself when it already is that row.
    /// </summary>
    public static Task<StoredFile?> CurrentFileOfDocumentAsync(
        SilexGisDbContext db, Guid fileId, CancellationToken ct = default) =>
        (from file in db.StoredFiles.AsNoTracking()
         join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
         join current in db.DocumentVersions.AsNoTracking() on version.DocumentId equals current.DocumentId
         join currentFile in db.StoredFiles.AsNoTracking() on current.Id equals currentFile.DocumentVersionId
         where file.Id == fileId && current.IsCurrent
         orderby currentFile.CreatedAt, currentFile.Id
         select currentFile).FirstOrDefaultAsync(ct);

    /// <summary>
    /// The file a document currently serves, with its revision — the row attachments point
    /// at. Null while the document has no current revision at all, which the write path
    /// makes unreachable.
    /// </summary>
    public static Task<DocumentFile?> CurrentFileAsync(
        SilexGisDbContext db, Guid documentId, CancellationToken ct = default) =>
        (from version in db.DocumentVersions.AsNoTracking()
         join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
         where version.DocumentId == documentId && version.IsCurrent
         orderby file.CreatedAt, file.Id
         select new DocumentFile(file, version)).FirstOrDefaultAsync(ct);

    /// <summary>
    /// The metadata schema a document type published as a given version, or null when that
    /// version was never published. A document stamps the version it was validated against,
    /// and is re-checked against that text rather than against whatever the schema became
    /// afterwards — so a tightened schema does not retroactively invalidate what is stored.
    /// </summary>
    public static Task<string?> TypeSchemaOfVersionAsync(
        SilexGisDbContext db, long documentTypeId, int version, CancellationToken ct = default) =>
        db.DocumentTypeSchemas.AsNoTracking()
            .Where(s => s.DocumentTypeId == documentTypeId && s.Version == version)
            .Select(s => s.Schema)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The files of one revision, oldest first — the upload itself, then anything derived
    /// from it (a cloud-optimized rendition, a second scan added to the same revision).
    /// </summary>
    public static IQueryable<StoredFile> FilesOfVersion(SilexGisDbContext db, Guid versionId) =>
        db.StoredFiles
            .Where(f => f.DocumentVersionId == versionId)
            .OrderBy(f => f.CreatedAt)
            .ThenBy(f => f.Id);
}
