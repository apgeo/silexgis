// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using SilexGis.Domain.Export;
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
/// <param name="Markers">How many markers stand in it.</param>
/// <param name="Photos">How many photographs it shows.</param>
/// <param name="PhotosShown">Which of the scene's photographs those are, counted from 0 in the scene's own order.</param>
public sealed record FlybyBundleSummary(string FolderName, int Frames, int Models, int Markers = 0, int Photos = 0)
{
    public IReadOnlyList<int> PhotosShown { get; init; } = [];

    /// <summary>By date: what the reveal had no date for. <see cref="FlybyBundleRevealDates.None"/> for any other reveal.</summary>
    public FlybyBundleRevealDates RevealDates { get; init; } = FlybyBundleRevealDates.None;
}

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
/// zero, and each frame's camera is raised along its own up by the same rule, with the relief
/// those very keys give at that frame — so a cave and the camera looking at it are never drawn at
/// two different reliefs, nor by two different rules. A colour over the walls or over one cave's
/// walls, the colour of the lines, a layer shown, hidden or faded and the colour of the sky are
/// tracks on the models and on the world. The sun and the sky are as strong as the daylight the
/// flyby had at each frame: they go out while its camera is under the ground. The lamp on the
/// camera is always on, and where the daylight goes it is turned up by the square of the distance
/// from the camera to the nearest wall, so that walls seen from far away in the dark are lit as
/// walls a few metres away are. Survey lines are drawn in their own colours whatever the light.
/// </para>
///
/// <para>
/// <b>What the scene points out rides on the caves.</b> An entrance, a feature of the surface and
/// a photograph taken at a station are each pinned to a point of a cave's model, in that model's
/// own metres, so they go where the cave goes when the relief stretches it — and, like the caves,
/// say how they lie to one another and not where. A marker is as large as reads from the nearest
/// the camera comes to it; a photograph is shown while the camera is near its station.
/// </para>
/// </summary>
public static partial class FlybyBlenderBundle
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

    /// <summary>
    /// How many times farther than its nearest limit a camera may draw before each frame is given
    /// limits of its own: past this a depth buffer runs out of steps, and a flight that starts far
    /// above a country and ends a metre from a wall has both ends in one clip.
    /// </summary>
    private const double DeepestOneRange = 100_000;

    /// <summary>
    /// How many times a triangle of the walls is set beside the camera of a frame, at most, when
    /// the lamp's reach is worked out. A scene with more walls and more frames in the dark than
    /// this has the distance worked out on every few of those frames, evenly; the lamp's energy
    /// goes straight from one to the next, which a camera that moves a little a frame does not show.
    /// </summary>
    private const long MostLampWork = 200_000_000;

    /// <summary>
    /// Writes the bundle of <paramref name="scene"/> into <paramref name="destination"/>, which
    /// stays open. Nothing is written anywhere else: each mesh goes from memory into the zip.
    /// </summary>
    /// <exception cref="ArgumentException">The scene has nothing to draw, or no frame.</exception>
    public static FlybyBundleSummary Write(FlybyBundleScene scene, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(destination);

        // A mesh is made into the bytes of its file when the writer comes to it, one at a time.
        var meshes = new Dictionary<string, Func<Stream>>(StringComparer.Ordinal);
        var (plan, photosShown) = Plan(
            scene,
            (path, mesh) => meshes[path] = () => new MemoryStream(PlyWriter.Write(mesh()), writable: false),
            (path, picture) => meshes[path] = () => new MemoryStream(picture, writable: false));

        var romanian = FlybyBundleNotes.IsRomanian(scene.Language);
        var revealed = (plan.Changes ?? []).Any(change => change.Reveal is not null);
        var byDate = scene.Reveal?.By == FlybyBundleRevealBy.Date;
        var dates = byDate ? FlybyBundleRevealValues.DatesOf(scene.Caves) : FlybyBundleRevealDates.None;
        var content = new RenderBundleContent
        {
            Plan = plan,
            Draft = ScenePlanBuilder.Draft(plan),
            Streams = meshes,
            Language = romanian ? "ro" : "en",
            ReadmeIntro = FlybyBundleNotes.Intro(romanian),
            ReadmeSections =
            [
                .. FlybyBundleNotes.Sections(
                        romanian,
                        daylightGoes: scene.Changes.Daylight.Any(key => key.Value < 1),
                        markers: (plan.Annotations ?? []).Count(a => a.Kind == PlanAnnotationKind.Marker),
                        photos: (plan.Annotations ?? []).Count(a => a.Kind == PlanAnnotationKind.Photo),
                        reveal: byDate
                            ? new FlybyBundleNotesReveal(ByDate: true, dates.LegsWithoutDate, dates.CavesOnTheSurveyDate, dates.CavesWithoutDate, revealed)
                            : revealed ? new FlybyBundleNotesReveal(ByDate: false) : null,
                        lampReaches: (plan.Changes ?? []).Any(change =>
                            change is { Energy: not null, Target.Id: { } lit }
                            && plan.Look.Lights.Any(light => light.Id == lit && light.Kind == PlanLightKind.Headlamp)))
                    .Select(section => new RenderBundleReadmeSection(section.Title, section.Text)),
            ],
            // What only the plan knows is left out is said with the rest: the photographs there
            // are at the stations of these caves beyond the ones the scene shows.
            NotCarried = scene.Photos.Found > photosShown.Count
                ? [.. scene.LeftOut, FlybyBundleOmissions.PhotosNotShown(scene.Photos.Found - photosShown.Count, romanian)]
                : scene.LeftOut,
            Captions = [.. Subtitles.OneAtATime(Captions(scene.Texts.Captions, ClipSeconds(scene)))],
        };
        var folder = FolderName(scene.Name);
        RenderBundleWriter.WriteZip(content, destination, folder);
        return new FlybyBundleSummary(
            folder,
            plan.Timeline.FrameCount,
            plan.Models.Count,
            (plan.Annotations ?? []).Count(a => a.Kind == PlanAnnotationKind.Marker),
            photosShown.Count)
        {
            PhotosShown = photosShown,
            RevealDates = dates,
        };
    }

    /// <summary>The name a bundle's one folder, its zip and its movie take from what the flyby is called.</summary>
    public static string FolderName(string? name) => Slug(name) + "-blender";

    /// <summary>The scene plan of <paramref name="scene"/>, as the JSON a bundle holds, with no mesh written.</summary>
    public static string PlanJson(FlybyBundleScene scene) =>
        ScenePlanSerializer.Write(Plan(scene, (_, _) => { }, (_, _) => { }).Plan);

    /// <param name="writeMesh">Takes each model's file by its path in the bundle, and what makes its mesh.</param>
    /// <param name="writePhoto">Takes each photograph's file by its path in the bundle.</param>
    private static (ScenePlan Plan, IReadOnlyList<int> PhotosShown) Plan(
        FlybyBundleScene scene, Action<string, Func<CaveMesh>> writeMesh, Action<string, byte[]> writePhoto)
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

        // The rock a cave's walls are drawn in and the lights are the survey library's own: its
        // rock made of noise, a lamp that rides the camera and its sun. Asked of it rather than
        // written down here, so they stay the library's when it changes them. Its sky is not
        // taken: what stands behind everything is the flyby's.
        var rock = ScenePlanBuilder.Material(new MaterialsSpec { Rock = RockMaterial.Procedural }, BoundingBox.Empty, RockMaterialId);
        IReadOnlyList<PlanLight> lights =
        [
            .. ScenePlanBuilder.Lighting(new LightingSpec { Rig = LightingRig.Headlamp }).Lights,
            .. ScenePlanBuilder.Lighting(new LightingSpec { Rig = LightingRig.SunSky }).Lights,
        ];

        var models = new List<PlanModel>();
        var timed = new List<PlanChange>();
        var materials = new List<PlanMaterial> { rock };
        var spheres = new List<(SceneVector Centre, double Radius)>();
        var layout = new Layout(frame, stretching, tallest, stretching || reliefs[0] != 1 ? new CaveVector3(1, 1, Rounded(reliefs[0])) : null);
        var stretch = stretching
            ? changes.Relief.Select(key => new PlanVectorKey(Seconds(key.Frame), new CaveVector3(1, 1, Rounded(key.Value)), Easing(key.Easing))).ToList()
            : null;

        // Where the caves appear part by part: over which seconds, on frames of the clip.
        var revealFrom = 0.0;
        var revealTo = 0.0;
        if (scene.Reveal is { } asked)
        {
            var last = scene.Poses.Count - 1;
            var from = Math.Clamp((int)Math.Round(asked.StartS * fps), 0, Math.Max(last - 1, 0));
            var to = Math.Clamp((int)Math.Round(asked.EndS * fps), from + 1, Math.Max(last, from + 1));
            (revealFrom, revealTo) = (Seconds(from), Seconds(to));
        }

        var stands = new List<Standing>();

        // The models a light shows: the walls, and the passages drawn at a size nobody measured.
        // The lamp reaches as far as the nearest of them.
        var lit = new List<(Standing Stand, IndexedMesh Mesh)>();

        // What changes of one model: the layer it belongs to shown and hidden — coming and going
        // eased, as a flyby eases everything —, how much of it is there, the colour laid over
        // it, its stretch, and the threshold it appears by where the scene reveals its cave.
        // Which number each cave appears by. By distance each cave has its own, from its entrances,
        // and appears over the whole of the reveal; by date they all run on one clock of years,
        // from the earliest day of any of them to the latest, so that the counter on the screen
        // says one year of all of them.
        var by = scene.Reveal?.By ?? FlybyBundleRevealBy.Distance;
        var revealed = scene.Caves.Select(cave => scene.Reveal is null ? null : FlybyBundleRevealValues.Of(cave, by)).ToList();
        (double Min, double Max)? clock = null;
        if (by == FlybyBundleRevealBy.Date && revealed.OfType<FlybyBundleRevealValues>().ToList() is { Count: > 0 } dated)
        {
            clock = (dated.Min(values => values.Min), dated.Max(values => values.Max));
            revealed = [.. revealed.Select(values => values?.From(clock.Value.Min))];
        }

        void Changing(string id, FlybyBundleLayerChanges layer, IReadOnlyList<FlybyBundleTintKey>? tint, FlybyBundleRevealValues? reveal)
        {
            var (threshold, front) = reveal is null
                ? (null, null)
                : Revealing(PlanTarget(id), clock ?? (reveal.Min, reveal.Max), revealFrom, revealTo, fps);
            var change = new PlanChange
            {
                Target = new PlanChangeTarget { Id = id },
                Visible = layer.Shown.Count > 0
                    ? [
                        .. layer.Shown.Select(key => new PlanVisibleKey(
                            Seconds(key.Frame), key.Shown, Seconds(key.FadeFrames), key.FadeFrames > 0 ? PlanEasing.Smooth : PlanEasing.Linear)),
                    ]
                    : null,
                Opacity = layer.Opacity.Count > 0 ? Numbers(layer.Opacity, Seconds) : null,
                Tint = tint is { Count: > 0 }
                    ? [
                        .. tint.Select((key, index) =>
                            new PlanTintKey(Seconds(key.Frame), Linear(key.Colour) ?? default, Rounded(key.Strength), Easing(key.Easing))
                            {
                                Mix = Mixed(index > 0 ? tint[index - 1].Colour : null, key.Colour, key.Easing),
                            }),
                    ]
                    : null,
                Scale = stretch,
                Reveal = threshold,
                RevealFront = front,
            };
            if (change is not { Visible: null, Opacity: null, Tint: null, Scale: null, Reveal: null })
            {
                timed.Add(change);
            }
        }

        for (var i = 0; i < scene.Caves.Count; i++)
        {
            var cave = scene.Caves[i];
            var number = (i + 1).ToString("00", CultureInfo.InvariantCulture);
            var reveal = revealed[i];

            // A colour over the walls is a track only where it is ever seen.
            var wallsTint = cave.WallsTint ?? changes.Walls.Tint;
            var tinted = wallsTint.Any(key => key.Strength > 0) ? wallsTint : null;

            if (cave.Walls is { Mesh.TriangleCount: > 0 } walls)
            {
                var id = $"cave-{number}-walls";
                models.Add(Place(layout, id, i, WallsRole, RockMaterialId, walls, cave.AltitudeShiftM, spheres, stands, writeMesh, reveal?.On(walls)));
                lit.Add((stands[^1], walls.Mesh));
                Changing(id, changes.Walls, tinted, reveal);
            }

            if (cave.Estimated is { Mesh.TriangleCount: > 0 } estimated)
            {
                var id = $"cave-{number}-estimated";
                var colour = cave.EstimatedColor ?? GlbWriter.WallColor;
                var material = FlatMaterial("estimated", colour, materials);
                models.Add(Place(layout, id, i, EstimatedRole, material, estimated, cave.AltitudeShiftM, spheres, stands, writeMesh, reveal?.On(estimated)));
                lit.Add((stands[^1], estimated.Mesh));
                Changing(id, changes.Walls, tinted, reveal);
            }

            if (cave.Legs.Count > 0)
            {
                // The lines' colour over the clip: this cave's own, else every cave's, else the one
                // the look gives them for the whole of it.
                var colours = cave.LinesTint ?? (changes.Lines.Tint.Count > 0 ? changes.Lines.Tint : null);
                var oneColour = colours is null
                    ? scene.Look.LineColour
                    : colours.All(key => key.Strength > 0) ? colours[0].Colour : null;
                if (Lines(layout, cave, scene.Look, oneColour, number, materials, reveal) is { } lines)
                {
                    var id = $"cave-{number}-lines";
                    models.Add(Place(layout, id, i, LinesRole, lines.Material, lines.Mesh, cave.AltitudeShiftM, spheres, stands, writeMesh, lines.Reveal));
                    Changing(
                        id,
                        cave.LinesStandIn ? changes.Walls : changes.Lines,
                        colours is { Count: > 1 } || (colours is not null && oneColour is null && colours.Any(key => key.Strength > 0)) ? colours : null,
                        reveal);
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
            sky ??= "#000000";
        }

        var world = Linear(sky) is { } behind
            ? new PlanWorld { Kind = PlanWorldKind.Color, Color = behind, Strength = 1 }
            : null;

        // The daylight: the sun as strong as there is daylight, and the sky with it — a sky that
        // lights nothing is not seen either, which is what stands behind a cave from inside it:
        // the ground hides the sky from a camera under it, and a bundle has no ground to do it.
        // The lamp on the camera takes over where the daylight goes (below).
        var daylight = changes.Daylight.Any(key => key.Value < 1) ? changes.Daylight : null;
        if (daylight is not null)
        {
            var sun = lights.Single(light => light.Kind == PlanLightKind.Sun);
            timed.Add(new PlanChange
            {
                Target = new PlanChangeTarget { Id = sun.Id },
                Energy = Numbers(daylight, Seconds, sun.Energy),
            });
        }

        if (world is not null && (changes.Sky.Count > 1 || daylight is not null))
        {
            timed.Add(new PlanChange
            {
                WorldColor = changes.Sky.Count > 1
                    ? [
                        .. changes.Sky.Select((key, index) =>
                            new PlanColorKey(Seconds(key.Frame), Linear(key.Colour) ?? default, Easing(key.Easing))
                            {
                                Mix = Mixed(index > 0 ? changes.Sky[index - 1].Colour : null, key.Colour, key.Easing),
                            }),
                    ]
                    : null,
                WorldStrength = daylight is null ? null : Numbers(daylight, Seconds, world.Strength),
            });
        }

        // Each frame's camera, raised by the rule the models are stretched by, at the relief the
        // models have at that frame.
        var views = new List<SceneView>(scene.Poses.Count);
        for (var i = 0; i < scene.Poses.Count; i++)
        {
            var p = scene.Poses[i];
            views.Add(frame.ViewAbout(p.Longitude, p.Latitude, p.HeightM, p.HeadingDeg, p.PitchDeg, p.RollDeg, reliefs[i]));
        }

        var lenses = scene.Poses
            .Select(p => Rounded(SceneTangentFrame.FocalLength(
                SceneTangentFrame.HorizontalFieldOfView(p.LensDeg, scene.Width, scene.Height), CameraPlanner.SensorWidthMm)))
            .ToList();
        var lensChanges = lenses.Any(lens => lens != lenses[0]);

        // How near and how far each frame's camera draws; one pair for the clip where one pair
        // does, and a pair a pose where the flight goes through more depth than one can hold.
        var ranges = views.Select(view => ClipRange(view, spheres)).ToList();
        var clipStart = ranges.Min(range => range.Start);
        var clipEnd = ranges.Max(range => range.End);
        var ownRanges = clipEnd / clipStart > DeepestOneRange;

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
                ClipStart = ownRanges ? ranges[i].Start : null,
                ClipEnd = ownRanges ? ranges[i].End : null,
            });
        }

        var name = string.IsNullOrWhiteSpace(scene.Name) ? "Flyby" : scene.Name.Trim();
        var timeline = new PlanTimeline { Fps = fps, FrameCount = views.Count };

        // What the scene points out: pinned to the caves' models, sized by the nearest the camera
        // comes, shown as the flyby shows its layer.
        var pointed = Annotations(scene, stands, layout, views, reliefs, timeline, writePhoto);
        timed.AddRange(pointed.Changes);

        var plan = new ScenePlan
        {
            Generator = "SilexGIS",
            Name = name,
            // The modules every plan has; the ones its sections need are listed below, by the
            // library, each at its place in the order they run.
            Features = ScenePlan.BasicFeatures,
            // Zero, and no coordinate system: the frame is laid out about a point it does not name.
            Frame = new PlanFrame(),
            Timeline = timeline,
            Models = models,
            Annotations = pointed.Annotations.Count > 0 ? pointed.Annotations : null,
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
                World = world,
            },
            Changes = timed.Count > 0 ? timed : null,
            Output = new PlanOutput
            {
                Kind = PlanOutputKind.Video,
                Container = PlanContainer.Mp4,
                Directory = OutputDirectory,
                BaseName = Slug(scene.Name),
            },
        };

        // The lamp on the camera, turned up where the daylight goes by how far the nearest wall is:
        // keys of its energy, worked out by the survey library from the distances given it.
        if (LampDistances(lit, views, reliefs, layout, Played(changes.Daylight, views.Count, 1), Seconds) is { } reach)
        {
            plan = LampReach.Apply(plan, distances: reach);
        }

        // The modules its sections need, and what is written over the picture — laid out for the
        // plan as it stands, so the north arrow turns by the plan's own poses.
        var texts = Texts(scene, plan);

        // By date, the year the reveal has got to, counted on the screen from the threshold of a
        // model that keys it: every revealed model is keyed on the one clock, so any of them says it.
        if (clock is { } years && timed.FirstOrDefault(change => change.Reveal is not null)?.Target is { } counted)
        {
            texts = (texts ?? new PlanScreenTexts()) with
            {
                Items = [.. texts?.Items ?? [], RevealPlanner.Counter(counted, years.Min, years.Max)],
            };
        }

        plan = ScenePlanBuilder.WithTexts(ScenePlanBuilder.WithNeededFeatures(plan), texts);
        RefuseOnePropertyChangedByIdAndByRole(plan);

        var errors = ScenePlanValidator.Validate(plan);
        if (errors.Count > 0)
        {
            // What was sent has already been checked field by field; a plan that still does not
            // pass is this code's mistake, and is reported as one rather than handed over.
            throw new InvalidOperationException(
                "The scene plan of a flyby did not pass its validator: "
                + string.Join("; ", errors.Take(5).Select(e => $"{e.Path}: {e.Message}")));
        }

        return (plan, pointed.PhotosShown);
    }

    // ---- models ----

    /// <summary>
    /// How the models of one scene are laid out: the frame, whether the relief changes while the
    /// clip runs (each model is then stretched about a point level with the frame's zero), the
    /// tallest relief of the clip, and the stretch every model starts with.
    /// </summary>
    private sealed record Layout(SceneTangentFrame Frame, bool Stretching, double Tallest, CaveVector3? Stretch);

    /// <summary>
    /// Where a model stands, for whatever is pinned to it: the cave it is of, the place on the
    /// globe its own zero is — at the true height the scene drew it —, how far its mesh was
    /// raised along its own up, and how the plan puts it.
    /// </summary>
    private sealed record Standing(
        string Id,
        int Cave,
        LocalTangentFrame About,
        double LiftM,
        SceneVector Translation,
        SceneVector Axis,
        double Degrees);

    /// <param name="reveal">A number for every vertex of the mesh, where the scene reveals its cave.</param>
    private static PlanModel Place(
        Layout layout,
        string id,
        int cave,
        string role,
        string material,
        FlybyBundleMesh mesh,
        double altitudeShiftM,
        List<(SceneVector Centre, double Radius)> spheres,
        List<Standing> stands,
        Action<string, Func<CaveMesh>> writeMesh,
        IReadOnlyList<double>? reveal)
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
        writeMesh(path, () => Mesh(mesh.Mesh, lift) with { Reveal = reveal });
        stands.Add(new Standing(
            id, cave, new LocalTangentFrame(mesh.AnchorLongitude, mesh.AnchorLatitude, height), lift, translation, axis, degrees));

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
    private static CaveMesh Mesh(IndexedMesh mesh, double lift)
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

        return new CaveMesh { Vertices = vertices, Triangles = triangles };
    }

    /// <summary>
    /// A cave's survey legs as thin tubes about the first leg's start, and the material they wear:
    /// one colour, or the colours by depth below the cave's highest point. Either is a material
    /// no light changes: a line is a drawing, not a thing in the cave, and reads the same under
    /// the sun, by the lamp and in the dark, as it does on the screen.
    /// </summary>
    /// <param name="oneColour">The one colour the lines have for the whole clip; null for their colours by depth.</param>
    /// <param name="reveal">The numbers the cave appears by, where the scene reveals it.</param>
    private static (FlybyBundleMesh Mesh, string Material, IReadOnlyList<double>? Reveal)? Lines(
        Layout layout,
        FlybyBundleCave cave,
        FlybyBundleLook look,
        string? oneColour,
        string number,
        List<PlanMaterial> materials,
        FlybyBundleRevealValues? reveal)
    {
        var first = cave.Legs[0];
        var about = new LocalTangentFrame(first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        var tubes = SurveyLineTubes.Build(
            cave.Legs.Select(leg =>
            {
                var (from, to) = reveal?.Of(leg) ?? (0, 0);
                return (
                    about.ToLocal(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM),
                    about.ToLocal(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM),
                    from,
                    to);
            }),
            LineRadiusM,
            out var numbers);
        if (tubes.TriangleCount == 0)
        {
            return null;
        }

        var appearing = reveal is null ? null : numbers;

        var placed = new FlybyBundleMesh(tubes, first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        var bands = look.DepthBands
            .Select(band => (band.FromM, Colour: Linear(band.Colour)))
            .Where(band => band.Colour is not null && double.IsFinite(band.FromM) && band.FromM >= 0)
            .OrderBy(band => band.FromM)
            .Select(band => (band.FromM, Colour: band.Colour!.Value))
            .ToList();

        if (Linear(oneColour) is not null || bands.Count == 0)
        {
            var colour = Linear(oneColour) is { } given ? (given.R, given.G, given.B) : GlbWriter.WallColor;
            return (placed, FlatMaterial(LinesMaterialId, colour, materials, unlit: true), appearing);
        }

        // The colours by depth are a ramp over height in the lines' own mesh, in true metres: a
        // colour is a depth below the cave's highest leg and stays with the leg it is on, however
        // tall the relief draws the cave and whether or not the relief changes. The mesh's zero is
        // the first leg's start, raised by what the mesh itself is raised by where it is stretched
        // about another level.
        var top = cave.Legs.Max(leg => Math.Max(leg.FromAltitudeM, leg.ToAltitudeM));
        var bottom = cave.Legs.Min(leg => Math.Min(leg.FromAltitudeM, leg.ToAltitudeM));
        var lift = layout.Stretching
            ? layout.Frame.PlacementAbout(first.FromLongitude, first.FromLatitude, first.FromAltitudeM + cave.AltitudeShiftM).LiftM
            : 0;
        var high = top - first.FromAltitudeM + lift;
        var span = Math.Max(top - bottom, 1.0);

        // A ramp in bands: one stop a band, at the height the band begins at from below — the
        // depth the next band down starts at —, its colour holding from there up to the next
        // stop. The colour changes at a stop at once, as the scene cuts a leg where a band begins.
        var stops = new List<PlanColorStop>
        {
            new(0, bands.Last(band => band.FromM <= span || band.FromM == bands[0].FromM).Colour),
        };
        for (var b = bands.Count - 1; b >= 1; b--)
        {
            // As a share of the way from the lowest line to the highest.
            var at = 1 - (bands[b].FromM / span);
            if (at > stops[^1].At + 1e-6 && at < 1 - 1e-6)
            {
                stops.Add(new PlanColorStop(Rounded(at), bands[b - 1].Colour));
            }
        }

        if (stops.Count == 1)
        {
            // One band from top to bottom: a ramp has two stops at the least.
            stops.Add(new PlanColorStop(1, stops[0].Color));
        }

        var id = $"{LinesMaterialId}-{number}";
        materials.Add(new PlanMaterial
        {
            Id = id,
            Kind = PlanMaterialKind.HeightRamp,
            Color = bands[0].Colour,
            Roughness = 0.9,
            Stops = stops,
            Banded = true,
            Unlit = true,
            Space = PlanRampSpace.Model,
            Low = Rounded(high - span),
            High = Rounded(high),
        });
        return (placed, id, appearing);
    }

    // ---- what changes ----

    /// <summary>
    /// The relief at every frame: what the keys give, as a renderer playing them works it out.
    /// With no key, the scene's one factor.
    /// </summary>
    private static double[] Reliefs(FlybyBundleScene scene)
    {
        var reliefs = Played(scene.Changes.Relief, scene.Poses.Count, scene.Relief);

        // To what the plan writes a stretch to, so the camera and the models read one number.
        for (var i = 0; i < reliefs.Length; i++)
        {
            reliefs[i] = Rounded(reliefs[i]);
        }

        return reliefs;
    }

    /// <summary>
    /// A track of numbers at every frame of the clip, as a renderer playing its keys works it out:
    /// held before the first key and after the last, and between two keys moved as the later one
    /// says. A key before the clip's first frame is one the clip begins on the way from. Of the
    /// keys on one frame the second is what that frame shows, and the last where the way to the
    /// next key starts from — the second again, unless a third says otherwise.
    /// </summary>
    /// <param name="none">The value at every frame of a track with no key.</param>
    private static double[] Played(IReadOnlyList<FlybyBundleNumberKey> keys, int frames, double none)
    {
        var values = new double[frames];
        var next = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (keys.Count == 0)
            {
                values[i] = none;
                continue;
            }

            while (next < keys.Count && keys[next].Frame <= i)
            {
                next++;
            }

            if (next == 0)
            {
                values[i] = keys[0].Value;
                continue;
            }

            // The keys on the last frame that has any, at this frame or before it.
            var last = next - 1;
            var first = last;
            while (first > 0 && keys[first - 1].Frame == keys[last].Frame)
            {
                first--;
            }

            if (keys[last].Frame == i)
            {
                values[i] = keys[Math.Min(first + 1, last)].Value;
                continue;
            }

            if (next == keys.Count)
            {
                values[i] = keys[last].Value;
                continue;
            }

            var (before, after) = (keys[last], keys[next]);
            var share = (i - before.Frame) / (double)(after.Frame - before.Frame);
            values[i] = after.Easing switch
            {
                FlybyBundleEasing.Constant => before.Value,
                FlybyBundleEasing.Smooth => before.Value + ((after.Value - before.Value) * share * share * (3 - (2 * share))),
                _ => before.Value + ((after.Value - before.Value) * share),
            };
        }

        return values;
    }

    /// <summary>A track of numbers as the plan's keys, each number multiplied by <paramref name="times"/>.</summary>
    private static List<PlanNumberKey> Numbers(IReadOnlyList<FlybyBundleNumberKey> keys, Func<int, double> seconds, double times = 1) =>
        [.. keys.Select(key => new PlanNumberKey(seconds(key.Frame), Rounded(key.Value * times), Easing(key.Easing)))];

    /// <summary>
    /// How a colour is mixed on its way to a key from the key before: as a screen mixes two
    /// colours, because that is what the flyby showed between them. Said only where there is a way
    /// to go — another colour before, and a key that does not jump.
    /// </summary>
    private static PlanColorMix Mixed(string? before, string colour, FlybyBundleEasing easing) =>
        before is not null && easing != FlybyBundleEasing.Constant && !string.Equals(before, colour, StringComparison.OrdinalIgnoreCase)
            ? PlanColorMix.Srgb
            : PlanColorMix.Linear;

    private static PlanEasing Easing(FlybyBundleEasing easing) => easing switch
    {
        FlybyBundleEasing.Smooth => PlanEasing.Smooth,
        FlybyBundleEasing.Constant => PlanEasing.Constant,
        _ => PlanEasing.Linear,
    };

    /// <summary>The id of a material of one plain colour, added to the list the first time it is asked for.</summary>
    /// <param name="unlit">Whether no light changes it: it then shows its colour as a screen shows it.</param>
    private static string FlatMaterial(string prefix, (double R, double G, double B) colour, List<PlanMaterial> materials, bool unlit = false)
    {
        var rounded = new ColorRgb(Rounded(colour.R), Rounded(colour.G), Rounded(colour.B));
        var known = materials.FirstOrDefault(m => m.Kind == PlanMaterialKind.Flat && m.Color == rounded && m.Id.StartsWith(prefix, StringComparison.Ordinal));
        if (known is not null)
        {
            return known.Id;
        }

        var taken = materials.Count(m => m.Id.StartsWith(prefix, StringComparison.Ordinal));
        var id = taken == 0 ? prefix : $"{prefix}-{(taken + 1).ToString(CultureInfo.InvariantCulture)}";
        materials.Add(new PlanMaterial { Id = id, Kind = PlanMaterialKind.Flat, Color = rounded, Roughness = 0.9, Unlit = unlit });
        return id;
    }

    // ---- camera ----

    /// <summary>
    /// How near and how far the camera of one frame draws: far enough to hold every model, and as
    /// near as how close it is to the closest of them allows.
    /// </summary>
    private static (double Start, double End) ClipRange(SceneView view, IReadOnlyList<(SceneVector Centre, double Radius)> spheres)
    {
        var farthest = 0.0;
        var clearest = double.PositiveInfinity;
        foreach (var (centre, radius) in spheres)
        {
            var distance = (view.Eye - centre).Length;
            farthest = Math.Max(farthest, distance + radius);
            clearest = Math.Min(clearest, Math.Max(0, distance - radius));
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
    /// its places, its sizes, its north arrow turning by the plan's own poses — and timed as the
    /// flyby times them.
    /// </summary>
    private static PlanScreenTexts? Texts(FlybyBundleScene scene, ScenePlan plan)
    {
        var said = scene.Texts;
        var clip = ClipSeconds(scene);
        var title = said.Title is { Seconds: > 0 } t && !string.IsNullOrWhiteSpace(t.Text) ? t : null;
        var end = said.End is { Seconds: > 0 } e && !string.IsNullOrWhiteSpace(e.Text) ? e : null;

        var overlays = new OverlaySpec
        {
            Title = title?.Text.Trim(),
            Subtitle = title is null || string.IsNullOrWhiteSpace(title.Subtitle) ? null : title.Subtitle.Trim(),

            // The flyby's title has gone by the moment it names, having faded over the time the
            // flyby fades a card over: the library's card stands for its seconds and then goes.
            TitleSeconds = title is null ? null : Rounded(Math.Min(title.Seconds, clip) - CardFade(title, clip)),
            TitleFade = title is null ? null : Rounded(CardFade(title, clip)),
            EndCard = end is null
                ? null
                : new EndCardSpec
                {
                    Text = end.Text.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(end.Subtitle) ? null : end.Subtitle.Trim(),
                    Seconds = Rounded(Math.Min(end.Seconds, clip)),
                    Fade = Rounded(CardFade(end, clip)),

                    // Over the last of the flight, which goes on under it, and on no card of
                    // black: the clip is as long as the flyby.
                    Backdrop = 0,
                    OverScene = true,
                },
            Captions = Captions(said.Captions, clip),
            Corner = string.IsNullOrWhiteSpace(said.Corner) ? null : said.Corner.Trim(),
            CornerCaptions = Captions(said.CornerCaptions, clip),
            FadeFromBlack = Rounded(Math.Clamp(said.FadeInS, 0, clip)),
            FadeToBlack = Rounded(Math.Clamp(said.FadeOutS, 0, clip)),
            NorthArrow = said.NorthArrow,
        };

        var laid = ScreenTextPlanner.Plan(overlays, plan);
        if (laid is null)
        {
            return null;
        }

        // The layout and the times are the library's, from what was said above. One thing it
        // cannot be told: a title that fades has to fade in as well, and the flyby's is there
        // from the first frame — a recording opens on it.
        return laid with
        {
            Items =
            [
                .. laid.Items.Select(item =>
                    item.Role is ScreenTextPlanner.TitleRole or ScreenTextPlanner.SubtitleRole ? item with { FadeIn = null } : item),
            ],
        };
    }

    /// <summary>
    /// How long a card takes to go, or to come: the time the flyby fades a card over, and no more
    /// than a quarter of the time the card is there.
    /// </summary>
    private static double CardFade(FlybyBundleCard card, double clipSeconds) =>
        Math.Min(CardFadeSeconds, Math.Min(card.Seconds, clipSeconds) / 4);

    // ---- small things ----

    private static ColorRgb? Linear(string? hex) =>
        GlbWriter.LinearFromHex(hex) is { } c ? new ColorRgb(Rounded(c.R), Rounded(c.G), Rounded(c.B)) : null;

    private static CaveVector3 Vector(SceneVector v) => new(v.X, v.Y, v.Z);

    private static CaveVector3 Rounded(SceneVector v) => new(Rounded(v.X), Rounded(v.Y), Rounded(v.Z));

    /// <summary>
    /// Refuses a plan in which one property of one thing is changed both by a change that names
    /// it and by one that names its role. The renderer lets the change by name win, so such a
    /// plan plays — but this code never means it: each property of a model is keyed by its name
    /// and each of a layer of markers by its role, and a property keyed both ways is two rules
    /// saying two things, one of which would be silently lost. Two changes by name, or two by
    /// role, the plan's own validator refuses.
    /// </summary>
    internal static void RefuseOnePropertyChangedByIdAndByRole(ScenePlan plan)
    {
        if (plan.Changes is not { Count: > 0 })
        {
            return;
        }

        using var json = JsonDocument.Parse(ScenePlanSerializer.Write(plan));
        var root = json.RootElement;
        var ofRole = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var section in new[] { "models", "lines", "annotations" })
        {
            if (!root.TryGetProperty(section, out var things) || things.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var thing in things.EnumerateArray())
            {
                if (thing.TryGetProperty("role", out var role) && role.GetString() is { } named
                    && thing.TryGetProperty("id", out var id) && id.GetString() is { } name)
                {
                    (ofRole.TryGetValue(named, out var members) ? members : ofRole[named] = []).Add(name);
                }
            }
        }

        var byId = new HashSet<(string Id, string Property)>();
        var byRole = new HashSet<(string Id, string Property)>();
        foreach (var change in root.GetProperty("changes").EnumerateArray())
        {
            if (!change.TryGetProperty("target", out var target))
            {
                continue;
            }

            var named = target.TryGetProperty("id", out var id) ? id.GetString() : null;
            IReadOnlyList<string> things = named is not null
                ? [named]
                : target.TryGetProperty("role", out var role) && role.GetString() is { } r && ofRole.TryGetValue(r, out var members)
                    ? members
                    : [];
            foreach (var property in change.EnumerateObject().Where(p => p.Name != "target"))
            {
                foreach (var thing in things)
                {
                    (named is not null ? byId : byRole).Add((thing, property.Name));
                }
            }
        }

        if (byId.FirstOrDefault(byRole.Contains) is { Id: not null } twice)
        {
            throw new InvalidOperationException(
                $"The scene plan of a flyby changes '{twice.Property}' of '{twice.Id}' both by its id and by its role.");
        }
    }

    private static double Rounded(double value) => Math.Round(value, 6);

    /// <summary>A part of a turn: nine decimals hold a direction to a millionth of a degree.</summary>
    private static double Turn(double value) => Math.Round(value, 9);

    /// <summary>
    /// A name as a plain file name every file system takes: lower-case letters and digits in words
    /// joined by hyphens; "flyby" when nothing of it is left. A name Windows gives to a device
    /// (<c>con</c>, <c>nul</c>, <c>com1</c>…), which would open the device rather than a file on a
    /// Windows machine and which the survey library refuses as a base name, has <c>-flyby</c> added.
    /// </summary>
    public static string Slug(string? name)
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
        return text.Length == 0 ? "flyby" : PortableNames.IsPortable(text) ? text : text + "-flyby";
    }
}
