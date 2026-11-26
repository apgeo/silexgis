// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Export;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a render bundle points out — entrances and features as markers, names beside them,
/// photographs at stations — and how its caves appear part by part: the plan and the files the
/// writer makes of an invented scene. Nothing here is a real cave, place or photograph; the
/// pictures are bands of colour made in the test.
/// </summary>
public class FlybyBundlePointedTests
{
    private const double Lon = 7.5;
    private const double Lat = 41.25;
    private const double Top = 830;

    /// <summary>A metre of latitude and of longitude there, in degrees.</summary>
    private const double North = 1 / 111_050.0;
    private const double East = 1 / 83_816.5;

    [Fact]
    public void A_marker_is_a_ball_in_its_layers_colour_pinned_to_the_nearest_caves_model_and_sized_by_the_nearest_the_camera_comes()
    {
        var plan = Bundle(Scene() with
        {
            MarkerLayers =
            [
                new FlybyBundleMarkerLayer(
                    "entrance",
                    "#146262",
                    [new FlybyBundleMarker(Lon + (20 * East), Lat, 3, null), new FlybyBundleMarker(Lon + (1040 * East), Lat, -2, null)],
                    [],
                    []),
            ],
        }).Plan;

        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldContain("annotations");
        var markers = plan.GetProperty("annotations").EnumerateArray().ToList();
        markers.Count.ShouldBe(2);
        markers.Select(m => m.GetProperty("id").GetString()).ShouldBe(["entrance-001", "entrance-002"]);
        markers.ShouldAllBe(m => m.GetProperty("kind").GetString() == "marker" && m.GetProperty("role").GetString() == "entrance");

        // The colour as light, not as a screen's number.
        var colour = markers[0].GetProperty("color");
        colour[1].GetDouble().ShouldBe(Math.Pow((0x62 / 255.0 + 0.055) / 1.055, 2.4), 1e-5);

        // The first stands twenty metres east of the first cave and rides its walls; the second a
        // kilometre away, beside the second cave, and rides that one's lines. Each at a point of
        // its model: metres east, north and up of the model's own zero — which the scene hung from
        // the surface, so the ground the marker stands on is three metres above it.
        markers[0].GetProperty("model").GetString().ShouldBe("cave-01-walls");
        var at = Numbers(markers[0].GetProperty("at"));
        at[0].ShouldBe(20, 0.02);
        at[1].ShouldBe(0, 0.01);
        at[2].ShouldBe(3, 0.01);
        markers[1].GetProperty("model").GetString().ShouldBe("cave-02-lines");

        // The camera comes within thirty metres of the first cave: a ball of a quarter of a metre.
        var nearest = Enumerable.Range(0, 50).Min(i => Math.Sqrt(Math.Pow(Away(i), 2) + Math.Pow(20, 2) + Math.Pow(Up - 3, 2)));
        markers[0].GetProperty("radius").GetDouble().ShouldBe(nearest * FlybyBlenderBundle.MarkerShare, 0.002);
        markers[1].GetProperty("radius").GetDouble().ShouldBeGreaterThan(8);

        // Nothing shows or hides a layer the flyby leaves alone.
        plan.TryGetProperty("changes", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_name_is_written_above_its_marker_and_a_layer_comes_and_goes_with_its_names()
    {
        var plan = Bundle(Scene() with
        {
            MarkerLayers =
            [
                new FlybyBundleMarkerLayer(
                    "entrance",
                    "#146262",
                    [new FlybyBundleMarker(Lon, Lat + (15 * North), 0, "  Mouth of nowhere "), new FlybyBundleMarker(Lon, Lat - (15 * North), 0, null)],
                    [new FlybyBundleShownKey(10, false, 5), new FlybyBundleShownKey(30, true)],
                    [new FlybyBundleNumberKey(0, 1), new FlybyBundleNumberKey(20, 0.5, FlybyBundleEasing.Smooth)]),
                new FlybyBundleMarkerLayer("feature-layer-1", "#722ed1", [new FlybyBundleMarker(Lon, Lat, 0, null)], [], []),
            ],
        }).Plan;

        var said = plan.GetProperty("annotations").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!);
        said.Keys.ShouldBe(["entrance-001", "entrance-001-name", "entrance-002", "feature-layer-1-001"], ignoreOrder: true);
        var name = said["entrance-001-name"];
        name.GetProperty("kind").GetString().ShouldBe("callout");
        name.GetProperty("role").GetString().ShouldBe("entrance-name");
        name.GetProperty("text").GetString().ShouldBe("Mouth of nowhere");
        name.GetProperty("model").GetString().ShouldBe(said["entrance-001"].GetProperty("model").GetString());
        name.GetProperty("at").GetRawText().ShouldBe(said["entrance-001"].GetProperty("at").GetRawText());
        var radius = said["entrance-001"].GetProperty("radius").GetDouble();
        name.GetProperty("size").GetDouble().ShouldBe(radius * FlybyBlenderBundle.NameLetters, 1e-5);
        Numbers(name.GetProperty("offset")).ShouldBe([0, 0, Math.Round(Math.Round(radius * 2.5, 6) * 4, 6)]);

        // One change for the markers of the layer and one for its names, the same keys in both;
        // the layer the flyby leaves alone has none.
        var changes = plan.GetProperty("changes").EnumerateArray().ToList();
        changes.Select(c => c.GetProperty("target").GetProperty("role").GetString()).ShouldBe(["entrance", "entrance-name"]);
        foreach (var change in changes)
        {
            var visible = change.GetProperty("visible").EnumerateArray().ToList();
            visible[0].GetProperty("t").GetDouble().ShouldBe(0.4, 1e-9);
            visible[0].GetProperty("value").GetBoolean().ShouldBeFalse();
            visible[0].GetProperty("fade").GetDouble().ShouldBe(0.2, 1e-9);
            visible[0].GetProperty("easing").GetString().ShouldBe("smooth");
            visible[1].GetProperty("t").GetDouble().ShouldBe(1.2, 1e-9);
            visible[1].TryGetProperty("fade", out _).ShouldBeFalse();
            change.GetProperty("opacity").GetArrayLength().ShouldBe(2);
        }
    }

    [Fact]
    public void A_photograph_is_a_file_of_the_bundle_shown_while_its_layer_is_on_and_the_camera_is_near_its_station()
    {
        var picture = Picture(200, 120);
        var (entries, plan) = Bundle(Scene() with
        {
            Photos = new FlybyBundlePhotos(
                [
                    // At a station of the first cave, twenty metres down; and one said to be of the
                    // second cave, kilometres off, where the camera never comes near enough to
                    // show a photograph of any width a bundle makes.
                    new FlybyBundlePhoto(0, Lon, Lat, Top - 20, picture, 200, 120),
                    new FlybyBundlePhoto(1, Lon + 0.04, Lat, Top - 55, Picture(64, 64), 64, 64),
                ],
                [new FlybyBundleShownKey(45, false)])
            {
                Found = 5,
            },
        });

        // The picture is in the bundle as it was given, under a name that says nothing.
        entries["two-caves-blender/photos/photo-001.jpg"].ShouldBe(picture);
        var photos = plan.GetProperty("annotations").EnumerateArray().ToList();
        var photo = photos.Single(a => a.GetProperty("kind").GetString() == "photo" && a.GetProperty("id").GetString() == "photo-001");
        photo.GetProperty("role").GetString().ShouldBe(FlybyBlenderBundle.PhotoRole);
        photo.GetProperty("image").GetString().ShouldBe("photos/photo-001.jpg");
        photo.GetProperty("model").GetString().ShouldBe("cave-01-walls");
        Numbers(photo.GetProperty("at")).ShouldBe([0, 0, -20], 0.01);

        // As wide as a fifth of the distance it is read from — here not the nearest the camera
        // comes, which it reaches on the last frame and flies towards at a hundred and forty
        // metres a second, but two fifths of how far it is a second before: so the photograph is
        // up for a second and not for a frame. It stands above the station by half its height and
        // a quarter of its width, with a line down to it and a border.
        double From(int i) => Math.Sqrt(Math.Pow(Away(i), 2) + Math.Pow(Up + 20, 2));
        var readFrom = Math.Max(From(49), From(24) / 2.5);
        readFrom.ShouldBeGreaterThan(From(49));
        var width = photo.GetProperty("width").GetDouble();
        width.ShouldBe(readFrom * FlybyBlenderBundle.PhotoShare, 0.01);
        FlybyBlenderBundle.ReadFrom([.. Enumerable.Range(0, 50).Select(From)], 25).ShouldBe(readFrom, 1e-9);
        // A camera that stays where it is reads from where it is.
        FlybyBlenderBundle.ReadFrom([40, 40, 40], 25).ShouldBe(40);
        Numbers(photo.GetProperty("offset")).ShouldBe([0, 0, (width * 120 / 200 / 2) + (width / 4)], 1e-4);
        photo.GetProperty("lineWidth").GetDouble().ShouldBe(width * 0.01, 1e-5);
        photo.GetProperty("frame").GetProperty("width").GetDouble().ShouldBe(width * 0.02, 1e-5);
        photo.TryGetProperty("height", out _).ShouldBeFalse("the picture keeps its own shape");
        photo.TryGetProperty("text", out _).ShouldBeFalse("a photograph carries no title: a title may name a place");

        // In the picture from the frame the camera is within twelve and a half of its widths —
        // fading in over half a second — until its layer is switched off at frame 45.
        var near = width * FlybyBlenderBundle.PhotoNearWidths;
        var first = Enumerable.Range(0, 50).First(i => From(i) <= near);
        first.ShouldBeLessThanOrEqualTo(24, "a second before the camera is nearest, the photograph is up");
        first.ShouldBeGreaterThan(0);
        var shown = plan.GetProperty("changes").EnumerateArray()
            .Single(c => c.GetProperty("target").GetProperty("id").GetString() == "photo-001")
            .GetProperty("visible").EnumerateArray().ToList();
        shown.Count.ShouldBe(2);
        shown[0].GetProperty("value").GetBoolean().ShouldBeTrue();
        shown[0].GetProperty("t").GetDouble().ShouldBe(first / 25.0, 1e-6);
        shown[0].GetProperty("fade").GetDouble().ShouldBeGreaterThan(0);
        shown[1].GetProperty("value").GetBoolean().ShouldBeFalse();
        (shown[1].GetProperty("t").GetDouble() + shown[1].GetProperty("fade").GetDouble()).ShouldBe(45 / 25.0, 1e-6);

        // The one the camera stays far from is neither in the plan nor in the zip — and the notes
        // say how many photographs there are that the bundle does not show.
        photos.Count.ShouldBe(1);
        entries.Keys.Count(key => key.Contains("/photos/", StringComparison.Ordinal)).ShouldBe(1);
        var readme = Encoding.UTF8.GetString(entries["two-caves-blender/README.txt"]);
        readme.ShouldContain("photos/photo-001.jpg");
        readme.ShouldContain("  - 4 photographs taken at stations");
        readme.ShouldContain("WHAT IS POINTED OUT");
        readme.ShouldContain("The scene has 1 photograph,");
    }

    [Fact]
    public void A_bundle_shows_only_so_many_photographs_and_none_while_their_layer_is_off_throughout()
    {
        var picture = Picture(32, 32);
        var many = Enumerable.Range(0, FlybyBlenderBundle.MaxPhotos + 5)
            .Select(i => new FlybyBundlePhoto(0, Lon + (i * East), Lat, Top - 10, picture, 32, 32))
            .ToList();
        using var zip = new MemoryStream();
        var summary = FlybyBlenderBundle.Write(Scene() with { Photos = new FlybyBundlePhotos(many, []) { Found = many.Count } }, zip);
        summary.Photos.ShouldBe(FlybyBlenderBundle.MaxPhotos);
        summary.PhotosShown.ShouldBe(Enumerable.Range(0, FlybyBlenderBundle.MaxPhotos));

        using var none = new MemoryStream();
        var hidden = FlybyBlenderBundle.Write(
            Scene() with { Photos = new FlybyBundlePhotos(many, [new FlybyBundleShownKey(0, false)]) { Found = many.Count } }, none);
        hidden.Photos.ShouldBe(0);
        none.Position = 0;
        using var archive = new ZipArchive(none, ZipArchiveMode.Read);
        archive.Entries.ShouldAllBe(entry => !entry.FullName.Contains("photos/", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_the_places_asked_about_the_ones_the_camera_comes_nearest_to_come_first()
    {
        var poses = Scene().Poses;
        var order = FlybyBlenderBundle.NearestToTheCamera(
            poses,
            [(Lon + 0.01, Lat, 0), (Lon, Lat, 0), (Lon, Lat - (40 * North), 30), (Lon + 0.02, Lat, 0)],
            3);
        // The camera ends thirty metres south and thirty up: on the third place, then the second.
        order.ShouldBe([2, 1, 0]);
        FlybyBlenderBundle.NearestToTheCamera(poses, [], 3).ShouldBeEmpty();
    }

    [Fact]
    public void A_cave_that_is_revealed_carries_on_every_vertex_the_distance_along_its_passages_from_its_entrances()
    {
        // Three legs in a line of 30 m each, and a side passage of 40 m from the first joint:
        //   a — b — c — d, and b — e. The survey flags d as the entrance.
        FlybyBundleLeg Leg(string from, double fromEast, string to, double toEast, double toNorth = 0) =>
            new(Lon + (fromEast * East), Lat, 500, Lon + (toEast * East), Lat + (toNorth * North), 500) { FromStation = from, ToStation = to };
        FlybyBundleLeg[] legs = [Leg("a", 0, "b", 30), Leg("b", 30, "c", 60), Leg("c", 60, "d", 90), Leg("b", 30, "e", 30, 40)];
        var cave = new FlybyBundleCave(null, null, null, legs, 0) { SurveyLegs = legs, EntranceStations = ["d"] };
        var values = FlybyBundleRevealValues.Of(cave).ShouldNotBeNull();

        values.Min.ShouldBe(0);
        values.Max.ShouldBe(100, 0.3);
        values.Of(legs[2]).From.ShouldBe(30, 0.3);
        values.Of(legs[2]).To.ShouldBe(0);
        values.Of(legs[0]).From.ShouldBe(90, 0.3);
        values.Of(legs[3]).To.ShouldBe(100, 0.3);

        // With no entrance flagged, from the first station of the first leg.
        var unflagged = FlybyBundleRevealValues.Of(cave with { EntranceStations = [] }).ShouldNotBeNull();
        unflagged.Of(legs[0]).From.ShouldBe(0);
        unflagged.Of(legs[2]).To.ShouldBe(90, 0.3);

        // Two legs that name one station meet there, whatever their coordinates' last digit says.
        var apart = legs.Select((leg, i) => i == 1 ? leg with { FromAltitudeM = 500.0004 } : leg).ToArray();
        FlybyBundleRevealValues.Of(cave with { SurveyLegs = apart }).ShouldNotBeNull().Max.ShouldBe(100, 0.3);

        // A wall takes the number of the nearest place on the centreline.
        var wall = new FlybyBundleMesh(new IndexedMesh([45, 2, 1, 0, -3, 0, 30, 39, 0], [0, 1, 2]), Lon, Lat, 500);
        var on = values.On(wall);
        on[0].ShouldBe(45, 0.8);
        on[1].ShouldBe(90, 0.8);
        on[2].ShouldBe(99, 0.8);

        // A cave with no survey has nothing to be revealed by.
        FlybyBundleRevealValues.Of(new FlybyBundleCave(wall, null, null, [], 0)).ShouldBeNull();
    }

    [Fact]
    public void A_scene_that_reveals_its_caves_writes_the_numbers_into_each_mesh_and_one_threshold_a_model()
    {
        var scene = Scene();
        var revealed = scene with
        {
            Caves = [.. scene.Caves.Select(cave => cave with { SurveyLegs = cave.Legs })],
            Reveal = new FlybyBundleReveal(0.4, 1.6),
        };
        var (entries, plan) = Bundle(revealed);

        // The modules the plan needs are the library's to list; a reveal is one of its changes.
        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldContain("changes");
        var changes = plan.GetProperty("changes").EnumerateArray().ToDictionary(c => c.GetProperty("target").GetProperty("id").GetString()!);
        changes.Keys.ShouldBe(["cave-01-walls", "cave-01-estimated", "cave-01-lines", "cave-02-lines"], ignoreOrder: true);
        foreach (var (id, change) in changes)
        {
            var keys = change.GetProperty("reveal").EnumerateArray().ToList();
            keys.Count.ShouldBe(2);
            keys[0].GetProperty("t").GetDouble().ShouldBe(0.4, 1e-9);
            keys[1].GetProperty("t").GetDouble().ShouldBe(1.6, 1e-9);
            keys[0].GetProperty("value").GetDouble().ShouldBe(0);
            var front = change.GetProperty("revealFront");
            front.GetProperty("edge").GetDouble().ShouldBeGreaterThan(0);
            front.GetProperty("glow").GetProperty("strength").GetDouble().ShouldBe(3);
            BundlePly.Reveal(entries[$"two-caves-blender/{id}.ply"]).ShouldNotBeNull().Min().ShouldBeGreaterThanOrEqualTo(0);
        }

        // Every model of a cave finishes on the same number: the cave's own length from its start.
        changes["cave-01-walls"].GetProperty("reveal")[1].GetProperty("value").GetDouble()
            .ShouldBe(changes["cave-01-lines"].GetProperty("reveal")[1].GetProperty("value").GetDouble());
        var lines = BundlePly.Reveal(entries["two-caves-blender/cave-01-lines.ply"])!;
        lines.Take(4).ShouldAllBe(value => value == 0);
        lines.Max().ShouldBeGreaterThan(120);

        Encoding.UTF8.GetString(entries["two-caves-blender/README.txt"]).ShouldContain("THE REVEAL");

        // Without a reveal no mesh carries a number and no model a threshold; a cave with no
        // survey to measure along is there from the first frame while the others appear.
        BundlePly.Reveal(Bundle(scene).Entries["two-caves-blender/cave-01-lines.ply"]).ShouldBeNull();
        var partly = Bundle(revealed with { Caves = [revealed.Caves[0] with { SurveyLegs = [] }, revealed.Caves[1]] });
        partly.Plan.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("target").GetProperty("id").GetString())
            .ShouldBe(["cave-02-lines"]);
        BundlePly.Reveal(partly.Entries["two-caves-blender/cave-01-walls.ply"]).ShouldBeNull();

        // A reveal asked for past the clip is fitted into it, on two different frames.
        var late = Bundle(revealed with { Reveal = new FlybyBundleReveal(30, 60) }).Plan.GetProperty("changes")[0].GetProperty("reveal");
        late[0].GetProperty("t").GetDouble().ShouldBe(48 / 25.0, 1e-9);
        late[1].GetProperty("t").GetDouble().ShouldBe(49 / 25.0, 1e-9);
    }

    [Fact]
    public void The_notes_say_what_is_pointed_out_and_how_the_caves_appear_in_both_languages_and_in_lines_of_the_notes_length()
    {
        foreach (var romanian in (bool[])[false, true])
        {
            var sections = FlybyBundleNotes.Sections(romanian, daylightGoes: false, markers: 21, photos: 1, reveal: new FlybyBundleNotesReveal(ByDate: false));
            sections.Count.ShouldBe(4);
            sections.SelectMany(section => section.Text.Split('\n')).ShouldAllBe(line => line.Length <= 96);
            sections[2].Text.ShouldContain(romanian ? "21 de marcaje și 1 fotografie" : "21 markers and 1 photograph");
            sections[3].Text.ShouldContain("\"revealFront\"");
            FlybyBundleNotes.Sections(romanian, false, markers: 0, photos: 3)[2].Text
                .ShouldContain(romanian ? "Scena are 3 fotografii," : "The scene has 3 photographs,");
            FlybyBundleNotes.Sections(romanian, false).Count.ShouldBe(2);
        }

        FlybyBundleOmissions.MarkersNotDrawn(1).ShouldStartWith("1 of the entrances and features asked for");
        FlybyBundleOmissions.MarkersNotDrawn(3, romanian: true).ShouldStartWith("3 dintre intrările și obiectele cerute");
        FlybyBundleOmissions.PhotosNotShown(25, romanian: true).ShouldStartWith("25 de fotografii");
        foreach (var omission in Enum.GetValues<FlybyBundleOmission>())
        {
            FlybyBundleOmissions.Sentence(omission).ShouldNotBeNullOrWhiteSpace();
            FlybyBundleOmissions.Sentence(omission, romanian: true).ShouldNotBe(FlybyBundleOmissions.Sentence(omission));
        }
    }

    // ---- the invented scene ----

    private const double Up = 30;

    /// <summary>How far south of the first cave the camera is at a frame: from 300 m to 30.</summary>
    private static double Away(int frame) => 300 - (frame * 270 / 49.0);

    /// <summary>
    /// Two caves, as the writer's other tests have them — one with walls, an estimated passage
    /// and lines where the scene is laid out, one a kilometre east known by its lines — hung from
    /// the surface of a scene with no relief. Fifty frames: the camera thirty metres up, coming in
    /// from 300 m south of the first cave to 30.
    /// </summary>
    private static FlybyBundleScene Scene()
    {
        var cube = new IndexedMesh(
            [-5, -5, -10, 5, -5, -10, 5, 5, -10, -5, 5, -10, -5, -5, 0, 5, -5, 0, 5, 5, 0, -5, 5, 0],
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0]);
        var tube = SurveyLineTubes.Build([((0, 0, -10), (30, 0, -40))], 0.5);
        var first = new FlybyBundleCave(
            new FlybyBundleMesh(cube, Lon, Lat, Top),
            new FlybyBundleMesh(tube, Lon, Lat, Top),
            (0.8, 0.2, 0.05),
            [
                new FlybyBundleLeg(Lon, Lat, Top, Lon + 0.0002, Lat, Top - 60),
                new FlybyBundleLeg(Lon + 0.0002, Lat, Top - 60, Lon + 0.0004, Lat + 0.0001, Top - 120),
            ],
            AltitudeShiftM: -Top);
        var second = new FlybyBundleCave(
            null,
            null,
            null,
            [new FlybyBundleLeg(Lon + 0.0125, Lat, Top - 40, Lon + 0.0127, Lat, Top - 70)],
            AltitudeShiftM: -(Top - 40));
        return new FlybyBundleScene(
            "Two caves",
            1280,
            720,
            25,
            FlybyBundleQuality.Low,
            Lon,
            Lat,
            0,
            1,
            [.. Enumerable.Range(0, 50).Select(i => new FlybyBundlePose(Lon, Lat - (Away(i) * North), Up, 0, -20, 0, 100, 60))],
            [first, second],
            new FlybyBundleLook("#101820", null, [new FlybyBundleDepthBand(0, "#d9480f")]),
            FlybyBundleTexts.None,
            []);
    }

    /// <summary>A picture of nothing: two bands of colour, as the bytes of a JPEG file.</summary>
    private static byte[] Picture(uint width, uint height)
    {
        using var image = new MagickImage(new MagickColor(30, 90, 160), width, height);
        using var band = new MagickImage(new MagickColor(230, 200, 60), width, height / 3);
        image.Composite(band, 0, (int)(height / 3));
        return image.ToByteArray(MagickFormat.Jpeg);
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
        return (entries, JsonDocument.Parse(entries[$"{top}/scene-plan.json"]).RootElement.Clone());
    }

    private static double[] Numbers(JsonElement list) => [.. list.EnumerateArray().Select(n => n.GetDouble())];
}
