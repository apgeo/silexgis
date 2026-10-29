// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

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
    /// removed — when its picture's file was, or when its cave was, whichever is latest. The pins'
    /// time is what tells a phone holding the map that its list of pins moved, since the list is
    /// replaced wholesale. The picture's time carries a turn recorded in the browser, which changes
    /// how every rendition relates to the frame the pins are measured in and is written on the file
    /// alone. The cave's time carries a map to a phone that already read past it when the cave
    /// comes into what the set hands this reader through a change to its own row alone.
    /// </summary>
    private static readonly SyncStreamOrder<SyncRasterRow> ByChange = new(
        r => r.Map.UpdatedAt > r.ContentChangedAt
            ? (r.Map.UpdatedAt > r.CaveUpdatedAt ? r.Map.UpdatedAt : r.CaveUpdatedAt)
            : (r.ContentChangedAt > r.CaveUpdatedAt ? r.ContentChangedAt : r.CaveUpdatedAt),
        r => r.Map.Id);

    private static readonly SyncStreamOrder<SyncRasterGone> ByGone = new(g => g.At, g => g.Id);

    private static readonly SyncStreamOrder<SyncRasterPinGone> ByPinGone = new(g => g.At, g => g.Id);

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
                opening.Set.Revision, opening.Settings, [], [], [], opening.Cursor, false));
        }

        var handed = await SyncScope.HandedAsync(db, protection, ctx, opening.Roots, ct);

        // The pins' latest change is read over removed pins too — that a pin went is a change to its
        // map — and over nothing else: a pin of a map that went is hidden with the map.
        var live =
            from carried in Carried(db, handed)
            let map = carried.Map
            let pinsAt = PlacePointQueries.Rows(db)
                .Where(p => p.MapId == map.Id)
                .Max(p => (DateTimeOffset?)p.UpdatedAt) ?? map.UpdatedAt
            // The pinned places' own times: a pin whose place comes into (or goes out of) what the
            // set hands this reader through a change to the place's own row alone is a change to the
            // map's list, and the pins' times do not move with it.
            let placesAt = (from p in PlacePointQueries.Rows(db)
                            where p.MapId == map.Id && p.DeletedAt == null
                            join f in db.Features on p.PlaceId equals f.Id
                            select (DateTimeOffset?)f.UpdatedAt).Max()
            let pinsChangedAt = placesAt != null && placesAt > pinsAt ? placesAt.Value : pinsAt
            // The current picture's files: a turn recorded in the browser is written on the file,
            // and moves neither the map nor its document.
            let pictureAt = (from v in db.DocumentVersions
                             where v.DocumentId == map.DocumentId && v.IsCurrent
                             join f in db.StoredFiles on v.Id equals f.DocumentVersionId
                             select (DateTimeOffset?)f.UpdatedAt).Max()
            select new SyncRasterRow
            {
                Map = map,
                CaveUpdatedAt = carried.CaveUpdatedAt,
                ContentChangedAt = pictureAt != null && pictureAt > pinsChangedAt ? pictureAt.Value : pinsChangedAt,
            };
        live = SyncStreamPaging.After(live, ByChange, opening.From);
        var liveRows = await SyncStreamPaging.Probe(live, ByChange, opening.Size).ToListAsync(ct);

        var goneRows = SyncStreamPaging.Probe(
            SyncStreamPaging.After((await GoneAsync(db, handed, opening.From, ct)).AsQueryable(), ByGone, opening.From),
            ByGone,
            opening.Size).ToList();

        var pinsGone = PinsGone(db, handed);
        pinsGone = SyncStreamPaging.After(pinsGone, ByPinGone, opening.From);
        var pinsGoneRows = await SyncStreamPaging.Probe(pinsGone, ByPinGone, opening.Size).ToListAsync(ct);

        var cut = SyncStreamPaging.Cut(
            opening,
            liveRows.Select(ByChange.Entry),
            goneRows.Select(ByGone.Entry),
            pinsGoneRows.Select(ByPinGone.Entry));
        var maps = await ShapeAsync(db, handed, [.. cut.Rows.OfType<SyncRasterRow>().Select(r => r.Map)], ct);

        return TypedResults.Ok(new SyncRastersPageDto(
            opening.Set.Revision,
            opening.Settings,
            maps,
            [.. cut.Rows.OfType<SyncRasterGone>().Select(g => new SyncTombstoneDto(g.Id, g.At))],
            [.. cut.Rows.OfType<SyncRasterPinGone>().Select(g => new SyncRasterPinTombstoneDto(g.Id, g.MapId, g.At))],
            cut.NextCursor,
            cut.HasMore));
    }

    /// <summary>
    /// The live maps the stream carries for a reader: those of the caves in <paramref name="handed"/>.
    /// Every question about which maps, and so which of their pins, a reader is given starts here.
    /// </summary>
    internal static IQueryable<SyncCarriedMap> Carried(SilexGisDbContext db, IQueryable<Feature> handed) =>
        from map in db.RasterMaps.AsNoTracking()
        join cave in handed on map.CaveFeatureId equals cave.Id
        select new SyncCarriedMap { Map = map, CaveUpdatedAt = cave.UpdatedAt };

    /// <summary>
    /// Maps as the stream hands them to a reader, each with the pins it may be given and the area
    /// it hangs in named only when that area is handed too. <paramref name="maps"/> are maps the
    /// stream carries for that reader.
    /// </summary>
    internal static async Task<List<SyncRasterMapDto>> ShapeAsync(
        SilexGisDbContext db, IQueryable<Feature> handed, IReadOnlyList<RasterMap> maps, CancellationToken ct)
    {
        var pins = await PinsAsync(db, handed, [.. maps.Select(m => m.Id)], ct);

        // The area a map hangs in is a feature of its own: it is named only to a reader the set
        // hands it to, and a map of a withheld area is otherwise complete.
        var areaIds = maps.Where(m => m.CaveAreaFeatureId is not null)
            .Select(m => m.CaveAreaFeatureId!.Value).Distinct().ToList();
        var handedAreas = areaIds.Count == 0
            ? []
            : (await handed.Where(f => areaIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        // The pins' frame: the current picture's size as the file was read when it was stored —
        // or, for a picture stored before that was recorded, as its camera profile states it.
        // A revision may hold more than one file (a converted copy beside the original); the
        // first stored is the original, as every listing of a photograph takes it, and it is
        // also the file the address route draws renditions from — so the turn told here is the
        // one on that file, and the frame it turns from is that file's own.
        var documentIds = maps.Select(m => m.DocumentId).Distinct().ToList();
        var pictures = documentIds.Count == 0
            ? []
            : (await (from version in db.DocumentVersions.AsNoTracking()
                      where version.IsCurrent && documentIds.Contains(version.DocumentId)
                      join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                      select new
                      {
                          version.DocumentId,
                          file.Id,
                          file.CreatedAt,
                          file.Metadata,
                          file.PixelWidth,
                          file.PixelHeight,
                          file.PixelOrientation,
                          file.OrientationQuarterTurns,
                      })
                    .ToListAsync(ct))
                .GroupBy(f => f.DocumentId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var original = g.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).First();
                        var stated = PhotoExif.FromMetadata(original.Metadata);
                        var width = original.PixelWidth ?? stated.WidthPixels;
                        var height = original.PixelHeight ?? stated.HeightPixels;
                        if (width is null)
                        {
                            return SyncPicture.Unread;
                        }

                        // The code the renderer obeys, read at write; for an older file, the code
                        // its camera profile states, on the same terms as its size.
                        var (mirrored, turn) = PhotoOrientation.Upright(
                            original.PixelOrientation ?? stated.Orientation, original.OrientationQuarterTurns);
                        return new SyncPicture(width, height, mirrored, turn);
                    });

        return [.. maps.Select(m =>
        {
            var picture = pictures.GetValueOrDefault(m.DocumentId) ?? SyncPicture.Unread;
            return new SyncRasterMapDto(
                m.Id,
                m.CaveFeatureId,
                m.CaveAreaFeatureId is { } area && handedAreas.Contains(area) ? area : null,
                m.Title,
                m.MapType,
                m.SortOrder,
                m.DocumentId,
                picture.Width,
                picture.Height,
                picture.Mirrored,
                picture.Turn,
                m.UpdatedAt,
                pins.GetValueOrDefault(m.Id) ?? []);
        })];
    }

    /// <summary>
    /// A map's picture as the stream describes it: its stored size, and how its renditions are drawn
    /// from that frame. All four are null together when the size could not be read.
    /// </summary>
    private sealed record SyncPicture(int? Width, int? Height, bool? Mirrored, int? Turn)
    {
        public static readonly SyncPicture Unread = new(null, null, null, null);
    }

    /// <summary>
    /// The pins removed from maps the stream carries, each stamped with the moment it went — only
    /// those of places the reader is handed now, which are exactly the pins it would be given
    /// were they live.
    /// </summary>
    /// <remarks>
    /// A removal of a pin whose place is withheld stays silent, as the pin itself does: naming it
    /// would say a pin stood there. A pin of a map that went is not repeated here — the map's own
    /// tombstone removes its pins with it, and a removed map is not carried.
    /// </remarks>
    private static IQueryable<SyncRasterPinGone> PinsGone(SilexGisDbContext db, IQueryable<Feature> handed)
    {
        var handedIds = handed.Select(f => f.Id);
        return
            from pin in PlacePointQueries.Rows(db).AsNoTracking()
            where pin.DeletedAt != null
            join carried in Carried(db, handed) on pin.MapId equals carried.Map.Id
            where handedIds.Contains(pin.PlaceId)
            select new SyncRasterPinGone { Id = pin.Id, MapId = pin.MapId, At = pin.DeletedAt!.Value };
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
    internal static async Task<Dictionary<Guid, List<SyncRasterPinDto>>> PinsAsync(
        SilexGisDbContext db,
        IQueryable<Feature> handed,
        IReadOnlyList<Guid> mapIds,
        CancellationToken ct)
    {
        if (mapIds.Count == 0)
        {
            return [];
        }

        var pins = await PlacePointQueries.Rows(db).AsNoTracking()
            .Where(p => mapIds.Contains(p.MapId) && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var placeIds = pins.Select(p => p.PlaceId).Distinct().ToList();
        var handedPlaces = placeIds.Count == 0
            ? []
            : (await handed.Where(f => placeIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        // The pixel the phone sent is read back as it was stored — the stored-frame pixel the pin
        // carries, never one worked back from the fraction the browser draws it at.
        return pins
            .Where(p => handedPlaces.Contains(p.PlaceId))
            .Select(p => (Row: p, Pixel: PlacePointQueries.Pixel(p)))
            .Where(p => p.Pixel is not null)
            .GroupBy(p => p.Row.MapId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new SyncRasterPinDto(p.Row.Id, p.Row.PlaceId, p.Pixel!.Value.X, p.Pixel.Value.Y, p.Row.UpdatedAt)).ToList());
    }
}

/// <summary>One map of the rasters stream before it is cut, with the times its order is made of.</summary>
internal sealed class SyncRasterRow
{
    public required RasterMap Map { get; init; }

    /// <summary>When the map's cave last changed.</summary>
    public required DateTimeOffset CaveUpdatedAt { get; init; }

    /// <summary>
    /// When any of the map's pins, the places they pin or its current picture's files last changed,
    /// or the map's own time if none of them is later.
    /// </summary>
    public required DateTimeOffset ContentChangedAt { get; init; }
}

/// <summary>A live map the rasters stream carries for a reader, with its cave's last change.</summary>
internal sealed class SyncCarriedMap
{
    public required RasterMap Map { get; init; }

    public required DateTimeOffset CaveUpdatedAt { get; init; }
}

/// <summary>A pin removed from a map the rasters stream carries, as the stream tells it.</summary>
internal sealed class SyncRasterPinGone
{
    public Guid Id { get; init; }

    public Guid MapId { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>A map that has gone, as the rasters stream tells it.</summary>
internal sealed class SyncRasterGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    public Guid CaveId { get; init; }
}
