// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Photos;

/// <summary>
/// The reads the gallery is built from: which photographs a caller may see, narrowed by what
/// they asked about, and the rows a response is drawn from.
///
/// <para>
/// A gallery is a <em>bulk</em> path to material that per-object pages release one at a time,
/// so every narrowing here is applied on top of the ordinary document read rule rather than
/// instead of it. Nothing in this file decides who may see what; it composes the filter that
/// does, which is why the visibility term is applied first and every condition after it can
/// only ever narrow.
/// </para>
/// </summary>
internal static class PhotoQueries
{
    /// <summary>
    /// How many photographs a map-extent query looks at before it gives up narrowing.
    ///
    /// <para>
    /// A bbox needs the positions resolved before the page can be counted, because whether a
    /// photograph's fix may be shown is a per-caller answer and not a condition SQL can carry.
    /// Resolving it after paging would leave the total announcing how many were withheld, which
    /// is exactly the number that must not be published — so the candidates are bounded instead
    /// and the bound is stated rather than silent.
    /// </para>
    /// </summary>
    public const int MaxExtentCandidates = 3000;

    /// <summary>
    /// Narrows a photograph query by what the caller asked about.
    /// </summary>
    /// <remarks>
    /// Every condition is a term of the same visibility-filtered query rather than a pass over
    /// its results, so naming an object the caller may not read is indistinguishable from naming
    /// one that is not there. That is what keeps the filters from becoming a way to test whether
    /// something exists — asking "photographs of cave X" when X is invisible answers empty, not
    /// forbidden.
    /// </remarks>
    public static IQueryable<Document> Narrow(
        this IQueryable<Document> photographs, SilexGisDbContext db, PhotoQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Every condition below that talks about the picture's bytes is written as the same
        // nested existence test, spelled out rather than factored into a helper: a method call
        // inside an expression tree is not something the database can be asked, and one that
        // silently ran in memory instead would fetch the whole archive to filter it.
        if (query.CaveId is { } caveId)
        {
            // The cave and everything below it, matched on the stored ancestry rather than by
            // walking the containment tree: a cave's pictures include its entrances'.
            photographs = photographs.Where(d => db.Attachments.Any(a =>
                a.FeatureId != null
                && db.FeatureAncestors.Any(fa => fa.FeatureId == a.FeatureId && fa.AncestorId == caveId)
                && db.StoredFiles.Any(f => f.Id == a.FileId
                    && db.DocumentVersions.Any(v =>
                        v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id))));
        }

        if (query.FeatureId is { } featureId)
        {
            photographs = photographs.Where(d => db.Attachments.Any(a =>
                a.FeatureId == featureId
                && db.StoredFiles.Any(f => f.Id == a.FileId
                    && db.DocumentVersions.Any(v =>
                        v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id))));
        }

        if (query.TripLogId is { } tripId)
        {
            photographs = photographs.AttachedToTrip(db, tripId);
        }

        if (query.CaverId is { } caverId)
        {
            photographs = photographs.Where(d =>
                db.PhotoDetails.Any(p => p.DocumentId == d.Id && p.PhotographerCaverId == caverId));
        }

        if (query.TagId is { } tagId)
        {
            // Tags sit on the file, where every other tag in this application sits.
            photographs = photographs.Where(d => db.Taggings.Any(t =>
                t.TagId == tagId
                && t.EntityType == AttachedEntityType.StoredFile
                && db.StoredFiles.Any(f => f.Id == t.EntityId
                    && db.DocumentVersions.Any(v =>
                        v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id))));
        }

        if (query.AlbumId is { } albumId)
        {
            photographs = photographs.Where(d =>
                db.AlbumItems.Any(i => i.AlbumId == albumId && i.DocumentId == d.Id));
        }

        if (query.UploadBatchId is { } batchId)
        {
            photographs = photographs.Where(d => d.UploadBatchId == batchId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            photographs = photographs.Where(d =>
                EF.Functions.ILike(d.Title, term)
                || db.PhotoDetails.Any(p => p.DocumentId == d.Id
                    && (EF.Functions.ILike(p.Caption ?? string.Empty, term)
                        || EF.Functions.ILike(p.PlaceName ?? string.Empty, term))));
        }

        if (!string.IsNullOrWhiteSpace(query.Camera))
        {
            // Read out of the metadata bag the picture's own facts live in. Not indexed and not
            // meant to be: a camera filter is chosen from the cameras this archive actually
            // holds, so the candidate set is already narrow by the time it is applied.
            var pattern = $"%{query.Camera.Trim()}%";
            photographs = photographs.Where(d => db.StoredFiles.Any(f =>
                EF.Functions.ILike(f.Metadata, pattern)
                && db.DocumentVersions.Any(v =>
                    v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));
        }

        if (query.From is { } from)
        {
            var at = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            photographs = photographs.Where(d => db.StoredFiles.Any(f =>
                (f.ContentCreatedAt ?? f.CreatedAt) >= at
                && db.DocumentVersions.Any(v =>
                    v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));
        }

        if (query.To is { } to)
        {
            var at = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            photographs = photographs.Where(d => db.StoredFiles.Any(f =>
                (f.ContentCreatedAt ?? f.CreatedAt) < at
                && db.DocumentVersions.Any(v =>
                    v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));
        }

        if (query.Unplaced == true)
        {
            photographs = photographs.Where(d => db.StoredFiles.Any(f =>
                f.Geom == null
                && db.DocumentVersions.Any(v =>
                    v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));
        }

        return photographs;
    }

    /// <summary>
    /// The photographs inside a map extent whose position this caller may actually be told.
    /// </summary>
    /// <remarks>
    /// The two conditions are separate and a picture needs both: its point has to be in the box,
    /// and this caller has to be allowed to know where it was taken. The second is the shared
    /// rule about a capture point, asked here exactly as the map layer asks it — a gallery whose
    /// map mode showed a pin the map itself withholds would be a way round the map.
    /// </remarks>
    public static async Task<IReadOnlyCollection<Guid>> InExtentAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        PhotoPositionDisclosure disclosure,
        IQueryable<Document> photographs,
        Geometry extent,
        CancellationToken ct)
    {
        var candidates = await (from file in db.StoredFiles.AsNoTracking()
                                join version in db.DocumentVersions.AsNoTracking()
                                    on file.DocumentVersionId equals version.Id
                                where version.IsCurrent
                                    && file.Geom != null
                                    && file.Geom!.Intersects(extent)
                                    && photographs.Any(d => d.Id == version.DocumentId)
                                orderby file.CreatedAt descending, file.Id
                                select new { file.Id, version.DocumentId })
            .Take(MaxExtentCandidates)
            .ToListAsync(ct);

        var disclosable = await disclosure.DisclosableIdsAsync(
            ctx, [.. candidates.Select(c => c.Id)], ct);

        return [.. candidates.Where(c => disclosable.Contains(c.Id)).Select(c => c.DocumentId)];
    }

}
