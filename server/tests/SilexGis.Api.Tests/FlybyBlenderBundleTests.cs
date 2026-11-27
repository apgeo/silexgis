// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// A flyby written as a render bundle: what the zip holds, what its scene plan says about the
/// caves, the camera, the look and the texts — and what it does not say.
///
/// <para>
/// Everything here is invented and built in code: two caves at a made-up place, a camera that
/// turns about the first. Nothing is rendered; the plan and the zip are read back as the plain
/// JSON and files they are.
/// </para>
/// </summary>
public class FlybyBlenderBundleTests
{
    /// <summary>An invented place.</summary>
    private const double Lon = 7.5;
    private const double Lat = 41.25;
    private const double Top = 830;

    [Fact]
    public void The_zip_is_one_folder_with_the_plan_its_meshes_the_player_and_its_licence()
    {
        var (entries, _) = Bundle(Scene());

        var names = entries.Keys.Order(StringComparer.Ordinal).ToList();
        names.ShouldAllBe(name => name.StartsWith("two-caves-blender/", StringComparison.Ordinal));
        string[] expected =
        [
            "LICENSE", "NOTICE", "README.txt", "render.cmd", "render.py", "render.sh",
            "scene-plan.json", "scene-plan.draft.json",
            "cave-01-walls.ply", "cave-01-estimated.ply", "cave-01-lines.ply", "cave-02-lines.ply",
            "thide_player/core.py",
        ];
        foreach (var name in expected)
        {
            names.ShouldContain("two-caves-blender/" + name);
        }

        // A mesh is a binary PLY, which every Blender reads without an add-on.
        Encoding.ASCII.GetString(entries["two-caves-blender/cave-01-walls.ply"], 0, 3).ShouldBe("ply");

        // Nothing but the plan, its meshes, the notes and the player: no file of this application's own.
        names.Where(n => !n.Contains("/thide_player/", StringComparison.Ordinal)).Count().ShouldBe(12);
    }

    [Theory]
    [InlineData("Con", "con-flyby")]
    [InlineData("NUL", "nul-flyby")]
    [InlineData("com1", "com1-flyby")]
    [InlineData("LPT9", "lpt9-flyby")]
    [InlineData("Aux.", "aux-flyby")]
    [InlineData("Peștera Con", "pestera-con")]
    [InlineData("Lpt9.txt", "lpt9-txt")]
    public void A_name_that_is_a_device_of_Windows_is_a_base_name_with_flyby_added(string name, string slug)
    {
        FlybyBlenderBundle.Slug(name).ShouldBe(slug);
        FlybyBlenderBundle.FolderName(name).ShouldBe(slug + "-blender");

        // And the survey library's checks, which refuse a device name as a base name on every
        // system, take the bundle's plans: it is written, not refused.
        var (entries, plan) = Bundle(Scene() with { Name = name });
        plan.GetProperty("output").GetProperty("baseName").GetString().ShouldBe(slug);
        entries.Keys.ShouldAllBe(entry => entry.StartsWith(slug + "-blender/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_plan_is_laid_out_about_a_point_it_does_not_name()
    {
        var (_, plan) = Bundle(Scene());

        var frame = plan.GetProperty("frame");
        frame.GetProperty("units").GetString().ShouldBe("metres");
        frame.TryGetProperty("crs", out _).ShouldBeFalse();
        if (frame.TryGetProperty("origin", out var origin))
        {
            origin.EnumerateArray().Select(n => n.GetDouble()).ShouldBe([0, 0, 0]);
        }

        // Neither the plan nor any text file of the bundle carries the place the scene was laid
        // out about, in any spelling a number of degrees would take.
        var (entries, _) = Bundle(Scene());
        foreach (var (name, bytes) in entries.Where(e => !e.Key.EndsWith(".ply", StringComparison.Ordinal)))
        {
            var text = Encoding.UTF8.GetString(bytes);
            text.ShouldNotContain("41.25", Case.Sensitive, name);
            text.ShouldNotContain("7.5,", Case.Sensitive, name);
        }

        plan.GetProperty("generator").GetString().ShouldBe("SilexGIS");
    }

    [Fact]
    public void Each_cave_is_a_model_placed_by_its_offset_and_hung_where_the_scene_drew_it()
    {
        var (_, plan) = Bundle(Scene());

        var models = plan.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);
        models.Keys.Order(StringComparer.Ordinal).ShouldBe(
            ["cave-01-estimated", "cave-01-lines", "cave-01-walls", "cave-02-lines"]);

        // The first cave's walls stand at the origin: its anchor is the place the scene is laid
        // out about, and the scene hung it from the surface by its top, so its zero is the zero.
        Translation(models["cave-01-walls"]).ShouldBe([0, 0, 0]);
        models["cave-01-walls"].GetProperty("role").GetString().ShouldBe("walls");
        models["cave-01-walls"].GetProperty("material").GetString().ShouldBe("rock");
        models["cave-01-estimated"].GetProperty("role").GetString().ShouldBe("estimated-passages");
        models["cave-01-estimated"].GetProperty("material").GetString().ShouldBe("estimated");

        // The second cave is a little over a kilometre east, and its own top — 40 m lower as
        // surveyed — is hung from the same surface: level with the first, not below it.
        var second = Translation(models["cave-02-lines"]);
        second[0].ShouldBe(1045, 3);
        Math.Abs(second[1]).ShouldBeLessThan(0.2);
        Math.Abs(second[2]).ShouldBeLessThan(0.2);
        models["cave-02-lines"].GetProperty("role").GetString().ShouldBe("survey-lines");

        // A kilometre east its "up" and its "north" are not quite the origin's — the two places
        // differ by a turn of the Earth through the longitude between them — and the plan says so
        // rather than standing the cave on the origin's axes.
        var turn = models["cave-02-lines"].GetProperty("rotation");
        turn.GetProperty("degrees").GetDouble().ShouldBe(0.0125, 1e-6);
    }

    [Fact]
    public void Relief_stretches_every_model_along_its_own_up_and_lifts_the_camera_with_them()
    {
        var plain = Bundle(Scene()).Plan;
        var tall = Bundle(Scene() with { Relief = 2 }).Plan;

        foreach (var model in tall.GetProperty("models").EnumerateArray())
        {
            model.GetProperty("scale").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([1, 1, 2]);
        }

        plain.GetProperty("models")[0].TryGetProperty("scale", out _).ShouldBeFalse();

        // The camera stood 120 m above the origin's height; drawn twice as tall, the origin is at
        // twice its height and the camera at twice its own — 240 m above it. (Less the seven
        // millimetres the Earth curves away over the 300 m between them.)
        Eye(plain, 0)[2].ShouldBe(120, 0.01);
        Eye(tall, 0)[2].ShouldBe(240, 0.01);
    }

    [Fact]
    public void The_camera_has_one_pose_a_frame_each_with_its_own_turn()
    {
        var scene = Scene();
        var (_, plan) = Bundle(scene);

        plan.GetProperty("timeline").GetProperty("fps").GetInt32().ShouldBe(25);
        plan.GetProperty("timeline").GetProperty("frameCount").GetInt32().ShouldBe(scene.Poses.Count);

        var camera = plan.GetProperty("camera");
        camera.GetProperty("interpolation").GetString().ShouldBe("linear");
        var track = camera.GetProperty("track").EnumerateArray().ToList();
        track.Count.ShouldBe(scene.Poses.Count);
        for (var i = 0; i < track.Count; i++)
        {
            track[i].GetProperty("t").GetDouble().ShouldBe(i / 25.0, 1e-6);
            var turn = track[i].GetProperty("orientation").EnumerateArray().Select(n => n.GetDouble()).ToArray();
            turn.Length.ShouldBe(4);
            Math.Sqrt(turn.Sum(n => n * n)).ShouldBe(1, 1e-6);
        }

        // The first pose stands 300 m south of the origin and 120 m above it, facing north and
        // tilted 20 degrees down; what it focuses on is 320 m along that line.
        var eye = Eye(plan, 0);
        eye[0].ShouldBe(0, 0.01);
        eye[1].ShouldBe(-300, 0.5);
        eye[2].ShouldBe(120, 0.01);
        var target = track[0].GetProperty("target").EnumerateArray().Select(n => n.GetDouble()).ToArray();
        var along = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(target[k] - eye[k], 2)));
        along.ShouldBe(320, 1e-3);
        ((target[2] - eye[2]) / along).ShouldBe(-Math.Sin(20 * Math.PI / 180), 1e-4);

        // A 60-degree lens across a landscape frame on the plan's 36 mm sensor.
        camera.GetProperty("sensorWidth").GetDouble().ShouldBe(36);
        camera.GetProperty("lens").GetDouble().ShouldBe(31.177, 1e-3);
        track[0].TryGetProperty("lens", out _).ShouldBeFalse();

        // Far enough to hold the second cave from every frame.
        camera.GetProperty("clipEnd").GetDouble().ShouldBeGreaterThan(1300);
    }

    [Fact]
    public void A_lens_that_changes_is_written_on_every_pose_and_a_portrait_frame_narrows_it()
    {
        var scene = Scene();
        var zooming = scene with
        {
            Width = 1080,
            Height = 1920,
            Poses = [.. scene.Poses.Select((p, i) => p with { LensDeg = 60 - (i * 0.5) })],
        };

        var (_, plan) = Bundle(zooming);

        var track = plan.GetProperty("camera").GetProperty("track").EnumerateArray().ToList();
        // Sixty degrees down the long side of a 9:16 frame is 35.98 across it: a 55.4 mm lens.
        track[0].GetProperty("lens").GetDouble().ShouldBe(55.43, 0.01);
        track[10].GetProperty("lens").GetDouble().ShouldBeGreaterThan(track[0].GetProperty("lens").GetDouble());
        plan.GetProperty("look").GetProperty("width").GetInt32().ShouldBe(1080);
        plan.GetProperty("look").GetProperty("height").GetInt32().ShouldBe(1920);
    }

    [Fact]
    public void The_look_is_rock_a_lamp_on_the_camera_a_sun_and_the_sky_the_flyby_had()
    {
        var (_, plan) = Bundle(Scene());
        var look = plan.GetProperty("look");

        var lights = look.GetProperty("lights").EnumerateArray().Select(l => l.GetProperty("kind").GetString()).ToList();
        lights.ShouldBe(["headlamp", "sun"], ignoreOrder: true);

        var materials = look.GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);
        materials["rock"].GetProperty("kind").GetString().ShouldBe("noiseRamp");
        materials["estimated"].GetProperty("kind").GetString().ShouldBe("flat");

        // The first cave's lines are coloured by depth below its own top: 0–50 m in the first
        // colour, deeper in the second, the change of colour 50 m down a cave 120 m deep. The
        // depths are measured in the lines' own mesh, whose zero is the first leg's start — here
        // the cave's top — so a colour stays with its leg however the model is stretched.
        var ramp = materials["lines-01"];
        ramp.GetProperty("kind").GetString().ShouldBe("heightRamp");
        ramp.GetProperty("space").GetString().ShouldBe("model");
        ramp.GetProperty("high").GetDouble().ShouldBe(0, 1e-6);
        ramp.GetProperty("low").GetDouble().ShouldBe(-120, 1e-6);
        // In bands: one stop a band, its colour holding from the stop up to the next.
        ramp.GetProperty("banded").GetBoolean().ShouldBeTrue();
        var stops = ramp.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("at").GetDouble()).ToList();
        stops.ShouldBe([0, 1 - (50.0 / 120.0)], 1e-6);

        // Lines are a drawing: no light changes them, in a ramp or in one colour. The rock and the
        // passages of a size nobody measured are things in the cave, and are lit.
        ramp.GetProperty("unlit").GetBoolean().ShouldBeTrue();
        materials["rock"].TryGetProperty("unlit", out _).ShouldBeFalse();
        materials["estimated"].TryGetProperty("unlit", out _).ShouldBeFalse();

        look.GetProperty("world").GetProperty("kind").GetString().ShouldBe("color");
        look.GetProperty("samples").GetInt32().ShouldBe(96);

        // One colour for every line, when the flyby says so.
        var plainLines = Bundle(Scene() with { Look = new FlybyBundleLook(null, "#ff8800", []) }).Plan.GetProperty("look");
        plainLines.TryGetProperty("world", out _).ShouldBeFalse();
        var plain = plainLines.GetProperty("materials").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "lines");
        plain.GetProperty("kind").GetString().ShouldBe("flat");
        plain.GetProperty("unlit").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void The_texts_keep_the_flybys_own_times_and_the_arrow_its_headings()
    {
        var scene = Scene() with
        {
            Texts = new FlybyBundleTexts(
                Title: new FlybyBundleCard("Two invented caves", "A test", 1.2),
                End: new FlybyBundleCard("The end", null, 0.8),
                Captions: [new FlybyBundleCaption("First cave", 0.4, 1.4), new FlybyBundleCaption("Second cave", 1.0, 1.9)],
                Corner: "Invented survey",
                CornerCaptions: [new FlybyBundleCaption("Relief ×2", 0, 2)],
                FadeInS: 0.5,
                FadeOutS: 0.25,
                NorthArrow: true),
        };

        var (entries, plan) = Bundle(scene);

        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["models", "camera_track", "look", "texts", "output"]);
        var texts = plan.GetProperty("texts");
        texts.GetProperty("fromBlack").GetDouble().ShouldBe(0.5);
        texts.GetProperty("toBlack").GetDouble().ShouldBe(0.25);
        // No frame is held after the flight: the clip is as long as the flyby.
        if (texts.TryGetProperty("hold", out var hold))
        {
            hold.GetDouble().ShouldBe(0);
        }

        var items = texts.GetProperty("items").EnumerateArray().ToList();
        JsonElement Item(string id) => items.Single(i => i.GetProperty("id").GetString() == id);

        // The title has gone by the moment the flyby names, over the last quarter of its time.
        Item("title").GetProperty("text").GetString().ShouldBe("Two invented caves");
        Item("title").GetProperty("end").GetDouble().ShouldBe(1.2);
        Item("title").GetProperty("fadeOut").GetDouble().ShouldBe(0.3, 1e-6);
        Item("subtitle").GetProperty("text").GetString().ShouldBe("A test");

        // The end card comes over the last of the flight — two seconds of clip, a card of 0.8 s —
        // and not on a black card after it: its words on a dark box of their own, coming over the
        // first quarter of its time.
        Item("end-card-text").GetProperty("start").GetDouble().ShouldBe(1.2, 1e-6);
        Item("end-card-text").GetProperty("fadeIn").GetDouble().ShouldBe(0.2, 1e-6);
        Item("end-card-text").GetProperty("backing").GetBoolean().ShouldBeTrue();
        items.ShouldNotContain(i => i.GetProperty("kind").GetString() == "box" && i.GetProperty("role").GetString() == "end-card");

        // One caption at a time: the first gives way when the second begins.
        Item("caption.1").GetProperty("end").GetDouble().ShouldBe(1.0);
        Item("caption.2").GetProperty("start").GetDouble().ShouldBe(1.0);
        Item("corner").GetProperty("text").GetString().ShouldBe("Invented survey");
        Item("corner.1").GetProperty("text").GetString().ShouldBe("Relief ×2");

        // The arrow turns by the poses of the plan itself: the camera turned from north to 49
        // degrees east over the clip, evenly, and two turns say it.
        var turns = Item("north-arrow").GetProperty("turn").EnumerateArray()
            .Select(t => (T: t.GetProperty("t").GetDouble(), Degrees: t.GetProperty("degrees").GetDouble())).ToList();
        turns[0].ShouldBe((0, 0));
        turns[^1].T.ShouldBe(49 / 25.0, 1e-6);
        turns[^1].Degrees.ShouldBe(49, 1e-3);

        // The captions also lie beside where the movie will be, as subtitles.
        Encoding.UTF8.GetString(entries["two-caves-blender/out/two-caves.srt"]).ShouldContain("Second cave");
    }

    [Fact]
    public void What_is_left_out_is_said_in_the_bundles_own_notes()
    {
        var (entries, _) = Bundle(Scene() with
        {
            LeftOut = ["the ground and the map drawn on it", "2 of the caves asked for (nothing of them is available to draw)"],
        });

        var readme = Encoding.UTF8.GetString(entries["two-caves-blender/README.txt"]);
        readme.ShouldContain("NOT IN THIS SCENE");
        readme.ShouldContain("  - the ground and the map drawn on it");
        readme.ShouldContain("  - 2 of the caves asked for");
        readme.ShouldContain("Needs Blender 4.2 or newer");
        readme.ShouldContain("Mozilla Public License 2.0");

        Encoding.UTF8.GetString(Bundle(Scene()).Entries["two-caves-blender/README.txt"]).ShouldNotContain("NOT IN THIS SCENE");
    }

    [Fact]
    public void The_notes_open_with_what_the_folder_is_and_say_how_the_scene_is_laid_out_and_lit()
    {
        var readme = Encoding.UTF8.GetString(Bundle(Scene()).Entries["two-caves-blender/README.txt"]);

        // This application's own opening, in place of the survey library's.
        readme.ShouldContain("A flyby of caves made by SilexGIS for Blender");
        readme.ShouldContain("Nothing in this folder needs SilexGIS or ThIDE.");
        readme.ShouldNotContain("A scene for Blender: a cave model");

        // Its own sections stand after the library's and say what the scene is laid out about —
        // without saying where that is — and how it is lit.
        readme.IndexOf("HOW THE SCENE IS LAID OUT", StringComparison.Ordinal)
            .ShouldBeGreaterThan(readme.IndexOf("THE FILES", StringComparison.Ordinal));
        readme.ShouldContain("not where on a map they are");
        readme.ShouldContain("THE LIGHT");
        readme.ShouldContain("the camera does not go\nunder the ground");

        // Where the daylight goes, the notes say when, and how to keep it.
        var dark = Bundle(Scene() with { Changes = FlybyBundleChanges.None with { Daylight = [new(0, 1), new(10, 1), new(35, 0, FlybyBundleEasing.Smooth)] } });
        var darkNotes = Encoding.UTF8.GetString(dark.Entries["two-caves-blender/README.txt"]);
        darkNotes.ShouldContain("The sun and the sky go out while the camera is under");
        darkNotes.ShouldContain("\"worldStrength\"");
        darkNotes.ShouldContain("the lamp is turned up by the square of the distance from the camera to the\nnearest wall");
        darkNotes.ShouldContain("the change of the light \"Headlamp\"");
        darkNotes.ShouldNotContain("a few metres");
        darkNotes.ShouldNotContain("stays dim");

        // And that the lines need no light, whether or not the daylight goes.
        readme.ShouldContain("The survey lines are drawn in their own colours whatever the light");
        darkNotes.ShouldContain("The survey lines are drawn in their own colours whatever the light");

        // A scene with no walls has no lamp to speak of: its lines are all there is, and they are unlit.
        var linesOnly = Scene();
        linesOnly = linesOnly with
        {
            Caves = [.. linesOnly.Caves.Select(cave => cave with { Walls = null, Estimated = null })],
            Changes = FlybyBundleChanges.None with { Daylight = [new(0, 1), new(10, 1), new(35, 0, FlybyBundleEasing.Smooth)] },
        };
        var linesOnlyNotes = Encoding.UTF8.GetString(Bundle(linesOnly).Entries["two-caves-blender/README.txt"]);
        linesOnlyNotes.ShouldContain("\"worldStrength\"");
        linesOnlyNotes.ShouldNotContain("Headlamp");

        // The lines this application writes are no longer than the notes' own.
        foreach (var language in (bool[])[false, true])
        {
            var own = SilexGis.Domain.Export.FlybyBundleNotes.Sections(language, daylightGoes: true)
                .Concat(SilexGis.Domain.Export.FlybyBundleNotes.Sections(language, daylightGoes: true, lampReaches: true))
                .Concat(SilexGis.Domain.Export.FlybyBundleNotes.Sections(language, daylightGoes: false))
                .SelectMany(section => section.Text.Split('\n'))
                .Concat(SilexGis.Domain.Export.FlybyBundleNotes.Intro(language).Split('\n'));
            own.ShouldAllBe(line => line.Length <= 96);
        }
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("ro-RO")]
    [InlineData("RO_ro")]
    public void The_notes_are_in_Romanian_for_a_reader_who_has_the_application_in_Romanian(string language)
    {
        var (entries, plan) = Bundle(Scene() with
        {
            Language = language,
            LeftOut = [SilexGis.Domain.Export.FlybyBundleOmissions.Sentence(SilexGis.Domain.Export.FlybyBundleOmission.Ground, romanian: true)],
        });

        var readme = Encoding.UTF8.GetString(entries["two-caves-blender/README.txt"]);
        readme.ShouldContain("Un survol printre peșteri, făcut de SilexGIS pentru Blender");
        readme.ShouldContain("CUM SE RANDEAZĂ");
        readme.ShouldContain("CUM ESTE AȘEZATĂ SCENA");
        readme.ShouldContain("LUMINA");
        readme.ShouldContain("CE LIPSEȘTE DIN ACEASTĂ SCENĂ");
        readme.ShouldContain("  - terenul și harta desenată pe el");
        readme.ShouldNotContain("HOW TO RENDER");

        // The scene itself is the same whatever language its notes are in.
        ScenePlanJson(plan).ShouldBe(ScenePlanJson(Bundle(Scene()).Plan));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en-GB")]
    [InlineData("de")]
    public void The_notes_are_in_English_for_every_other_reader(string? language)
    {
        var readme = Encoding.UTF8.GetString(Bundle(Scene() with { Language = language }).Entries["two-caves-blender/README.txt"]);
        readme.ShouldContain("HOW TO RENDER");
        readme.ShouldContain("HOW THE SCENE IS LAID OUT");
    }

    [Theory]
    [InlineData(1, "1 dintre peșterile cerute", "pereții unei peșteri,")]
    [InlineData(2, "2 dintre peșterile cerute", "pereții a 2 peșteri,")]
    [InlineData(19, "19 dintre peșterile cerute", "pereții a 19 peșteri,")]
    [InlineData(20, "20 dintre peșterile cerute", "pereții a 20 de peșteri,")]
    [InlineData(101, "101 dintre peșterile cerute", "pereții a 101 peșteri,")]
    [InlineData(120, "120 dintre peșterile cerute", "pereții a 120 de peșteri,")]
    public void What_only_the_server_knows_is_left_out_is_counted_in_Romanian_as_Romanian_counts(int count, string notDrawn, string walls)
    {
        SilexGis.Domain.Export.FlybyBundleOmissions.CavesNotDrawn(count, romanian: true).ShouldStartWith(notDrawn);
        SilexGis.Domain.Export.FlybyBundleOmissions.WallsNotAvailable(count, romanian: true).ShouldStartWith(walls);
        SilexGis.Domain.Export.FlybyBundleOmissions.WallsOverBudget(count, romanian: true).ShouldStartWith(walls);

        // And in English as before.
        SilexGis.Domain.Export.FlybyBundleOmissions.CavesNotDrawn(count).ShouldContain("of the caves asked for");
        SilexGis.Domain.Export.FlybyBundleOmissions.WallsNotAvailable(count).ShouldStartWith("the walls of " + count);
    }

    // ---- what changes while the clip runs ----

    [Fact]
    public void A_flyby_that_changes_nothing_has_no_changes_in_its_plan()
    {
        var (_, plan) = Bundle(Scene());

        plan.TryGetProperty("changes", out _).ShouldBeFalse();
        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["models", "camera_track", "look", "output"]);
    }

    [Fact]
    public void A_relief_that_changes_stretches_every_model_by_its_keys_and_lifts_each_frames_camera_by_what_they_give()
    {
        // True for the first ten frames, then up to twice as tall by the fortieth, eased.
        var scene = Scene() with
        {
            Changes = FlybyBundleChanges.None with
            {
                Relief = [new(0, 1), new(10, 1), new(40, 2, FlybyBundleEasing.Smooth)],
            },
        };
        var (_, plan) = Bundle(scene);

        // The player's module for changes is asked for, after what it changes.
        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["models", "camera_track", "look", "changes", "output"]);

        var changes = Changes(plan);
        foreach (var model in plan.GetProperty("models").EnumerateArray())
        {
            // Each model starts at the relief of the first frame and is stretched by the keys.
            model.GetProperty("scale").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([1, 1, 1]);
            var keys = changes[model.GetProperty("id").GetString()!].GetProperty("scale").EnumerateArray().ToList();
            keys.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.4, 1.6]);
            keys.Select(k => k.GetProperty("value")[2].GetDouble()).ShouldBe([1, 1, 2]);
            keys[2].GetProperty("value").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([1, 1, 2]);
            keys[2].GetProperty("easing").GetString().ShouldBe("smooth");
            keys[1].TryGetProperty("easing", out _).ShouldBeFalse();
        }

        // The camera stands 120 m above the origin's height. Each frame it is drawn at 120 m times
        // the relief the keys give there: half-way through the ramp, half-way up.
        Eye(plan, 0)[2].ShouldBe(120, 0.01);
        Eye(plan, 10)[2].ShouldBe(120, 0.01);
        Eye(plan, 25)[2].ShouldBe(180, 0.01);
        Eye(plan, 16)[2].ShouldBe(120 * (1 + ((3 * 0.04) - (2 * 0.008))), 0.01);
        Eye(plan, 40)[2].ShouldBe(240, 0.01);
        Eye(plan, 49)[2].ShouldBe(240, 0.01);
    }

    [Fact]
    public void Under_a_changing_relief_a_model_is_stretched_about_the_level_of_the_scenes_zero()
    {
        // A scene with relief of its own: the caves where they were surveyed, 830 m up, about an
        // origin 500 m up. The first cave stands 330 m above the origin's level.
        var scene = Scene();
        scene = scene with
        {
            OriginHeightM = 500,
            Caves = [.. scene.Caves.Select(cave => cave with { AltitudeShiftM = 0 })],
            Changes = FlybyBundleChanges.None with { Relief = [new(0, 1), new(49, 3)] },
        };
        var (entries, plan) = Bundle(scene);
        var walls = plan.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "cave-01-walls");

        // The point it is stretched about is the origin's own level under it — the scene's zero —
        // and its mesh is lifted by the 330 m it stands above that: a cube from 320 to 330.
        Translation(walls).ShouldBe([0, 0, 0]);
        var (vertices, _) = Ply(entries["two-caves-blender/cave-01-walls.ply"]);
        vertices.Min(v => v[2]).ShouldBe(320, 1e-3);
        vertices.Max(v => v[2]).ShouldBe(330, 1e-3);

        // So at a relief of k its top is k × 330 above the origin drawn at k × 500: at k × 830,
        // where the globe draws it. And no number of the plan or of the mesh is the 830 itself:
        // what the bundle holds are heights above the origin, never above the sea.
        foreach (var model in plan.GetProperty("models").EnumerateArray())
        {
            Math.Abs(Translation(model)[2]).ShouldBeLessThan(1);
        }

        // Without a change of relief a model stands at its own zero and its mesh is as it was read.
        var still = Bundle(scene with { Changes = FlybyBundleChanges.None, Relief = 2 });
        Translation(still.Plan.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "cave-01-walls"))
            .ShouldBe([0, 0, 660]);
        Ply(still.Entries["two-caves-blender/cave-01-walls.ply"]).Vertices.Max(v => v[2]).ShouldBe(0, 1e-6);
    }

    [Fact]
    public void Under_a_changing_relief_the_lines_keep_their_colours_by_depth_on_the_legs_they_are_on()
    {
        // A scene with relief of its own, as above: the first cave's lines start 330 m above the
        // origin's level, and their mesh is lifted by that before it is stretched.
        var scene = Scene();
        scene = scene with
        {
            OriginHeightM = 500,
            Caves = [.. scene.Caves.Select(cave => cave with { AltitudeShiftM = 0 })],
        };
        var still = Bundle(scene);
        var stretched = Bundle(scene with { Changes = FlybyBundleChanges.None with { Relief = [new(0, 1), new(49, 2)] } });

        foreach (var (bundle, top) in (((Dictionary<string, byte[]> Entries, JsonElement Plan), double)[])[(still, 0), (stretched, 330)])
        {
            var materials = bundle.Plan.GetProperty("look").GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("id").GetString()!);

            // A ramp over height in the lines' own mesh: the cave is stretched, the mesh is not, so
            // each colour stays on the leg it is on. No colour is written into the mesh itself, and
            // no leg is cut.
            var ramp = materials["lines-01"];
            ramp.GetProperty("kind").GetString().ShouldBe("heightRamp");
            ramp.GetProperty("space").GetString().ShouldBe("model");
            ramp.GetProperty("high").GetDouble().ShouldBe(top, 1e-3);
            ramp.GetProperty("low").GetDouble().ShouldBe(top - 120, 1e-3);
            materials.Keys.ShouldNotContain("lines-by-depth");
            bundle.Plan.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "cave-01-lines")
                .GetProperty("material").GetString().ShouldBe("lines-01");

            var (vertices, colours) = Ply(bundle.Entries["two-caves-blender/cave-01-lines.ply"]);
            vertices.Count.ShouldBe(16);
            colours.ShouldBeNull();

            // The second colour begins 50 m below the top: at that share of the way up the ramp
            // the colour changes at once, from the second band's to the first's — a ramp in
            // bands, one stop a band, each where its band begins from below.
            ramp.GetProperty("banded").GetBoolean().ShouldBeTrue();
            var stops = ramp.GetProperty("stops").EnumerateArray()
                .Select(stop => (At: stop.GetProperty("at").GetDouble(), Red: stop.GetProperty("color")[0].GetDouble()))
                .ToList();
            stops.Select(stop => stop.At).ShouldBe([0, 70.0 / 120], 1e-6);
            stops[1].Red.ShouldNotBe(stops[0].Red);
        }

        // The stops are the same shares in both: the relief has no part in where a colour begins.
        string Stops((Dictionary<string, byte[]> Entries, JsonElement Plan) bundle) => bundle.Plan.GetProperty("look").GetProperty("materials")
            .EnumerateArray().Single(m => m.GetProperty("id").GetString() == "lines-01").GetProperty("stops").GetRawText();
        Stops(stretched).ShouldBe(Stops(still));
    }

    [Fact]
    public void A_layer_shown_hidden_and_faded_and_the_colours_laid_over_it_are_tracks_on_each_model()
    {
        var scene = Scene();
        scene = scene with
        {
            Caves =
            [
                // The first cave is coloured apart; the second has only lines, which stand in for
                // walls it has none of.
                scene.Caves[0] with
                {
                    WallsTint = [new(0, "#3a3f4a", 0.6), new(5, "#ffcc00", 0, FlybyBundleEasing.Constant), new(15, "#ffcc00", 0.85, FlybyBundleEasing.Smooth)],
                    LinesTint = [new(0, "#000000", 0), new(5, "#ffcc00", 1, FlybyBundleEasing.Constant)],
                },
                scene.Caves[1] with { LinesStandIn = true },
            ],
            Changes = FlybyBundleChanges.None with
            {
                Walls = new FlybyBundleLayerChanges(
                    [new(20, false, FadeFrames: 10)],
                    [new(0, 1), new(10, 1), new(40, 0.5, FlybyBundleEasing.Smooth)],
                    [new(0, "#3a3f4a", 0.6)]),
                Lines = new FlybyBundleLayerChanges([new(10, false), new(20, true)], [new(0, 0.5)], []),
            },
        };
        var changes = Changes(Bundle(scene).Plan);

        // The walls and the estimated passages are one layer: they go over ten frames from the
        // twentieth, eased, and the renderer hides them when they have gone. How much of them
        // there is besides is a track of its own, which the renderer multiplies in.
        foreach (var id in (string[])["cave-01-walls", "cave-01-estimated"])
        {
            var visible = changes[id].GetProperty("visible").EnumerateArray().Single();
            visible.GetProperty("t").GetDouble().ShouldBe(0.8);
            visible.GetProperty("value").GetBoolean().ShouldBeFalse();
            visible.GetProperty("fade").GetDouble().ShouldBe(0.4);
            visible.GetProperty("easing").GetString().ShouldBe("smooth");

            var opacity = changes[id].GetProperty("opacity").EnumerateArray().ToList();
            opacity.Select(k => k.GetProperty("value").GetDouble()).ShouldBe([1, 1, 0.5]);
            opacity[2].GetProperty("easing").GetString().ShouldBe("smooth");

            // The cave's own colour, not the colour of every cave: three keys, the jump and the rise.
            var tint = changes[id].GetProperty("tint").EnumerateArray().ToList();
            tint.Select(k => k.GetProperty("strength").GetDouble()).ShouldBe([0.6, 0, 0.85]);
            tint.Select(k => k.TryGetProperty("easing", out var e) ? e.GetString() : null).ShouldBe([null, "constant", "smooth"]);
            // #ffcc00 as the light it stands for.
            tint[2].GetProperty("color").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([1, 0.603827, 0], 1e-6);
            // The colour jumps and then only its strength moves: nothing is mixed on the way.
            tint.Any(k => k.TryGetProperty("mix", out _)).ShouldBeFalse();
            changes[id].TryGetProperty("scale", out _).ShouldBeFalse();
        }

        // The first cave's lines go with the lines' layer — cut, with nothing said of a fade —
        // and take the cave's own colour.
        var lines = changes["cave-01-lines"];
        lines.GetProperty("visible").EnumerateArray().Select(k => k.GetProperty("value").GetBoolean()).ShouldBe([false, true]);
        lines.GetProperty("visible").EnumerateArray().Any(k => k.TryGetProperty("fade", out _) || k.TryGetProperty("easing", out _)).ShouldBeFalse();
        lines.GetProperty("opacity").EnumerateArray().Single().GetProperty("value").GetDouble().ShouldBe(0.5);
        lines.GetProperty("tint").EnumerateArray().Select(k => k.GetProperty("strength").GetDouble()).ShouldBe([0, 1]);

        // The second cave's lines are all there is of it: shown and faded as the walls are, in
        // their colours by depth, with no colour laid over them.
        var standIn = changes["cave-02-lines"];
        standIn.GetProperty("visible").EnumerateArray().Single().GetProperty("t").GetDouble().ShouldBe(0.8);
        standIn.GetProperty("opacity").EnumerateArray().Count().ShouldBe(3);
        standIn.TryGetProperty("tint", out _).ShouldBeFalse();
    }

    [Fact]
    public void Lines_wear_one_colour_or_their_colours_by_depth_as_their_track_needs()
    {
        // One colour throughout, by a track of one key: a plain material and nothing to play.
        var scene = Scene();
        var one = Bundle(scene with { Changes = FlybyBundleChanges.None with { Lines = new([], [], [new(0, "#ff8800", 1)]) } }).Plan;
        one.GetProperty("models").EnumerateArray().Where(m => m.GetProperty("role").GetString() == "survey-lines")
            .ShouldAllBe(m => m.GetProperty("material").GetString() == "lines");
        one.TryGetProperty("changes", out _).ShouldBeFalse();

        // Two colours one after the other: plain, in the first, with the second laid over it whole.
        var two = Bundle(scene with
        {
            Changes = FlybyBundleChanges.None with { Lines = new([], [], [new(0, "#ff8800", 1), new(20, "#0088ff", 1, FlybyBundleEasing.Constant)]) },
        }).Plan;
        two.GetProperty("models").EnumerateArray().Where(m => m.GetProperty("role").GetString() == "survey-lines")
            .ShouldAllBe(m => m.GetProperty("material").GetString() == "lines");
        Changes(two)["cave-02-lines"].GetProperty("tint").EnumerateArray().Count().ShouldBe(2);

        // By depth for part of the clip: the colours by depth underneath, a colour over them while it lasts.
        var mixed = Bundle(scene with
        {
            Changes = FlybyBundleChanges.None with { Lines = new([], [], [new(0, "#000000", 0), new(20, "#0088ff", 1, FlybyBundleEasing.Constant)]) },
        }).Plan;
        mixed.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "cave-01-lines")
            .GetProperty("material").GetString().ShouldBe("lines-01");
        Changes(mixed)["cave-01-lines"].GetProperty("tint").EnumerateArray().Select(k => k.GetProperty("strength").GetDouble()).ShouldBe([0, 1]);
    }

    [Fact]
    public void A_sky_that_changes_is_a_track_on_a_world_of_one_colour()
    {
        var keys = new FlybyBundleColourKey[] { new(0, "#000000"), new(10, "#000000"), new(11, "#101820"), new(12, "#203040") };
        var plan = Bundle(Scene() with { Look = new FlybyBundleLook(null, null, []), Changes = FlybyBundleChanges.None with { Sky = keys } }).Plan;

        // No sky is black — and a black that is about to change needs a world to change.
        var world = plan.GetProperty("look").GetProperty("world");
        world.GetProperty("kind").GetString().ShouldBe("color");
        world.GetProperty("color").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([0, 0, 0]);

        var change = plan.GetProperty("changes").EnumerateArray().Single(c => !c.TryGetProperty("target", out _));
        var track = change.GetProperty("worldColor").EnumerateArray().ToList();
        track.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.4, 0.44, 0.48]);
        track[3].GetProperty("value").EnumerateArray().Select(n => n.GetDouble()).ShouldBe([0.014444, 0.029557, 0.051269], 1e-6);

        // Where the colour is another than the key before had, it is mixed on the way as a screen
        // mixes two colours — which is what the flyby showed; where it is the same, nothing is said.
        track.Select(k => k.TryGetProperty("mix", out var mix) ? mix.GetString() : null).ShouldBe([null, null, "srgb", "srgb"]);

        // One key is the sky of the whole clip, and no change at all.
        var still = Bundle(Scene() with { Changes = FlybyBundleChanges.None with { Sky = [new(0, "#203040")] } }).Plan;
        still.TryGetProperty("changes", out _).ShouldBeFalse();
        still.GetProperty("look").GetProperty("world").GetProperty("color")[2].GetDouble().ShouldBe(0.051269, 1e-6);
    }

    [Fact]
    public void A_colour_that_crosses_into_another_is_mixed_as_a_screen_mixes_and_one_that_jumps_is_not()
    {
        var scene = Scene();
        scene = scene with
        {
            Changes = FlybyBundleChanges.None with
            {
                Walls = new FlybyBundleLayerChanges(
                    [],
                    [],
                    [
                        new(0, "#ff0000", 0.3),
                        new(10, "#ff0000", 0.3),
                        new(20, "#0000ff", 0.9, FlybyBundleEasing.Smooth),
                        new(30, "#00ff00", 0.9, FlybyBundleEasing.Constant),
                        new(40, "#00FF00", 0.2),
                    ]),
            },
        };

        var tint = Changes(Bundle(scene).Plan)["cave-01-walls"].GetProperty("tint").EnumerateArray().ToList();
        tint.Select(k => k.TryGetProperty("mix", out var mix) ? mix.GetString() : null).ShouldBe([null, null, "srgb", null, null]);
        tint[2].GetProperty("easing").GetString().ShouldBe("smooth");
    }

    [Fact]
    public void A_change_may_end_after_the_clip_and_end_in_a_jump_on_one_frame()
    {
        // Fifty frames. The relief is still growing when they end: its real end is frame 80.
        // The sky comes to a colour on frame 30 and is another from that very frame on.
        var scene = Scene() with
        {
            Changes = FlybyBundleChanges.None with
            {
                Relief = [new(0, 1), new(40, 1), new(80, 2, FlybyBundleEasing.Smooth)],
                Sky = [new(0, "#000000"), new(30, "#203040", FlybyBundleEasing.Smooth), new(30, "#101820", FlybyBundleEasing.Constant)],
            },
        };
        var (_, plan) = Bundle(scene);

        var stretch = Changes(plan)["cave-01-walls"].GetProperty("scale").EnumerateArray().ToList();
        stretch.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 1.6, 3.2]);
        plan.GetProperty("timeline").GetProperty("frameCount").GetInt32().ShouldBe(50);

        // The camera reads the same keys: at the last frame it is nine fortieths of the way up.
        var share = 9 / 40.0;
        Eye(plan, 49)[2].ShouldBe(120 * (1 + ((3 * share * share) - (2 * share * share * share))), 0.01);
        Eye(plan, 40)[2].ShouldBe(120, 0.01);

        var sky = plan.GetProperty("changes").EnumerateArray().Single(c => !c.TryGetProperty("target", out _)).GetProperty("worldColor").EnumerateArray().ToList();
        sky.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 1.2, 1.2]);
        sky.Select(k => k.TryGetProperty("easing", out var e) ? e.GetString() : null).ShouldBe([null, "smooth", "constant"]);
    }

    [Fact]
    public void A_change_may_begin_before_the_clip_and_start_from_a_value_no_frame_shows()
    {
        // Fifty frames. The relief has been growing since twenty frames before the first and is
        // done twenty frames in. The walls were switched off ten frames before the clip and take
        // thirty to go. The first cave's colour comes to one value on frame 10, shows another
        // there, and goes on from a third that no frame shows.
        var scene = Scene();
        scene = scene with
        {
            Caves =
            [
                scene.Caves[0] with
                {
                    WallsTint =
                    [
                        new(0, "#3a3f4a", 0.6),
                        new(10, "#3a3f4a", 0.2),
                        new(10, "#3a3f4a", 0.6, FlybyBundleEasing.Constant),
                        new(10, "#ffcc00", 0, FlybyBundleEasing.Constant),
                        new(30, "#ffcc00", 0.8, FlybyBundleEasing.Smooth),
                    ],
                },
                scene.Caves[1],
            ],
            Changes = FlybyBundleChanges.None with
            {
                Relief = [new(-20, 1), new(20, 2, FlybyBundleEasing.Smooth)],
                Walls = new FlybyBundleLayerChanges([new(-10, false, 30)], [], []),
            },
        };
        var (_, plan) = Bundle(scene);
        var walls = Changes(plan)["cave-01-walls"];

        // The stretch keeps its two keys, the first before the clip — and the camera reads the
        // same keys: half-way up on the first frame, done on frame 20.
        var stretch = walls.GetProperty("scale").EnumerateArray().ToList();
        stretch.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([-0.8, 0.8]);
        stretch[1].GetProperty("easing").GetString().ShouldBe("smooth");
        Eye(plan, 0)[2].ShouldBe(120 * 1.5, 0.01);
        Eye(plan, 10)[2].ShouldBe(120 * (1 + ((3 * 0.75 * 0.75) - (2 * 0.75 * 0.75 * 0.75))), 0.01);
        Eye(plan, 20)[2].ShouldBe(240, 0.01);
        plan.GetProperty("models").EnumerateArray().First().GetProperty("scale")[2].GetDouble().ShouldBe(1.5, 1e-6);

        // The fade began before the clip: the renderer has the walls a third gone on its first frame.
        var shown = walls.GetProperty("visible").EnumerateArray().Single();
        shown.GetProperty("t").GetDouble().ShouldBe(-0.4);
        shown.GetProperty("value").GetBoolean().ShouldBeFalse();
        shown.GetProperty("fade").GetDouble().ShouldBe(1.2);

        // Three keys on one frame, the second and the third of them jumps.
        var tint = walls.GetProperty("tint").EnumerateArray().ToList();
        tint.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.4, 0.4, 0.4, 1.2]);
        tint.Select(k => k.TryGetProperty("easing", out var e) ? e.GetString() : null).ShouldBe([null, null, "constant", "constant", "smooth"]);
        tint.Select(k => k.GetProperty("strength").GetDouble()).ShouldBe([0.6, 0.2, 0.6, 0, 0.8]);

        // The rise is from the cave's own colour at no strength to the same colour: nothing to
        // mix as a screen mixes.
        tint[4].TryGetProperty("mix", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_camera_reads_three_keys_on_a_frame_as_the_renderer_plays_them()
    {
        // The relief comes to 1.5 on frame 10, which shows 1.2, and goes on from 2 — which no
        // frame shows — to 3 on frame 30.
        var relief = new FlybyBundleNumberKey[]
        {
            new(0, 1),
            new(10, 1.5),
            new(10, 1.2, FlybyBundleEasing.Constant),
            new(10, 2, FlybyBundleEasing.Constant),
            new(30, 3),
        };
        var (_, plan) = Bundle(Scene() with { Changes = FlybyBundleChanges.None with { Relief = relief } });

        // The models are stretched by the keys as they are given.
        var stretch = Changes(plan)["cave-01-walls"].GetProperty("scale").EnumerateArray().ToList();
        stretch.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.4, 0.4, 0.4, 1.2]);
        stretch.Select(k => k.GetProperty("value")[2].GetDouble()).ShouldBe([1, 1.5, 1.2, 2, 3]);

        // And the camera, 120 m up, is raised by what a renderer makes of them: on the way to the
        // first of the three, at the second on their frame, and from the third a frame later.
        Eye(plan, 9)[2].ShouldBe(120 * 1.45, 0.01);
        Eye(plan, 10)[2].ShouldBe(120 * 1.2, 0.01);
        Eye(plan, 11)[2].ShouldBe(120 * 2.05, 0.01);
        Eye(plan, 30)[2].ShouldBe(360, 0.01);
    }

    [Fact]
    public void The_sun_and_the_sky_are_as_strong_as_the_daylight_and_the_lamp_takes_over_as_it_goes()
    {
        var daylight = new FlybyBundleNumberKey[] { new(0, 1), new(10, 1), new(35, 0, FlybyBundleEasing.Smooth), new(60, 0) };
        var (_, plan) = Bundle(Scene() with { Changes = FlybyBundleChanges.None with { Daylight = daylight } });

        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString())
            .ShouldBe(["models", "camera_track", "look", "changes", "output"]);
        var lights = plan.GetProperty("look").GetProperty("lights").EnumerateArray().ToDictionary(l => l.GetProperty("id").GetString()!);
        var whole = lights["Sun"].GetProperty("energy").GetDouble();

        // The sun: its own strength times the daylight, at the daylight's keys and with its easing.
        var changes = plan.GetProperty("changes").EnumerateArray().ToList();
        changes.Count.ShouldBe(3);
        var sun = changes.Single(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == "Sun");
        var energy = sun.GetProperty("energy").EnumerateArray().ToList();
        energy.Select(k => k.GetProperty("t").GetDouble()).ShouldBe([0, 0.4, 1.4, 2.4]);
        energy.Select(k => k.GetProperty("value").GetDouble()).ShouldBe([whole, whole, 0, 0]);
        energy[2].GetProperty("easing").GetString().ShouldBe("smooth");

        // The sky lights nothing either, and is not seen: there is nothing in a bundle to hide it.
        var world = changes.Single(c => !c.TryGetProperty("target", out _));
        world.GetProperty("worldStrength").EnumerateArray().Select(k => k.GetProperty("value").GetDouble()).ShouldBe([1, 1, 0, 0]);
        world.TryGetProperty("worldColor", out _).ShouldBeFalse();

        // The lamp on the camera: as it is built while the daylight is whole, and turned up as it
        // goes — by the square of how far the nearest wall is, here some three hundred metres.
        var built = lights["Headlamp"].GetProperty("energy").GetDouble();
        var lamp = changes.Single(c => c.TryGetProperty("target", out var target) && target.GetProperty("id").GetString() == "Headlamp")
            .GetProperty("energy").EnumerateArray().ToList();
        lamp[0].GetProperty("t").GetDouble().ShouldBe(0);
        lamp[0].GetProperty("value").GetDouble().ShouldBe(built);
        lamp[1].GetProperty("t").GetDouble().ShouldBe(0.4);
        lamp[1].GetProperty("value").GetDouble().ShouldBe(built);
        lamp.Select(k => k.GetProperty("value").GetDouble()).ShouldBe(lamp.Select(k => k.GetProperty("value").GetDouble()).Order());
        lamp[^1].GetProperty("t").GetDouble().ShouldBe(1.96, 1e-6);
        (lamp[^1].GetProperty("value").GetDouble() / built).ShouldBeInRange(8_000, 12_000);

        // A scene with no sky has no world to turn down; its sun still goes out.
        var noSky = Bundle(Scene() with { Look = new FlybyBundleLook(null, null, []), Changes = FlybyBundleChanges.None with { Daylight = daylight } }).Plan;
        noSky.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("target").GetProperty("id").GetString()).ShouldBe(["Sun", "Headlamp"]);

        // And where the sky changes its colour too, the two are said in one change of the world.
        var both = Bundle(Scene() with
        {
            Changes = FlybyBundleChanges.None with { Daylight = daylight, Sky = [new(0, "#101820"), new(20, "#000000", FlybyBundleEasing.Smooth)] },
        }).Plan;
        var ofTheWorld = both.GetProperty("changes").EnumerateArray().Single(c => !c.TryGetProperty("target", out _));
        ofTheWorld.TryGetProperty("worldColor", out _).ShouldBeTrue();
        ofTheWorld.TryGetProperty("worldStrength", out _).ShouldBeTrue();

        // Daylight that never goes says nothing.
        Bundle(Scene() with { Changes = FlybyBundleChanges.None with { Daylight = [new(0, 1)] } }).Plan.TryGetProperty("changes", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_flight_through_more_depth_than_one_range_holds_gives_each_pose_a_range_of_its_own()
    {
        // The camera never far from the caves: one near and far limit for the whole clip.
        var near = Bundle(Scene()).Plan.GetProperty("camera");
        near.GetProperty("track").EnumerateArray().Any(pose => pose.TryGetProperty("clipStart", out _) || pose.TryGetProperty("clipEnd", out _)).ShouldBeFalse();

        // In from 300 km above, down to ten metres from the first cave's walls.
        var scene = Scene();
        var poses = scene.Poses
            .Select((pose, i) => pose with { Latitude = Lat, HeightM = i < 10 ? 300_000 - (i * 30_000) : 20, PitchDeg = -89 })
            .ToList();
        var camera = Bundle(scene with { Poses = poses }).Plan.GetProperty("camera");
        var (start, end) = (camera.GetProperty("clipStart").GetDouble(), camera.GetProperty("clipEnd").GetDouble());
        (end / start).ShouldBeGreaterThan(100_000);

        var track = camera.GetProperty("track").EnumerateArray().ToList();
        foreach (var pose in track)
        {
            var (from, to) = (pose.GetProperty("clipStart").GetDouble(), pose.GetProperty("clipEnd").GetDouble());
            from.ShouldBeGreaterThanOrEqualTo(start);
            to.ShouldBeLessThanOrEqualTo(end);
        }

        // Far away the camera draws from ten metres on and as far as it has to; close by, from a
        // tenth of a metre and no farther than the caves are.
        track[0].GetProperty("clipStart").GetDouble().ShouldBe(10);
        track[0].GetProperty("clipEnd").GetDouble().ShouldBeGreaterThan(300_000);
        track[^1].GetProperty("clipStart").GetDouble().ShouldBeLessThan(1);
        track[^1].GetProperty("clipEnd").GetDouble().ShouldBeLessThan(2000);
    }

    [Fact]
    public void A_plan_whose_changes_its_own_validator_refuses_is_not_handed_over()
    {
        // A second key on a frame that is no jump: the route's own checks stop this long before,
        // and the survey library's validator is the second lock — a plan it does not pass is this
        // code's fault.
        var scene = Scene() with { Changes = FlybyBundleChanges.None with { Sky = [new(3, "#000000"), new(3, "#ffffff")] } };

        Should.Throw<InvalidOperationException>(() => FlybyBlenderBundle.PlanJson(scene)).Message.ShouldContain("validator");
    }

    [Fact]
    public void A_flyby_with_nothing_to_draw_or_no_frame_is_refused()
    {
        var scene = Scene();

        Should.Throw<ArgumentException>(() => FlybyBlenderBundle.PlanJson(scene with { Caves = [] }));
        Should.Throw<ArgumentException>(() => FlybyBlenderBundle.PlanJson(scene with { Poses = [] }));
    }

    [Fact]
    public void A_bundle_is_written_into_the_stream_it_is_given_which_stays_open()
    {
        // Every mesh goes from memory into the zip: the writer is handed a stream and nothing
        // else — no directory to make and none to clear away.
        using var zip = new MemoryStream();

        var summary = FlybyBlenderBundle.Write(Scene(), zip);

        zip.Length.ShouldBeGreaterThan(0);
        zip.CanWrite.ShouldBeTrue();
        summary.FolderName.ShouldBe("two-caves-blender");
        summary.Frames.ShouldBe(50);
        summary.Models.ShouldBe(4);
    }

    [Theory]
    [InlineData("Peștera Mare — tur", "pestera-mare-tur-blender")]
    [InlineData("  ", "flyby-blender")]
    [InlineData("../../etc/passwd", "etc-passwd-blender")]
    [InlineData("CON", "con-flyby-blender")]
    public void The_folder_is_named_after_the_flyby_in_letters_every_file_system_takes(string name, string expected)
    {
        FlybyBlenderBundle.FolderName(name).ShouldBe(expected);
    }

    [Fact]
    public void Survey_legs_become_four_sided_tubes_and_a_leg_of_no_length_none()
    {
        var mesh = SurveyLineTubes.Build(
            [((0, 0, 0), (10, 0, 0)), ((10, 0, 0), (10, 0, 0)), ((10, 0, 0), (10, 0, -8))],
            radiusM: 0.5);

        // Two legs with a length: eight corners and eight faces each.
        mesh.VertexCount.ShouldBe(16);
        mesh.TriangleCount.ShouldBe(16);

        // Every corner is half a metre from the line of its leg.
        for (var v = 0; v < 8; v++)
        {
            Math.Sqrt(Math.Pow(mesh.Positions[(v * 3) + 1], 2) + Math.Pow(mesh.Positions[(v * 3) + 2], 2)).ShouldBe(0.5, 1e-9);
        }

        for (var v = 8; v < 16; v++)
        {
            Math.Sqrt(Math.Pow(mesh.Positions[v * 3] - 10, 2) + Math.Pow(mesh.Positions[(v * 3) + 1], 2)).ShouldBe(0.5, 1e-9);
        }
    }

    // ---- the invented scene ----

    /// <summary>
    /// Two caves: one with walls, an estimated passage and lines, anchored at the place the scene
    /// is laid out about; one a little over a kilometre east, known by its lines alone. The scene
    /// had no relief, so each is hung from the surface by its own top. Fifty frames at 25 a second,
    /// the camera 300 m south and 120 m up, turning a degree a frame.
    /// </summary>
    private static FlybyBundleScene Scene()
    {
        var cube = new IndexedMesh(
            [-5, -5, -10, 5, -5, -10, 5, 5, -10, -5, 5, -10, -5, -5, 0, 5, -5, 0, 5, 5, 0, -5, 5, 0],
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]);
        var tube = SurveyLineTubes.Build([((0, 0, -10), (30, 0, -40))], 0.5);

        var first = new FlybyBundleCave(
            Walls: new FlybyBundleMesh(cube, Lon, Lat, Top),
            Estimated: new FlybyBundleMesh(tube, Lon, Lat, Top),
            EstimatedColor: (0.8, 0.2, 0.05),
            Legs:
            [
                new FlybyBundleLeg(Lon, Lat, Top, Lon + 0.0002, Lat, Top - 60),
                new FlybyBundleLeg(Lon + 0.0002, Lat, Top - 60, Lon + 0.0004, Lat + 0.0001, Top - 120),
            ],
            AltitudeShiftM: -Top);
        var second = new FlybyBundleCave(
            Walls: null,
            Estimated: null,
            EstimatedColor: null,
            Legs: [new FlybyBundleLeg(Lon + 0.0125, Lat, Top - 40, Lon + 0.0127, Lat, Top - 70)],
            AltitudeShiftM: -(Top - 40));

        // 300 m south of the origin, as degrees of latitude on the ellipsoid there.
        const double south = 300 / 111_050.0;
        var poses = Enumerable.Range(0, 50)
            .Select(i => new FlybyBundlePose(Lon, Lat - south, 120, i, -20, 0, 320, 60))
            .ToList();

        return new FlybyBundleScene(
            Name: "Two caves",
            Width: 1280,
            Height: 720,
            Fps: 25,
            Quality: FlybyBundleQuality.Medium,
            OriginLongitude: Lon,
            OriginLatitude: Lat,
            OriginHeightM: 0,
            Relief: 1,
            Poses: poses,
            Caves: [first, second],
            Look: new FlybyBundleLook(
                "#101820",
                null,
                [new FlybyBundleDepthBand(0, "#d9480f"), new FlybyBundleDepthBand(50, "#8c2f6b"), new FlybyBundleDepthBand(150, "#6b3d9e")]),
            Texts: FlybyBundleTexts.None,
            LeftOut: []);
    }

    private static (Dictionary<string, byte[]> Entries, JsonElement Plan) Bundle(FlybyBundleScene scene)
    {
        using var zip = new MemoryStream();
        FlybyBlenderBundle.Write(scene, zip);

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var held = new MemoryStream();
            using (var input = entry.Open())
            {
                input.CopyTo(held);
            }

            entries[entry.FullName] = held.ToArray();
        }

        var top = entries.Keys.First().Split('/')[0];
        var plan = JsonDocument.Parse(entries[$"{top}/scene-plan.json"]).RootElement.Clone();
        return (entries, plan);
    }

    /// <summary>A plan as its text, for setting two beside each other.</summary>
    private static string ScenePlanJson(JsonElement plan) => plan.GetRawText();

    /// <summary>The plan's changes by the id of the model each is of.</summary>
    private static Dictionary<string, JsonElement> Changes(JsonElement plan) =>
        plan.GetProperty("changes").EnumerateArray()
            .Where(c => c.TryGetProperty("target", out _))
            .ToDictionary(c => c.GetProperty("target").GetProperty("id").GetString()!);

    private static (List<double[]> Vertices, List<string>? Colours) Ply(byte[] file) => BundlePly.Read(file);

    private static double[] Translation(JsonElement model) =>
        model.TryGetProperty("translation", out var at) ? [.. at.EnumerateArray().Select(n => n.GetDouble())] : [0, 0, 0];

    private static double[] Eye(JsonElement plan, int frame) =>
        [.. plan.GetProperty("camera").GetProperty("track")[frame].GetProperty("eye").EnumerateArray().Select(n => n.GetDouble())];
}
