// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.TestData;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The survey a generated cave is grown as: one connected network from the main entrance, of the
/// size asked for, under the ground everywhere but at its entrances, drawn over dated surveys —
/// and the cave's register measures are this survey's own.
/// </summary>
public class CaveNetworkGeneratorTests
{
    private static readonly DateOnly First = new(1978, 7, 14);

    private static CaveNetworkRequest Request(CaveArchetype archetype, double length, double depth = 40, Func<double, double, double>? surface = null, IReadOnlyList<(double, double, double)>? others = null) =>
        new(archetype, length, depth, [35, 125], others ?? [], surface ?? ((x, y) => 15 + (0.05 * x)), First, TestDataPlanner.LastDay);

    [Theory]
    [InlineData(CaveArchetype.PhreaticTubes)]
    [InlineData(CaveArchetype.VadoseCanyon)]
    [InlineData(CaveArchetype.ShaftSeries)]
    [InlineData(CaveArchetype.FossilChambers)]
    [InlineData(CaveArchetype.Maze)]
    [InlineData(CaveArchetype.SpringCave)]
    [InlineData(CaveArchetype.RockShelter)]
    [InlineData(CaveArchetype.Mine)]
    public void Every_archetype_grows_one_connected_survey_of_about_the_length_asked_for_under_the_ground(CaveArchetype archetype)
    {
        var request = Request(archetype, 800, others: [(120, -40, 6)]);
        var network = CaveNetworkGenerator.Grow(request, TestDataRng.For(7, "net"));

        network.LengthM.ShouldBeGreaterThanOrEqualTo(800);
        network.LengthM.ShouldBeLessThan(800 + 200, "the growth stops within a joining passage of the target");

        // One network: every named station is reached from the main entrance.
        var reached = new HashSet<int> { 0 };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var leg in network.Legs)
            {
                if (reached.Contains(leg.From) && reached.Add(leg.To))
                {
                    changed = true;
                }

                if (reached.Contains(leg.To) && reached.Add(leg.From))
                {
                    changed = true;
                }
            }
        }

        network.Stations.Select((s, i) => (s, i)).Where(x => x.s.Name.Length > 0).ShouldAllBe(x => reached.Contains(x.i));

        // Under the ground with rock above, but for the ways in.
        foreach (var station in network.Stations.Where(s => s.Name.Length > 0 && !s.Entrance))
        {
            (station.Z + station.Up).ShouldBeLessThanOrEqualTo(request.SurfaceAbove(station.X, station.Y) - CaveNetworkGenerator.CoverM + 1e-9, station.Name);
        }

        network.Stations.Count(s => s.Entrance).ShouldBe(2);
        network.Stations[0].ShouldBe(network.Stations[0] with { X = 0, Y = 0, Z = 0, Entrance = true });
        network.Stations.Where(s => s.Name.Length > 0).Select(s => s.Name).Distinct().Count()
            .ShouldBe(network.Stations.Count(s => s.Name.Length > 0), "station names repeat");
    }

    [Fact]
    public void A_shaft_series_goes_about_as_deep_as_asked_and_a_canyon_descends()
    {
        var shaft = CaveNetworkGenerator.Grow(Request(CaveArchetype.ShaftSeries, 600, depth: 250), TestDataRng.For(11, "net"));
        shaft.NegativeDepthM.ShouldBeGreaterThan(180);

        var canyon = CaveNetworkGenerator.Grow(Request(CaveArchetype.VadoseCanyon, 600, depth: 80), TestDataRng.For(11, "net"));
        canyon.NegativeDepthM.ShouldBeGreaterThan(20);
        canyon.DepthM.ShouldBe(canyon.TopZ - canyon.BottomZ);
    }

    [Fact]
    public void The_surveys_are_dated_in_order_between_the_first_survey_and_the_last_day()
    {
        var network = CaveNetworkGenerator.Grow(Request(CaveArchetype.FossilChambers, 3000), TestDataRng.For(3, "net"));

        network.Surveys.Count.ShouldBeGreaterThan(2);
        network.Surveys[0].Date.ShouldBe(First);
        network.Surveys.Select(s => s.Date).ShouldBeInOrder();
        network.Surveys.ShouldAllBe(s => s.Date <= TestDataPlanner.LastDay);
        network.Legs.ShouldAllBe(l => l.Survey >= 0 && l.Survey < network.Surveys.Count);
        network.Legs.ShouldContain(l => l.Splay);
        network.Stations.ShouldContain(s => s.Continuation, "a cave being explored leaves leads");
    }

    [Fact]
    public void The_same_request_and_stream_grow_the_same_survey()
    {
        var a = CaveNetworkGenerator.Grow(Request(CaveArchetype.Maze, 500), TestDataRng.For(5, "net"));
        var b = CaveNetworkGenerator.Grow(Request(CaveArchetype.Maze, 500), TestDataRng.For(5, "net"));
        var c = CaveNetworkGenerator.Grow(Request(CaveArchetype.Maze, 500), TestDataRng.For(6, "net"));

        b.Stations.ShouldBe(a.Stations);
        b.Legs.ShouldBe(a.Legs);
        c.Stations.ShouldNotBe(a.Stations);
    }

    [Fact]
    public void A_planned_cave_takes_its_register_measures_from_its_own_survey()
    {
        var plan = TestDataPlanner.Plan(TestDataProfiles.Default);

        foreach (var cave in plan.Caves)
        {
            var network = cave.Network.ShouldNotBeNull();
            cave.SurveyedLength.ShouldBe(Math.Round((decimal)network.LengthM, 1), cave.Name);
            cave.Depth.ShouldBe(Math.Round((decimal)network.DepthM, 1), cave.Name);
            cave.NegativeDepth.ShouldBe(Math.Round((decimal)network.NegativeDepthM, 1), cave.Name);
            network.Stations.Count(s => s.Entrance).ShouldBe(cave.Entrances.Count, cave.Name);
            if (cave.SurveyedLength is { } length && cave.CaveTypeCode != "rock_shelter")
            {
                // The description tells the same length as the register, in the register's words.
                cave.Description.ShouldContain(length.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("ro-RO")), Case.Sensitive, cave.Name);
            }
        }

        plan.Caves.Count(c => c.WithSurvey).ShouldBe(325); // 65 % of 500
        plan.Caves.Where(c => c.CaveTypeCode == "pit").Average(c => (double)c.NegativeDepth!.Value)
            .ShouldBeGreaterThan(plan.Caves.Where(c => c.CaveTypeCode == "cave").Average(c => (double)c.NegativeDepth!.Value));
    }
}
