// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
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
    /// The page a device gets when it does not ask for a size. Well under the announced ceiling:
    /// a first sync happens on whatever connection a car park has, and a page that has to be
    /// retried whole is cheaper to retry small.
    /// </summary>
    /// <remarks>
    /// A page is a count of rows and not a budget of bytes, and the two are not close: a selection
    /// carries everything contained in its roots, so a synced cave brings its surveyed centerline,
    /// whose geometry is the whole survey as a multi-line string and can be larger on its own than
    /// the other ninety-nine rows together. Nothing here bounds that, which is why the size is a
    /// request a device is expected to lower rather than a promise that a page is small.
    /// </remarks>
    public const int DefaultPageSize = 100;

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

        // A sync set has exactly one reader, its owner, and a set somebody else owns is answered
        // like one that is not there — the same rule the rest of this slice keeps.
        var set = await db.SyncSets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ctx.UserId, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("sync.set_not_found");
        }

        SyncCursor? from = null;
        if (cursor is not null)
        {
            if (!SyncCursor.TryDecode(cursor, out var decoded))
            {
                return ApiProblems.BadRequest(
                    "sync.cursor_invalid", "That resume position was not issued by this server.");
            }

            // A resume position means "everything in this selection, after here". Change the
            // selection and the second half stops describing the first: a cave added to the set
            // moves no feature row, so nothing under it is ever after a caught-up device's
            // position and the whole cave would stay invisible until an unrelated edit happened
            // to touch it. The device is told to drop the position rather than handed a page that
            // silently omits what it just asked to carry.
            if (decoded.SetRevision != set.Revision)
            {
                return ApiProblems.Conflict(
                    "sync.cursor_stale",
                    "The selection changed after that resume position was issued. "
                    + "Drop the cursor and read the set from the beginning.");
            }

            from = decoded;
        }

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, options.Value.ResolvedPageSizeMax);
        var settings = JsonSerializer.Deserialize<JsonElement>(set.Settings);

        var roots = await db.SyncSetMembers.AsNoTracking()
            .Where(m => m.SyncSetId == set.Id)
            .Select(m => m.RootFeatureId)
            .ToListAsync(ct);
        if (roots.Count == 0)
        {
            return TypedResults.Ok(new SyncDownloadPageDto(set.Revision, settings, [], [], cursor, false));
        }

        // Membership names roots; what is in the set is everything contained in one of them, read
        // off the row's own ancestry rather than walked per request. The array carries the row
        // itself, so a root is in its own set.
        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        var live = visible.Where(f => f.AncestorIds.Any(a => roots.Contains(a)));

        // The one place this endpoint looks past the filter that hides deleted rows, and the
        // reason it does: a delete that never reaches a device is a delete that undoes itself the
        // next time the device uploads, putting back the cave the club removed. The carve-out is
        // kept as narrow as it can be — a separate query, restricted to rows that are actually
        // gone, projected to an identifier and a time before it leaves the database, so nothing
        // else can ride along with it.
        //
        // Its ancestry is read unfiltered, and only its ancestry. A row inherits its audience
        // from the rows containing it, so asking that question against live rows alone would ask
        // who may read a deleted cave and find that the cave itself is no longer there to answer
        // — every deletion of a whole cave would then produce no record at all, which is exactly
        // the case the tombstone exists for. Reading the ancestry unfiltered cannot widen the
        // live half in return, because deletion is stamped over the whole containment subtree:
        // nothing live sits under something deleted.
        var goneAncestry = db.Features.AsNoTracking().IgnoreQueryFilters();
        var gone = db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => f.DeletedAt != null)
            .VisibleTo(ctx, goneAncestry, db.FeatureSetMembers)
            .Where(f => f.AncestorIds.Any(a => roots.Contains(a)));

        // Both halves move on one key. A row's change time is when it went if it has gone and when
        // it was last written otherwise: a delete is stamped straight onto the column without
        // touching the update time, so a cursor that read the update time alone would hand a device
        // tombstones behind its own watermark and never deliver a single one of them.
        live = Advance(live, from);
        gone = Advance(gone, from);

        // What this caller may not be given, decided in the one place that decides it, and folded
        // back into the query before the page is ordered, cut or counted.
        //
        // The live half only. A stub is an identifier and a moment, and neither locates anything
        // — while a delete that does not propagate is permanent: a device that held the row from
        // before it was guarded keeps it for ever and puts it back on its next upload. There is
        // nothing to trade away either, because the tombstone half has already been filtered to
        // rows this account may read, and the same account reads those same rows through the rest
        // of this API, grid-snapped, whenever it likes. The stub tells it nothing it could not
        // already ask for; withholding it would only lose the deletion.
        var withheld = await SyncWithhold.WithheldIdsAsync(protection, ctx, [live], ct);
        if (withheld.Count > 0)
        {
            var withheldIds = withheld.ToArray();
            live = live.Where(f => !withheldIds.Contains(f.Id));
        }

        // One more than the page, from each half, so the merge below can see whether anything was
        // waiting behind it without asking a second question.
        var liveRows = await Order(live).Take(size + 1).ToListAsync(ct);
        var goneRows = await Order(gone)
            .Take(size + 1)
            .Select(f => new SyncTombstoneDto(f.Id, f.DeletedAt!.Value))
            .ToListAsync(ct);

        // The two halves are read separately because they are asked different questions of the
        // database, and merged here because they are one stream to the device: interleaving them
        // by the same key is what keeps a single cursor able to resume either.
        var page = liveRows.Select(f => (Key: f.DeletedAt ?? f.UpdatedAt, f.Id, Row: (object)f))
            .Concat(goneRows.Select(t => (Key: t.DeletedAt, t.Id, Row: (object)t)))
            .OrderBy(x => x.Key).ThenBy(x => x.Id)
            .ToList();
        var hasMore = page.Count > size;
        if (hasMore)
        {
            page = page[..size];
        }

        var emittedFeatures = page.Select(x => x.Row).OfType<Feature>().ToList();
        var tombstones = page.Select(x => x.Row).OfType<SyncTombstoneDto>().ToList();

        var next = page.Count > 0
            ? new SyncCursor(set.Revision, page[^1].Key, page[^1].Id).Encode()
            : cursor;

        return TypedResults.Ok(new SyncDownloadPageDto(
            set.Revision,
            settings,
            await SyncFeatureShaping.ToDtosAsync(db, visible, emittedFeatures, ct),
            tombstones,
            next,
            hasMore));
    }

    /// <summary>
    /// Restricts a half of the stream to what lies strictly after the device's last position.
    /// Compared as a pair, so two rows sharing a change time are still ordered against each other
    /// and neither is repeated nor skipped.
    /// </summary>
    private static IQueryable<Feature> Advance(IQueryable<Feature> rows, SyncCursor? from) =>
        from is not { } c
            ? rows
            : rows.Where(f =>
                (f.DeletedAt ?? f.UpdatedAt) > c.ChangedAt
                || ((f.DeletedAt ?? f.UpdatedAt) == c.ChangedAt && f.Id > c.Id));

    /// <summary>
    /// The order the cursor is defined against. Ties are broken by identifier in both halves —
    /// without it the database is free to return equal rows in any order and does, so a row could
    /// arrive on two consecutive pages and another on neither.
    /// </summary>
    private static IQueryable<Feature> Order(IQueryable<Feature> rows) =>
        rows.OrderBy(f => f.DeletedAt ?? f.UpdatedAt).ThenBy(f => f.Id);
}
