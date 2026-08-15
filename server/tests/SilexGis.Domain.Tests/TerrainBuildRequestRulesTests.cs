// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What an installation accepts as a request to build terrain, and where a step's own progress sits
/// in the whole build's.
/// </summary>
/// <remarks>
/// These rules are the only thing between a rectangle dragged wrong on a world map and hours of
/// work over an area nobody meant to ask for, so every refusal is asserted beside the acceptance it
/// is meant to be distinguishable from — a guard that refuses everything looks identical to a guard
/// that works until somebody tries to use the feature.
/// </remarks>
public class TerrainBuildRequestRulesTests
{
    private const int SomeDepth = 13;

    [Fact]
    public void An_ordinary_rectangle_is_accepted()
    {
        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 46.7, SomeDepth).ShouldBeNull();
    }

    [Fact]
    public void Corners_out_of_order_are_refused_and_the_same_corners_in_order_are_not()
    {
        // East of west and north of south. Reversed, the rectangle describes the rest of the world
        // rather than nothing at all, which is why "distinct corners" is not the check.
        TerrainBuildRequestRules.Refuse(22.7, 46.5, 22.5, 46.7, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);
        TerrainBuildRequestRules.Refuse(22.5, 46.7, 22.7, 46.5, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);

        // A rectangle with no width is a line with no ground under it.
        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.5, 46.7, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);

        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 46.7, SomeDepth).ShouldBeNull();
    }

    [Fact]
    public void Coordinates_that_are_not_numbers_or_not_on_the_Earth_are_refused()
    {
        TerrainBuildRequestRules.Refuse(double.NaN, 46.5, 22.7, 46.7, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);
        TerrainBuildRequestRules.Refuse(22.5, 46.5, double.PositiveInfinity, 46.7, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);
        TerrainBuildRequestRules.Refuse(-181d, 46.5, 22.7, 46.7, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);
        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 91d, SomeDepth)!
            .Code.ShouldBe(TerrainBuildRequestRules.ExtentInvalidCode);
    }

    [Fact]
    public void An_area_past_the_ceiling_is_refused_and_told_what_the_ceiling_is()
    {
        var refusal = TerrainBuildRequestRules.Refuse(0d, 0d, 20d, 20d, SomeDepth);

        refusal.ShouldNotBeNull();
        refusal.Code.ShouldBe(TerrainBuildRequestRules.ExtentTooLargeCode);

        // The number is in the sentence: "too large" without saying how large is a refusal somebody
        // can only respond to by guessing.
        refusal.Detail.ShouldContain(
            TerrainBuildRequestRules.MaxAreaSquareDegrees.ToString("0.##"));

        // The largest area that is still accepted, so the ceiling is a ceiling and not a floor.
        TerrainBuildRequestRules.Refuse(0d, 0d, 5d, 5d, SomeDepth).ShouldBeNull();
    }

    [Fact]
    public void A_depth_outside_what_can_be_built_is_refused_at_both_ends()
    {
        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 46.7, TerrainBuildRequestRules.MinDepth - 1)!
            .Code.ShouldBe(TerrainBuildRequestRules.DepthInvalidCode);
        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 46.7, TerrainBuildRequestRules.MaxDepth + 1)!
            .Code.ShouldBe(TerrainBuildRequestRules.DepthInvalidCode);

        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 46.7, TerrainBuildRequestRules.MinDepth)
            .ShouldBeNull();
        TerrainBuildRequestRules.Refuse(22.5, 46.5, 22.7, 46.7, TerrainBuildRequestRules.MaxDepth)
            .ShouldBeNull();
    }

    [Fact]
    public void The_rectangle_is_closed_on_its_south_west_corner_in_the_coordinate_system_builds_use()
    {
        var polygon = TerrainBuildRequestRules.Rectangle(22.5, 46.5, 22.7, 46.7);

        polygon.SRID.ShouldBe(4326);
        polygon.IsValid.ShouldBeTrue();
        polygon.ExteriorRing.IsClosed.ShouldBeTrue();
        polygon.EnvelopeInternal.MinX.ShouldBe(22.5);
        polygon.EnvelopeInternal.MaxX.ShouldBe(22.7);
        polygon.EnvelopeInternal.MinY.ShouldBe(46.5);
        polygon.EnvelopeInternal.MaxY.ShouldBe(46.7);
    }

    /// <summary>
    /// The steps run in the order each consumes what the one before produced, and the step that is
    /// only a waiting state is not one of them.
    /// </summary>
    [Fact]
    public void The_steps_are_walked_in_the_order_the_work_happens()
    {
        TerrainPhases.Order.ShouldBe(
        [
            TerrainBuildPhase.Fetch,
            TerrainBuildPhase.Prepare,
            TerrainBuildPhase.Bake,
            TerrainBuildPhase.Validate,
            TerrainBuildPhase.Publish,
        ]);

        TerrainPhases.Order.ShouldNotContain(TerrainBuildPhase.Pending);
    }

    /// <summary>
    /// One number describes a chain of unlike work, so every step's own nought-to-a-hundred lands
    /// inside the slice of the whole it occupies — and never outside the range the database will
    /// accept, whatever arithmetic a tool hands over.
    /// </summary>
    [Fact]
    public void A_steps_own_progress_lands_inside_its_slice_of_the_whole_and_never_outside_nought_to_a_hundred()
    {
        // Each step starts where the one before it finished: no gaps, and the bar never goes back.
        var boundaries = TerrainPhases.Order
            .Select(p => (Start: TerrainPhases.Overall(p, 0), End: TerrainPhases.Overall(p, 100)))
            .ToList();

        boundaries[0].Start.ShouldBe(0);
        boundaries[^1].End.ShouldBe(100);
        for (var i = 1; i < boundaries.Count; i++)
        {
            boundaries[i].Start.ShouldBe(boundaries[i - 1].End);
            boundaries[i].End.ShouldBeGreaterThan(boundaries[i].Start);
        }

        // A percentage derived from a count against an estimate is out of range often enough that
        // this is an ordinary input rather than a defensive one — and the column it is written to
        // is bounded by a database constraint, so an unclamped number is a refused write thrown
        // from inside the reporting that was meant to be harmless.
        TerrainPhases.Overall(TerrainBuildPhase.Bake, 103).ShouldBe(
            TerrainPhases.Overall(TerrainBuildPhase.Bake, 100));
        TerrainPhases.Overall(TerrainBuildPhase.Bake, -7).ShouldBe(
            TerrainPhases.Overall(TerrainBuildPhase.Bake, 0));
        TerrainPhases.Overall(TerrainBuildPhase.Pending, 100).ShouldBe(0);
    }
}
