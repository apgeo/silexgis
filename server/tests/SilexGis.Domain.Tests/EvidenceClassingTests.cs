// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

public class EvidenceClassingTests
{
    private static readonly EvidenceLayer Slope = EvidenceLayers.Find(EvidenceLayers.Slope)!;
    private static readonly EvidenceLayer Aspect = EvidenceLayers.Find(EvidenceLayers.Aspect)!;
    private static readonly EvidenceLayer Lithology = EvidenceLayers.Find(EvidenceLayers.Lithology)!;

    // Bounds only: a model that counts its weights from known caves gives its classes no scores.
    private static readonly EvidenceTransform Table =
        new(EvidenceTransformKind.Classes, Classes: [new(5, null), new(15, null), new(null, null)]);

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(4.999, 0)]
    [InlineData(5, 1)]      // a value on a bound belongs to the class that starts there
    [InlineData(14.9, 1)]
    [InlineData(15, 2)]
    [InlineData(80, 2)]
    public void A_measured_value_falls_in_the_class_a_judged_table_would_score_it_in(double value, int expected)
    {
        EvidenceClassing.ClassOf(Slope, Table, value).ShouldBe(expected);

        // The same rule the judged overlay reclassifies by, shown with scores that name their row.
        IReadOnlyList<EvidenceClass> scored = [new(5, 0), new(15, 0.5), new(null, 1)];
        Favourability.Reclassify(scored, value).ShouldBe(expected / 2d);
        Favourability.ClassIndex(scored, value).ShouldBe(expected);
    }

    [Fact]
    public void A_bearing_of_a_full_turn_is_classed_with_north()
    {
        var quarters = new EvidenceTransform(
            EvidenceTransformKind.Classes, Classes: [new(90, null), new(180, null), new(270, null), new(null, null)]);

        EvidenceClassing.ClassOf(Aspect, quarters, 360).ShouldBe(0);
        EvidenceClassing.ClassOf(Aspect, quarters, -10).ShouldBe(3);
        EvidenceClassing.ClassOf(Aspect, quarters, 359.9).ShouldBe(3);
        EvidenceClassing.ClassOf(Aspect, quarters, 90).ShouldBe(1);
    }

    [Fact]
    public void A_categorical_cell_is_already_its_class_and_every_class_of_the_layer_counts()
    {
        var categories = new EvidenceTransform(EvidenceTransformKind.Categories, Categories: []);

        EvidenceClassing.ClassCount(Lithology, categories).ShouldBe(Lithology.Categories.Count);
        EvidenceClassing.ClassOf(Lithology, categories, 0).ShouldBe(0);
        EvidenceClassing.ClassOf(Lithology, categories, Lithology.Categories.Count - 1).ShouldBe(Lithology.Categories.Count - 1);
        EvidenceClassing.ClassOf(Lithology, categories, Lithology.Categories.Count).ShouldBe(EvidenceClassing.NoClass);
        EvidenceClassing.ClassOf(Lithology, categories, -1).ShouldBe(EvidenceClassing.NoClass);
    }

    [Fact]
    public void A_cell_with_no_reading_has_no_class()
    {
        EvidenceClassing.Classify(Slope, Table, [2f, float.NaN, 20f, float.PositiveInfinity, 5f])
            .ShouldBe([0, EvidenceClassing.NoClass, 2, EvidenceClassing.NoClass, 1]);
        EvidenceClassing.ClassCount(Slope, Table).ShouldBe(3);
    }

    [Fact]
    public void A_measured_layer_cannot_be_classed_by_a_ramp_or_an_empty_table()
    {
        var ramp = new EvidenceTransform(EvidenceTransformKind.FuzzyLinear, Low: 0, High: 30);
        var empty = new EvidenceTransform(EvidenceTransformKind.Classes, Classes: []);

        Should.Throw<ArgumentException>(() => EvidenceClassing.ClassCount(Slope, ramp));
        Should.Throw<ArgumentException>(() => EvidenceClassing.ClassOf(Slope, ramp, 3));
        Should.Throw<ArgumentException>(() => EvidenceClassing.Classify(Slope, empty, []));
    }

    [Fact]
    public void A_position_is_in_the_cell_whose_centre_the_grid_reports_and_off_the_grid_past_its_edges()
    {
        var grid = EvidenceGrid.Over(20, 59.99, 20.02, 60.01, 50)!;

        for (var row = 0; row < grid.Rows; row += 7)
        {
            for (var column = 0; column < grid.Columns; column += 5)
            {
                grid.CellAt(grid.LongitudeOf(column), grid.LatitudeOf(row)).ShouldBe(grid.IndexOf(column, row));
            }
        }

        // A cell owns its western and northern edges, so the grid's own corner is its first cell…
        grid.CellAt(grid.West, grid.North).ShouldBe(0);
        // …and its eastern and southern edges belong to no cell.
        grid.CellAt(grid.East, grid.LatitudeOf(0)).ShouldBeNull();
        grid.CellAt(grid.LongitudeOf(0), grid.South).ShouldBeNull();
        grid.CellAt(grid.West - 1e-9, grid.LatitudeOf(0)).ShouldBeNull();
        grid.CellAt(grid.LongitudeOf(0), grid.North + 1e-9).ShouldBeNull();
        grid.CellAt(double.NaN, 60).ShouldBeNull();
    }
}
