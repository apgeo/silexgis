// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Cave3D;
using Therion.CaveGeometry;
using PlyWriter = Therion.CaveGeometry.Writers.PlyWriter;

namespace SilexGis.Infrastructure.Flybys;

/// <summary>What a written bundle holds, for whoever has to say so.</summary>
/// <param name="FolderName">The one folder the zip unpacks into.</param>
/// <param name="Frames">How many frames the movie has.</param>
/// <param name="Models">How many meshes the scene is made of.</param>
public sealed record FlybyBundleSummary(string FolderName, int Frames, int Models);

/// <summary>
/// Writes a flyby as a render bundle: a zip somebody unpacks on a machine of their own and renders
/// with Blender, holding the scene's description, its meshes and the player that builds the scene.
///
/// <para>
/// <b>Nothing is rendered here and nothing is started.</b> The scene's description is the survey
/// library's scene plan, built with that library's own records and checked by its own validator;
/// the zip is written by its bundle writer, which also puts in the player — Python the library
/// carries as data and hands over under its own licence. Whether there is a Blender on this host
/// is no question this code asks.
/// </para>
///
/// <para>
/// <b>The frame is laid out about one point and says nothing of where that point is.</b> Every
/// position in the plan is metres east, north and up of the flyby's first subject, on the plane
/// touching the Earth there; the plan's own origin is zero and it names no coordinate system. A
/// cave stands by its offset from that point, turned by the little its own "up" leans from the
/// origin's. So the file shows the shapes of caves and how they lie to each other, and not where
/// on a map they are.
/// </para>
///
/// <para>
/// <b>The camera is taken as given, one pose a frame.</b> The path was worked out where the flyby
/// was played — over that scene's ground, with its timing — and nothing of it is worked out again
/// here: each frame's place, heading and tilt become one pose with its own orientation, so a
/// camera looking straight down keeps its heading and one far from the origin keeps its own
/// horizon.
/// </para>
///
/// <para>
/// <b>What changes while the clip runs is written as keys, and the camera reads the same keys.</b>
/// A relief that grows stretches every model along its own up, about the level of the frame's
/// zero; the height of each frame's camera is multiplied by the relief those very keys give at
/// that frame, so a cave and the camera looking at it are never drawn at two different reliefs.
/// A colour over the walls or over one cave's walls, the colour of the lines, a layer shown,
/// hidden or faded and the colour of the sky are tracks on the models and on the world. The lights
/// do not change: the scene plan has no key for a light.
/// </para>
/// </summary>
public static class FlybyBlenderBundle
{
    /// <summary>Half the width of the tube a survey leg is drawn as, in metres.</summary>
    public const double LineRadiusM = 0.3;

    /// <summary>The folder inside a bundle its render writes into.</summary>
    public const string OutputDirectory = "out";

    private const string RockMaterialId = "rock";
    private const string LinesMaterialId = "lines";
    private const string LinesByDepthMaterialId = "lines-by-depth";
    private const string WallsRole = "walls";
    private const string EstimatedRole = "estimated-passages";
    private const string LinesRole = "survey-lines";

    /// <summary>How long a title card takes to go and an end card to come, at most, in seconds.</summary>
    private const double CardFadeSeconds = 0.8;

    private static readonly Lazy<(PlanMaterial Rock, IReadOnlyList<PlanLight> Lights)> Borrowed = new(BorrowLook);

    /// <summary>
    /// Writes the bundle of <paramref name="scene"/> into <paramref name="destination"/>, which
    /// stays open.
    /// </summary>
    /// <param name="workDirectory">
    /// A directory the meshes are written into on their way into the zip. It is made here and
    /// removed again, whatever happens.
    /// </param>
    /// <exception cref="ArgumentException">The scene has nothing to draw, or no frame.</exception>
    public static FlybyBundleSummary Write(FlybyBundleScene scene, string workDirectory, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);

        Directory.CreateDirectory(workDirectory);
        try
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var plan = Plan(scene, (path, mesh) =>
            {
                var onDisk = Path.Combine(workDirectory, path);
                PlyWriter.WriteFile(mesh, onDisk);
                files[path] = onDisk;
            });

            var content = new RenderBundleContent
            {
                Plan = plan,
                Draft = ScenePlanBuilder.Draft(plan),
                Files = files,
                NotCarried = scene.LeftOut,
                Captions = [.. Subtitles.OneAtATime(Captions(scene.Texts.Captions, ClipSeconds(scene)))],
            };
            var folder = FolderName(scene.Name);
            RenderBundleWriter.WriteZip(content, destination, folder);
            return new FlybyBundleSummary(folder, plan.Timeline.FrameCount, plan.Models.Count);
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The bundle is written or the failure is already on its way out; a scratch file
                // that would not go is not worth replacing either with.
            }
        }
    }

    /// <summary>The name a bundle's one folder, its zip and its movie take from what the flyby is called.</summary>
    public static string FolderName(string? name) => Slug(name) + "-blender";

    /// <summary>The scene plan of <paramref name="scene"/>, as the JSON a bundle holds, with no mesh written.</summary>
    public static string PlanJson(FlybyBundleScene scene) =>
        ScenePlanSerializer.Write(Plan(scene, (_, _) => { }));

    private static ScenePlan Plan(FlybyBundleScene scene, Action<string, CaveMesh> writeMesh)
    {
        if (scene.Poses.Count == 0)
        {
            throw new ArgumentException("A flyby has at least one frame.", nameof(scene));
        }

        var fps = scene.Fps;
        var changes = scene.Changes;
        double Seconds(int index) => Rounded(index / (double)fps);

        // The relief at every frame, from the keys the models are stretched by. Where it changes,
        // the models are laid out in the frame of the middle of its range — what a model placed
        // once is off by grows with how far the relief is from its frame's — and each is stretched
        // about a point level with the frame's zero.
        var reliefs = Reliefs(scene);
        var flattest = reliefs.Min();
        var tallest = reliefs.Max();
        var stretching = tallest > flattest;
        var frame = new SceneTangentFrame(
            scene.OriginLongitude, scene.OriginLatitude, scene.OriginHeightM, stretching ? (flattest + tallest) / 2 : reliefs[0]);
        var (rock, lights) = Borrowed.Value;

        var models = new List<PlanModel>();
        var timed = new List<PlanChange>();
        var materials = new List<PlanMaterial> { rock with { Id = RockMaterialId } };
        var spheres = new List<(SceneVector Centre, double Radius)>();
        var layout = new Layout(frame, stretching, tallest, stretching || reliefs[0] != 1 ? new CaveVector3(1, 1, Rounded(reliefs[0])) : null);
        var stretch = stretching
            ? changes.Relief.Select(key => new PlanVectorKey(Seconds(key.Frame), new CaveVector3(1, 1, Rounded(key.Value)), Easing(key.Easing))).ToList()
            : null;

        // What changes of one model: the layer it belongs to shown, hidden and faded, the colour
        // laid over it, and its stretch.
        void Changing(string id, FlybyBundleLayerChanges layer, IReadOnlyList<FlybyBundleTintKey>? tint)
        {
            var change = new PlanChange
            {
                Target = new PlanChangeTarget { Id = id },
                Visible = layer.Shown.Count > 0 ? [.. layer.Shown.Select(key => new PlanVisibleKey(Seconds(key.Frame), key.Shown))] : null,
                Opacity = layer.Opacity.Count > 0
                    ? [.. layer.Opacity.Select(key => new PlanNumberKey(Seconds(key.Frame), Rounded(key.Value), Easing(key.Easing)))]
                    : null,
                Tint = tint is { Count: > 0 }
                    ? [.. tint.Select(key => new PlanTintKey(Seconds(key.Frame), Linear(key.Colour) ?? default, Rounded(key.Strength), Easing(key.Easing)))]
                    : null,
                Scale = stretch,
            };
            if (change is not { Visible: null, Opacity: null, Tint: null, Scale: null })
            {
                timed.Add(change);
            }
        }

        for (var i = 0; i < scene.Caves.Count; i++)
        {
            var cave = scene.Caves[i];
            var number = (i + 1).ToString("00", CultureInfo.InvariantCulture);

            // A colour over the walls is a track only where it is ever seen.
            var wallsTint = cave.WallsTint ?? changes.Walls.Tint;
            var tinted = wallsTint.Any(key => key.Strength > 0) ? wallsTint : null;

            if (cave.Walls is { Mesh.TriangleCount: > 0 } walls)
            {
                var id = $"cave-{number}-walls";
                models.Add(Place(layout, id, WallsRole, RockMaterialId, walls, null, cave.AltitudeShiftM, spheres, writeMesh));
                Changing(id, changes.Walls, tinted);
            }

            if (cave.Estimated is { Mesh.TriangleCount: > 0 } estimated)
            {
                var id = $"cave-{number}-estimated";
                var colour = cave.EstimatedColor ?? GlbWriter.WallColor;
                var material = FlatMaterial("estimated", colour, materials);
                models.Add(Place(layout, id, EstimatedRole, material, estimated, null, cave.AltitudeShiftM, spheres, writeMesh));
                Changing(id, changes.Walls, tinted);
            }

            if (cave.Legs.Count > 0)
            {
                // The lines' colour over the clip: this cave's own, else every cave's, else the one
                // the look gives them for the whole of it.
                var colours = cave.LinesTint ?? (changes.Lines.Tint.Count > 0 ? changes.Lines.Tint : null);
                var oneColour = colours is null
                    ? scene.Look.LineColour
                    : colours.All(key => key.Strength > 0) ? colours[0].Colour : null;
                if (Lines(layout, cave, scene.Look, oneColour, number, materials) is { } lines)
                {
                    var id = $"cave-{number}-lines";
                    models.Add(Place(layout, id, LinesRole, lines.Material, lines.Mesh, lines.Colours, cave.AltitudeShiftM, spheres, writeMesh));
                    Changing(
                        id,
                        cave.LinesStandIn ? changes.Walls : changes.Lines,
                        colours is { Count: > 1 } || (colours is not null && oneColour is null && colours.Any(key => key.Strength > 0)) ? colours : null);
                }
            }
        }

        if (models.Count == 0)
        {
            throw new ArgumentException("The flyby has nothing to draw.", nameof(scene));
        }

        // The sky: one colour, or a colour that changes — which needs a world that has one.
        var sky = changes.Sky.Count > 0 ? changes.Sky[0].Colour : scene.Look.SkyColour;
        if (changes.Sky.Count > 1)
        {
            timed.Add(new PlanChange
            {
                WorldColor = [.. changes.Sky.Select(key => new PlanColorKey(Seconds(key.Frame), Linear(key.Colour) ?? default, Easing(key.Easing)))],
            });
            sky ??= "#000000";
        }

        // Each frame's camera in the frame of its own moment: its height times the relief the
        // models have at that frame.
        var views = new List<SceneView>(scene.Poses.Count);
        var at = frame;
        for (var i = 0; i < scene.Poses.Count; i++)
        {
            var p = scene.Poses[i];
            at = at.Relief == reliefs[i] ? at : frame.WithRelief(reliefs[i]);
            views.Add(at.ViewAt(p.Longitude, p.Latitude, p.HeightM, p.HeadingDeg, p.PitchDeg, p.RollDeg));
        }

        var lenses = scene.Poses
            .Select(p => Rounded(SceneTangentFrame.FocalLength(
                SceneTangentFrame.HorizontalFieldOfView(p.LensDeg, scene.Width, scene.Height), CameraPlanner.SensorWidthMm)))
            .ToList();
        var lensChanges = lenses.Any(lens => lens != lenses[0]);

        var track = new List<PlanCameraKey>(views.Count);
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var focus = Math.Max(scene.Poses[i].FocusM, 0.01);
            var turn = PlanQuaternion.FromView(Vector(view.Forward), Vector(view.Up));
            track.Add(new PlanCameraKey(
                Seconds(i),
                Rounded(view.Eye),
                Rounded(view.Eye + (view.Forward * focus)),
                lensChanges ? lenses[i] : null)
            {
                Orientation = new PlanQuaternion(Turn(turn.W), Turn(turn.X), Turn(turn.Y), Turn(turn.Z)),
            });
        }

        var (clipStart, clipEnd) = ClipRange(views, spheres);
        var texts = Texts(scene, views.Count);
        var name = string.IsNullOrWhiteSpace(scene.Name) ? "Flyby" : scene.Name.Trim();

        var plan = new ScenePlan
        {
            Generator = "SilexGIS",
            Name = name,
            // The player's own order: what changes comes after what it changes, texts after that.
            Features =
            [
                .. ScenePlan.PlayerFeatures.Where(f =>
                    ScenePlan.BasicFeatures.Contains(f)
                    || (f == ScenePlan.ChangesFeature && timed.Count > 0)
                    || (f == ScenePlan.TextsFeature && texts is not null)),
            ],
            // Zero, and no coordinate system: the frame is laid out about a point it does not name.
            Frame = new PlanFrame(),
            Timeline = new PlanTimeline { Fps = fps, FrameCount = views.Count },
            Models = models,
            Camera = new PlanCamera
            {
                SensorWidth = CameraPlanner.SensorWidthMm,
                Lens = lenses[0],
                ClipStart = clipStart,
                ClipEnd = clipEnd,
                Interpolation = PlanInterpolation.Linear,
                Track = track,
            },
            Look = new PlanLook
            {
                Samples = scene.Quality switch
                {
                    FlybyBundleQuality.Low => 32,
                    FlybyBundleQuality.Medium => 96,
                    _ => 256,
                },
                Width = scene.Width,
                Height = scene.Height,
                Materials = materials,
                Lights = lights,
                World = Linear(sky) is { } behind
                    ? new PlanWorld { Kind = PlanWorldKind.Color, Color = behind, Strength = 1 }
                    : null,
            },
            Changes = timed.Count > 0 ? timed : null,
            Texts = texts,
            Output = new PlanOutput
            {
                Kind = PlanOutputKind.Video,
                Container = PlanContainer.Mp4,
                Directory = OutputDirectory,
                BaseName = Slug(scene.Name),
            },
        };

        var errors = ScenePlanValidator.Validate(plan);
        if (errors.Count > 0)
        {
            // What was sent has already been checked field by field; a plan that still does not
            // pass is this code's mistake, and is reported as one rather than handed over.
            throw new InvalidOperationException(
                "The scene plan of a flyby did not pass its validator: "
                + string.Join("; ", errors.Take(5).Select(e => $"{e.Path}: {e.Message}")));
        }

        return plan;
    }

    // ---- models ----

    /// <summary>
    /// How the models of one scene are laid out: the frame, whether the relief changes while the
    /// clip runs (each model is then stretched about a point level with the frame's zero), the
    /// tallest relief of the clip, and the stretch every model starts with.
    /// </summary>
    private sealed record Layout(SceneTangentFrame Frame, bool Stretching, double Tallest, CaveVector3? Stretch);

    private static PlanModel Place(
        Layout layout,
        string id,
        string role,
        string material,
        FlybyBundleMesh mesh,
        IReadOnlyList<CaveColor>? colours,
        double altitudeShiftM,
        List<(SceneVector Centre, double Radius)> spheres,
        Action<string, CaveMesh> writeMesh)
    {
        var height = mesh.AnchorAltitudeM + altitudeShiftM;
        SceneVector translation;
        SceneVector axis;
        double degrees;
        var lift = 0.0;
        if (layout.Stretching)
        {
            (translation, axis, degrees, lift) = layout.Frame.PlacementAbout(mesh.AnchorLongitude, mesh.AnchorLatitude, height);
        }
        else
        {
            (translation, axis, degrees) = layout.Frame.PlacementAt(mesh.AnchorLongitude, mesh.AnchorLatitude, height);
        }

        var path = id + ".ply";
        writeMesh(path, Mesh(mesh.Mesh, lift, colours));

        // How far the mesh reaches from the point it stands at, for the camera's far limit: taller
        // by the relief, which stretches it along its own up — by the tallest the clip reaches.
        var reach = 0.0;
        var positions = mesh.Mesh.Positions;
        for (var v = 0; v < positions.Length; v += 3)
        {
            var up = (positions[v + 2] + lift) * layout.Tallest;
            reach = Math.Max(reach, (positions[v] * positions[v]) + (positions[v + 1] * positions[v + 1]) + (up * up));
        }

        spheres.Add((translation, Math.Sqrt(reach)));

        return new PlanModel
        {
            Id = id,
            Role = role,
            Path = path,
            Translation = Rounded(translation),
            Rotation = degrees == 0
                ? null
                : new PlanRotation { Axis = Rounded(axis), Degrees = Math.Round(degrees, 9) },
            Scale = layout.Stretch,
            Material = material,
        };
    }

    /// <param name="lift">Metres every vertex is raised by, along the mesh's own up.</param>
    private static CaveMesh Mesh(IndexedMesh mesh, double lift, IReadOnlyList<CaveColor>? colours)
    {
        var vertices = new CaveVector3[mesh.VertexCount];
        for (var v = 0; v < vertices.Length; v++)
        {
            vertices[v] = new CaveVector3(mesh.Positions[v * 3], mesh.Positions[(v * 3) + 1], mesh.Positions[(v * 3) + 2] + lift);
        }

        var triangles = new CaveTriangle[mesh.TriangleCount];
        for (var t = 0; t < triangles.Length; t++)
        {
            triangles[t] = new CaveTriangle(
                (uint)mesh.Indices[t * 3], (uint)mesh.Indices[(t * 3) + 1], (uint)mesh.Indices[(t * 3) + 2]);
        }

        return new CaveMesh { Vertices = vertices, Triangles = triangles, VertexColors = colours };
    }

    /// <summary>
    /// A cave's survey legs as thin tubes about the first leg's start, and the material they wear:
    /// one colour, or the colours by depth below the cave's highest point.
    /// </summary>
    /// <param name="oneColour">The one colour the lines have for the whole clip; null for their colours by depth.</param>
    private static (FlybyBundleMesh Mesh, string Material, IReadOnlyList<CaveColor>? Colours)? Lines(
        Layout layout,
        FlybyBundleCave cave,
        FlybyBundleLook look,
        string? oneColour,
        string number,
        List<PlanMaterial> materials)
    {
        var frame = layout.Frame;
        var first = cave.Legs[0];
        var about = new LocalTangentFrame(first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        FlybyBundleMesh Placed(IndexedMesh tubes) => new(tubes, first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        IndexedMesh Tubes(IEnumerable<FlybyBundleLeg> legs, out int[] legOfVertex) => SurveyLineTubes.Build(
            legs.Select(leg => (
                about.ToLocal(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM),
                about.ToLocal(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM))),
            LineRadiusM,
            out legOfVertex);

        var bands = look.DepthBands
            .Select(band => (band.FromM, Hex: band.Colour, Colour: Linear(band.Colour)))
            .Where(band => band.Colour is not null && double.IsFinite(band.FromM) && band.FromM >= 0)
            .OrderBy(band => band.FromM)
            .Select(band => (band.FromM, band.Hex, Colour: band.Colour!.Value))
            .ToList();

        if (Linear(oneColour) is not null || bands.Count == 0)
        {
            var mesh = Tubes(cave.Legs, out _);
            if (mesh.TriangleCount == 0)
            {
                return null;
            }

            var colour = Linear(oneColour) is { } given ? (given.R, given.G, given.B) : GlbWriter.WallColor;
            return (Placed(mesh), FlatMaterial(LinesMaterialId, colour, materials), null);
        }

        if (layout.Stretching)
        {
            // A ramp of colours over height is over height in the scene, and a cave that is being
            // stretched would slide through it. So the colours are put on the lines themselves:
            // each leg is cut where a band begins, and every piece carries its band's colour.
            var top = cave.Legs.Max(leg => Math.Max(leg.FromAltitudeM, leg.ToAltitudeM));
            var pieces = new List<FlybyBundleLeg>();
            var pieceBand = new List<int>();
            foreach (var leg in cave.Legs)
            {
                // The shares of the leg at which it passes into another band, in order along it.
                var cuts = new List<double> { 0 };
                var fall = leg.ToAltitudeM - leg.FromAltitudeM;
                if (fall != 0)
                {
                    cuts.AddRange(bands.Skip(1)
                        .Select(band => (top - band.FromM - leg.FromAltitudeM) / fall)
                        .Where(share => share > 0 && share < 1)
                        .Order());
                }

                cuts.Add(1);
                for (var c = 0; c + 1 < cuts.Count; c++)
                {
                    FlybyBundleLeg piece = new(
                        Along(leg.FromLongitude, leg.ToLongitude, cuts[c]),
                        Along(leg.FromLatitude, leg.ToLatitude, cuts[c]),
                        Along(leg.FromAltitudeM, leg.ToAltitudeM, cuts[c]),
                        Along(leg.FromLongitude, leg.ToLongitude, cuts[c + 1]),
                        Along(leg.FromLatitude, leg.ToLatitude, cuts[c + 1]),
                        Along(leg.FromAltitudeM, leg.ToAltitudeM, cuts[c + 1]));
                    var depth = top - ((piece.FromAltitudeM + piece.ToAltitudeM) / 2);
                    pieces.Add(piece);
                    pieceBand.Add(bands.FindLastIndex(band => band.FromM <= depth) is var found and >= 0 ? found : 0);
                }
            }

            var mesh = Tubes(pieces, out var pieceOfVertex);
            if (mesh.TriangleCount == 0)
            {
                return null;
            }

            var palette = bands.Select(band => Bytes(band.Hex)).ToList();
            if (materials.All(m => m.Id != LinesByDepthMaterialId))
            {
                materials.Add(new PlanMaterial
                {
                    Id = LinesByDepthMaterialId,
                    Kind = PlanMaterialKind.VertexColor,
                    Color = bands[0].Colour,
                    Roughness = 0.9,
                });
            }

            return (Placed(mesh), LinesByDepthMaterialId, [.. pieceOfVertex.Select(piece => palette[pieceBand[piece]])]);
        }

        var whole = Tubes(cave.Legs, out _);
        if (whole.TriangleCount == 0)
        {
            return null;
        }

        var placed = Placed(whole);

        // The ramp runs over the heights this cave's lines stand at in the scene, so its top is the
        // cave's own highest leg and a colour means a depth below that — as the lines were drawn.
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        foreach (var leg in cave.Legs)
        {
            foreach (var z in (ReadOnlySpan<double>)
                     [
                         frame.PointAt(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM + cave.AltitudeShiftM).Z,
                         frame.PointAt(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM + cave.AltitudeShiftM).Z,
                     ])
            {
                low = Math.Min(low, z);
                high = Math.Max(high, z);
            }
        }

        var span = Math.Max(high - low, 1.0);
        var stops = new List<PlanColorStop>();
        var deepest = span / frame.Relief;
        stops.Add(new PlanColorStop(0, bands.Last(band => band.FromM <= deepest || band.FromM == bands[0].FromM).Colour));
        for (var b = bands.Count - 1; b >= 1; b--)
        {
            // Where this band begins, as a share of the way from the lowest line to the highest.
            var at = 1 - (bands[b].FromM * frame.Relief / span);
            const double edge = 1e-4;
            if (at - edge > stops[^1].At + 1e-6 && at < 1 - 1e-6)
            {
                stops.Add(new PlanColorStop(Rounded(at - edge), bands[b].Colour));
                stops.Add(new PlanColorStop(Rounded(at), bands[b - 1].Colour));
            }
        }

        stops.Add(new PlanColorStop(1, bands[0].Colour));

        var id = $"{LinesMaterialId}-{number}";
        materials.Add(new PlanMaterial
        {
            Id = id,
            Kind = PlanMaterialKind.HeightRamp,
            Color = bands[0].Colour,
            Roughness = 0.9,
            Stops = stops,
            Low = Rounded(high - span),
            High = Rounded(high),
        });
        return (placed, id, null);
    }

    private static double Along(double from, double to, double share) => from + ((to - from) * share);

    /// <summary>A colour written <c>#rrggbb</c> as the three bytes a mesh file carries a vertex's colour in.</summary>
    private static CaveColor Bytes(string hex)
    {
        var rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new CaveColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
    }

    // ---- what changes ----

    /// <summary>
    /// The relief at every frame: what the keys give, as a renderer playing them works it out —
    /// held before the first key and after the last, and between two keys moved as the later one
    /// says. With no key, the scene's one factor.
    /// </summary>
    private static double[] Reliefs(FlybyBundleScene scene)
    {
        var keys = scene.Changes.Relief;
        var reliefs = new double[scene.Poses.Count];
        var next = 0;
        for (var i = 0; i < reliefs.Length; i++)
        {
            if (keys.Count == 0)
            {
                reliefs[i] = scene.Relief;
                continue;
            }

            while (next < keys.Count && keys[next].Frame <= i)
            {
                next++;
            }

            if (next == 0 || next == keys.Count)
            {
                reliefs[i] = keys[Math.Max(next - 1, 0)].Value;
                continue;
            }

            var (before, after) = (keys[next - 1], keys[next]);
            var share = (i - before.Frame) / (double)(after.Frame - before.Frame);
            reliefs[i] = after.Easing switch
            {
                FlybyBundleEasing.Constant => before.Value,
                FlybyBundleEasing.Smooth => before.Value + ((after.Value - before.Value) * share * share * (3 - (2 * share))),
                _ => before.Value + ((after.Value - before.Value) * share),
            };
        }

        // To what the plan writes a stretch to, so the camera and the models read one number.
        for (var i = 0; i < reliefs.Length; i++)
        {
            reliefs[i] = Rounded(reliefs[i]);
        }

        return reliefs;
    }

    private static PlanEasing Easing(FlybyBundleEasing easing) => easing switch
    {
        FlybyBundleEasing.Smooth => PlanEasing.Smooth,
        FlybyBundleEasing.Constant => PlanEasing.Constant,
        _ => PlanEasing.Linear,
    };

    /// <summary>The id of a material of one plain colour, added to the list the first time it is asked for.</summary>
    private static string FlatMaterial(string prefix, (double R, double G, double B) colour, List<PlanMaterial> materials)
    {
        var rounded = new ColorRgb(Rounded(colour.R), Rounded(colour.G), Rounded(colour.B));
        var known = materials.FirstOrDefault(m => m.Kind == PlanMaterialKind.Flat && m.Color == rounded && m.Id.StartsWith(prefix, StringComparison.Ordinal));
        if (known is not null)
        {
            return known.Id;
        }

        var taken = materials.Count(m => m.Id.StartsWith(prefix, StringComparison.Ordinal));
        var id = taken == 0 ? prefix : $"{prefix}-{(taken + 1).ToString(CultureInfo.InvariantCulture)}";
        materials.Add(new PlanMaterial { Id = id, Kind = PlanMaterialKind.Flat, Color = rounded, Roughness = 0.9 });
        return id;
    }

    // ---- camera ----

    /// <summary>
    /// How near and how far the camera draws, one pair for the whole clip: far enough to hold every
    /// model from every frame, and as near as the closest it ever comes to one allows.
    /// </summary>
    private static (double Start, double End) ClipRange(
        IReadOnlyList<SceneView> views, IReadOnlyList<(SceneVector Centre, double Radius)> spheres)
    {
        var farthest = 0.0;
        var clearest = double.PositiveInfinity;
        foreach (var view in views)
        {
            foreach (var (centre, radius) in spheres)
            {
                var distance = (view.Eye - centre).Length;
                farthest = Math.Max(farthest, distance + radius);
                clearest = Math.Min(clearest, Math.Max(0, distance - radius));
            }
        }

        var start = Math.Clamp(clearest * 0.01, 0.1, 10);
        var end = Math.Max(1000, farthest * 1.2);
        return (Rounded(start), Rounded(end));
    }

    // ---- texts ----

    private static double ClipSeconds(FlybyBundleScene scene) => scene.Poses.Count / (double)scene.Fps;

    /// <summary>Captions that lie in the clip, cut to it.</summary>
    private static List<CaptionSpec> Captions(IReadOnlyList<FlybyBundleCaption> captions, double clipSeconds) =>
    [
        .. captions
            .Where(c => !string.IsNullOrWhiteSpace(c.Text) && c.StartS < clipSeconds && c.EndS > Math.Max(c.StartS, 0))
            .OrderBy(c => c.StartS)
            .Select(c => new CaptionSpec
            {
                Text = c.Text.Trim(),
                Start = Rounded(Math.Max(c.StartS, 0)),
                End = Rounded(Math.Min(c.EndS, clipSeconds)),
            })
            .Where(c => c.End > c.Start),
    ];

    /// <summary>
    /// What the flyby writes over its picture, laid out the way the survey library lays texts out —
    /// its places, its sizes — and timed as the flyby times them.
    /// </summary>
    private static PlanScreenTexts? Texts(FlybyBundleScene scene, int frames)
    {
        var said = scene.Texts;
        var clip = ClipSeconds(scene);
        var title = said.Title is { Seconds: > 0 } t && !string.IsNullOrWhiteSpace(t.Text) ? t : null;
        var end = said.End is { Seconds: > 0 } e && !string.IsNullOrWhiteSpace(e.Text) ? e : null;

        var overlays = new OverlaySpec
        {
            Title = title?.Text.Trim(),
            Subtitle = title is null || string.IsNullOrWhiteSpace(title.Subtitle) ? null : title.Subtitle.Trim(),
            TitleSeconds = title is null ? null : Rounded(Math.Min(title.Seconds, clip)),
            EndCard = end is null
                ? null
                : new EndCardSpec
                {
                    Text = end.Text.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(end.Subtitle) ? null : end.Subtitle.Trim(),
                    Seconds = Rounded(Math.Min(end.Seconds, clip)),
                },
            Captions = Captions(said.Captions, clip),
            Corner = string.IsNullOrWhiteSpace(said.Corner) ? null : said.Corner.Trim(),
            CornerCaptions = Captions(said.CornerCaptions, clip),
            FadeFromBlack = Rounded(Math.Clamp(said.FadeInS, 0, clip)),
            FadeToBlack = Rounded(Math.Clamp(said.FadeOutS, 0, clip)),
            NorthArrow = said.NorthArrow,
        };
        if (!overlays.Any)
        {
            return null;
        }

        var spec = new SceneSpec
        {
            Labels = new LabelsSpec { Overlays = overlays },
            Animation = new AnimationSpec { Fps = scene.Fps, DurationSeconds = clip },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = scene.Width, Height = scene.Height },
        };
        var laid = ScreenTextPlanner.Plan(spec, new CameraPlan { FrameCount = frames });
        if (laid is null)
        {
            return null;
        }

        // The layout is the library's. Three things about time are the flyby's, and are put back:
        // its title has gone by the moment it names; its end card comes over the last of the flight
        // rather than on a card of black after it, so the clip is as long as the flyby; and its
        // north arrow turns by the headings the camera was given, which a camera looking straight
        // down still has.
        var items = new List<PlanScreenItem>(laid.Items.Count);
        foreach (var item in laid.Items)
        {
            if (item.Role is ScreenTextPlanner.TitleRole or ScreenTextPlanner.SubtitleRole && title is not null)
            {
                var until = Math.Min(title.Seconds, clip);
                items.Add(item with { End = Rounded(until), FadeOut = Rounded(Math.Min(CardFadeSeconds, until / 4)) });
            }
            else if (item.Role == ScreenTextPlanner.EndCardRole && end is not null)
            {
                if (item.Kind == PlanScreenItemKind.Box)
                {
                    continue;
                }

                var lasting = Math.Min(end.Seconds, clip);
                items.Add(item with
                {
                    Start = Rounded(clip - lasting),
                    FadeIn = Rounded(Math.Min(CardFadeSeconds, lasting / 4)),
                    Backing = true,
                });
            }
            else if (item.Role == ScreenTextPlanner.NorthArrowRole && item.Kind == PlanScreenItemKind.Arrow)
            {
                items.Add(item with { Turn = Turns(scene.Poses, scene.Fps) });
            }
            else
            {
                items.Add(item);
            }
        }

        return laid with { Hold = 0, Items = items };
    }

    /// <summary>
    /// The north arrow's turn over the clip: the camera's heading at each frame, carried on past a
    /// full circle rather than jumping back, and left out where the frames around it give it.
    /// </summary>
    private static List<PlanTurn> Turns(IReadOnlyList<FlybyBundlePose> poses, int fps)
    {
        var headings = new double[poses.Count];
        for (var i = 0; i < poses.Count; i++)
        {
            var heading = poses[i].HeadingDeg;
            headings[i] = i == 0 ? heading : headings[i - 1] + Math.IEEERemainder(heading - headings[i - 1], 360.0);
        }

        const double tolerance = 0.05;
        var turns = new List<PlanTurn>();
        void Keep(int index) => turns.Add(new PlanTurn(Rounded(index / (double)fps), Math.Round(headings[index], 3)));

        Keep(0);
        var from = 0;
        for (var to = 2; to < headings.Length; to++)
        {
            var straight = true;
            for (var i = from + 1; i < to && straight; i++)
            {
                var onLine = headings[from] + ((headings[to] - headings[from]) * (i - from) / (to - from));
                straight = Math.Abs(onLine - headings[i]) <= tolerance;
            }

            if (!straight)
            {
                Keep(to - 1);
                from = to - 1;
            }
        }

        if (headings.Length > 1 && headings[^1] != headings[from])
        {
            Keep(headings.Length - 1);
        }

        return turns;
    }

    // ---- the look that is the library's own ----

    /// <summary>
    /// The rock a cave's walls are drawn in and the lights of the scene, taken from the survey
    /// library's own defaults by asking it for the plan of an empty scene: its rock, a lamp that
    /// rides the camera and its sun. Asked rather than written down here, so they stay the
    /// library's when it changes them.
    /// </summary>
    private static (PlanMaterial Rock, IReadOnlyList<PlanLight> Lights) BorrowLook()
    {
        var framing = new CameraFraming
        {
            LocalBounds = new BoundingBox(new CaveVector3(-1, -1, -1), new CaveVector3(1, 1, 1)),
        };
        ScenePlan Lit(LightingRig rig) => ScenePlanBuilder.FromSpec(
            new SceneSpec
            {
                Source = new SourceSpec { PlyPath = "model.ply" },
                Materials = new MaterialsSpec { Rock = RockMaterial.Procedural },
                Lighting = new LightingSpec { Rig = rig },
                Output = new OutputSpec { OutputDirectory = OutputDirectory },
            },
            framing);

        var lamp = Lit(LightingRig.Headlamp);
        var sun = Lit(LightingRig.SunSky);
        return (lamp.Look.Materials[0], [.. lamp.Look.Lights, .. sun.Look.Lights]);
    }

    // ---- small things ----

    private static ColorRgb? Linear(string? hex) =>
        GlbWriter.LinearFromHex(hex) is { } c ? new ColorRgb(Rounded(c.R), Rounded(c.G), Rounded(c.B)) : null;

    private static CaveVector3 Vector(SceneVector v) => new(v.X, v.Y, v.Z);

    private static CaveVector3 Rounded(SceneVector v) => new(Rounded(v.X), Rounded(v.Y), Rounded(v.Z));

    private static double Rounded(double value) => Math.Round(value, 6);

    /// <summary>A part of a turn: nine decimals hold a direction to a millionth of a degree.</summary>
    private static double Turn(double value) => Math.Round(value, 9);

    /// <summary>
    /// A name as a plain file name every file system takes: lower-case letters and digits in words
    /// joined by hyphens; "flyby" when nothing of it is left.
    /// </summary>
    private static string Slug(string? name)
    {
        var slug = new StringBuilder();
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(c);
            }
            else if (c is >= 'A' and <= 'Z')
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                     && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }

            if (slug.Length >= 60)
            {
                break;
            }
        }

        var text = slug.ToString().Trim('-');
        return text.Length == 0 ? "flyby" : text;
    }
}
