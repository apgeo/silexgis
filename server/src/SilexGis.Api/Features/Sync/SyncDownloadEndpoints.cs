// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The read half of the mobile contract: what one device may carry away, in pages it can stop
/// and resume, and the record of what has gone since it last asked.
/// </summary>
public static class SyncDownloadEndpoints
{
    /// <summary>
    /// The feature stream's order: a row changed when it went if it has gone, and when it was last
    /// written otherwise, ties broken by identifier.
    /// </summary>
    private static readonly SyncStreamOrder<Feature> ByChange = new(f => f.DeletedAt ?? f.UpdatedAt, f => f.Id);

    public static void MapSyncDownloadEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/download", DownloadAsync)
            .WithName("syncDownload")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the features a sync set carries, and the rows that have gone.");
    }

    private static async Task<Results<Ok<SyncDownloadPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        DownloadAsync(
            Guid id,
            string? cursor,
            int? pageSize,
            SilexGisDbContext db,
            FeatureProtection protection,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (opening, problem) = await SyncStreamPaging.OpenAsync(
            db, ctx, SyncStream.Features, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        if (opening.Roots.Count == 0)
        {
            return TypedResults.Ok(new SyncDownloadPageDto(
                opening.Set.Revision, opening.Settings, [], [], opening.Cursor, false));
        }

        var live = SyncScope.Readable(db, ctx, opening.Roots);
        var gone = SyncScope.Gone(db, ctx, opening.Roots);

        // Both halves move on one key. A row's change time is when it went if it has gone and when
        // it was last written otherwise: a delete is stamped straight onto the column without
        // touching the update time, so a cursor that read the update time alone would hand a device
        // tombstones behind its own watermark and never deliver a single one of them.
        live = SyncStreamPaging.After(live, ByChange, opening.From);
        gone = SyncStreamPaging.After(gone, ByChange, opening.From);

        // What this caller may not be given, folded back into the query before the page is
        // ordered, cut or counted. The live half only: the tombstone half withholds nothing, for
        // the reason given where it is defined.
        live = await SyncScope.WithoutWithheldAsync(live, protection, ctx, ct);

        var liveRows = await SyncStreamPaging.Probe(live, ByChange, opening.Size).ToListAsync(ct);
        var goneRows = await SyncStreamPaging.Probe(gone, ByChange, opening.Size)
            .Select(f => new SyncTombstoneDto(f.Id, f.DeletedAt!.Value))
            .ToListAsync(ct);

        var cut = SyncStreamPaging.Cut(
            opening,
            liveRows.Select(ByChange.Entry),
            goneRows.Select(t => new SyncStreamEntry(t.DeletedAt, t.Id, t)));

        // Parents are named only where the caller may read them, whether or not they are in the set.
        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        return TypedResults.Ok(new SyncDownloadPageDto(
            opening.Set.Revision,
            opening.Settings,
            await SyncFeatureShaping.ToDtosAsync(db, visible, [.. cut.Rows.OfType<Feature>()], ct),
            [.. cut.Rows.OfType<SyncTombstoneDto>()],
            cut.NextCursor,
            cut.HasMore));
    }
}
