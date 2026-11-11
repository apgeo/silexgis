// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Placing a scan by pairs of a picked point and a station: a known placement is recovered from
/// pairs it generated, noise shows up in the residuals and not in the placement, a wrong pair is
/// flagged and the others are not, and pairs that cannot decide a placement are refused with a
/// reason.
/// </summary>
public class ScanControlPointsTests
{
    // An invented spot in the Apuseni, well away from any cave this application knows.
    private const double Lon = 22.7351;
    private const double Lat = 46.5432;
    private const double Height = 812.4;

    private static readonly (double X, double Y, double Z)[] Passage =
    [
        (0, 0, 0),
        (12.5, 3.1, -1.2),
        (25.0, -4.0, -3.5),
        (31.2, 14.8, -6.0),
        (8.4, 22.0, -2.2),
    ];

    private static ScanControlPair[] PairsFor(ScanPlacement truth, IEnumerable<(double X, double Y, double Z)> points) =>
        points.Select(p =>
        {
            var (lon, lat, h) = truth.PositionOf(p.X, p.Y, p.Z);
            return new ScanControlPair(p.X, p.Y, p.Z, lon, lat, h);
        }).ToArray();

    private static void ShouldBeAt(ScanPlacement fitted, ScanPlacement truth, double metres)
    {
        LocalTangentFrame.ToEcef(fitted.Longitude, fitted.Latitude, fitted.HeightM)
            .DistanceTo(LocalTangentFrame.ToEcef(truth.Longitude, truth.Latitude, truth.HeightM))
            .ShouldBeLessThan(metres);
    }

    private static double BearingGap(double a, double b)
    {
        var gap = Math.Abs(a - b) % 360;
        return gap > 180 ? 360 - gap : gap;
    }

    [Fact]
    public void The_convention_a_heading_of_ninety_turns_the_tiles_north_axis_to_the_east()
    {
        var placement = ScanPlacement.Manual(Lon, Lat, Height, 90);

        var frame = new LocalTangentFrame(Lon, Lat, Height);
        var (east, north, up) = frame.ToLocal(placement.EcefOf(0, 10, 0));

        east.ShouldBe(10, tolerance: 1e-6);
        north.ShouldBe(0, tolerance: 1e-6);
        up.ShouldBe(0, tolerance: 1e-6);
    }

    [Fact]
    public void The_convention_a_positive_pitch_raises_the_x_end_and_a_positive_roll_raises_the_y_end()
    {
        var frame = new LocalTangentFrame(Lon, Lat, Height);

        var pitched = new ScanPlacement(Lon, Lat, Height, 0, ScanPlacementSource.ControlPoints, PitchDeg: 2);
        frame.ToLocal(pitched.EcefOf(10, 0, 0)).Up.ShouldBe(10 * Math.Sin(2 * Math.PI / 180), tolerance: 1e-6);

        var rolled = new ScanPlacement(Lon, Lat, Height, 0, ScanPlacementSource.ControlPoints, RollDeg: 2);
        frame.ToLocal(rolled.EcefOf(0, 10, 0)).Up.ShouldBe(10 * Math.Sin(2 * Math.PI / 180), tolerance: 1e-6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37.5)]
    [InlineData(179.9)]
    [InlineData(271)]
    public void Two_exact_pairs_recover_position_and_heading(double heading)
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, heading);

        var outcome = ScanControlPoints.Fit(PairsFor(truth, Passage.Take(2)));

        outcome.Problems.ShouldBeEmpty();
        var fit = outcome.Fit.ShouldNotBeNull();
        ShouldBeAt(fit.Placement, truth, 1e-4);
        BearingGap(fit.Placement.HeadingDeg, heading).ShouldBeLessThan(1e-6);
        fit.Placement.Source.ShouldBe(ScanPlacementSource.ControlPoints);
        fit.Placement.PitchDeg.ShouldBe(0);
        fit.Placement.RollDeg.ShouldBe(0);
        fit.RmsM.ShouldBeLessThan(1e-4);
        fit.Residuals.Count.ShouldBe(2);
        fit.FlaggedCount.ShouldBe(0);
        fit.TiltFitted.ShouldBeFalse();
    }

    [Fact]
    public void A_zero_point_far_from_the_stations_is_found_in_its_own_frame()
    {
        // The tiles' zero point two kilometres from the passage: the frame the heading is stored
        // in is the one at the zero point, not the one at the stations, and the two differ by a
        // hundredth of a degree — which a fit that stopped in the stations' frame would leave in.
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 123.4);
        var far = Passage.Select(p => (p.X + 1400, p.Y - 1500, p.Z - 40));

        var fit = ScanControlPoints.Fit(PairsFor(truth, far)).Fit.ShouldNotBeNull();

        ShouldBeAt(fit.Placement, truth, 1e-3);
        BearingGap(fit.Placement.HeadingDeg, 123.4).ShouldBeLessThan(1e-7);
        fit.RmsM.ShouldBeLessThan(1e-3);
    }

    [Fact]
    public void Noise_shows_in_the_residuals_and_barely_moves_the_placement()
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 211.0);
        var random = new Random(20261109);
        double Noise() => 0.03 * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var noisy = Passage.Select(p => (p.X + Noise(), p.Y + Noise(), p.Z + Noise()));

        var fit = ScanControlPoints.Fit(PairsFor(truth, Passage)
            .Zip(noisy, (pair, n) => pair with { ScanX = n.Item1, ScanY = n.Item2, ScanZ = n.Item3 })
            .ToArray()).Fit.ShouldNotBeNull();

        ShouldBeAt(fit.Placement, truth, 0.1);
        BearingGap(fit.Placement.HeadingDeg, 211.0).ShouldBeLessThan(0.5);
        fit.RmsM.ShouldBeGreaterThan(0.005);
        fit.RmsM.ShouldBeLessThan(0.15);
        fit.FlaggedCount.ShouldBe(0);
        Math.Sqrt(fit.Residuals.Average(r => r.DistanceM * r.DistanceM)).ShouldBe(fit.RmsM, tolerance: 1e-12);
    }

    [Fact]
    public void A_pair_put_on_the_wrong_station_is_the_one_flagged()
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 64.0);
        var pairs = PairsFor(truth, Passage);
        // A metre and a bit off: enough to stand out. A least-squares fit spreads a wrong pair's
        // error over the others, so with five pairs a mistake of several metres drags every pair
        // past the warning distance — the flag says "this placement is poor", and the largest
        // residual says where to look.
        var (lon, lat, h) = truth.PositionOf(Passage[3].X + 1.2, Passage[3].Y, Passage[3].Z);
        pairs[3] = pairs[3] with { StationLongitude = lon, StationLatitude = lat, StationHeightM = h };

        var fit = ScanControlPoints.Fit(pairs).Fit.ShouldNotBeNull();

        fit.Residuals[3].Flagged.ShouldBeTrue();
        fit.Residuals[3].HorizontalM.ShouldBeGreaterThan(fit.Residuals.Where((_, i) => i != 3).Max(r => r.HorizontalM));
        fit.FlaggedCount.ShouldBe(1);
        fit.WarnM.ShouldBe(ScanControlPoints.DefaultWarnM);
    }

    [Fact]
    public void The_warning_distance_is_the_caller_s()
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 10);
        var pairs = PairsFor(truth, Passage);
        pairs[1] = pairs[1] with { StationHeightM = pairs[1].StationHeightM + 0.3 };

        ScanControlPoints.Fit(pairs, new ScanControlOptions(WarnM: 0.5)).Fit!.FlaggedCount.ShouldBe(0);
        ScanControlPoints.Fit(pairs, new ScanControlOptions(WarnM: 0.1)).Fit!.Residuals[1].Flagged.ShouldBeTrue();
    }

    [Fact]
    public void Without_tilt_a_tilted_scan_is_fitted_level_and_the_tilt_shows_as_height_residuals()
    {
        var truth = new ScanPlacement(Lon, Lat, Height, 45, ScanPlacementSource.Manual, PitchDeg: 1.5);

        var fit = ScanControlPoints.Fit(PairsFor(truth, Passage)).Fit.ShouldNotBeNull();

        fit.Placement.PitchDeg.ShouldBe(0);
        fit.Placement.RollDeg.ShouldBe(0);
        fit.Residuals.Max(r => Math.Abs(r.UpM)).ShouldBeGreaterThan(0.2);
    }

    [Theory]
    [InlineData(45, 1.5, -0.8)]
    [InlineData(300, -3, 2.5)]
    public void With_tilt_a_tilted_scan_is_recovered(double heading, double pitch, double roll)
    {
        var truth = new ScanPlacement(Lon, Lat, Height, heading, ScanPlacementSource.Manual, pitch, roll);

        var fit = ScanControlPoints.Fit(PairsFor(truth, Passage), new ScanControlOptions(FitTilt: true)).Fit.ShouldNotBeNull();

        ShouldBeAt(fit.Placement, truth, 1e-4);
        BearingGap(fit.Placement.HeadingDeg, heading).ShouldBeLessThan(1e-6);
        fit.Placement.PitchDeg.ShouldBe(pitch, tolerance: 1e-6);
        fit.Placement.RollDeg.ShouldBe(roll, tolerance: 1e-6);
        fit.RmsM.ShouldBeLessThan(1e-4);
        fit.TiltFitted.ShouldBeTrue();
    }

    [Fact]
    public void A_tilt_beyond_a_small_correction_is_refused()
    {
        var truth = new ScanPlacement(Lon, Lat, Height, 0, ScanPlacementSource.Manual, PitchDeg: 25);

        var outcome = ScanControlPoints.Fit(PairsFor(truth, Passage), new ScanControlOptions(FitTilt: true));

        outcome.Fit.ShouldBeNull();
        outcome.Problems.Single().Code.ShouldBe(ScanControlPoints.TiltTooLarge);
    }

    [Fact]
    public void A_tilt_needs_three_pairs_off_one_line()
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 0);
        var tilt = new ScanControlOptions(FitTilt: true);

        ScanControlPoints.Fit(PairsFor(truth, Passage.Take(2)), tilt).Problems.Single().Code
            .ShouldBe(ScanControlPoints.TiltNeedsThree);
        ScanControlPoints.Fit(PairsFor(truth, [(0, 0, 0), (5, 5, -1), (10, 10, -2)]), tilt).Problems.Single().Code
            .ShouldBe(ScanControlPoints.TiltPointsInLine);

        // The same three in a line fit without tilt, and a third point off the line fits with it.
        ScanControlPoints.Fit(PairsFor(truth, [(0, 0, 0), (5, 5, -1), (10, 10, -2)])).Fit.ShouldNotBeNull();
        ScanControlPoints.Fit(PairsFor(truth, [(0, 0, 0), (5, 5, -1), (10, 4, -2)]), tilt).Fit.ShouldNotBeNull();
    }

    [Fact]
    public void Points_too_close_to_decide_a_heading_are_refused()
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 0);

        var outcome = ScanControlPoints.Fit(PairsFor(truth, [(0, 0, 0), (0.4, 0.3, -5)]));

        outcome.Fit.ShouldBeNull();
        outcome.Problems.Select(p => p.Code).ShouldBe(
            [ScanControlPoints.ScanPointsTooClose, ScanControlPoints.StationsTooClose]);
    }

    [Fact]
    public void Too_few_too_many_and_unreadable_pairs_are_refused()
    {
        var truth = ScanPlacement.Manual(Lon, Lat, Height, 0);
        var pairs = PairsFor(truth, Passage);

        ScanControlPoints.Fit(pairs.Take(1).ToArray()).Problems.Single().Code.ShouldBe(ScanControlPoints.TooFewPairs);
        ScanControlPoints.Fit(Enumerable.Repeat(pairs[0], ScanControlPoints.MaximumPairs + 1).ToArray())
            .Problems.Single().Code.ShouldBe(ScanControlPoints.TooManyPairs);

        var nan = pairs.ToArray();
        nan[2] = nan[2] with { ScanZ = double.NaN };
        ScanControlPoints.Fit(nan).Problems.Single().Code.ShouldBe(ScanControlPoints.NotANumber);

        var offEarth = pairs.ToArray();
        offEarth[1] = offEarth[1] with { StationLatitude = 91 };
        ScanControlPoints.Fit(offEarth).Problems.Single().Code.ShouldBe(ScanControlPoints.StationOffEarth);

        var tooFar = pairs.ToArray();
        tooFar[0] = tooFar[0] with { ScanX = ScanControlPoints.MaximumOffsetM + 1 };
        ScanControlPoints.Fit(tooFar).Problems.Single().Code.ShouldBe(ScanControlPoints.OffsetTooFar);
    }

    [Fact]
    public void Pairs_that_put_the_zero_point_underground_beyond_any_cave_are_refused()
    {
        // Stations at 450 m and picked points a kilometre above the zero point: the zero point
        // lands at -550 m, below the lowest place a scan may be declared at.
        var pairs = new[]
        {
            new ScanControlPair(0, 0, 1000, Lon, Lat, 450),
            new ScanControlPair(10, 0, 1000, Lon + 0.0002, Lat, 450),
        };

        ScanControlPoints.Fit(pairs).Problems.Single().Code.ShouldBe(ScanControlPoints.PlacementOffEarth);
    }
}
