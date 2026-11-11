// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which names inside a scan's tileset archive may be asked for: the tiler's own layout is
/// served, and a name built to walk out of the archive or to reach something that is not a tile
/// is not — whatever the archive holds.
/// </summary>
public sealed class TilesetEntryNamesTests
{
    [Theory]
    [InlineData("tileset.json", "application/json")]
    [InlineData("r.pnts", "application/octet-stream")]
    [InlineData("r0123.pnts", "application/octet-stream")]
    [InlineData("r0.json", "application/json")]
    [InlineData("tiles/r.pnts", "application/octet-stream")]
    [InlineData("a_b-c.1/deep/r-1_2.pnts", "application/octet-stream")]
    public void The_tiler_s_own_names_are_served_as_json_or_as_bytes(string name, string contentType)
    {
        TilesetEntryNames.IsServed(name).ShouldBeTrue();
        TilesetEntryNames.ContentTypeOf(name).ShouldBe(contentType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tileset.JSON")] // the tiler writes lower case; a case-insensitive match would be a second name for one entry
    [InlineData("r.PNTS")]
    [InlineData("notes.txt")]
    [InlineData("tileset.json.bak")]
    [InlineData("tileset")]
    [InlineData(".json")]
    [InlineData("r.pnts/")]
    [InlineData("/tileset.json")]
    [InlineData("//tileset.json")]
    [InlineData("tiles//r.pnts")]
    [InlineData("../tileset.json")]
    [InlineData("tiles/../tileset.json")]
    [InlineData("./tileset.json")]
    [InlineData(".hidden/r.pnts")]
    [InlineData("tiles\\r.pnts")]
    [InlineData("C:/tiles/r.pnts")]
    [InlineData("tiles/r .pnts")]
    [InlineData("tiles/r%2e.pnts")]
    [InlineData("tiles/ř.pnts")]
    [InlineData("tiles/r\0.pnts")]
    public void A_name_that_walks_out_or_is_not_a_tile_is_not_served(string? name)
    {
        TilesetEntryNames.IsServed(name).ShouldBeFalse();
        TilesetEntryNames.ContentTypeOf(name).ShouldBeNull();
    }

    [Fact]
    public void A_name_longer_than_any_tiler_writes_is_not_served()
    {
        var name = new string('r', 600) + ".pnts";
        TilesetEntryNames.IsServed(name).ShouldBeFalse();
    }

    [Fact]
    public void The_root_entry_is_the_tileset_and_is_served()
    {
        TilesetEntryNames.Root.ShouldBe("tileset.json");
        TilesetEntryNames.IsServed(TilesetEntryNames.Root).ShouldBeTrue();
    }
}
