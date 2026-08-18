// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading a tile's position out of its file name.
///
/// <para>
/// The stakes here are not "an upload is refused". A name misread by one degree is a hundred and
/// eleven kilometres of ground placed somewhere it is not, and the raster that results is perfectly
/// well-formed: it opens, it draws, it meshes. Nothing downstream can notice. So every case below
/// asserts the two integers, and the refusals assert that nothing was returned rather than that
/// something went wrong.
/// </para>
/// </summary>
public class SrtmTileNameTests
{
    [Theory]
    [InlineData("N45E024.hgt", 45, 24)]
    [InlineData("N00E000.hgt", 0, 0)]
    [InlineData("S01W001.hgt", -1, -1)]
    [InlineData("S89W180.hgt", -89, -180)]
    [InlineData("N89E179.hgt", 89, 179)]
    public void A_name_gives_up_the_corner_it_describes(string name, int south, int west)
    {
        var tile = SrtmTileName.Parse(name).ShouldNotBeNull();

        tile.South.ShouldBe(south);
        tile.West.ShouldBe(west);
    }

    [Theory]
    [InlineData("n45e024.hgt")]
    [InlineData("N45E025.HGT")]
    [InlineData("N45e024.Hgt")]
    public void Case_is_not_part_of_what_a_name_says(string name) =>
        SrtmTileName.Parse(name).ShouldNotBeNull().FileName.ShouldBe(
            name.StartsWith("N45E025", StringComparison.OrdinalIgnoreCase) ? "N45E025.hgt" : "N45E024.hgt");

    /// <remarks>
    /// Publishers put the dataset in the middle of the name, and the square such a file covers is
    /// exactly the square the leading characters give. Refusing them would be refusing perfectly
    /// placeable data over a naming detail.
    /// </remarks>
    [Fact]
    public void What_a_publisher_put_between_the_corner_and_the_extension_is_ignored() =>
        SrtmTileName.Parse("N45E025.SRTMGL1.hgt").ShouldBe(new SrtmTileName(45, 25));

    [Fact]
    public void A_path_is_read_by_its_last_part_only() =>
        SrtmTileName.Parse(Path.Combine("N01E001", "N45E024.hgt")).ShouldBe(new SrtmTileName(45, 24));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("elevation.hgt")]
    [InlineData("tile.hgt")]
    [InlineData("01a013a764587cf5a205ab81e06102a9.hgt")]
    [InlineData("N45E024.tif")]
    [InlineData("N45E024")]
    [InlineData("X45E024.hgt")]
    [InlineData("N45X024.hgt")]
    [InlineData("N4XE024.hgt")]
    [InlineData("N45E0X4.hgt")]
    [InlineData("N45E24.hgt")]
    [InlineData("N4E024.hgt")]
    public void A_name_that_does_not_describe_a_square_places_nothing(string? name) =>
        SrtmTileName.Parse(name).ShouldBeNull();

    /// <remarks>
    /// A tile covers the degree above the corner it names, so a south edge of 90 would be a square
    /// off the top of the earth. Both of these have the shape of a name and describe no ground.
    /// </remarks>
    [Theory]
    [InlineData("N90E024.hgt")]
    [InlineData("N45E180.hgt")]
    [InlineData("S91E024.hgt")]
    [InlineData("N45W181.hgt")]
    public void A_corner_that_is_not_on_the_earth_places_nothing(string name) =>
        SrtmTileName.Parse(name).ShouldBeNull();

    /// <remarks>
    /// The name is rebuilt from the two integers and never from the characters they were read out
    /// of. That is what lets a file arrive from a browser, where the name is the one part of it
    /// somebody outside chose, without any of it reaching a path.
    /// </remarks>
    [Theory]
    [InlineData(45, 24, "N45E024.hgt")]
    [InlineData(-1, -1, "S01W001.hgt")]
    [InlineData(0, 0, "N00E000.hgt")]
    [InlineData(-89, -180, "S89W180.hgt")]
    public void The_canonical_name_is_built_from_the_numbers(int south, int west, string expected) =>
        new SrtmTileName(south, west).FileName.ShouldBe(expected);

    [Fact]
    public void Every_name_that_parses_survives_being_written_out_and_read_back()
    {
        for (var south = -90; south <= 89; south++)
        {
            for (var west = -180; west <= 179; west += 7)
            {
                var tile = new SrtmTileName(south, west);
                SrtmTileName.Parse(tile.FileName).ShouldBe(tile);
            }
        }
    }

    [Theory]
    [InlineData("N45E024.hgt", true)]
    [InlineData("anything.HGT", true)]
    [InlineData("N45E024.tif", false)]
    [InlineData(null, false)]
    public void What_carries_its_position_only_in_its_name_is_recognised(string? name, bool expected) =>
        SrtmTileName.IsTileExtension(name).ShouldBe(expected);
}
