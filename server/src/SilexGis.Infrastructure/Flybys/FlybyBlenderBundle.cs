// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
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
/// zero, and each frame's camera is raised along its own up by the same rule, with the relief
/// those very keys give at that frame — so a cave and the camera looking at it are never drawn at
/// two different reliefs, nor by two different rules. A colour over the walls or over one cave's
/// walls, the colour of the lines, a layer shown, hidden or faded and the colour of the sky are
/// tracks on the models and on the world. The sun and the sky are as strong as the daylight the
/// flyby had at each frame: they go out while its camera is under the ground. The lamp on the
/// camera is always on.
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

    /// <summary>
    /// How many times farther than its nearest limit a camera may draw before each frame is given
    /// limits of its own: past this a depth buffer runs out of steps, and a flight that starts far
    /// above a country and ends a metre from a wall has both ends in one clip.
    /// </summary>
    private const double DeepestOneRange = 100_000;

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
        var plan = Plan(scene, (path, mesh) => meshes[path] = () => new MemoryStream(PlyWriter.Write(mesh()), writable: false));

        var romanian = FlybyBundleNotes.IsRomanian(scene.Language);
        var content = new RenderBundleContent
        {
            Plan = plan,
            Draft = ScenePlanBuilder.Draft(plan),
            Streams = meshes,
            Language = romanian ? "ro" : "en",
            ReadmeIntro = FlybyBundleNotes.Intro(romanian),
            ReadmeSections =
            [
                .. FlybyBundleNotes.Sections(romanian, daylightGoes: scene.Changes.Daylight.Any(key => key.Value < 1))
                    .Select(section => new RenderBundleReadmeSection(section.Title, section.Text)),
            ],
            NotCarried = scene.LeftOut,
            Captions = [.. Subtitles.OneAtATime(Captions(scene.Texts.Captions, ClipSeconds(scene)))],
        };
        var folder = FolderName(scene.Name);
        RenderBundleWriter.WriteZip(content, destination, folder);
        return new FlybyBundleSummary(folder, plan.Timeline.FrameCount, plan.Models.Count);
    }

    /// <summary>The name a bundle's one folder, its zip and its movie take from what the flyby is called.</summary>
    public static string FolderName(string? name) => Slug(name) + "-blender";

    /// <summary>The scene plan of <paramref name="scene"/>, as the JSON a bundle holds, with no mesh written.</summary>
    public static string PlanJson(FlybyBundleScene scene) =>
        ScenePlanSerializer.Write(Plan(scene, (_, _) => { }));

    /// <param name="writeMesh">Takes each model's file by its path in the bundle, and what makes its mesh.</param>
    private static ScenePlan Plan(FlybyBundleScene scene, Action<string, Func<CaveMesh>> writeMesh)
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

        // What changes of one model: the layer it belongs to shown and hidden — coming and going
        // eased, as a flyby eases everything —, how much of it is there, the colour laid over
        // it, and its stretch.
        void Changing(string id, FlybyBundleLayerChanges layer, IReadOnlyList<FlybyBundleTintKey>? tint)
        {
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
                models.Add(Place(layout, id, WallsRole, RockMaterialId, walls, cave.AltitudeShiftM, spheres, writeMesh));
                Changing(id, changes.Walls, tinted);
            }

            if (cave.Estimated is { Mesh.TriangleCount: > 0 } estimated)
            {
                var id = $"cave-{number}-estimated";
                var colour = cave.EstimatedColor ?? GlbWriter.WallColor;
                var material = FlatMaterial("estimated", colour, materials);
                models.Add(Place(layout, id, EstimatedRole, material, estimated, cave.AltitudeShiftM, spheres, writeMesh));
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
                    models.Add(Place(layout, id, LinesRole, lines.Material, lines.Mesh, cave.AltitudeShiftM, spheres, writeMesh));
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
            sky ??= "#000000";
        }

        var world = Linear(sky) is { } behind
            ? new PlanWorld { Kind = PlanWorldKind.Color, Color = behind, Strength = 1 }
            : null;

        // The daylight: the sun as strong as there is daylight, and the sky with it — a sky that
        // lights nothing is not seen either, which is what stands behind a cave from inside it.
        // The lamp on the camera has no part in this.
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

        var plan = new ScenePlan
        {
            Generator = "SilexGIS",
            Name = name,
            // The modules every plan has; the ones its sections need are listed below, by the
            // library, each at its place in the order they run.
            Features = ScenePlan.BasicFeatures,
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

        // The modules its sections need, and what is written over the picture — laid out for the
        // plan as it stands, so the north arrow turns by the plan's own poses.
        plan = ScenePlanBuilder.WithTexts(ScenePlanBuilder.WithNeededFeatures(plan), Texts(scene, plan));

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
        double altitudeShiftM,
        List<(SceneVector Centre, double Radius)> spheres,
        Action<string, Func<CaveMesh>> writeMesh)
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
        writeMesh(path, () => Mesh(mesh.Mesh, lift));

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
    /// one colour, or the colours by depth below the cave's highest point.
    /// </summary>
    /// <param name="oneColour">The one colour the lines have for the whole clip; null for their colours by depth.</param>
    private static (FlybyBundleMesh Mesh, string Material)? Lines(
        Layout layout,
        FlybyBundleCave cave,
        FlybyBundleLook look,
        string? oneColour,
        string number,
        List<PlanMaterial> materials)
    {
        var first = cave.Legs[0];
        var about = new LocalTangentFrame(first.FromLongitude, first.FromLatitude, first.FromAltitudeM);
        var tubes = SurveyLineTubes.Build(
            cave.Legs.Select(leg => (
                about.ToLocal(leg.FromLongitude, leg.FromLatitude, leg.FromAltitudeM),
                about.ToLocal(leg.ToLongitude, leg.ToLatitude, leg.ToAltitudeM))),
            LineRadiusM);
        if (tubes.TriangleCount == 0)
        {
            return null;
        }

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
            return (placed, FlatMaterial(LinesMaterialId, colour, materials));
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

        var stops = new List<PlanColorStop>
        {
            new(0, bands.Last(band => band.FromM <= span || band.FromM == bands[0].FromM).Colour),
        };
        for (var b = bands.Count - 1; b >= 1; b--)
        {
            // Where this band begins, as a share of the way from the lowest line to the highest;
            // the colour changes there at once, as the scene cuts a leg where a band begins.
            var at = 1 - (bands[b].FromM / span);
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
            Space = PlanRampSpace.Model,
            Low = Rounded(high - span),
            High = Rounded(high),
        });
        return (placed, id);
    }

    // ---- what changes ----

    /// <summary>
    /// The relief at every frame: what the keys give, as a renderer playing them works it out —
    /// held before the first key and after the last, and between two keys moved as the later one
    /// says; of two keys on one frame the later is the value from that frame on. With no key, the
    /// scene's one factor.
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
            TitleSeconds = title is null ? null : Rounded(Math.Min(title.Seconds, clip)),
            EndCard = end is null
                ? null
                : new EndCardSpec
                {
                    Text = end.Text.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(end.Subtitle) ? null : end.Subtitle.Trim(),
                    Seconds = Rounded(Math.Min(end.Seconds, clip)),

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

        // The layout is the library's. How long a card takes to go and to come is the flyby's,
        // and is put back: its title has gone by the moment it names, and both cards fade over
        // the time the flyby fades them over.
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
                items.Add(item with { FadeIn = Rounded(Math.Min(CardFadeSeconds, Math.Min(end.Seconds, clip) / 4)) });
            }
            else
            {
                items.Add(item);
            }
        }

        return laid with { Items = items };
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
