// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The per-cave indices and the declared-versus-computed comparison, against fixtures whose
/// expected values are worked out here rather than read off a run.
///
/// <para>
/// The fixtures are laid out on the equator, where a degree of longitude and a degree of latitude
/// are the same distance on a sphere, so a distance in metres converts to an offset in degrees by
/// dividing by one constant — the equatorial degree, which is the earth radius times pi over a
/// hundred and eighty. That makes every straight-line distance in these tests a number that was
/// arrived at with a right-angled triangle rather than by asking the code what it thought.
/// </para>
/// <para>
/// Note that a piece of passage carries its own length as a field: the class is arithmetic over
/// measured pieces and must take the stated length rather than re-deriving one from the
/// coordinates. Several fixtures below state a length that does not match the distance between
/// their endpoints on purpose, which is how a sinuosity of exactly three can be arranged.
/// </para>
/// </summary>
public class CaveSurveyIndicesTests
{
    /// <summary>Metres in one degree along a great circle — the equatorial degree.</summary>
    private const double MetreDegree = Geodesy.EarthRadiusMeters * Math.PI / 180d;

    private static double Deg(double metres) => metres / MetreDegree;

    /// <summary>
    /// A piece of passage from one east/north offset in metres to another, with its length and
    /// altitudes stated independently of those offsets.
    /// </summary>
    private static PassageSegment Piece(
        (double East, double North) from,
        (double East, double North) to,
        double planLengthM,
        int? pathIndex = 0,
        int segmentIndex = 0,
        double? slopeLengthM = null,
        double? deltaZM = null,
        double? midZM = null,
        bool isDuplicate = false) =>
        new(
            pathIndex,
            segmentIndex,
            isDuplicate,
            Deg(from.East),
            Deg(from.North),
            Deg(to.East),
            Deg(to.North),
            planLengthM,
            slopeLengthM,
            deltaZM,
            midZM);

    [Fact]
    public void A_path_that_goes_straight_there_is_not_sinuous_and_one_that_wanders_is()
    {
        // One hundred metres of passage covering one hundred metres of ground.
        var straight = CaveSurveyIndices.Compute(
            [Piece((0, 0), (0, 100), planLengthM: 100)]);

        straight.Sinuosity.ShouldNotBeNull();
        straight.Sinuosity!.Value.ShouldBe(1d, 1e-9);

        // Three hundred metres of passage between two points one hundred metres apart. What the
        // pieces do in between does not enter the ratio, only the two ends and the total.
        var wandering = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (80, 40), planLengthM: 100, segmentIndex: 0),
            Piece((80, 40), (-80, 60), planLengthM: 100, segmentIndex: 1),
            Piece((-80, 60), (0, 100), planLengthM: 100, segmentIndex: 2),
        ]);

        wandering.PathCount.ShouldBe(1);
        wandering.Sinuosity.ShouldNotBeNull();
        wandering.Sinuosity!.Value.ShouldBe(3d, 1e-6);
    }

    [Fact]
    public void A_path_that_returns_to_where_it_started_has_no_sinuosity_rather_than_an_infinite_one()
    {
        var loop = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (100, 0), planLengthM: 100, segmentIndex: 0),
            Piece((100, 0), (0, 0), planLengthM: 100, segmentIndex: 1),
        ]);

        var paths = CaveSurveyIndices.PathSinuosities(
        [
            Piece((0, 0), (100, 0), planLengthM: 100, segmentIndex: 0),
            Piece((100, 0), (0, 0), planLengthM: 100, segmentIndex: 1),
        ]);

        paths.ShouldHaveSingleItem();
        paths[0].StraightLineM.ShouldBe(0d, 1e-6);
        paths[0].Sinuosity.ShouldBeNull();
        loop.Sinuosity.ShouldBeNull();

        // The loop is still two hundred metres of passage; only the ratio is refused.
        loop.TotalLengthM.ShouldBe(200d, 1e-9);
    }

    [Fact]
    public void Passage_surveyed_twice_is_measured_once_but_is_still_as_deep_as_it_is()
    {
        var cave = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 100), planLengthM: 100, segmentIndex: 0, slopeLengthM: 100, deltaZM: 0, midZM: 0),

            // The same passage walked on another trip, fifty metres lower. Its length must not be
            // added; its altitude is real ground and widens the range.
            Piece(
                (0, 100), (0, 200), planLengthM: 100, segmentIndex: 1,
                slopeLengthM: 100, deltaZM: 0, midZM: -50, isDuplicate: true),
        ]);

        cave.SegmentCount.ShouldBe(2);
        cave.TotalLengthM.ShouldBe(100d, 1e-9);
        cave.PlanLengthM.ShouldBe(100d, 1e-9);
        cave.HighestZM!.Value.ShouldBe(0d, 1e-9);
        cave.LowestZM!.Value.ShouldBe(-50d, 1e-9);
        cave.VerticalExtentM!.Value.ShouldBe(50d, 1e-9);
    }

    [Fact]
    public void Line_work_with_no_altitudes_refuses_every_vertical_figure_rather_than_calling_the_cave_flat()
    {
        var plan = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 100), planLengthM: 100, segmentIndex: 0),
            Piece((0, 100), (0, 200), planLengthM: 100, segmentIndex: 1),
        ]);

        plan.HasAltitudes.ShouldBeFalse();
        plan.HighestZM.ShouldBeNull();
        plan.LowestZM.ShouldBeNull();
        plan.VerticalExtentM.ShouldBeNull();
        plan.Verticality.ShouldBeNull();
        plan.LengthToDepthRatio.ShouldBeNull();

        // Everything that does not need a third coordinate is still answered.
        plan.TotalLengthM.ShouldBe(200d, 1e-9);
        plan.Horizontality!.Value.ShouldBe(1d, 1e-9);

        // And the same fixture with altitudes does report them, so this test cannot pass by
        // refusing everything.
        var withZ = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 100), planLengthM: 100, segmentIndex: 0, slopeLengthM: 100, deltaZM: 0, midZM: 10),
            Piece((0, 100), (0, 200), planLengthM: 100, segmentIndex: 1, slopeLengthM: 100, deltaZM: 0, midZM: 4),
        ]);

        withZ.HasAltitudes.ShouldBeTrue();
        withZ.VerticalExtentM!.Value.ShouldBe(6d, 1e-9);
    }

    [Fact]
    public void A_steep_pitch_and_a_level_gallery_are_told_apart_by_the_ratios()
    {
        // One piece thirty metres across the map and forty metres down: a three-four-five triangle,
        // so fifty metres of passage. Verticality 40/50 = 0.8, horizontality 30/50 = 0.6, and since
        // the whole cave is that one straight piece its furthest two points are its own ends, so
        // linearity is 30/50 = 0.6 as well. Length over depth is 50/40 = 1.25.
        var pitch = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 30), planLengthM: 30, slopeLengthM: 50, deltaZM: -40, midZM: -20),
        ]);

        pitch.TotalLengthM.ShouldBe(50d, 1e-9);
        pitch.PlanLengthM.ShouldBe(30d, 1e-9);
        pitch.VerticalExtentM!.Value.ShouldBe(40d, 1e-9);
        pitch.Verticality!.Value.ShouldBe(0.8d, 1e-9);
        pitch.Horizontality!.Value.ShouldBe(0.6d, 1e-9);
        pitch.MaximumExtentM!.Value.ShouldBe(30d, 1e-3);
        pitch.Linearity!.Value.ShouldBe(0.6d, 1e-6);
        pitch.LengthToDepthRatio!.Value.ShouldBe(1.25d, 1e-9);

        // A level gallery of the same length: no descent at all, so it is entirely horizontal and
        // has no length-to-depth ratio to report rather than an enormous one.
        var gallery = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 50), planLengthM: 50, slopeLengthM: 50, deltaZM: 0, midZM: 0),
        ]);

        gallery.Verticality!.Value.ShouldBe(0d, 1e-9);
        gallery.Horizontality!.Value.ShouldBe(1d, 1e-9);
        gallery.LengthToDepthRatio.ShouldBeNull();
    }

    [Fact]
    public void The_two_furthest_points_are_found_past_every_point_between_them()
    {
        // A right angle three hundred metres east and four hundred metres north of one corner: the
        // furthest pair is the hypotenuse, five hundred metres, and it is a pair neither of whose
        // members is the corner the passage starts from. The point in the middle of the triangle
        // exists to prove the answer is not simply the last pair looked at.
        var cave = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (300, 0), planLengthM: 300, segmentIndex: 0),
            Piece((300, 0), (100, 120), planLengthM: 240, segmentIndex: 1),
            Piece((100, 120), (0, 400), planLengthM: 300, segmentIndex: 2),
        ]);

        cave.MaximumExtentM.ShouldNotBeNull();
        cave.MaximumExtentM!.Value.ShouldBe(500d, 0.5d);

        // Eight hundred and forty metres of passage covering five hundred metres of ground.
        cave.Linearity!.Value.ShouldBe(500d / 840d, 1e-3);
    }

    [Fact]
    public void Legs_with_no_reconstructed_path_measure_the_cave_but_yield_no_sinuosity()
    {
        // Survey legs form a network, not ordered paths; nothing has reconstructed the paths, so
        // the substrate hands over pieces with no path index.
        var network = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 100), planLengthM: 100, pathIndex: null, segmentIndex: 0,
                slopeLengthM: 100, deltaZM: 0, midZM: 0),
            Piece((0, 0), (100, 0), planLengthM: 100, pathIndex: null, segmentIndex: 1,
                slopeLengthM: 100, deltaZM: 0, midZM: 0),
        ]);

        network.PathCount.ShouldBe(0);
        network.Sinuosity.ShouldBeNull();
        CaveSurveyIndices.PathSinuosities(
        [
            Piece((0, 0), (0, 100), planLengthM: 100, pathIndex: null),
        ]).ShouldBeEmpty();

        // Everything that does not need a path is still measured.
        network.TotalLengthM.ShouldBe(200d, 1e-9);
        network.MaximumExtentM!.Value.ShouldBe(Math.Sqrt(2d) * 100d, 0.5d);

        // The same two pieces with paths do report a sinuosity, so this is a refusal about the
        // missing path and not about the fixture.
        var pathed = CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 100), planLengthM: 100, pathIndex: 0, segmentIndex: 0),
            Piece((0, 0), (100, 0), planLengthM: 100, pathIndex: 1, segmentIndex: 0),
        ]);

        pathed.PathCount.ShouldBe(2);
        pathed.Sinuosity!.Value.ShouldBe(1d, 1e-6);
    }

    [Fact]
    public void An_empty_cave_is_answered_rather_than_refused()
    {
        var nothing = CaveSurveyIndices.Compute([]);

        nothing.SegmentCount.ShouldBe(0);
        nothing.PathCount.ShouldBe(0);
        nothing.TotalLengthM.ShouldBe(0d);
        nothing.HasAltitudes.ShouldBeFalse();
        nothing.MaximumExtentM.ShouldBeNull();
        nothing.Linearity.ShouldBeNull();
        nothing.Sinuosity.ShouldBeNull();
    }

    [Fact]
    public void A_declared_length_the_survey_contradicts_is_flagged_and_one_it_confirms_is_not()
    {
        // A kilometre surveyed against twelve hundred metres claimed: two hundred metres and a
        // sixth of the larger figure, past both gates.
        var wrong = CaveSurveyIndices.CompareLength(1000d, 1200m);

        wrong.Agreement.ShouldBe(MorphometryAgreement.Disagrees);
        wrong.Disagrees.ShouldBeTrue();
        wrong.DifferenceM!.Value.ShouldBe(200d, 1e-9);
        wrong.RelativeDifference!.Value.ShouldBe(200d / 1200d, 1e-9);

        // The same survey against a figure that matches it.
        var right = CaveSurveyIndices.CompareLength(1000d, 1010m);

        right.Agreement.ShouldBe(MorphometryAgreement.Agrees);
        right.Disagrees.ShouldBeFalse();
        right.DifferenceM!.Value.ShouldBe(10d, 1e-9);
    }

    [Fact]
    public void The_shortfall_the_documented_approximation_costs_is_not_reported_as_a_disagreement()
    {
        // Where a cave has no parsed survey file its passage is measured over a shape-based
        // reduction of its centerline, which is documented as keeping 92-95% of the surveyed
        // length. A declared figure that is exactly right therefore sits five to eight per cent
        // above the computed one, and the tolerance has to clear that band or the flag would be
        // reporting the method instead of the cave.
        CaveSurveyIndices.CompareLength(930d, 1000m)
            .Agreement.ShouldBe(MorphometryAgreement.Agrees);

        CaveSurveyIndices.CompareLength(920d, 1000m)
            .Agreement.ShouldBe(MorphometryAgreement.Agrees);

        // A fifth off is not the method.
        CaveSurveyIndices.CompareLength(800d, 1000m)
            .Agreement.ShouldBe(MorphometryAgreement.Disagrees);
    }

    [Fact]
    public void A_difference_large_in_proportion_but_small_in_metres_is_left_alone()
    {
        // Twelve metres surveyed, fifteen declared: a fifth of the larger figure, but three metres,
        // which is where a survey was started and stopped rather than a discrepancy.
        var small = CaveSurveyIndices.CompareLength(12d, 15m);

        small.RelativeDifference!.Value.ShouldBeGreaterThan(CaveSurveyIndices.RelativeTolerance);
        small.Agreement.ShouldBe(MorphometryAgreement.Agrees);

        // And the proportional gate on its own is not enough either: a hundred metres missing from
        // a two-kilometre cave is only five per cent.
        CaveSurveyIndices.CompareLength(1900d, 2000m)
            .Agreement.ShouldBe(MorphometryAgreement.Agrees);
    }

    [Fact]
    public void Depth_is_compared_on_a_tighter_floor_than_length_is()
    {
        // Forty metres of vertical range against a declared thirty: ten metres and a quarter.
        CaveSurveyIndices.CompareDepth(40d, 30m)
            .Agreement.ShouldBe(MorphometryAgreement.Disagrees);

        // Four metres against three is a quarter as well, but only one metre — a depth is typed to
        // the metre and this is the rounding, not a difference.
        CaveSurveyIndices.CompareDepth(4d, 3m)
            .Agreement.ShouldBe(MorphometryAgreement.Agrees);

        // The same one-metre gap would clear the length floor no more easily.
        CaveSurveyIndices.CompareLength(4d, 3m)
            .Agreement.ShouldBe(MorphometryAgreement.Agrees);

        // But a five-metre gap on a twenty-metre pit does clear the depth floor while staying
        // inside the length one, which is the whole reason the two constants differ.
        CaveSurveyIndices.CompareDepth(20d, 25m)
            .Agreement.ShouldBe(MorphometryAgreement.Disagrees);
        CaveSurveyIndices.CompareLength(20d, 25m)
            .Agreement.ShouldBe(MorphometryAgreement.Agrees);
    }

    [Fact]
    public void A_figure_nobody_typed_and_one_the_survey_cannot_produce_are_different_answers()
    {
        var undeclared = CaveSurveyIndices.CompareLength(1000d, null);
        undeclared.Agreement.ShouldBe(MorphometryAgreement.NotDeclared);
        undeclared.ComputedM!.Value.ShouldBe(1000d);
        undeclared.DeclaredM.ShouldBeNull();
        undeclared.Disagrees.ShouldBeFalse();

        // A plan-only centerline yields no vertical extent, so the declared depth has nothing to be
        // compared against. That is not agreement.
        var uncomputed = CaveSurveyIndices.CompareDepth(null, 154m);
        uncomputed.Agreement.ShouldBe(MorphometryAgreement.NotComputed);
        uncomputed.DeclaredM!.Value.ShouldBe(154m);
        uncomputed.ComputedM.ShouldBeNull();
        uncomputed.Disagrees.ShouldBeFalse();

        // Neither side present at all.
        CaveSurveyIndices.CompareDepth(null, null)
            .Agreement.ShouldBe(MorphometryAgreement.NotDeclared);
    }

    [Fact]
    public void A_negative_piece_of_passage_is_refused_rather_than_quietly_shortening_the_cave()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CaveSurveyIndices.Compute(
        [
            Piece((0, 0), (0, 100), planLengthM: -100),
        ]));
    }
}
