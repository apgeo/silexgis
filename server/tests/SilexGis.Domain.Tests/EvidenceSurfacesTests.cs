// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A run's grid, and the surfaces made on it from positions: distance to the nearest, density
/// around, and which outline a cell falls in.
/// </summary>
public class EvidenceSurfacesTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    /// <summary>A grid on the equator, where a degree is the same length both ways: 100 m cells, 40 by 30.</summary>
    private static EvidenceGrid EquatorGrid()
    {
        var cell = 100d / EvidenceGrid.MetersPerDegree;
        return EvidenceGrid.Over(10, -(30 * cell), 10 + (40 * cell), 0, 100)!;
    }

    [Fact]
    public void A_grid_covers_its_box_in_whole_cells_from_the_north_west_corner()
    {
        var grid = EquatorGrid();

        grid.Columns.ShouldBe(40);
        grid.Rows.ShouldBe(30);
        grid.CellCount.ShouldBe(1200);
        grid.West.ShouldBe(10);
        grid.North.ShouldBe(0);
        grid.LongitudeOf(0).ShouldBe(10 + (grid.CellWidthDegrees / 2), 1e-12);
        grid.LatitudeOf(0).ShouldBe(-grid.CellHeightDegrees / 2, 1e-12);
        grid.IndexOf(3, 2).ShouldBe(83);
    }

    [Fact]
    public void A_box_that_is_not_a_whole_number_of_cells_gains_the_cell_that_covers_the_rest()
    {
        var cell = 100d / EvidenceGrid.MetersPerDegree;
        var grid = EvidenceGrid.Over(10, -(2.5 * cell), 10 + (3.2 * cell), 0, 100)!;

        grid.Columns.ShouldBe(4);
        grid.Rows.ShouldBe(3);
        grid.East.ShouldBeGreaterThan(10 + (3.2 * cell));
        grid.South.ShouldBeLessThan(-(2.5 * cell));
    }

    [Fact]
    public void Cells_stay_square_on_the_ground_away_from_the_equator()
    {
        // At sixty degrees north a degree of longitude is half as long, so a cell is twice as
        // many degrees wide as it is high.
        var grid = EvidenceGrid.Over(20, 59.99, 20.02, 60.01, 50)!;

        (grid.CellWidthDegrees / grid.CellHeightDegrees).ShouldBe(2, 1e-3);
        (grid.CellWidthDegrees * grid.MetersPerDegreeLongitude).ShouldBe(50, 1e-2);
    }

    [Theory]
    [InlineData(4.9, EvidenceGridRules.CellSizeCode)]
    [InlineData(1000.1, EvidenceGridRules.CellSizeCode)]
    [InlineData(double.NaN, EvidenceGridRules.CellSizeCode)]
    [InlineData(5, EvidenceGridRules.TooManyCellsCode)] // a tenth of a degree at five metres is millions of cells
    [InlineData(100, null)]
    public void A_grid_is_refused_for_its_cell_size_or_its_cell_count(double cellMeters, string? expected)
    {
        EvidenceGridRules.Problem(25, 46, 25.1, 46.1, cellMeters).ShouldBe(expected);
        (EvidenceGrid.Over(25, 46, 25.1, 46.1, cellMeters) is null).ShouldBe(expected is not null);
    }

    [Theory]
    [InlineData(25.1, 46, 25, 46.1)] // east of west
    [InlineData(25, 46.1, 25.1, 46)] // north of south
    [InlineData(25, 46, 25, 46.1)] // no width
    [InlineData(-181, 46, 25, 46.1)]
    [InlineData(25, 46, 25.1, 91)]
    public void A_grid_is_refused_over_a_box_that_is_not_one(double west, double south, double east, double north) =>
        EvidenceGridRules.Problem(west, south, east, north, 100).ShouldBe(EvidenceGridRules.ExtentCode);

    [Fact]
    public void Distance_to_a_point_is_the_ground_distance_from_each_cell_centre()
    {
        var grid = EquatorGrid();
        // The centre of the cell in column 10, row 5.
        var source = Factory.CreatePoint(new Coordinate(grid.LongitudeOf(10), grid.LatitudeOf(5)));

        var distance = EvidenceSurfaces.DistanceMeters(grid, [source]);

        distance[grid.IndexOf(10, 5)].ShouldBe(0f, 0.01f);
        distance[grid.IndexOf(13, 5)].ShouldBe(300f, 0.5f); // three cells east
        distance[grid.IndexOf(10, 9)].ShouldBe(400f, 0.5f); // four cells south
        distance[grid.IndexOf(13, 9)].ShouldBe(500f, 0.5f); // and the diagonal of the two
    }

    [Fact]
    public void Distance_is_to_the_nearest_of_several_sources_and_to_the_nearest_part_of_a_line()
    {
        var grid = EquatorGrid();
        var point = Factory.CreatePoint(new Coordinate(grid.LongitudeOf(2), grid.LatitudeOf(2)));
        // A line along the centres of row 20, from column 10 to column 30.
        var line = Factory.CreateLineString(
        [
            new Coordinate(grid.LongitudeOf(10), grid.LatitudeOf(20)),
            new Coordinate(grid.LongitudeOf(30), grid.LatitudeOf(20)),
        ]);

        var distance = EvidenceSurfaces.DistanceMeters(grid, [point, line]);

        distance[grid.IndexOf(2, 4)].ShouldBe(200f, 0.5f); // the point is nearer
        distance[grid.IndexOf(20, 17)].ShouldBe(300f, 0.5f); // straight down onto the line
        distance[grid.IndexOf(34, 20)].ShouldBe(400f, 0.5f); // off the line's end
    }

    [Fact]
    public void A_cell_inside_an_outline_is_at_nought_and_one_outside_is_measured_to_its_edge()
    {
        var grid = EquatorGrid();
        var cell = grid.CellWidthDegrees;
        // An outline over columns 10–19 and rows 10–19, edge to edge.
        var outline = Factory.ToGeometry(new Envelope(
            grid.West + (10 * cell), grid.West + (20 * cell), grid.North - (20 * cell), grid.North - (10 * cell)));

        var distance = EvidenceSurfaces.DistanceMeters(grid, [outline]);

        distance[grid.IndexOf(15, 15)].ShouldBe(0f); // deep inside, far from any edge
        distance[grid.IndexOf(22, 15)].ShouldBe(250f, 0.5f); // centre is two and a half cells past the east edge
    }

    [Fact]
    public void Distance_stops_counting_at_its_reach_and_says_so_everywhere_when_nothing_is_recorded()
    {
        var grid = EquatorGrid();
        var farEast = Factory.CreatePoint(new Coordinate(grid.West + 1, grid.LatitudeOf(0))); // a hundred kilometres off

        EvidenceSurfaces.DistanceMeters(grid, [farEast])
            .ShouldAllBe(v => v == (float)EvidenceSurfaces.DistanceReachMeters);
        EvidenceSurfaces.DistanceMeters(grid, [])
            .ShouldAllBe(v => v == (float)EvidenceSurfaces.DistanceReachMeters);
    }

    [Fact]
    public void A_source_outside_the_grid_still_counts_for_the_cells_along_that_edge()
    {
        var grid = EquatorGrid();
        // Two cells west of the grid's first column, level with row 3.
        var outside = Factory.CreatePoint(new Coordinate(grid.LongitudeOf(-2), grid.LatitudeOf(3)));

        var distance = EvidenceSurfaces.DistanceMeters(grid, [outside]);

        distance[grid.IndexOf(0, 3)].ShouldBe(200f, 0.5f);
        distance[grid.IndexOf(4, 3)].ShouldBe(600f, 0.5f);
    }

    [Fact]
    public void Density_peaks_on_the_point_at_the_kernels_own_height_and_falls_off_as_a_gaussian()
    {
        var grid = EquatorGrid();
        var point = new Coordinate(grid.LongitudeOf(20), grid.LatitudeOf(15));
        const double Bandwidth = 300;

        var density = EvidenceSurfaces.KernelDensityPerKm2(grid, [point], Bandwidth);

        // One point under a Gaussian of deviation h: 1 / (2 pi h^2) per square metre at its centre.
        var peak = 1_000_000d / (2 * Math.PI * Bandwidth * Bandwidth);
        density[grid.IndexOf(20, 15)].ShouldBe((float)peak, 1e-3f);
        // One deviation away the kernel is e^(-1/2) of its peak.
        density[grid.IndexOf(23, 15)].ShouldBe((float)(peak * Math.Exp(-0.5)), 1e-3f);
        density.Max().ShouldBe(density[grid.IndexOf(20, 15)]);
    }

    [Fact]
    public void Density_adds_up_over_the_grid_to_the_number_of_points()
    {
        var grid = EquatorGrid();
        Coordinate[] points =
        [
            new(grid.LongitudeOf(18), grid.LatitudeOf(14)),
            new(grid.LongitudeOf(20), grid.LatitudeOf(15)),
            new(grid.LongitudeOf(22), grid.LatitudeOf(16)),
        ];

        // Narrow enough that the whole of each kernel lies on the grid.
        var density = EvidenceSurfaces.KernelDensityPerKm2(grid, points, 150);

        // Per square kilometre times a cell's hundredth of one, summed: the points themselves.
        density.Sum(v => v * 0.01d).ShouldBe(3, 0.05);
    }

    [Fact]
    public void Density_is_nought_where_no_point_reaches_and_everywhere_without_points()
    {
        var grid = EquatorGrid();
        var corner = new Coordinate(grid.LongitudeOf(0), grid.LatitudeOf(0));

        EvidenceSurfaces.KernelDensityPerKm2(grid, [corner], 100)[grid.IndexOf(39, 29)].ShouldBe(0f);
        EvidenceSurfaces.KernelDensityPerKm2(grid, [], 100).ShouldAllBe(v => v == 0f);
        Should.Throw<ArgumentOutOfRangeException>(() => EvidenceSurfaces.KernelDensityPerKm2(grid, [corner], 0));
    }

    [Fact]
    public void The_density_kernel_is_two_cells_wide_and_never_narrower_than_half_a_kilometre()
    {
        EvidenceSurfaces.DensityBandwidthMeters(10).ShouldBe(500);
        EvidenceSurfaces.DensityBandwidthMeters(400).ShouldBe(800);
        EvidenceSurfaces.DensityReachMeters(10).ShouldBe(1500);
    }

    [Fact]
    public void A_cell_takes_the_class_of_the_outline_it_falls_in_and_a_later_outline_covers_an_earlier()
    {
        var grid = EquatorGrid();
        var cell = grid.CellWidthDegrees;
        var regional = Factory.ToGeometry(new Envelope(
            grid.West, grid.West + (20 * cell), grid.North - (30 * cell), grid.North));
        var detail = Factory.ToGeometry(new Envelope(
            grid.West + (5 * cell), grid.West + (10 * cell), grid.North - (10 * cell), grid.North - (5 * cell)));

        var classes = EvidenceSurfaces.ClassesInside(grid, [(regional, 0), (detail, 4)]);

        classes[grid.IndexOf(2, 2)].ShouldBe(0f); // the regional unit alone
        classes[grid.IndexOf(7, 7)].ShouldBe(4f); // the detailed unit laid over it
        float.IsNaN(classes[grid.IndexOf(30, 15)]).ShouldBeTrue(); // no unit mapped here
    }

    [Fact]
    public void A_point_or_a_line_classes_no_cell()
    {
        var grid = EquatorGrid();
        var point = Factory.CreatePoint(new Coordinate(grid.LongitudeOf(3), grid.LatitudeOf(3)));

        EvidenceSurfaces.ClassesInside(grid, [(point, 2)]).ShouldAllBe(v => float.IsNaN(v));
    }

    [Fact]
    public void The_mask_of_an_outline_is_the_cells_whose_centre_it_holds()
    {
        var grid = EquatorGrid();
        var cell = grid.CellWidthDegrees;
        // A triangle over the north-west: the corner cell is in, the far corner is out.
        var triangle = Factory.CreatePolygon(
        [
            new Coordinate(grid.West, grid.North),
            new Coordinate(grid.West + (20 * cell), grid.North),
            new Coordinate(grid.West, grid.North - (20 * cell)),
            new Coordinate(grid.West, grid.North),
        ]);

        var inside = EvidenceSurfaces.Inside(grid, triangle);

        inside[grid.IndexOf(0, 0)].ShouldBeTrue();
        inside[grid.IndexOf(5, 5)].ShouldBeTrue();
        inside[grid.IndexOf(15, 15)].ShouldBeFalse();
        inside[grid.IndexOf(39, 29)].ShouldBeFalse();
        // Half of a twenty-cell square, to within the cells the diagonal cuts.
        inside.Count(v => v).ShouldBeInRange(180, 220);
    }

    [Fact]
    public void A_layer_made_from_no_features_used_no_protected_position()
    {
        EvidenceProvenance.None.UsedProtectedPositions.ShouldBeFalse();
        new EvidenceProvenance([Guid.NewGuid()], 0).UsedProtectedPositions.ShouldBeFalse();
        new EvidenceProvenance([Guid.NewGuid()], 1).UsedProtectedPositions.ShouldBeTrue();
    }
}
