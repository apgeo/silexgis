// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Export;
using SilexGis.Domain.Geo;
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
/// <b>It is still a copy that outlives the check that made it</b>, as a movie of the same flyby
/// is, so each one is recorded: who took it and which caves went into it.
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
                + "and neither is one with no survey; the answer counts them together.");
        return api;
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> DownloadAsync(
        FlybyBlenderBundleRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IFileStore fileStore,
        ICurrentUser currentUser,
        IOptions<MapOptions> mapOptions,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
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
                var segments = request.Look!.Lines || !cave.Walls || wallsFellBack
                    ? (await SurveySegmentSource.ForCaveAsync(db, ctx, cave.CaveId, ct)).Segments
                        .Where(s => !s.IsDuplicate)
                        .ToList()
                    : [];

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
                var legs = segments
                    .Select(s => s.MidZM is { } mid
                        ? new FlybyBundleLeg(
                            s.FromLongitude, s.FromLatitude, mid - ((s.DeltaZM ?? 0) / 2),
                            s.ToLongitude, s.ToLatitude, mid + ((s.DeltaZM ?? 0) / 2))
                        : new FlybyBundleLeg(s.FromLongitude, s.FromLatitude, flat, s.ToLongitude, s.ToLatitude, flat))
                    .ToList();

                // A colour of this cave's own, where the flyby gives it one; and lines that are all
                // there is of a cave — because its walls were wanted and are not there, or because
                // the look draws no lines at all — are shown and hidden as the walls are.
                var apart = request.Changes?.Caves?.FirstOrDefault(c => c.CaveId == cave.CaveId);
                caves.Add(new FlybyBundleCave(walls, estimated, estimatedColor, legs, shift)
                {
                    WallsTint = apart?.Walls is { } wallsTint ? Tints(wallsTint) : null,
                    LinesTint = apart?.Lines is { } linesTint ? Tints(linesTint) : null,
                    LinesStandIn = walls is null && estimated is null && (wallsFellBack || !request.Look.Lines),
                });
                (walls is not null || estimated is not null ? withWalls : withLines).Add(cave.CaveId);
            }

            if (caves.Count == 0)
            {
                return ApiProblems.UnprocessableContent(
                    NothingToDrawCode,
                    "Nothing of the caves asked for is available to draw, so there is no scene to make a bundle of.");
            }

            var leftOut = (request.LeftOut ?? [])
                .Distinct()
                .Order()
                .Select(FlybyBundleOmissions.Sentence)
                .ToList();
            if (wallsUnavailable > 0)
            {
                leftOut.Add(FlybyBundleOmissions.WallsNotAvailable(wallsUnavailable));
            }

            if (wallsOverBudget > 0)
            {
                leftOut.Add(FlybyBundleOmissions.WallsOverBudget(wallsOverBudget));
            }

            if (notDrawn > 0)
            {
                leftOut.Add(FlybyBundleOmissions.CavesNotDrawn(notDrawn));
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
                [.. request.Frames!.Select(f => new FlybyBundlePose(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7]))],
                caves,
                new FlybyBundleLook(
                    request.Look!.Sky,
                    request.Look.LineColour,
                    [.. (request.Look.DepthBands ?? []).Select(b => new FlybyBundleDepthBand(b.FromM, b.Colour))]),
                Texts(request.Texts),
                leftOut)
            {
                Changes = Changes(request.Changes),
            };

            // Recorded before the bytes are handed over, in the same request, so there is no window
            // in which a file exists and the record of who took it does not. No position is in it:
            // which caves, and how each went in.
            db.Set<AuditEntry>().Add(new AuditEntry
            {
                UserId = currentUser.UserId,
                Action = AuditActions.Exported,
                EntityType = AuditEntityType,
                EntityId = Guid.CreateVersion7().ToString(),
                Changes = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["format"] = "blender",
                    ["frames"] = scene.Poses.Count,
                    ["wallsCaveIds"] = Sorted(withWalls),
                    ["linesCaveIds"] = Sorted(withLines),
                    ["notDrawnCount"] = notDrawn,
                }),
            });
            await db.SaveChangesAsync(ct);

            using var zip = new MemoryStream();
            var work = Path.Combine(Path.GetTempPath(), $"silexgis-blender-{Guid.NewGuid():N}");
            var summary = FlybyBlenderBundle.Write(scene, work, zip);

            http.Response.Headers[WallsHeader] = Count(withWalls.Count);
            http.Response.Headers[LinesHeader] = Count(withLines.Count);
            http.Response.Headers[NotDrawnHeader] = Count(notDrawn);
            http.Response.Headers[WallsLeftOutHeader] = Count(wallsUnavailable + wallsOverBudget);
            return TypedResults.File(zip.ToArray(), "application/zip", summary.FolderName + ".zip");
        }
        finally
        {
            OneAtATime.Release();
        }
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
            Layer(changes.Lines));

    private static FlybyBundleLayerChanges Layer(FlybyBundleLayerChangesRequest? layer) => layer is null
        ? FlybyBundleLayerChanges.None
        : new FlybyBundleLayerChanges(
            [.. (layer.Shown ?? []).Select(k => new FlybyBundleShownKey(k.Frame, k.Shown))],
            [.. (layer.Opacity ?? []).Select(k => new FlybyBundleNumberKey(k.Frame, k.Value, k.Easing ?? FlybyBundleEasing.Linear))],
            Tints(layer.Tint ?? []));

    private static List<FlybyBundleTintKey> Tints(IReadOnlyList<FlybyBundleTintKeyRequest> keys) =>
        [.. keys.Select(k => new FlybyBundleTintKey(k.Frame, k.Colour, k.Strength, k.Easing ?? FlybyBundleEasing.Linear))];

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static List<string> Sorted(IEnumerable<Guid> ids) =>
        [.. ids.Select(id => id.ToString()).Order(StringComparer.Ordinal)];
}
