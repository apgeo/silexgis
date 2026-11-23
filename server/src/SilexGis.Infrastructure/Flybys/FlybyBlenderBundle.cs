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
/// </summary>
public static class FlybyBlenderBundle
{
    /// <summary>Half the width of the tube a survey leg is drawn as, in metres.</summary>
    public const double LineRadiusM = 0.3;

    /// <summary>The folder inside a bundle its render writes into.</summary>
    public const string OutputDirectory = "out";

    private const string RockMaterialId = "rock";
    private const string LinesMaterialId = "lines";
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

        var relief = scene.Relief;
        var frame = new SceneTangentFrame(scene.OriginLongitude, scene.OriginLatitude, scene.OriginHeightM, relief);
        var (rock, lights) = Borrowed.Value;

        var models = new List<PlanModel>();
        var materials = new List<PlanMaterial> { rock with { Id = RockMaterialId } };
        var spheres = new List<(SceneVector Centre, double Radius)>();
        CaveVector3? stretch = relief == 1 ? null : new CaveVector3(1, 1, Rounded(relief));

        for (var i = 0; i < scene.Caves.Count; i++)
        {
            var cave = scene.Caves[i];
            var number = (i + 1).ToString("00", CultureInfo.InvariantCulture);

            if (cave.Walls is { Mesh.TriangleCount: > 0 } walls)
            {
                models.Add(Place(frame, $"cave-{number}-walls", WallsRole, RockMaterialId, walls, cave.AltitudeShiftM, stretch, spheres, writeMesh));
            }

            if (cave.Estimated is { Mesh.TriangleCount: > 0 } estimated)
            {
                var colour = cave.EstimatedColor ?? GlbWriter.WallColor;
                var material = FlatMaterial("estimated", colour, materials);
                models.Add(Place(frame, $"cave-{number}-estimated", EstimatedRole, material, estimated, cave.AltitudeShiftM, stretch, spheres, writeMesh));
            }

            if (cave.Legs.Count > 0 && Lines(frame, cave, scene.Look, number, materials) is { } lines)
            {
                models.Add(Place(frame, $"cave-{number}-lines", LinesRole, lines.Material, lines.Mesh, cave.AltitudeShiftM, stretch, spheres, writeMesh));
            }
        }

        if (models.Count == 0)
        {
            throw new ArgumentException("The flyby has nothing to draw.", nameof(scene));
        }

        var fps = scene.Fps;
        var views = scene.Poses
            .Select(p => frame.ViewAt(p.Longitude, p.Latitude, p.HeightM, p.HeadingDeg, p.PitchDeg, p.RollDeg))
            .ToList();
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
                Rounded(i / (double)fps),
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
            Features = texts is null
                ? ScenePlan.BasicFeatures
                : [.. ScenePlan.PlayerFeatures.Where(f => f == ScenePlan.TextsFeature || ScenePlan.BasicFeatures.Contains(f))],
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
                World = Linear(scene.Look.SkyColour) is { } sky
                    ? new PlanWorld { Kind = PlanWorldKind.Color, Color = sky, Strength = 1 }
                    : null,
            },
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

    private static PlanModel Place(
        SceneTangentFrame frame,
        string id,
        string role,
        string material,
        FlybyBundleMesh mesh,
        double altitudeShiftM,
        CaveVector3? stretch,
        List<(SceneVector Centre, double Radius)> spheres,
        Action<string, CaveMesh> writeMesh)
    {
        var placement = frame.PlacementAt(mesh.AnchorLongitude, mesh.AnchorLatitude, mesh.AnchorAltitudeM + altitudeShiftM);
        var path = id + ".ply";
        writeMesh(path, Mesh(mesh.Mesh));

        // How far the mesh reaches from its zero point, for the camera's far limit: taller by the
        // relief, which stretches it along its own up.
        var reach = 0.0;
        var positions = mesh.Mesh.Positions;
        for (var v = 0; v < positions.Length; v += 3)
        {
            var up = positions[v + 2] * frame.Relief;
            reach = Math.Max(reach, (positions[v] * positions[v]) + (positions[v + 1] * positions[v + 1]) + (up * up));
        }

        spheres.Add((placement.Translation, Math.Sqrt(reach)));

        return new PlanModel
        {
            Id = id,
            Role = role,
            Path = path,
            Translation = Rounded(placement.Translation),
            Rotation = placement.Degrees == 0
                ? null
                : new PlanRotation { Axis = Rounded(placement.Axis), Degrees = Math.Round(placement.Degrees, 9) },
            Scale = stretch,
            Material = material,
        };
    }

    private static CaveMesh Mesh(IndexedMesh mesh)
    {
        var vertices = new CaveVector3[mesh.VertexCount];
        for (var v = 0; v < vertices.Length; v++)
        {
            vertices[v] = new CaveVector3(mesh.Positions[v * 3], mesh.Positions[(v * 3) + 1], mesh.Positions[(v * 3) + 2]);
        }

        var triangles = new CaveTriangle[mesh.TriangleCount];
        for (var t = 0; t < triangles.Length; t++)
        {
            triangles[t] = new CaveTriangle(
                (uint)mesh.Indices[t * 3], (uint)mesh.Indices[(t * 3) + 1], (uint)mesh.Indices[(t * 3) + 2]);
        }

        return new CaveMesh { Vertices = vertices, Triangles = triangles };
    }

    /// <summary>
    /// A cave's survey legs as thin tubes about the first leg's start, and the material they wear:
    /// one colour, or the colours by depth below the cave's highest point.
    /// </summary>
    private static (FlybyBundleMesh Mesh, string Material)? Lines(
        SceneTangentFrame frame, FlybyBundleCave cave, FlybyBundleLook look, string number, List<PlanMaterial> materials)
    {
        var first = cave.Legs[0];
        var about = new LocalTangentFrame(first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        var mesh = SurveyLineTubes.Build(
            cave.Legs.Select(leg => (
                about.ToLocal(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM),
                about.ToLocal(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM))),
            LineRadiusM);
        if (mesh.TriangleCount == 0)
        {
            return null;
        }

        var placed = new FlybyBundleMesh(mesh, first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        if (Linear(look.LineColour) is { } one)
        {
            return (placed, FlatMaterial(LinesMaterialId, (one.R, one.G, one.B), materials));
        }

        var bands = look.DepthBands
            .Select(band => (band.FromM, Colour: Linear(band.Colour)))
            .Where(band => band.Colour is not null && double.IsFinite(band.FromM) && band.FromM >= 0)
            .OrderBy(band => band.FromM)
            .Select(band => (band.FromM, Colour: band.Colour!.Value))
            .ToList();
        if (bands.Count == 0)
        {
            return (placed, FlatMaterial(LinesMaterialId, GlbWriter.WallColor, materials));
        }

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
        return (placed, id);
    }

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
