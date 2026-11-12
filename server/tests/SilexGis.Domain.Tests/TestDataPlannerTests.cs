// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.TestData;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The test-data generator's plan: the same profile always plans the same world, the shares a
/// profile asks for come out exactly, and every planned cave is where and what it claims to be.
/// </summary>
public class TestDataPlannerTests
{
    private static readonly TestDataProfile Small = TestDataProfiles.Small;

    [Fact]
    public void The_same_profile_plans_the_same_world_and_another_seed_a_different_one()
    {
        var first = TestDataPlanner.Plan(Small);
        var again = TestDataPlanner.Plan(Small);
        var other = TestDataPlanner.Plan(Small with { Seed = Small.Seed + 1 });

        again.Caves.Select(Fingerprint).ShouldBe(first.Caves.Select(Fingerprint));
        other.Caves.Select(c => c.Name).ShouldNotBe(first.Caves.Select(c => c.Name));
    }

    [Fact]
    public void A_cave_planned_later_does_not_change_the_caves_planned_before_it()
    {
        // One stream per cave: asking for more caves adds caves and leaves the first ones alone,
        // apart from the shares, which are spread over the whole run.
        var small = TestDataPlanner.Plan(Small);
        var bigger = TestDataPlanner.Plan(Small with { Caves = Small.Caves with { Count = Small.Caves.Count + 10 } });

        bigger.Caves.Take(small.Caves.Count).Select(c => (c.RegionCode, c.IdentificationCode))
            .ShouldBe(small.Caves.Select(c => (c.RegionCode, c.IdentificationCode)));
    }

    [Fact]
    public void Shares_come_out_as_asked_and_a_small_run_still_shows_every_case()
    {
        var plan = TestDataPlanner.Plan(Small);
        var caves = plan.Caves;

        caves.Count.ShouldBe(25);
        foreach (var visibility in Enum.GetValues<Visibility>())
        {
            caves.Count(c => c.Visibility == visibility).ShouldBeGreaterThan(0, $"no {visibility} cave in a small run");
        }

        caves.Count(c => c.LocationProtected).ShouldBe(2); // 8 % of 25, rounded
        caves.Count(c => c.Entrances.Count > 1).ShouldBeGreaterThan(0);
        foreach (var type in Small.Caves.TypeWeights.Keys)
        {
            caves.ShouldContain(c => c.CaveTypeCode == type, $"no {type} in a small run");
        }

        var big = TestDataPlanner.Plan(TestDataProfiles.Default).Caves;
        big.Count(c => c.Visibility == Visibility.Public).ShouldBe(225);
        big.Count(c => c.Visibility == Visibility.Private).ShouldBe(25);
        big.Count(c => c.LocationProtected).ShouldBe(40);
    }

    [Fact]
    public void Every_cave_is_unique_by_name_and_code_and_lies_inside_its_region()
    {
        var plan = TestDataPlanner.Plan(TestDataProfiles.Default);

        plan.Caves.Select(c => c.Name).Distinct().Count().ShouldBe(plan.Caves.Count);
        plan.Caves.Select(c => c.IdentificationCode).Distinct().Count().ShouldBe(plan.Caves.Count);
        foreach (var cave in plan.Caves)
        {
            var region = TestDataRegions.Find(cave.RegionCode)!;
            var main = cave.Entrances.Single(e => e.IsMain);
            region.Contains(main.Lon, main.Lat).ShouldBeTrue($"{cave.Name} lies outside {region.Code}");
            main.AltitudeM.ShouldBeInRange(region.SurfaceMinM, region.SurfaceMaxM);
            cave.IdentificationCode.ShouldStartWith(region.BasinCode + "/");
            cave.Description.ShouldEndWith(TestDataTextRo.Footer("testgenerated"));
        }
    }

    [Fact]
    public void Measures_agree_with_each_other()
    {
        foreach (var cave in TestDataPlanner.Plan(TestDataProfiles.Default).Caves)
        {
            (cave.PositiveDepth!.Value + cave.NegativeDepth!.Value).ShouldBe(cave.Depth!.Value, 0.11m);
            if (cave.CaveTypeCode == "pit")
            {
                cave.SurveyedLength!.Value.ShouldBeGreaterThanOrEqualTo(cave.Depth!.Value);
            }

            if (cave.ProjectedExtension is { } projected)
            {
                projected.ShouldBeLessThanOrEqualTo(cave.RealExtension!.Value + 0.1m);
            }

            if (cave.EstimatedLength is { } estimated)
            {
                estimated.ShouldBeGreaterThan(cave.SurveyedLength!.Value);
                cave.ExplorationStatus.ShouldBe(ExplorationStatus.Ongoing);
            }
        }
    }

    [Fact]
    public void A_mine_is_entered_through_artificial_entrances()
    {
        var mines = TestDataPlanner.Plan(TestDataProfiles.Default).Caves.Where(c => c.CaveTypeCode == "mine").ToList();
        mines.ShouldNotBeEmpty();
        mines.SelectMany(m => m.Entrances).ShouldAllBe(e => e.EntranceTypeCode == TestDataVocabulary.ArtificialEntrance);
    }

    [Fact]
    public void Chosen_regions_are_the_only_ones_used()
    {
        var plan = TestDataPlanner.Plan(Small with { Regions = ["bucegi", "praid"] });

        plan.Regions.Select(r => r.Region.Code).ShouldBe(["bucegi", "praid"]);
        plan.Caves.Select(c => c.RegionCode).Distinct().Order().ShouldBe(["bucegi", "praid"]);
    }

    [Fact]
    public void The_random_streams_do_not_change_between_versions()
    {
        // Pinned values: if the stream changed, every seed a person wrote down would give a
        // different dataset from the one they remember.
        var a = TestDataRng.For(42, "cave", 7);
        var b = TestDataRng.For(42, "cave", 7);
        Enumerable.Range(0, 20).Select(_ => a.NextULong()).ShouldBe(Enumerable.Range(0, 20).Select(_ => b.NextULong()));
        TestDataRng.For(42, "cave", 8).NextULong().ShouldNotBe(TestDataRng.For(42, "cave", 7).NextULong());
        TestDataRng.For(1, "x").NextULong().ShouldBe(PinnedFirstDraw);
    }

    [Fact]
    public void A_quota_adds_up_and_gives_every_case_asked_for_one_item_when_it_can()
    {
        var rng = TestDataRng.For(1, "quota");
        var values = TestDataQuota.Allocate(10, [("a", 97.0), ("b", 2.0), ("c", 1.0), ("d", 0.0)], rng);

        values.Length.ShouldBe(10);
        values.Count(v => v == "b").ShouldBe(1);
        values.Count(v => v == "c").ShouldBe(1);
        values.ShouldNotContain("d");

        // Too few items to go round: the largest cases keep theirs.
        TestDataQuota.Allocate(1, [("a", 90.0), ("b", 10.0)], rng).ShouldBe(["a"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60_000)]
    public void A_cave_count_outside_the_limits_is_refused(int count)
    {
        TestDataProfileRules.Problems(Small with { Caves = Small.Caves with { Count = count } }, 20_000)
            .ShouldContain(p => p.StartsWith("caves.count", StringComparison.Ordinal));
    }

    [Fact]
    public void A_profile_is_refused_for_each_thing_wrong_with_it_and_the_shipped_ones_are_not()
    {
        foreach (var shipped in TestDataProfiles.All.Values)
        {
            TestDataProfileRules.Problems(shipped, 20_000).ShouldBeEmpty();
        }

        var bad = Small with
        {
            Locale = "fr",
            Regions = ["atlantis"],
            Caves = Small.Caves with { TypeWeights = new Dictionary<string, int> { ["lava_tube"] = 1 } },
            Access = Small.Access with { VisibilityPercent = new TestDataVisibilityPercent { Public = 90 } },
            Marking = new TestDataMarkingProfile { Tag = "  " },
        };
        var problems = TestDataProfileRules.Problems(bad, 20_000);
        problems.ShouldContain(p => p.StartsWith("locale", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("atlantis", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("lava_tube", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("add up to 100", StringComparison.Ordinal));
        problems.ShouldContain(p => p.StartsWith("marking.tag", StringComparison.Ordinal));

        // The English pack is a language the generator knows but cannot write yet.
        TestDataProfileRules.Problems(Small with { Locale = "en" }, 20_000)
            .ShouldContain(p => p.Contains("not available yet", StringComparison.Ordinal));
    }

    [Fact]
    public void A_profile_reads_back_as_it_was_written_and_unknown_sections_are_ignored()
    {
        var json = TestDataProfiles.Default.ToJson();
        TestDataProfile.FromJson(json).ToJson().ShouldBe(json);

        var later = TestDataProfile.FromJson("""{"seed":7,"caves":{"count":3},"timeline":{"trips":4000}}""");
        later.Seed.ShouldBe(7);
        later.Caves.Count.ShouldBe(3);
        later.Caves.LengthMedianM.ShouldBe(TestDataProfiles.Default.Caves.LengthMedianM);
    }

    private const ulong PinnedFirstDraw = 8349411061673048546UL;

    private static string Fingerprint(PlannedCave c) =>
        $"{c.Name}|{c.IdentificationCode}|{c.Visibility}|{c.LocationProtected}|{c.Entrances[0].Lon:F6}|{c.Entrances[0].Lat:F6}|{c.SurveyedLength}";
}
