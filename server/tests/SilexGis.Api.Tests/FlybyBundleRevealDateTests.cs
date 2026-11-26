// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Export;
using SilexGis.Infrastructure.Flybys;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender.Plan;
using Therion.CaveGeometry;

namespace SilexGis.Api.Tests;

/// <summary>
/// A bundle whose caves appear in the order they were explored: each leg on the day it was
/// surveyed, every cave on one clock of years, with the year counted on the screen. Invented caves
/// and invented dates throughout.
/// </summary>
public class FlybyBundleRevealDateTests
{
    private const double Lon = 7.5;
    private const double Lat = 41.25;
    private const double Top = 830;

    /// <summary>A metre of longitude there, in degrees.</summary>
    private const double East = 1 / 83_816.5;

    private static readonly DateOnly First = new(1985, 6, 1);
    private static readonly DateOnly Second = new(1990, 1, 1);
    private static readonly DateOnly WholeSurvey = new(1995, 1, 1);

    [Fact]
    public void A_cave_revealed_by_date_carries_the_year_each_leg_was_surveyed_and_falls_back_on_its_surveys_date()
    {
        var dated = Dated();
        var values = FlybyBundleRevealValues.Of(dated, FlybyBundleRevealBy.Date).ShouldNotBeNull();

        values.Min.ShouldBe(RevealField.DecimalYear(First), 1e-9);
        values.Max.ShouldBe(1990, 1e-9);
        values.WholeOnTheSurveyDate.ShouldBeFalse();
        values.LegsWithoutDate.ShouldBe(1);

        // A leg's two ends are on its own day; the leg with none takes the cave's earliest, until
        // the scene's clock puts it at the earliest of all — there from the start of the reveal.
        values.Of(dated.SurveyLegs[1]).ShouldBe((1990, 1990));
        values.Of(dated.SurveyLegs[2]).From.ShouldBe(RevealField.DecimalYear(First), 1e-9);
        var onTheClock = values.From(1970);
        onTheClock.Min.ShouldBe(1970);
        onTheClock.Of(dated.SurveyLegs[2]).ShouldBe((1970, 1970));
        onTheClock.Of(dated.SurveyLegs[1]).ShouldBe((1990, 1990));
        FlybyBundleRevealValues.Of(Distance(), FlybyBundleRevealBy.Date)!.From(1970).Min.ShouldBe(1970);

        // A wall takes the day of the nearest place on the centreline.
        var wall = new FlybyBundleMesh(new IndexedMesh([5, 0, 1, 45, 0, -1, 0, 1, 0], [0, 1, 2]), Lon, Lat, Top);
        var on = values.On(wall);
        on[0].ShouldBe(RevealField.DecimalYear(First), 1e-6);
        on[1].ShouldBe(1990, 1e-6);

        // No leg with a day: the whole cave on its survey's one date; with neither, nothing.
        var whole = FlybyBundleRevealValues.Of(Undated(WholeSurvey), FlybyBundleRevealBy.Date).ShouldNotBeNull();
        whole.WholeOnTheSurveyDate.ShouldBeTrue();
        whole.LegsWithoutDate.ShouldBe(0);
        (whole.Min, whole.Max).ShouldBe((1995, 1995));
        FlybyBundleRevealValues.Of(Undated(null), FlybyBundleRevealBy.Date).ShouldBeNull();

        // By distance the dates change nothing.
        FlybyBundleRevealValues.Of(dated).ShouldNotBeNull().Max.ShouldBe(90, 0.3);

        FlybyBundleRevealValues.DatesOf([dated, Undated(WholeSurvey), Undated(null), Distance() with { SurveyLegs = [] }])
            .ShouldBe(new FlybyBundleRevealDates(1, 1, 1));
    }

    [Fact]
    public void A_scene_revealed_by_date_keys_every_model_on_one_clock_of_years_and_counts_the_years()
    {
        var scene = Scene(FlybyBundleRevealBy.Date);
        using var zip = new MemoryStream();
        var summary = FlybyBlenderBundle.Write(scene, zip);
        summary.RevealDates.ShouldBe(new FlybyBundleRevealDates(1, 1, 1));
        var (entries, plan) = Read(zip);

        // The two caves with a date are keyed on the one clock; the one with none is there from the start.
        var changes = plan.GetProperty("changes").EnumerateArray()
            .Where(c => c.TryGetProperty("reveal", out _))
            .ToDictionary(c => c.GetProperty("target").GetProperty("id").GetString()!);
        changes.Keys.ShouldBe(["cave-01-lines", "cave-02-lines"], ignoreOrder: true);
        var keys = changes.Values.Select(c => c.GetProperty("reveal").GetRawText()).Distinct().ToList();
        keys.ShouldHaveSingleItem();
        var threshold = changes["cave-01-lines"].GetProperty("reveal");
        threshold[0].GetProperty("t").GetDouble().ShouldBe(0.4, 1e-9);
        threshold[0].GetProperty("value").GetDouble().ShouldBe(Math.Round(RevealField.DecimalYear(First), 6));
        threshold[1].GetProperty("t").GetDouble().ShouldBe(1.6, 1e-9);
        threshold[1].GetProperty("value").GetDouble().ShouldBeGreaterThan(1995);

        // The survey library's rule for the keys, the edge and the glow, over the clock of the scene.
        var rule = RevealPlanner.Change(new PlanChangeTarget { Id = "cave-01-lines" }, RevealField.DecimalYear(First), 1995, 0.4, 1.6, glowColor: new Therion.Blender.RevealSpec().GlowColor, fps: 25);
        threshold[1].GetProperty("value").GetDouble().ShouldBe(rule.Reveal![1].Value, 1e-9);
        changes["cave-01-lines"].GetProperty("revealFront").GetProperty("edge").GetDouble().ShouldBe(rule.RevealFront!.Edge, 1e-9);

        // On the meshes: years. The undated leg of the first cave is at the earliest of the scene,
        // the cave with only its survey's date is at that date everywhere.
        var first = BundlePly.Reveal(entries["dated-caves-blender/cave-01-lines.ply"]).ShouldNotBeNull();
        first.Min().ShouldBe(RevealField.DecimalYear(First), 1e-4);
        first.Max().ShouldBe(1990, 1e-4);
        BundlePly.Reveal(entries["dated-caves-blender/cave-02-lines.ply"]).ShouldNotBeNull().ShouldAllBe(value => Math.Abs(value - 1995) < 1e-4);
        BundlePly.Reveal(entries["dated-caves-blender/cave-03-lines.ply"]).ShouldBeNull();

        // The year counter: the library's text item, counting the threshold of a model it keys.
        plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldContain("texts");
        var counter = plan.GetProperty("texts").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == RevealPlanner.CounterId);
        counter.GetProperty("text").GetString().ShouldBe("{}");
        counter.GetProperty("counter").GetProperty("target").GetProperty("id").GetString().ShouldBe("cave-01-lines");
        counter.GetProperty("counter").GetProperty("min").GetDouble().ShouldBe(1985);
        counter.GetProperty("counter").GetProperty("max").GetDouble().ShouldBe(1995);

        var notes = Encoding.UTF8.GetString(entries["dated-caves-blender/README.txt"]).ReplaceLineEndings(" ");
        notes.ShouldContain("THE REVEAL BY SURVEY DATE");
        notes.ShouldContain("1 survey leg has no date: it is there from the start.");
        notes.ShouldContain("1 cave has a date only for its whole survey: it appears whole on that date.");
        notes.ShouldContain("1 cave has no survey date at all: it is there from the start.");

        // By distance the same caves are each on their own numbers, and nothing is counted.
        var (_, byDistance) = Bundle(Scene(FlybyBundleRevealBy.Distance));
        byDistance.GetProperty("changes").EnumerateArray().Count(c => c.TryGetProperty("reveal", out _)).ShouldBe(3);
        byDistance.TryGetProperty("texts", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_scene_with_no_date_at_all_is_there_from_the_start_and_its_notes_say_why()
    {
        var scene = Scene(FlybyBundleRevealBy.Date) with { Caves = [Undated(null), Undated(null, 900)] };
        var (entries, plan) = Bundle(scene);

        plan.TryGetProperty("changes", out var changes).ShouldBeFalse(changes.ToString());
        plan.TryGetProperty("texts", out _).ShouldBeFalse();
        Encoding.UTF8.GetString(entries["dated-caves-blender/README.txt"]).ReplaceLineEndings(" ")
            .ShouldContain("None of the caves has a survey date, so all of them are there from the start.");
    }

    [Fact]
    public void The_notes_of_a_reveal_by_date_count_what_has_no_date_in_both_languages_in_lines_of_the_notes_length()
    {
        foreach (var romanian in (bool[])[false, true])
        {
            var sections = FlybyBundleNotes.Sections(romanian, daylightGoes: false, reveal: new FlybyBundleNotesReveal(true, 21, 2, 1));
            var section = sections[^1];
            section.Title.ShouldBe(romanian ? "APARIȚIA DUPĂ DATA RIDICĂRII" : "THE REVEAL BY SURVEY DATE");
            section.Text.Split('\n').ShouldAllBe(line => line.Length <= 96);
            var text = section.Text.ReplaceLineEndings(" ");
            text.ShouldContain(romanian ? "21 de vize nu au dată" : "21 survey legs have no date");
            text.ShouldContain(romanian ? "2 peșteri au o dată doar pentru întreaga ridicare" : "2 caves have a date only for the whole survey");
            text.ShouldContain(romanian ? "1 peșteră nu are nicio dată a ridicării" : "1 cave has no survey date at all");
            text.ShouldContain("\"reveal-counter\"");

            var none = FlybyBundleNotes.Sections(romanian, false, reveal: new FlybyBundleNotesReveal(true, CavesWithoutDate: 3, Revealed: false))[^1].Text;
            none.ShouldNotContain("revealFront");
            none.ShouldNotContain("3");
        }
    }

    [Fact]
    public void A_plan_that_changes_one_property_of_one_thing_by_its_id_and_by_its_role_is_refused_before_it_is_handed_over()
    {
        var plan = ScenePlanSerializer.Read(FlybyBlenderBundle.PlanJson(Scene(FlybyBundleRevealBy.Date)));
        PlanChange Shown(PlanChangeTarget target) => new() { Target = target, Visible = [new PlanVisibleKey(1, false)] };

        FlybyBlenderBundle.RefuseOnePropertyChangedByIdAndByRole(plan);
        var twice = plan with
        {
            Changes = [.. plan.Changes!, Shown(new PlanChangeTarget { Id = "cave-02-lines" }), Shown(new PlanChangeTarget { Role = "survey-lines" })],
        };
        Should.Throw<InvalidOperationException>(() => FlybyBlenderBundle.RefuseOnePropertyChangedByIdAndByRole(twice))
            .Message.ShouldContain("'visible' of 'cave-02-lines'");

        // Another property of the same thing by its role, or the same property of another role, is fine.
        FlybyBlenderBundle.RefuseOnePropertyChangedByIdAndByRole(plan with
        {
            Changes =
            [
                .. plan.Changes!,
                Shown(new PlanChangeTarget { Id = "cave-02-lines" }),
                new PlanChange { Target = new PlanChangeTarget { Role = "survey-lines" }, Opacity = [new PlanNumberKey(1, 0.5)] },
                Shown(new PlanChangeTarget { Role = "walls" }),
            ],
        });
    }

    // ---- invented caves ----

    private static FlybyBundleLeg Leg(string from, double fromEast, string to, double toEast, DateOnly? day) =>
        new(Lon + (fromEast * East), Lat, Top, Lon + (toEast * East), Lat, Top) { FromStation = from, ToStation = to, SurveyedOn = day };

    /// <summary>Three legs of 30 m in a line, surveyed on two days and one of them on none.</summary>
    private static FlybyBundleCave Dated()
    {
        FlybyBundleLeg[] legs = [Leg("a", 0, "b", 30, First), Leg("b", 30, "c", 60, Second), Leg("c", 60, "d", 90, null)];
        return new FlybyBundleCave(null, null, null, legs, -Top) { SurveyLegs = legs, EntranceStations = ["a"] };
    }

    private static FlybyBundleCave Distance() => Dated();

    /// <summary>Two legs with no day, some way east of the first cave, and the survey's own date, if any.</summary>
    private static FlybyBundleCave Undated(DateOnly? surveyDate, double eastM = 500)
    {
        FlybyBundleLeg[] legs = [Leg("p", eastM, "q", eastM + 20, null), Leg("q", eastM + 20, "r", eastM + 40, null)];
        return new FlybyBundleCave(null, null, null, legs, -Top) { SurveyLegs = legs, SurveyDate = surveyDate };
    }

    private static FlybyBundleScene Scene(FlybyBundleRevealBy by) => new(
        "Dated caves",
        640,
        360,
        25,
        FlybyBundleQuality.Low,
        Lon,
        Lat,
        0,
        1,
        [.. Enumerable.Range(0, 50).Select(i => new FlybyBundlePose(Lon + (i * East), Lat - 0.002, 300, 0, -20, 0, 100, 60))],
        [Dated(), Undated(WholeSurvey), Undated(null, 900)],
        new FlybyBundleLook("#101820", "#e0e0e0", []),
        FlybyBundleTexts.None,
        [])
    {
        Reveal = new FlybyBundleReveal(0.4, 1.6, by),
    };

    private static (Dictionary<string, byte[]> Entries, JsonElement Plan) Bundle(FlybyBundleScene scene)
    {
        using var zip = new MemoryStream();
        FlybyBlenderBundle.Write(scene, zip);
        return Read(zip);
    }

    private static (Dictionary<string, byte[]> Entries, JsonElement Plan) Read(MemoryStream zip)
    {
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
}
