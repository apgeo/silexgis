// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public sealed class MapPlacementTests
{
    private static MapPlacement Corners(string transformation = MapTransformations.Homography, string crs = "EPSG:4326") => new(
        MapPlacement.CurrentVersion,
        transformation,
        crs,
        [
            new(0, 0, 24.0, 45.5),
            new(1, 0, 24.1, 45.5),
            new(1, 1, 24.1, 45.4),
            new(0, 1, 24.0, 45.4),
        ],
        null);

    private static string[] Codes(MapPlacement placement, int maxPoints = 200) =>
        [.. MapPlacementRules.Validate(placement, maxPoints).Select(p => p.Code)];

    [Fact]
    public void Four_corners_are_a_usable_placement_for_every_transformation_that_fits_four_points()
    {
        foreach (var transformation in new[]
                 {
                     MapTransformations.Similarity, MapTransformations.Affine,
                     MapTransformations.Homography, MapTransformations.ThinPlateSpline,
                 })
        {
            Codes(Corners(transformation)).ShouldBeEmpty(transformation);
        }
    }

    [Theory]
    [InlineData(MapTransformations.Polynomial2, 6)]
    [InlineData(MapTransformations.Polynomial3, 10)]
    public void A_transformation_needs_its_minimum_number_of_points(string transformation, int minimum)
    {
        MapTransformations.MinimumPoints(transformation).ShouldBe(minimum);
        Codes(Corners(transformation)).ShouldContain(MapPlacementRules.TooFewPoints);
    }

    [Fact]
    public void An_unknown_transformation_version_or_system_is_named()
    {
        Codes(Corners("rubber")).ShouldContain(MapPlacementRules.TransformationUnknown);
        Codes(Corners() with { Version = 9 }).ShouldContain(MapPlacementRules.VersionUnknown);
        Codes(Corners(crs: "WGS84")).ShouldContain(MapPlacementRules.CrsUnknown);
        Codes(Corners(crs: "EPSG:-1")).ShouldContain(MapPlacementRules.CrsUnknown);

        // A server-written definition is accepted only where the server says it may be.
        var wkt = Corners(crs: "WKT:PROJCS[\"x\"]");
        MapPlacementRules.Validate(wkt, 200).Select(p => p.Code).ShouldContain(MapPlacementRules.CrsUnknown);
        MapPlacementRules.Validate(wkt, 200, allowWkt: true).ShouldBeEmpty();
    }

    [Fact]
    public void Points_on_one_line_or_on_one_spot_are_degenerate()
    {
        var line = Corners(MapTransformations.Affine) with
        {
            Gcps = [new(0, 0, 24, 45.5), new(0.5, 0.5, 24.05, 45.45), new(1, 1, 24.1, 45.4)],
        };
        Codes(line).ShouldContain(MapPlacementRules.PointsDegenerate);

        var same = Corners(MapTransformations.Similarity) with { Gcps = [new(0.3, 0.3, 24, 45.5), new(0.3, 0.3, 24.1, 45.4)] };
        Codes(same).ShouldContain(MapPlacementRules.PointsDegenerate);
    }

    [Fact]
    public void A_point_far_off_the_picture_or_not_a_number_is_refused_and_a_little_off_is_not()
    {
        var far = Corners() with { Gcps = [.. Corners().Gcps.Take(3), new(3, 1, 24, 45.4)] };
        Codes(far).ShouldContain(MapPlacementRules.PointOutsidePicture);

        var trimmed = Corners() with { Gcps = [.. Corners().Gcps.Take(3), new(-0.05, 1.05, 24, 45.4)] };
        Codes(trimmed).ShouldBeEmpty();

        var nan = Corners() with { Gcps = [.. Corners().Gcps.Take(3), new(0, 1, double.NaN, 45.4)] };
        Codes(nan).ShouldContain(MapPlacementRules.PointNotANumber);
    }

    [Fact]
    public void The_installations_ceiling_on_points_is_applied()
    {
        Codes(Corners(), maxPoints: 3).ShouldContain(MapPlacementRules.TooManyPoints);
    }

    [Fact]
    public void An_outline_must_be_a_simple_ring_on_the_picture()
    {
        IReadOnlyList<IReadOnlyList<double>> square = [[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]];
        Codes(Corners() with { Mask = square }).ShouldBeEmpty();

        IReadOnlyList<IReadOnlyList<double>> bowtie = [[0.1, 0.1], [0.9, 0.9], [0.9, 0.1], [0.1, 0.9]];
        Codes(Corners() with { Mask = bowtie }).ShouldContain(MapPlacementRules.MaskInvalid);

        IReadOnlyList<IReadOnlyList<double>> two = [[0.1, 0.1], [0.9, 0.9]];
        Codes(Corners() with { Mask = two }).ShouldContain(MapPlacementRules.MaskInvalid);

        IReadOnlyList<IReadOnlyList<double>> off = [[0.1, 0.1], [2, 0.1], [0.9, 0.9]];
        Codes(Corners() with { Mask = off }).ShouldContain(MapPlacementRules.MaskInvalid);
    }

    [Fact]
    public void A_placement_survives_being_stored()
    {
        var placement = Corners() with { Mask = [[0.1, 0.1], [0.9, 0.1], [0.5, 0.9]] };
        var back = MapPlacement.FromJson(placement.ToJson())!;
        back.Transformation.ShouldBe(placement.Transformation);
        back.Crs.ShouldBe("EPSG:4326");
        back.EpsgCode.ShouldBe(4326);
        back.Gcps.ShouldBe(placement.Gcps);
        back.Mask!.Count.ShouldBe(3);
        MapPlacement.FromJson("not json").ShouldBeNull();
        MapPlacement.FromJson(null).ShouldBeNull();
    }

    /// <summary>
    /// Two points fix scale, turn and shift: a picture 1000 × 500 pixels turned 30° and drawn at
    /// two ground units a pixel lands both points exactly and carries every other point with them.
    /// </summary>
    [Fact]
    public void Two_points_fit_a_scaled_turned_sheet_exactly()
    {
        const int width = 1000, height = 500;
        var theta = 30 * Math.PI / 180;
        const double scale = 2, x0 = 1000, y0 = 5000;

        // The truth: ground = origin + scale · R(θ) · (column, −row).
        (double X, double Y) Truth(double u, double v)
        {
            var (c, r) = (u * width, -(v * height));
            return (x0 + (scale * ((Math.Cos(theta) * c) - (Math.Sin(theta) * r))),
                y0 + (scale * ((Math.Sin(theta) * c) + (Math.Cos(theta) * r))));
        }

        var (ax, ay) = Truth(0.1, 0.2);
        var (bx, by) = Truth(0.8, 0.9);
        var gt = MapPlacementMath.SimilarityGeoTransform([new(0.1, 0.2, ax, ay), new(0.8, 0.9, bx, by)], width, height);

        foreach (var (u, v) in new[] { (0.1, 0.2), (0.8, 0.9), (0.0, 0.0), (1.0, 1.0), (0.3, 0.7) })
        {
            var (x, y) = MapPlacementMath.Apply(gt, u, v, width, height);
            var (tx, ty) = Truth(u, v);
            x.ShouldBe(tx, 1e-6);
            y.ShouldBe(ty, 1e-6);
        }

        // Never mirrored: the determinant of the pixel-to-ground part is negative, as it is for
        // every north-up raster (rows count down, northings up).
        ((gt[1] * gt[5]) - (gt[2] * gt[4])).ShouldBeLessThan(0);
    }
}
