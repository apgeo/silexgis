// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Export;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Flybys;

/// <summary>
/// A flyby of the 3D scene, handed back as a render bundle: a zip its asker unpacks and renders
/// with Blender on a machine of their own.
///
/// <para>
/// <b>What the bundle holds of a cave is decided here, per cave, and nowhere else.</b> The request
/// names caves by id and carries no geometry; every mesh in the answer is read by this route for
/// the account asking. A cave is drawn only when that account may read it and may place it exactly
/// — the rule a cave's survey models and its walls in the 3D scene are read by — because walls and
/// survey lines are a measured drawing of where the passage runs. A cave that fails either half is
/// simply not drawn, and so is one that does not exist and one with no survey: the answer gives one
/// count of caves not drawn and no reason, so it cannot be used to find out which caves are being
/// kept from whom.
/// </para>
///
/// <para>
/// <b>The bundle says how caves lie, not where.</b> Its frame is metres about the flyby's first
/// subject, with a zero origin and no coordinate system; no terrain and no map go into it. The
/// origin, like the camera path, is the asker's own number and is trusted as far as that: it can
/// move the scene, and it cannot make this route draw a cave the asker may not place.
/// </para>
///
/// <para>
/// <b>What the scene points out is decided the same way, thing by thing.</b> An entrance or a
/// feature is named by id and drawn as a marker only when the account asking may read it and may
/// have its exact place — a marker stands at a distance from the caves, so one put where the
/// account is shown it approximately would say where the caves lie on the grid such places are
/// moved to. Every other outcome is one count with no reason. Photographs are not named at all:
/// the route finds, for each cave it draws, the photographs somebody linked to stations of its
/// survey, under the rules those links and those pictures are listed by for this account, and
/// puts in a rendering of each — the picture as this application shows it, without the stored
/// file's own data, a camera's position least of all. A photograph the account may not see is not
/// opened, not counted and not mentioned.
/// </para>
///
/// <para>
/// <b>It is still a copy that outlives the check that made it</b>, as a movie of the same flyby
/// is, so each one is recorded: who took it and which caves, markers and photographs went into it.
/// </para>
/// </summary>
public static class FlybyBlenderBundleEndpoints
{
    /// <summary>The frame rate asked for is not one a bundle is made at.</summary>
    public const string FrameRateCode = "flyby_bundle.frame_rate_unsupported";

    /// <summary>The flyby has more frames than one bundle holds.</summary>
    public const string TooLongCode = "flyby_bundle.too_long";

    /// <summary>Nothing of any cave asked for could be drawn.</summary>
    public const string NothingToDrawCode = "flyby_bundle.nothing_to_draw";

    /// <summary>What a record of a bundle is filed under.</summary>
    public const string AuditEntityType = "FlybyBundle";

    /// <summary>How many of the caves asked for are drawn with walls.</summary>
    public const string WallsHeader = "X-SilexGis-Bundle-Walls";

    /// <summary>How many are drawn by their survey lines alone.</summary>
    public const string LinesHeader = "X-SilexGis-Bundle-Lines";

    /// <summary>How many are not drawn at all — for whatever reason, which is not said.</summary>
    public const string NotDrawnHeader = "X-SilexGis-Bundle-Not-Drawn";

    /// <summary>How many had their walls left out and are drawn by their lines instead.</summary>
    public const string WallsLeftOutHeader = "X-SilexGis-Bundle-Walls-Left-Out";

    /// <summary>How many entrances and features stand in the scene as markers.</summary>
    public const string MarkersHeader = "X-SilexGis-Bundle-Markers";

    /// <summary>How many of the entrances and features asked for are not drawn — for whatever reason, which is not said.</summary>
    public const string MarkersNotDrawnHeader = "X-SilexGis-Bundle-Markers-Not-Drawn";

    /// <summary>How many photographs the scene shows.</summary>
    public const string PhotosHeader = "X-SilexGis-Bundle-Photos";

    /// <summary>By date: how many survey legs of the caves drawn have no date, and are there from the start.</summary>
    public const string UndatedLegsHeader = "X-SilexGis-Bundle-Undated-Legs";

    /// <summary>By date: how many caves have a date only for their whole survey, and appear whole on it.</summary>
    public const string CavesOnSurveyDateHeader = "X-SilexGis-Bundle-Caves-On-Survey-Date";

    /// <summary>By date: how many caves have no survey date at all, and are there from the start.</summary>
    public const string UndatedCavesHeader = "X-SilexGis-Bundle-Undated-Caves";

    /// <summary>
    /// The longest side of a photograph in a bundle, in pixels: enough to fill a frame of the
    /// largest movie a bundle is ordinarily made at, and a twentieth of the memory a renderer
    /// would hold for the camera's own file.
    /// </summary>
    public const int PhotoLongestSidePx = 2000;

    /// <summary>The rendering a bundle's photograph is made from: the largest this application makes.</summary>
    private const int PhotoRenderingPx = 2400;

    private const string EntranceRole = "entrance";
    private const string SurfaceFeatureRole = "surface-feature";
    private const string FeatureLayerRole = "feature-layer";

    /// <summary>
    /// One bundle is assembled at a time. A bundle holds every mesh it names in memory while it is
    /// written, up to the size this installation lets one view pull, and two of those side by side
    /// is twice that for no gain to either asker: the second waits its turn.
    /// </summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public static RouteGroupBuilder MapFlybyBlenderBundleEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/flybys/blender-bundle", DownloadAsync)
            .WithValidation<FlybyBlenderBundleRequest>()
            .WithTags("Flybys")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary(
                "A flyby of the 3D scene as a render bundle (.zip) for Blender: the camera at every "
                + "frame as sent, and the walls or survey lines of the caves named, read for the "
                + "caller. A cave the caller may not read or may not place exactly is not drawn, "
                + "and neither is one with no survey; the answer counts them together. Entrances and "
                + "features named by id become markers under the same two rules, and the photographs "
                + "linked to stations of the caves drawn go in where the caller may see them.");
        return api;
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> DownloadAsync(
        FlybyBlenderBundleRequest request,
        HttpContext http,
        FlybyBundleServices services,
        CancellationToken ct)
    {
        var made = await MakeAsync(request, services, "blender", ct);
        if (made.Unauthorized)
        {
            return TypedResults.Unauthorized();
        }

        if (made.Problem is { } problem)
        {
            return problem;
        }

        var bundle = made.Bundle!;
        http.Response.Headers[MarkersHeader] = Count(bundle.Markers);
        http.Response.Headers[MarkersNotDrawnHeader] = Count(bundle.MarkersNotDrawn);
        http.Response.Headers[PhotosHeader] = Count(bundle.Photos);
        http.Response.Headers[WallsHeader] = Count(bundle.Walls);
        http.Response.Headers[LinesHeader] = Count(bundle.Lines);
        http.Response.Headers[NotDrawnHeader] = Count(bundle.NotDrawn);
        http.Response.Headers[WallsLeftOutHeader] = Count(bundle.WallsLeftOut);
        http.Response.Headers[UndatedLegsHeader] = Count(bundle.UndatedLegs);
        http.Response.Headers[CavesOnSurveyDateHeader] = Count(bundle.CavesOnSurveyDate);
        http.Response.Headers[UndatedCavesHeader] = Count(bundle.UndatedCaves);
        return TypedResults.File(bundle.Zip, "application/zip", bundle.FileName);
    }

    /// <summary>
    /// Makes the bundle a request asks for, for the account asking, and records that it was made —
    /// the one place a bundle is made, whether it is handed to the asker or stored for a render
    /// node. <paramref name="format"/> is what the record says it was made for.
    /// </summary>
    internal static async Task<FlybyBundleOutcome> MakeAsync(
        FlybyBlenderBundleRequest request,
        FlybyBundleServices services,
        string format,
        CancellationToken ct)
    {
        var db = services.Db;
        var protection = services.Protection;
        var fileStore = services.FileStore;
        var access = services.Access;
        var placePoints = services.PlacePoints;
        var placeStations = services.PlaceStations;
        var thumbnails = services.Thumbnails;
        var currentUser = services.CurrentUser;
        var mapOptions = services.MapOptions;
        var loggers = services.Loggers;
        var ctx = await services.AccessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return FlybyBundleOutcome.NotSignedIn;
        }

        var asked = request.Caves!;
        var placement = request.Placement!;

        // Readable first, then the exact-location rule over exactly those ids: the order the wall
        // meshes of a view are decided in. The exact-view rule answers only about position and
        // reads past every visibility filter, so asked first it would pass a private cave nobody
        // protects.
        var askedIds = asked.Select(c => c.CaveId).ToList();
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => askedIds.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .Select(f => f.Id)
            .ToListAsync(ct);
        var placeable = await protection.ExactViewIdsAsync(ctx, readable, ct);
        var open = readable.Where(placeable.Contains).ToHashSet();

        await OneAtATime.WaitAsync(ct);
        try
        {
            var wallRows = (await CaveWallMeshSql.ForCavesAsync(
                    db, ctx, [.. asked.Where(c => c.Walls && open.Contains(c.CaveId)).Select(c => c.CaveId)], ct))
                .ToDictionary(row => row.CaveId);

            var budget = mapOptions.Value.MeshesInViewMaxBytesLimit;
            long spent = 0;
            var caves = new List<FlybyBundleCave>();
            var drawnAt = new Dictionary<Guid, int>();
            var withWalls = new List<Guid>();
            var withLines = new List<Guid>();
            var notDrawn = 0;
            var wallsUnavailable = 0;
            var wallsOverBudget = 0;

            foreach (var cave in asked)
            {
                if (!open.Contains(cave.CaveId))
                {
                    notDrawn++;
                    continue;
                }

                FlybyBundleMesh? walls = null;
                FlybyBundleMesh? estimated = null;
                (double R, double G, double B)? estimatedColor = null;
                var wallsFellBack = false;
                var overBudget = false;
                if (cave.Walls)
                {
                    if (!wallRows.TryGetValue(cave.CaveId, out var row))
                    {
                        wallsFellBack = true;
                        wallsUnavailable++;
                    }
                    else if (row.SizeBytes > budget - spent)
                    {
                        wallsFellBack = true;
                        overBudget = true;
                        wallsOverBudget++;
                    }
                    else
                    {
                        spent += row.SizeBytes;
                        var parts = await ReadWallsAsync(fileStore, row, loggers, ct);
                        foreach (var part in parts)
                        {
                            var mesh = new FlybyBundleMesh(part.Mesh, row.AnchorLongitude, row.AnchorLatitude, row.AnchorHeightM);
                            if (part.Name == GlbWriter.EstimatedNodeName)
                            {
                                estimated = mesh;
                                estimatedColor = part.Color;
                            }
                            else
                            {
                                walls ??= mesh;
                            }
                        }

                        if (walls is null && estimated is null)
                        {
                            wallsFellBack = true;
                            wallsUnavailable++;
                        }
                    }
                }

                // Lines where the look shows them, and wherever walls were wanted and are not
                // there: a cave asked for is not left as a hole in the scene for want of a mesh.
                // A cave that appears part by part is measured along its survey whether or not its
                // lines are drawn.
                var linesDrawn = request.Look!.Lines || !cave.Walls || wallsFellBack;
                var surveyed = linesDrawn || request.Reveal is not null
                    ? (await SurveySegmentSource.ForCaveAsync(db, ctx, cave.CaveId, ct)).Segments
                        .Where(s => !s.IsDuplicate)
                        .ToList()
                    : [];
                var segments = linesDrawn ? surveyed : [];

                if (walls is null && estimated is null && segments.Count == 0)
                {
                    // Counted with the caves the caller may not have, and worded with them.
                    notDrawn++;
                    if (overBudget)
                    {
                        wallsOverBudget--;
                    }
                    else if (wallsFellBack)
                    {
                        wallsUnavailable--;
                    }

                    continue;
                }

                // What the scene hung this cave from: the number it told, else the top of the
                // lines as read here, else the walls' own zero — the order the scene itself falls
                // back in.
                double? linesTop = segments.Any(s => s.MidZM is not null)
                    ? segments.Where(s => s.MidZM is not null).Max(s => s.MidZM!.Value + Math.Abs((s.DeltaZM ?? 0) / 2))
                    : null;
                var top = cave.SurveyTopM ?? linesTop ?? (walls ?? estimated)?.AnchorAltitudeM ?? 0;
                var shift = SceneAltitude.DrawnShift(placement.Absolute, placement.OffsetM, top);

                // A leg with no altitudes is a plan, and is laid where the scene laid it: on the ground.
                var flat = (cave.GroundM ?? 0) - shift;
                FlybyBundleLeg Leg(SurveySegmentRow s) =>
                    (s.MidZM is { } mid
                        ? new FlybyBundleLeg(
                            s.FromLongitude, s.FromLatitude, mid - ((s.DeltaZM ?? 0) / 2),
                            s.ToLongitude, s.ToLatitude, mid + ((s.DeltaZM ?? 0) / 2))
                        : new FlybyBundleLeg(s.FromLongitude, s.FromLatitude, flat, s.ToLongitude, s.ToLatitude, flat))
                    with { FromStation = s.FromStationName, ToStation = s.ToStationName, SurveyedOn = s.SurveyedOn };

                var legs = segments.Select(Leg).ToList();

                // Where a reveal starts: the stations the survey itself flags as entrances.
                IReadOnlyList<FlybyBundleLeg> surveyLegs = request.Reveal is null ? [] : linesDrawn ? legs : [.. surveyed.Select(Leg)];
                var surveyModelIds = request.Reveal is null
                    ? []
                    : surveyed.Select(s => s.SurveyModelId).OfType<Guid>().Distinct().ToList();
                IReadOnlyList<string> entranceStations = surveyModelIds.Count == 0
                    ? []
                    : await db.SurveyStations.AsNoTracking()
                        .Where(s => surveyModelIds.Contains(s.SurveyModelId) && (s.Flags & SurveyStationFlags.Entrance) != 0)
                        .Select(s => s.Name)
                        .ToListAsync(ct);

                // By date, the one date of the survey the legs came from: what a cave none of
                // whose legs has a day of its own appears on.
                var surveyDate = request.Reveal?.By != FlybyBundleRevealBy.Date || surveyModelIds.Count == 0
                    ? null
                    : await db.SurveyModels.AsNoTracking()
                        .Where(m => surveyModelIds.Contains(m.Id) && m.SurveyedAt != null)
                        .MinAsync(m => m.SurveyedAt, ct);

                // A colour of this cave's own, where the flyby gives it one; and lines that are all
                // there is of a cave — because its walls were wanted and are not there, or because
                // the look draws no lines at all — are shown and hidden as the walls are.
                var apart = request.Changes?.Caves?.FirstOrDefault(c => c.CaveId == cave.CaveId);
                drawnAt[cave.CaveId] = caves.Count;
                caves.Add(new FlybyBundleCave(walls, estimated, estimatedColor, legs, shift)
                {
                    SurveyLegs = surveyLegs,
                    EntranceStations = entranceStations,
                    SurveyDate = surveyDate,
                    WallsTint = apart?.Walls is { } wallsTint ? Tints(wallsTint) : null,
                    LinesTint = apart?.Lines is { } linesTint ? Tints(linesTint) : null,
                    LinesStandIn = walls is null && estimated is null && (wallsFellBack || !request.Look.Lines),
                });
                (walls is not null || estimated is not null ? withWalls : withLines).Add(cave.CaveId);
            }

            if (caves.Count == 0)
            {
                return new FlybyBundleOutcome(ApiProblems.UnprocessableContent(
                    NothingToDrawCode,
                    "Nothing of the caves asked for is available to draw, so there is no scene to make a bundle of."));
            }

            var poses = request.Frames!.Select(f => new FlybyBundlePose(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7])).ToList();

            // ---- what the scene points out ----
            var (markerLayers, markerIds, markersNotDrawn) = await MarkersAsync(request.Markers ?? [], db, ctx, protection, ct);
            var (photos, photoDocumentIds) = await PhotosAsync(
                request.Photos, drawnAt, caves, poses, db, access, placePoints, placeStations, thumbnails, ctx, loggers, ct);

            // The bundle's notes, in the asker's language where they exist in it.
            var romanian = FlybyBundleNotes.IsRomanian(request.Language);
            var leftOut = (request.LeftOut ?? [])
                .Distinct()
                .Order()
                .Select(omission => FlybyBundleOmissions.Sentence(omission, romanian))
                .ToList();
            if (wallsUnavailable > 0)
            {
                leftOut.Add(FlybyBundleOmissions.WallsNotAvailable(wallsUnavailable, romanian));
            }

            if (wallsOverBudget > 0)
            {
                leftOut.Add(FlybyBundleOmissions.WallsOverBudget(wallsOverBudget, romanian));
            }

            if (notDrawn > 0)
            {
                leftOut.Add(FlybyBundleOmissions.CavesNotDrawn(notDrawn, romanian));
            }

            if (markersNotDrawn > 0)
            {
                leftOut.Add(FlybyBundleOmissions.MarkersNotDrawn(markersNotDrawn, romanian));
            }

            var scene = new FlybyBundleScene(
                request.Name?.Trim() ?? string.Empty,
                request.Output!.Width,
                request.Output.Height,
                (int)request.Output.Fps,
                request.Output.Quality!.Value,
                request.Origin!.Longitude,
                request.Origin.Latitude,
                request.Origin.HeightM,
                request.Relief,
                poses,
                caves,
                new FlybyBundleLook(
                    request.Look!.Sky,
                    request.Look.LineColour,
                    [.. (request.Look.DepthBands ?? []).Select(b => new FlybyBundleDepthBand(b.FromM, b.Colour))]),
                Texts(request.Texts),
                leftOut)
            {
                Changes = Changes(request.Changes),
                Language = request.Language,
                MarkerLayers = markerLayers,
                Photos = photos,
                Reveal = request.Reveal is { } reveal
                    ? new FlybyBundleReveal(reveal.StartS, reveal.EndS, reveal.By ?? FlybyBundleRevealBy.Distance)
                    : null,
            };

            // Written into memory first: which photographs the scene shows is known once its plan
            // is made, and the record below names them.
            using var zip = new MemoryStream();
            var summary = FlybyBlenderBundle.Write(scene, zip);

            // Recorded before the bytes are handed over, in the same request, so there is no window
            // in which somebody holds a file and the record of who took it does not exist. No
            // position is in it: which caves, and how each went in; which markers; which pictures.
            db.Set<AuditEntry>().Add(new AuditEntry
            {
                UserId = currentUser.UserId,
                Action = AuditActions.Exported,
                EntityType = AuditEntityType,
                EntityId = Guid.CreateVersion7().ToString(),
                Changes = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["format"] = format,
                    ["frames"] = scene.Poses.Count,
                    ["wallsCaveIds"] = Sorted(withWalls),
                    ["linesCaveIds"] = Sorted(withLines),
                    ["notDrawnCount"] = notDrawn,
                    ["markerFeatureIds"] = Sorted(markerIds),
                    ["markersNotDrawnCount"] = markersNotDrawn,
                    ["photoDocumentIds"] = Sorted(summary.PhotosShown.Select(index => photoDocumentIds[index]).Distinct()),
                    ["reveal"] = request.Reveal?.By switch
                    {
                        null => null,
                        FlybyBundleRevealBy.Date => "date",
                        _ => "distance",
                    },
                }),
            });
            await db.SaveChangesAsync(ct);

            return new FlybyBundleOutcome(new FlybyBundleMade(
                zip.ToArray(),
                summary.FolderName + ".zip",
                scene.Poses.Count,
                withWalls.Count,
                withLines.Count,
                notDrawn,
                wallsUnavailable + wallsOverBudget,
                summary.Markers,
                markersNotDrawn,
                summary.Photos,
                summary.RevealDates.LegsWithoutDate,
                summary.RevealDates.CavesOnTheSurveyDate,
                summary.RevealDates.CavesWithoutDate));
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>
    /// The markers of each layer asked for, read for the caller: an entrance or a feature is a
    /// marker when the caller may read it, may have its exact place, and it is a point. What its
    /// name is, where it stands and whether it is any of that are this route's to say; the request
    /// gave an id and the height of the ground.
    /// </summary>
    /// <returns>
    /// The layers with what is drawn of each, the ids drawn, and how many of the ids asked for are
    /// not — one number for every reason, as for caves.
    /// </returns>
    private static async Task<(IReadOnlyList<FlybyBundleMarkerLayer> Layers, IReadOnlyList<Guid> Drawn, int NotDrawn)> MarkersAsync(
        IReadOnlyList<FlybyBundleMarkerLayerRequest> asked,
        SilexGisDbContext db,
        AccessContext ctx,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ids = asked.SelectMany(layer => layer.Markers ?? []).Select(marker => marker.FeatureId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return ([], [], 0);
        }

        // Readable first, then the exact-location rule over exactly those ids — the order caves
        // are decided in above, for the reason given there.
        var rows = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ids.Contains(f.Id) && f.Geom != null)
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.Geom,
                CaveId = f.Kind == FeatureKind.CaveEntrance ? (Guid?)f.Entrance!.CaveFeatureId : null,
            })
            .ToListAsync(ct);
        var placeable = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(row => row.Id)], ct);
        var open = rows
            .Where(row => placeable.Contains(row.Id) && row.Geom is Point { IsEmpty: false })
            .ToDictionary(row => row.Id);

        // An entrance with no name of its own goes by its cave's, where the caller may read the
        // cave: the same fall-back, behind the same filter, as the map's own entrance layer.
        var caveIds = open.Values.Where(row => row.Name is null && row.CaveId is not null).Select(row => row.CaveId!.Value).Distinct().ToList();
        var caveNames = caveIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => caveIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Name })
                .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        var layers = new List<FlybyBundleMarkerLayer>();
        var drawn = new HashSet<Guid>();
        var notDrawn = 0;
        var chosen = 0;
        foreach (var layer in asked)
        {
            // Numbered by the request's own order, drawn or not, so what a layer is called does not
            // say how many of the layers before it had something to draw.
            var role = layer.Kind switch
            {
                FlybyBundleMarkerKind.Entrances => EntranceRole,
                FlybyBundleMarkerKind.SurfaceFeatures => SurfaceFeatureRole,
                _ => $"{FeatureLayerRole}-{(++chosen).ToString(CultureInfo.InvariantCulture)}",
            };
            var markers = new List<FlybyBundleMarker>();
            foreach (var marker in layer.Markers ?? [])
            {
                if (!open.TryGetValue(marker.FeatureId, out var row))
                {
                    notDrawn++;
                    continue;
                }

                var point = (Point)row.Geom!;
                var name = row.Name ?? (row.CaveId is { } caveId ? caveNames.GetValueOrDefault(caveId) : null);
                markers.Add(new FlybyBundleMarker(point.X, point.Y, marker.GroundM, marker.Named ? name : null));
                drawn.Add(row.Id);
            }

            if (markers.Count > 0)
            {
                layers.Add(new FlybyBundleMarkerLayer(
                    role,
                    layer.Colour,
                    markers,
                    [.. (layer.Shown ?? []).Select(k => new FlybyBundleShownKey(k.Frame, k.Shown, k.FadeFrames ?? 0))],
                    [.. (layer.Opacity ?? []).Select(k => new FlybyBundleNumberKey(k.Frame, k.Value, k.Easing ?? FlybyBundleEasing.Linear))]));
            }
        }

        return (layers, [.. drawn], notDrawn);
    }

    /// <summary>
    /// The photographs taken at stations of the caves asked for that are drawn, each as a
    /// rendering: the ones the camera comes nearest to, as many as a bundle shows.
    /// </summary>
    /// <returns>The photographs, and the document each is of, in the same order.</returns>
    private static async Task<(FlybyBundlePhotos Photos, IReadOnlyList<Guid> DocumentIds)> PhotosAsync(
        FlybyBundlePhotosRequest? asked,
        IReadOnlyDictionary<Guid, int> drawnAt,
        IReadOnlyList<FlybyBundleCave> caves,
        IReadOnlyList<FlybyBundlePose> poses,
        SilexGisDbContext db,
        IAccessService access,
        PlacePointDisclosure placePoints,
        PlaceStationDisclosure placeStations,
        ThumbnailService thumbnails,
        AccessContext ctx,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (asked?.Caves is not { Count: > 0 } wanted)
        {
            return (FlybyBundlePhotos.None, []);
        }

        // Only of caves that are drawn — which are caves this caller may read and place exactly,
        // the rule a survey's links and stations are answered by.
        var found = new List<(int Cave, StationPhotograph Photograph)>();
        var all = 0;
        foreach (var caveId in wanted)
        {
            if (!drawnAt.TryGetValue(caveId, out var index))
            {
                continue;
            }

            var (first, count) = await FlybyBundlePhotoSource.ForCaveAsync(db, access, placePoints, placeStations, ctx, caveId, ct);
            all += count;
            found.AddRange(first.Select(photograph => (index, photograph)));
        }

        // Which of them, before any is opened: the ones the camera passes nearest.
        var nearest = FlybyBlenderBundle.NearestToTheCamera(
            poses,
            [.. found.Select(each => (
                each.Photograph.Longitude,
                each.Photograph.Latitude,
                each.Photograph.AltitudeM + caves[each.Cave].AltitudeShiftM))],
            FlybyBlenderBundle.MaxPhotos);

        var photos = new List<FlybyBundlePhoto>();
        var documentIds = new List<Guid>();
        foreach (var at in nearest)
        {
            var (cave, photograph) = found[at];
            try
            {
                var (bytes, width, height) = await thumbnails.JpegAsync(
                    photograph.FileId, photograph.StoragePath, PhotoRenderingPx, PhotoLongestSidePx, ct, photograph.QuarterTurns);
                photos.Add(new FlybyBundlePhoto(cave, photograph.Longitude, photograph.Latitude, photograph.AltitudeM, bytes, width, height));
                documentIds.Add(photograph.DocumentId);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A picture that cannot be drawn is left out, as it is wherever pictures are shown.
                // Nothing logged here names a cave or a position: the file's id says which to look at.
                loggers.CreateLogger(typeof(FlybyBlenderBundleEndpoints)).LogWarning(
                    e, "The picture in file {FileId} could not be rendered for a render bundle", photograph.FileId);
            }
        }

        return (
            new FlybyBundlePhotos(
                photos,
                [.. (asked.Shown ?? []).Select(k => new FlybyBundleShownKey(k.Frame, k.Shown))])
            {
                Found = all,
            },
            documentIds);
    }

    /// <summary>
    /// The parts of a cave's wall mesh file, or none when the stored file cannot be read as one.
    /// </summary>
    /// <remarks>
    /// Nothing logged here names a cave or a position: a log is read by people the cave's record
    /// is closed to. The model's id says which file to look at.
    /// </remarks>
    private static async Task<IReadOnlyList<GlbPart>> ReadWallsAsync(
        IFileStore fileStore, CaveWallMesh row, ILoggerFactory loggers, CancellationToken ct)
    {
        try
        {
            await using var input = await fileStore.OpenReadAsync(row.StoragePath, ct);
            using var held = new MemoryStream(checked((int)Math.Min(row.SizeBytes, int.MaxValue)));
            await input.CopyToAsync(held, ct);
            return GlbReader.Read(held.GetBuffer().AsSpan(0, (int)held.Length));
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
        {
            loggers.CreateLogger(typeof(FlybyBlenderBundleEndpoints)).LogWarning(
                e, "The wall mesh of survey model {SurveyModelId} could not be read for a render bundle", row.SurveyModelId);
            return [];
        }
    }

    private static FlybyBundleTexts Texts(FlybyBundleTextsRequest? texts) => texts is null
        ? FlybyBundleTexts.None
        : new FlybyBundleTexts(
            texts.Title is { } title ? new FlybyBundleCard(title.Text, title.Subtitle, title.Seconds) : null,
            texts.End is { } end ? new FlybyBundleCard(end.Text, end.Subtitle, end.Seconds) : null,
            [.. (texts.Captions ?? []).Select(c => new FlybyBundleCaption(c.Text, c.StartS, c.EndS))],
            texts.Corner,
            [.. (texts.CornerCaptions ?? []).Select(c => new FlybyBundleCaption(c.Text, c.StartS, c.EndS))],
            texts.FadeInS,
            texts.FadeOutS,
            texts.NorthArrow);

    private static FlybyBundleChanges Changes(FlybyBundleChangesRequest? changes) => changes is null
        ? FlybyBundleChanges.None
        : new FlybyBundleChanges(
            [.. (changes.Relief ?? []).Select(k => new FlybyBundleNumberKey(k.Frame, k.Value, k.Easing ?? FlybyBundleEasing.Linear))],
            [.. (changes.Sky ?? []).Select(k => new FlybyBundleColourKey(k.Frame, k.Colour, k.Easing ?? FlybyBundleEasing.Linear))],
            Layer(changes.Walls),
            Layer(changes.Lines))
        {
            Daylight = [.. (changes.Daylight ?? []).Select(k => new FlybyBundleNumberKey(k.Frame, k.Value, k.Easing ?? FlybyBundleEasing.Linear))],
        };

    private static FlybyBundleLayerChanges Layer(FlybyBundleLayerChangesRequest? layer) => layer is null
        ? FlybyBundleLayerChanges.None
        : new FlybyBundleLayerChanges(
            [.. (layer.Shown ?? []).Select(k => new FlybyBundleShownKey(k.Frame, k.Shown, k.FadeFrames ?? 0))],
            [.. (layer.Opacity ?? []).Select(k => new FlybyBundleNumberKey(k.Frame, k.Value, k.Easing ?? FlybyBundleEasing.Linear))],
            Tints(layer.Tint ?? []));

    private static List<FlybyBundleTintKey> Tints(IReadOnlyList<FlybyBundleTintKeyRequest> keys) =>
        [.. keys.Select(k => new FlybyBundleTintKey(k.Frame, k.Colour, k.Strength, k.Easing ?? FlybyBundleEasing.Linear))];

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static List<string> Sorted(IEnumerable<Guid> ids) =>
        [.. ids.Select(id => id.ToString()).Order(StringComparer.Ordinal)];
}
