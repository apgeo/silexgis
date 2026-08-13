// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// A photograph and everything a response about it is built from, gathered once so a listing
/// does not fetch the same rows per item.
/// </summary>
public sealed record PhotoRow(
    Document Document,
    StoredFile File,
    PhotoDetails? Details,
    string? PhotographerLabel);

/// <summary>
/// Which photographs a caller may see, and the rows anything showing them is drawn from.
///
/// <para>
/// It lives below the feature slices because more than one of them shows the same pictures — a
/// gallery, an album, a trip written up as a document — and which pictures a caller may see is a
/// rule with one home. A surface that composed its own would eventually compose a different one,
/// and the difference would be a picture somebody was shown that they may not see.
/// </para>
/// <para>
/// Nothing here decides who may see what: it composes the ordinary document read rule, applied
/// first, so every condition added after it can only narrow.
/// </para>
/// </summary>
public static class PhotographReads
{
    /// <summary>
    /// Photographs this caller may read: the ordinary document rule, narrowed to pictures.
    /// </summary>
    public static async Task<IQueryable<Document>> VisiblePhotographsAsync(
        SilexGisDbContext db, AccessContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var photographs = db.Documents.AsNoTracking().Where(d => db.StoredFiles.Any(f =>
            f.Kind == FileKind.Image
            && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));

        // Reach through an attachment is resolved before the query rather than by dropping rows
        // after it, for the same reason the cabinet listing does it: resolved afterwards, the
        // total beside the grid would count pictures the grid did not show.
        var admitted = photographs
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);
        var reached = await DocumentAccessRules.ReachedByAttachmentAsync(
            db, ctx, photographs.Where(d => !admitted.Contains(d.Id)).Select(d => d.Id), ct);

        return photographs.VisibleTo(ctx, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets));
    }

    /// <summary>
    /// Narrows a photograph query to the pictures hanging on one trip.
    /// </summary>
    /// <remarks>
    /// A term of the same visibility-filtered query rather than a pass over its results, so
    /// naming a trip the caller may not read answers empty rather than refused.
    /// </remarks>
    public static IQueryable<Document> AttachedToTrip(
        this IQueryable<Document> photographs, SilexGisDbContext db, Guid tripId)
    {
        ArgumentNullException.ThrowIfNull(db);

        return photographs.Where(d => db.Attachments.Any(a =>
            a.EntityType == AttachedEntityType.TripLog
            && a.EntityId == tripId
            && db.StoredFiles.Any(f => f.Id == a.FileId
                && db.DocumentVersions.Any(v =>
                    v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id))));
    }

    /// <summary>
    /// Loads the rows a listing is drawn from: each photograph with its current file, what it
    /// says about itself, and its photographer's label.
    /// </summary>
    /// <remarks>
    /// Three reads for the whole page rather than three per picture. A gallery page is a hundred
    /// tiles, and a per-row lookup is what turns a grid into a query storm.
    /// </remarks>
    public static async Task<IReadOnlyList<PhotoRow>> RowsAsync(
        SilexGisDbContext db, IReadOnlyList<Document> documents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            return [];
        }

        var ids = documents.Select(d => d.Id).ToList();

        var files = await (from file in db.StoredFiles.AsNoTracking()
                           join version in db.DocumentVersions.AsNoTracking()
                               on file.DocumentVersionId equals version.Id
                           where version.IsCurrent && ids.Contains(version.DocumentId)
                                 && file.Kind == FileKind.Image
                           select new { version.DocumentId, File = file })
            .ToListAsync(ct);
        var fileByDocument = files
            .GroupBy(f => f.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.File.CreatedAt).ThenBy(f => f.File.Id).First().File);

        var details = await db.PhotoDetails.AsNoTracking()
            .Where(p => ids.Contains(p.DocumentId))
            .ToDictionaryAsync(p => p.DocumentId, ct);

        // Resolved rather than joined, because the name a person may be shown under is a rule
        // with one home — and it is never their address.
        var caverIds = details.Values
            .Where(d => d.PhotographerCaverId is not null)
            .Select(d => d.PhotographerCaverId!.Value)
            .Distinct()
            .ToList();
        var caverLabels = caverIds.Count == 0
            ? []
            : await db.Cavers.AsNoTracking()
                .Where(c => caverIds.Contains(c.Id))
                .Select(c => new { c.Id, c.FullName })
                .ToDictionaryAsync(c => c.Id, c => CaverProtection.Label(c.FullName, null), ct);

        return
        [
            .. documents
                .Where(d => fileByDocument.ContainsKey(d.Id))
                .Select(d =>
                {
                    var detail = details.GetValueOrDefault(d.Id);
                    var label = detail?.PhotographerCaverId is { } caver
                        ? caverLabels.GetValueOrDefault(caver)
                        : null;
                    return new PhotoRow(d, fileByDocument[d.Id], detail, label);
                }),
        ];
    }
}
