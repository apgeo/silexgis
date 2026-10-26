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
/// The raster maps of the caves a set carries, read in pages like the features themselves, each map
/// with the places pinned on it.
/// </summary>
/// <remarks>
/// <para>
/// A map is carried when its cave is one the feature stream would hand this reader right now
/// (inside the set, readable, and not withheld). A map of a withheld cave is absent, not stubbed: a
/// map names its cave, and a row hanging on a withheld cave says where to look for it.
/// </para>
/// <para>
/// A pin names a place, and on a picture that may one day be laid on the ground a pixel is a
/// position. So a pin of a place the set does not hand this reader is left out of the map, which
/// is otherwise complete. Both questions are put to the one answer every stream reads against,
/// never decided here. The picture itself is a document, and reaches a phone through the documents
/// stream under the rule that governs every photograph.
/// </para>
/// </remarks>
public static class SyncRasterEndpoints
{
    /// <summary>
    /// A map changed when it was itself last written, when any of its pins was — added, moved or
    /// removed — or when its cave was, whichever is latest. The pins' time is what tells a phone
    /// holding the map that its list of pins moved, since the list is replaced wholesale. The
    /// cave's time carries a map to a phone that already read past it when the cave comes into what
    /// the set hands this reader through a change to its own row alone.
    /// </summary>
    private static readonly SyncStreamOrder<SyncRasterRow> ByChange = new(
        r => r.Map.UpdatedAt > r.PinsChangedAt
            ? (r.Map.UpdatedAt > r.CaveUpdatedAt ? r.Map.UpdatedAt : r.CaveUpdatedAt)
            : (r.PinsChangedAt > r.CaveUpdatedAt ? r.PinsChangedAt : r.CaveUpdatedAt),
        r => r.Map.Id);

    private static readonly SyncStreamOrder<SyncRasterGone> ByGone = new(g => g.At, g => g.Id);

    public static void MapSyncRasterEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/rasters", RastersAsync)
            .WithName("syncRasters")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the raster maps of a sync set's caves, and the maps that have gone.");
    }

    private static async Task<Results<Ok<SyncRastersPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        RastersAsync(
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
            db, ctx, SyncStream.Rasters, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        if (opening.Roots.Count == 0)
        {
            return TypedResults.Ok(new SyncRastersPageDto(
                opening.Set.Revision, opening.Settings, [], [], opening.Cursor, false));
        }

        var handed = await SyncScope.HandedAsync(db, protection, ctx, opening.Roots, ct);

        // The pins' latest change is read over removed pins too — that a pin went is a change to its
        // map — and over nothing else: a pin of a map that went is hidden with the map.
        var live =
            from map in db.RasterMaps.AsNoTracking()
            join cave in handed on map.CaveFeatureId equals cave.Id
            let pinsAt = db.RasterPins
                .Where(p => p.RasterMapId == map.Id)
                .Max(p => (DateTimeOffset?)p.UpdatedAt) ?? map.UpdatedAt
            // The pinned places' own times: a pin whose place comes into (or goes out of) what the
            // set hands this reader through a change to the place's own row alone is a change to the
            // map's list, and the pins' times do not move with it.
            let placesAt = (from p in db.RasterPins
                            where p.RasterMapId == map.Id && p.DeletedAt == null
                            join f in db.Features on p.PlaceFeatureId equals f.Id
                            select (DateTimeOffset?)f.UpdatedAt).Max()
            select new SyncRasterRow
            {
                Map = map,
                CaveUpdatedAt = cave.UpdatedAt,
                PinsChangedAt = placesAt != null && placesAt > pinsAt ? placesAt.Value : pinsAt,
            };
        live = SyncStreamPaging.After(live, ByChange, opening.From);
        var liveRows = await SyncStreamPaging.Probe(live, ByChange, opening.Size).ToListAsync(ct);

        var goneRows = SyncStreamPaging.Probe(
            SyncStreamPaging.After((await GoneAsync(db, handed, opening.From, ct)).AsQueryable(), ByGone, opening.From),
            ByGone,
            opening.Size).ToList();

        var cut = SyncStreamPaging.Cut(opening, liveRows.Select(ByChange.Entry), goneRows.Select(ByGone.Entry));
        var rows = cut.Rows.OfType<SyncRasterRow>().ToList();
        var pins = await PinsAsync(db, handed, [.. rows.Select(r => r.Map.Id)], ct);

        // The area a map hangs in is a feature of its own: it is named only to a reader the set
        // hands it to, and a map of a withheld area is otherwise complete.
        var areaIds = rows.Where(r => r.Map.CaveAreaFeatureId is not null)
            .Select(r => r.Map.CaveAreaFeatureId!.Value).Distinct().ToList();
        var handedAreas = areaIds.Count == 0
            ? []
            : (await handed.Where(f => areaIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        return TypedResults.Ok(new SyncRastersPageDto(
            opening.Set.Revision,
            opening.Settings,
            [.. rows.Select(r => new SyncRasterMapDto(
                r.Map.Id,
                r.Map.CaveFeatureId,
                r.Map.CaveAreaFeatureId is { } area && handedAreas.Contains(area) ? area : null,
                r.Map.Title,
                r.Map.MapType,
                r.Map.SortOrder,
                r.Map.DocumentId,
                r.Map.UpdatedAt,
                pins.GetValueOrDefault(r.Map.Id) ?? []))],
            [.. cut.Rows.OfType<SyncRasterGone>().Select(g => new SyncTombstoneDto(g.Id, g.At))],
            cut.NextCursor,
            cut.HasMore));
    }

    /// <summary>
    /// The maps that have gone after the position, in caves the set hands this reader, each stamped
    /// with the moment it went.
    /// </summary>
    /// <remarks>
    /// Asked in two statements, for the reason the trips stream's removals are: reading a removed
    /// map means looking past the filter that hides removed rows, and that switch cannot be thrown
    /// for one table of a statement — it would lift the same filter off the features the maps are
    /// restricted to, and a deleted cave would count as handed.
    /// </remarks>
    private static async Task<List<SyncRasterGone>> GoneAsync(
        SilexGisDbContext db,
        IQueryable<Feature> handed,
        SyncCursor? from,
        CancellationToken ct)
    {
        var removed = db.RasterMaps.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.DeletedAt != null)
            .Select(m => new SyncRasterGone { Id = m.Id, At = m.DeletedAt!.Value, CaveId = m.CaveFeatureId });
        var candidates = await SyncStreamPaging.After(removed, ByGone, from).ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var caveIds = candidates.Select(c => c.CaveId).Distinct().ToList();
        var handedCaves = await handed.Where(f => caveIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        return [.. candidates.Where(c => handedCaves.Contains(c.CaveId))];
    }

    /// <summary>
    /// The live pins of the page's maps that this reader may be given, per map. A pin of a place the
    /// set does not hand this reader is left out — asked of the same answer the maps' caves were,
    /// for every place the page names at once.
    /// </summary>
    private static async Task<Dictionary<Guid, List<SyncRasterPinDto>>> PinsAsync(
        SilexGisDbContext db,
        IQueryable<Feature> handed,
        IReadOnlyList<Guid> mapIds,
        CancellationToken ct)
    {
        if (mapIds.Count == 0)
        {
            return [];
        }

        var pins = await db.RasterPins.AsNoTracking()
            .Where(p => mapIds.Contains(p.RasterMapId) && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var placeIds = pins.Select(p => p.PlaceFeatureId).Distinct().ToList();
        var handedPlaces = placeIds.Count == 0
            ? []
            : (await handed.Where(f => placeIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        return pins
            .Where(p => handedPlaces.Contains(p.PlaceFeatureId))
            .GroupBy(p => p.RasterMapId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new SyncRasterPinDto(p.Id, p.PlaceFeatureId, p.X, p.Y, p.UpdatedAt)).ToList());
    }
}

/// <summary>One map of the rasters stream before it is cut, with the times its order is made of.</summary>
internal sealed class SyncRasterRow
{
    public required RasterMap Map { get; init; }

    /// <summary>When the map's cave last changed.</summary>
    public required DateTimeOffset CaveUpdatedAt { get; init; }

    /// <summary>When any of the map's pins last changed, or the map's own time if it has none.</summary>
    public required DateTimeOffset PinsChangedAt { get; init; }
}

/// <summary>A map that has gone, as the rasters stream tells it.</summary>
internal sealed class SyncRasterGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    public Guid CaveId { get; init; }
}
