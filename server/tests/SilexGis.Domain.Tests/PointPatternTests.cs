// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Both statistics are asserted at both ends. A nearest-neighbour index checked only against a
/// clustered fixture is also satisfied by a function that returns a small constant, and an envelope
/// checked only against a clustered one is satisfied by a band that is always too narrow — so every
/// fact here pairs a pattern that should register with one that should not.
/// </summary>
public sealed class PointPatternTests
{
    private const double WindowMetres = 10_000d;

    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 32635);

    [Fact]
    public void Random_scatter_indexes_near_one_and_a_clustered_one_clearly_below()
    {
        var random = PointPattern.ClarkEvans(RandomScatter(400, 20260903), WindowMetres * WindowMetres);
        var clustered = PointPattern.ClarkEvans(Clustered(400, 8, 120d, 20260903), WindowMetres * WindowMetres);

        random.ShouldNotBeNull();
        clustered.ShouldNotBeNull();

        // Chance, measured against chance. The residual bias is upward — points near the window's
        // edge have neighbours only on one side — and it is a few per cent, not a pattern.
        random.Index.ShouldBeInRange(0.9, 1.15);
        Math.Abs(random.ZScore).ShouldBeLessThan(4d);

        // Eight tight groups over the same ground, and the index has to see it without ambiguity.
        clustered.Index.ShouldBeLessThan(0.5);
        clustered.ZScore.ShouldBeLessThan(-10d);
        clustered.PValue.ShouldBeLessThan(0.001);

        // The counts and the ground are what make either number readable, so both are reported.
        random.Count.ShouldBe(400);
        random.AreaM2.ShouldBe(WindowMetres * WindowMetres);
    }

    [Fact]
    public void A_lattice_indexes_above_one()
    {
        // The other end of the scale from clustering: a regular grid is as spaced out as points
        // get, and an index that only ever falls below one would pass the clustered test alone.
        var lattice = new List<PlanarPoint>();
        for (var i = 0; i < 20; i++)
        {
            for (var j = 0; j < 20; j++)
            {
                lattice.Add(new PlanarPoint((i + 0.5) * WindowMetres / 20, (j + 0.5) * WindowMetres / 20));
            }
        }

        var result = PointPattern.ClarkEvans(lattice, WindowMetres * WindowMetres);

        result.ShouldNotBeNull();
        result.Index.ShouldBeGreaterThan(1.5);
        result.ZScore.ShouldBeGreaterThan(10d);
    }

    [Fact]
    public void Too_few_points_answer_with_nothing_rather_than_a_number()
    {
        PointPattern.ClarkEvans([new PlanarPoint(0, 0)], 1_000d).ShouldBeNull();
        PointPattern.ClarkEvans([new PlanarPoint(0, 0), new PlanarPoint(1, 1)], 0d).ShouldBeNull();
    }

    [Fact]
    public void The_ripley_curve_leaves_the_envelope_when_clustered_and_stays_in_it_when_not()
    {
        var window = Square();

        var random = PointPattern.RipleyL(
            RandomScatter(200, 4711), window, 1_500d, 10, 39, seed: 7);
        var clustered = PointPattern.RipleyL(
            Clustered(200, 6, 150d, 4711), window, 1_500d, 10, 39, seed: 7);

        random.ShouldNotBeNull();
        clustered.ShouldNotBeNull();

        // Random scatter: the curve sits inside the band the random scatters themselves drew, at
        // every radius. This is the half that a too-narrow band would fail.
        foreach (var step in random.Steps)
        {
            // Asserted present before it is compared: a band that came back absent would otherwise
            // satisfy a comparison against nothing, and the test would pass without a band.
            step.LowerL.ShouldNotBeNull();
            step.UpperL.ShouldNotBeNull();
            step.ObservedL.ShouldBeInRange(step.LowerL.Value, step.UpperL.Value);
        }

        // Clustered: more neighbours close by than chance ever produced, so the curve is above the
        // band. This is the half a band of infinite width would fail.
        var clusteredUpper = clustered.Steps[0].UpperL;
        clusteredUpper.ShouldNotBeNull();
        clustered.Steps[0].ObservedL.ShouldBeGreaterThan(clusteredUpper.Value);

        // And the answer says what it was computed over.
        clustered.Count.ShouldBe(200);
        clustered.Simulations.ShouldBe(39);
        clustered.Seed.ShouldBe(7);
        clustered.AreaM2.ShouldBe(WindowMetres * WindowMetres, 1d);
    }

    [Fact]
    public void The_envelope_is_reproducible_from_its_seed_and_moves_when_the_seed_does()
    {
        var window = Square();
        var points = RandomScatter(120, 99);

        var first = PointPattern.RipleyL(points, window, 1_000d, 6, 19, seed: 42);
        var again = PointPattern.RipleyL(points, window, 1_000d, 6, 19, seed: 42);
        var other = PointPattern.RipleyL(points, window, 1_000d, 6, 19, seed: 43);

        first.ShouldNotBeNull();
        again.ShouldNotBeNull();
        other.ShouldNotBeNull();

        // Same seed, same band, to the last bit — otherwise a reader reloading the page is shown a
        // different answer to the same question.
        for (var i = 0; i < first.Steps.Count; i++)
        {
            again.Steps[i].LowerL.ShouldBe(first.Steps[i].LowerL);
            again.Steps[i].UpperL.ShouldBe(first.Steps[i].UpperL);

            // The observed curve is not simulated at all, so it does not move with the seed.
            other.Steps[i].ObservedL.ShouldBe(first.Steps[i].ObservedL);
        }

        // A different seed is a different draw; if it were not, the seed would not be doing
        // anything and the envelope would be a fixed decoration.
        Enumerable.Range(0, first.Steps.Count)
            .Any(i => Math.Abs((other.Steps[i].UpperL ?? 0d) - (first.Steps[i].UpperL ?? 0d)) > 1e-9)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_curve_asked_for_without_simulations_says_it_has_no_band_rather_than_drawing_one()
    {
        var window = Square();

        var result = PointPattern.RipleyL(
            RandomScatter(60, 13), window, 1_000d, 6, simulations: 0, seed: 3);

        result.ShouldNotBeNull();

        // Nought simulations is a legitimate request — the shape of the curve without the cost of
        // testing it. What it must not do is come back with the band collapsed onto the observed
        // curve, which reads to anything downstream as "exactly what chance would give" and is the
        // opposite of "chance was never tested".
        foreach (var step in result.Steps)
        {
            step.LowerL.ShouldBeNull();
            step.UpperL.ShouldBeNull();
            step.ObservedL.ShouldBeGreaterThan(0d);
        }

        // And the count reported is the number that ran, not the number that was asked for.
        result.Simulations.ShouldBe(0);
    }

    [Fact]
    public void Entrances_strung_along_a_line_give_an_aligned_rose_and_a_scatter_gives_a_round_one()
    {
        // Twelve entrances on a bearing of 045, a kilometre apart along it. Every joining line runs
        // that way, so the rose has to be concentrated there and nowhere else.
        var step = 1_000d / Math.Sqrt(2d);
        var strung = Enumerable.Range(0, 12)
            .Select(i => new PlanarPoint(i * step, i * step))
            .ToList();

        var alignedRose = OrientationStatistics.Summarize(
            PointPattern.PairAzimuths(strung, maxSeparationM: 20_000d, minSeparationM: 100d));

        alignedRose.SampleCount.ShouldBe(12 * 11 / 2);
        alignedRose.ByLength.MeanAxisDegrees.ShouldNotBeNull();
        alignedRose.ByLength.MeanAxisDegrees.Value.ShouldBe(45d, 1d);

        // Concentrated about that axis, not merely averaging to it — a rose spread evenly over
        // every sector averages to something too, and would pass the mean alone.
        alignedRose.ByLength.ResultantLength.ShouldBeGreaterThan(0.99d);

        // The other end. A scatter has no preferred joining direction, so the same arithmetic must
        // come back round; asserted at both ends because a function returning a constant
        // concentration passes either half on its own.
        var scatterRose = OrientationStatistics.Summarize(
            PointPattern.PairAzimuths(RandomScatter(120, 20_260_903), 20_000d, 100d));

        scatterRose.SampleCount.ShouldBeGreaterThan(1_000);
        scatterRose.ByLength.ResultantLength.ShouldBeLessThan(0.2d);

        // And the separation range really excludes: nothing survives a ceiling below every gap.
        PointPattern.PairAzimuths(strung, maxSeparationM: 10d, minSeparationM: 0d).ShouldBeEmpty();
        PointPattern.PairAzimuths(strung, maxSeparationM: 20_000d, minSeparationM: 19_000d)
            .Count.ShouldBeLessThan(alignedRose.SampleCount);
    }

    [Fact]
    public void A_joining_bearing_is_measured_clockwise_from_north_and_not_from_east()
    {
        // Due east of each other. The grid bearing is 090, and the mathematical convention that
        // measures anticlockwise from east would call the same pair 000 — a rose mirrored about
        // the north-east diagonal, which looks entirely plausible and points the wrong way.
        var eastWest = new List<PlanarPoint> { new(0d, 0d), new(1_000d, 0d) };
        var pair = PointPattern.PairAzimuths(eastWest, 20_000d, 10d);

        pair.Count.ShouldBe(1);
        pair[0].AzimuthDegrees.ShouldBe(90d, 1e-9);
        pair[0].WeightM.ShouldBe(1_000d, 1e-6);

        var northSouth = new List<PlanarPoint> { new(0d, 0d), new(0d, 1_000d) };
        PointPattern.PairAzimuths(northSouth, 20_000d, 10d)[0].AzimuthDegrees.ShouldBe(0d, 1e-9);
    }

    private static Polygon Square() => Factory.CreatePolygon(
    [
        new Coordinate(0, 0),
        new Coordinate(WindowMetres, 0),
        new Coordinate(WindowMetres, WindowMetres),
        new Coordinate(0, WindowMetres),
        new Coordinate(0, 0),
    ]);

    private static List<PlanarPoint> RandomScatter(int count, int seed)
    {
        var random = new Random(seed);
        var points = new List<PlanarPoint>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(new PlanarPoint(random.NextDouble() * WindowMetres, random.NextDouble() * WindowMetres));
        }

        return points;
    }

    /// <summary>The same number of points, gathered into a few tight groups over the same ground.</summary>
    private static List<PlanarPoint> Clustered(int count, int clusters, double spreadM, int seed)
    {
        var random = new Random(seed);
        var centres = new List<PlanarPoint>();
        for (var c = 0; c < clusters; c++)
        {
            // Kept off the window's edge so the fixture measures clustering rather than truncation.
            centres.Add(new PlanarPoint(
                (0.15 + (0.7 * random.NextDouble())) * WindowMetres,
                (0.15 + (0.7 * random.NextDouble())) * WindowMetres));
        }

        var points = new List<PlanarPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var centre = centres[i % clusters];
            points.Add(new PlanarPoint(
                centre.X + ((random.NextDouble() - 0.5) * spreadM),
                centre.Y + ((random.NextDouble() - 0.5) * spreadM)));
        }

        return points;
    }
}
