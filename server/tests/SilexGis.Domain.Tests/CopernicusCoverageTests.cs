// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which cells of the open elevation dataset a rectangle needs, and where each one is published.
///
/// <para>
/// The arithmetic is stated twice — once by the command-line tool an operator can still run by
/// hand, once by the application — so both must name the same cell for the same box. Getting it
/// wrong does not look wrong: the tiles that come back are real elevation, they are simply of
/// somewhere else, and the terrain built from them is smooth, plausible ground in the wrong place.
/// The boxes below are the ones the command-line tool's own tests use, for exactly that reason.
/// </para>
/// </summary>
public class CopernicusCoverageTests
{
    [Fact]
    public void A_one_degree_box_needs_the_single_cell_named_for_its_south_west_corner()
    {
        CopernicusCoverage.Cells(22, 46, 23, 47).ShouldBe(["N46_00_E022_00"]);
    }

    [Fact]
    public void The_cell_is_named_for_its_south_west_corner_on_both_sides_of_the_equator()
    {
        // A corner at exactly zero belongs to the northern and eastern names: the bucket publishes
        // S01_00_E015_00 and has no S00_00_W000_00 at all.
        CopernicusCoverage.Cells(15, -1, 16, 0).ShouldBe(["S01_00_E015_00"]);
        CopernicusCoverage.Cells(15, 0, 16, 1).ShouldBe(["N00_00_E015_00"]);
    }

    [Fact]
    public void West_of_Greenwich_and_south_of_the_equator_take_the_other_letters()
    {
        CopernicusCoverage.Cells(-1, 46, 0, 47).ShouldBe(["N46_00_W001_00"]);
        CopernicusCoverage.Cells(0, 46, 1, 47).ShouldBe(["N46_00_E000_00"]);
    }

    [Fact]
    public void Degrees_are_padded_to_the_widths_the_dataset_uses()
    {
        // Two digits of latitude, three of longitude, whatever the number.
        CopernicusCoverage.Cells(5, 7, 6, 8).ShouldBe(["N07_00_E005_00"]);
    }

    [Fact]
    public void A_box_spanning_several_cells_takes_them_all_row_by_row()
    {
        CopernicusCoverage.Cells(22, 46, 24, 48).ShouldBe(
        [
            "N46_00_E022_00", "N46_00_E023_00",
            "N47_00_E022_00", "N47_00_E023_00",
        ]);
    }

    [Fact]
    public void A_box_that_crosses_a_boundary_by_a_fraction_takes_both_cells()
    {
        // The rule that matters: rounding the edges inward would leave a strip of the area asked
        // for with no elevation under it, and terrain with a hole in it is drawn as smooth ground
        // rather than as a hole. So the south and west edges round down and the north and east
        // edges round up.
        CopernicusCoverage.Cells(22.5, 46.5, 23.000001, 46.9).ShouldBe(
            ["N46_00_E022_00", "N46_00_E023_00"]);
        CopernicusCoverage.Cells(22.5, 46.5, 22.9, 46.9).ShouldBe(["N46_00_E022_00"]);
    }

    [Fact]
    public void Every_cell_is_published_under_a_directory_repeating_its_own_name()
    {
        CopernicusCoverage.Url("N46_00_E022_00").ShouldBe(
            "https://copernicus-dem-30m.s3.amazonaws.com/"
            + "Copernicus_DSM_COG_10_N46_00_E022_00_DEM/Copernicus_DSM_COG_10_N46_00_E022_00_DEM.tif");
    }

    [Fact]
    public void A_downloaded_cell_is_kept_under_the_cells_own_name()
    {
        CopernicusCoverage.FileName("N46_00_E022_00").ShouldBe("N46_00_E022_00.tif");
    }

    [Fact]
    public void The_credit_names_the_dataset_the_licence_belongs_to()
    {
        // Not an assertion about wording but about what it is for: a pyramid must never carry a
        // credit belonging to data it does not contain, so the one credit held here names the one
        // dataset this application obtains by itself, and everything else is credited by whoever
        // supplies it.
        CopernicusCoverage.Attribution.ShouldContain("Copernicus DEM GLO-30");
        CopernicusCoverage.Licence.ShouldNotBeNullOrWhiteSpace();
    }
}
