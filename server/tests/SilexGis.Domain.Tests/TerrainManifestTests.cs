// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Putting a pyramid's manifest right after the tile-maker has added a raster to it in place.
/// </summary>
/// <remarks>
/// Measured on a pilot: the addition keeps every tile and rewrites the manifest from the new
/// raster alone — its bounds, and at every level only its rectangles — and reverts the version and
/// the credit to the tool's own constants. What comes out validates as damaged nowhere and draws
/// nothing but the newest patch. The rule tested here is what makes the addition usable at all, and
/// a merge that loses or shrinks one rectangle is the very failure it exists to repair, so every
/// case asserts the exact rectangles that come out rather than a count.
/// </remarks>
public class TerrainManifestTests
{
    /// <summary>
    /// The pilot's failure, exactly: a base holding a region and one fine island, then a second
    /// island added. The tool's own manifest advertises the second island and nothing else.
    /// Merged, every rectangle the base advertised is still there, and the new ones beside them.
    /// </summary>
    [Fact]
    public void The_bakers_output_alone_advertises_only_the_new_patch_and_the_merge_puts_every_base_rectangle_back()
    {
        var baseManifest = Manifest(
            bounds: [22.0, 46.0, 22.5, 46.5],
            levels:
            [
                [Range(0, 0, 1, 0)],
                [Range(0, 0, 3, 1)],
                [Range(1148, 760, 1160, 772)],
                [Range(2300, 1522, 2306, 1528)],
            ]);

        var afterAdding = Manifest(
            bounds: [22.30, 46.30, 22.34, 46.34],
            levels:
            [
                [Range(0, 0, 1, 0)],
                [Range(2, 1, 2, 1)],
                [Range(1156, 768, 1157, 769)],
                [Range(2312, 1536, 2314, 1538)],
            ]);

        // What the tool leaves is the trap: the base's deep rectangles are gone from it.
        TerrainManifest.Available(afterAdding)[3].ShouldNotContain(Range(2300, 1522, 2306, 1528));

        var merged = TerrainManifest.Merge(baseManifest, afterAdding);
        var levels = TerrainManifest.Available(merged);

        levels.Count.ShouldBe(4);
        levels[0].ShouldBe([Range(0, 0, 1, 0)]);
        levels[1].ShouldBe([Range(0, 0, 3, 1), Range(2, 1, 2, 1)]);
        levels[2].ShouldBe([Range(1148, 760, 1160, 772), Range(1156, 768, 1157, 769)]);
        levels[3].ShouldBe([Range(2300, 1522, 2306, 1528), Range(2312, 1536, 2314, 1538)]);

        TerrainManifest.Bounds(merged).ShouldBe([22.0, 46.0, 22.5, 46.5]);
    }

    [Fact]
    public void Two_rasters_apart_from_each_other_are_both_advertised_and_the_bounds_hold_both()
    {
        var west = Manifest(bounds: [22.0, 46.0, 22.2, 46.2], levels: [[Range(0, 0, 0, 0)], [Range(10, 10, 12, 12)]]);
        var east = Manifest(bounds: [22.6, 46.6, 22.8, 46.8], levels: [[Range(1, 1, 1, 1)], [Range(40, 40, 42, 42)]]);

        var merged = TerrainManifest.Merge(west, east);
        var levels = TerrainManifest.Available(merged);

        // Two rectangles, not one grown around both: the ground between them has no deep tiles,
        // and a bounding rectangle would send a viewer asking for tiles that are not there.
        levels[1].ShouldBe([Range(10, 10, 12, 12), Range(40, 40, 42, 42)]);
        levels[0].ShouldBe([Range(0, 0, 0, 0), Range(1, 1, 1, 1)]);
        TerrainManifest.Bounds(merged).ShouldBe([22.0, 46.0, 22.8, 46.8]);
    }

    /// <summary>
    /// Overlapping rectangles are both kept as they are. Trimming one against the other would be
    /// a second opinion about which tiles exist, and the tiles under both exist.
    /// </summary>
    [Fact]
    public void Two_rasters_that_overlap_keep_both_rectangles_untrimmed()
    {
        var first = Manifest(bounds: [22.0, 46.0, 22.3, 46.3], levels: [[Range(10, 10, 20, 20)]]);
        var second = Manifest(bounds: [22.2, 46.2, 22.5, 46.5], levels: [[Range(15, 15, 25, 25)]]);

        var merged = TerrainManifest.Merge(first, second);

        TerrainManifest.Available(merged)[0].ShouldBe([Range(10, 10, 20, 20), Range(15, 15, 25, 25)]);
        TerrainManifest.Bounds(merged).ShouldBe([22.0, 46.0, 22.5, 46.5]);
    }

    /// <summary>
    /// A coarse region with fine data deeper than anything the addition reaches: the deeper levels
    /// are the base's alone, and they survive.
    /// </summary>
    [Fact]
    public void A_base_with_more_levels_than_the_addition_keeps_its_deeper_levels()
    {
        var baseManifest = Manifest(
            bounds: [22.0, 46.0, 23.0, 47.0],
            levels: [[Range(0, 0, 1, 0)], [Range(0, 0, 3, 1)], [Range(8, 4, 9, 5)], [Range(16, 8, 19, 11)]]);
        var afterAdding = Manifest(
            bounds: [22.1, 46.1, 22.2, 46.2],
            levels: [[Range(0, 0, 1, 0)], [Range(1, 0, 1, 0)]]);

        var levels = TerrainManifest.Available(TerrainManifest.Merge(baseManifest, afterAdding));

        levels.Count.ShouldBe(4);
        levels[1].ShouldBe([Range(0, 0, 3, 1), Range(1, 0, 1, 0)]);
        levels[2].ShouldBe([Range(8, 4, 9, 5)]);
        levels[3].ShouldBe([Range(16, 8, 19, 11)]);
    }

    /// <summary>
    /// The usual reason to add at all: a fine survey over a coarse base reaches levels the base
    /// never had. Those levels are the addition's alone, and they are advertised.
    /// </summary>
    [Fact]
    public void An_addition_with_more_levels_than_the_base_adds_its_deeper_levels()
    {
        var baseManifest = Manifest(
            bounds: [22.0, 46.0, 23.0, 47.0],
            levels: [[Range(0, 0, 1, 0)], [Range(0, 0, 3, 1)]]);
        var afterAdding = Manifest(
            bounds: [22.1, 46.1, 22.2, 46.2],
            levels: [[Range(0, 0, 1, 0)], [Range(1, 0, 1, 0)], [Range(4, 2, 5, 3)], [Range(8, 4, 11, 7)]]);

        var levels = TerrainManifest.Available(TerrainManifest.Merge(baseManifest, afterAdding));

        levels.Count.ShouldBe(4);
        levels[0].ShouldBe([Range(0, 0, 1, 0)]);
        levels[1].ShouldBe([Range(0, 0, 3, 1), Range(1, 0, 1, 0)]);
        levels[2].ShouldBe([Range(4, 2, 5, 3)]);
        levels[3].ShouldBe([Range(8, 4, 11, 7)]);
    }

    [Fact]
    public void Empty_availability_on_either_side_leaves_the_other_sides_rectangles_exactly()
    {
        var full = Manifest(bounds: [22.0, 46.0, 23.0, 47.0], levels: [[Range(0, 0, 1, 0)], [Range(0, 0, 3, 1)]]);
        var empty = Manifest(bounds: null, levels: []);
        var emptyLevels = Manifest(bounds: null, levels: [[], []]);

        TerrainManifest.Available(TerrainManifest.Merge(full, empty))
            .ShouldBe([[Range(0, 0, 1, 0)], [Range(0, 0, 3, 1)]]);
        TerrainManifest.Available(TerrainManifest.Merge(empty, full))
            .ShouldBe([[Range(0, 0, 1, 0)], [Range(0, 0, 3, 1)]]);
        TerrainManifest.Available(TerrainManifest.Merge(emptyLevels, full))
            .ShouldBe([[Range(0, 0, 1, 0)], [Range(0, 0, 3, 1)]]);

        // Bounds stated on one side only are the bounds.
        TerrainManifest.Bounds(TerrainManifest.Merge(full, empty)).ShouldBe([22.0, 46.0, 23.0, 47.0]);
        TerrainManifest.Bounds(TerrainManifest.Merge(empty, full)).ShouldBe([22.0, 46.0, 23.0, 47.0]);
    }

    /// <summary>
    /// Merging the merged document with the base again changes nothing, which is what lets a run
    /// stopped between merging and recording that it had merged do it over.
    /// </summary>
    [Fact]
    public void Merging_a_second_time_advertises_nothing_twice()
    {
        var baseManifest = Manifest(bounds: [22.0, 46.0, 22.5, 46.5], levels: [[Range(0, 0, 1, 0)], [Range(4, 2, 7, 5)]]);
        var afterAdding = Manifest(bounds: [22.2, 46.2, 22.3, 46.3], levels: [[Range(0, 0, 1, 0)], [Range(5, 3, 5, 3)]]);

        var once = TerrainManifest.Merge(baseManifest, afterAdding);
        var twice = TerrainManifest.Merge(baseManifest, once);

        TerrainManifest.Available(twice).ShouldBe([[Range(0, 0, 1, 0)], [Range(4, 2, 7, 5), Range(5, 3, 5, 3)]]);
        twice.ToJsonString().ShouldBe(once.ToJsonString());
    }

    /// <summary>
    /// Everything that is not coverage comes from the document the tool wrote last, and neither
    /// input is changed by being merged.
    /// </summary>
    [Fact]
    public void The_rest_of_the_document_is_the_tools_and_the_inputs_are_left_alone()
    {
        var baseManifest = Manifest(bounds: [22.0, 46.0, 22.5, 46.5], levels: [[Range(0, 0, 1, 0)]]);
        baseManifest["version"] = "1.1.0-abcdef123456";
        baseManifest["attribution"] = "A survey";
        baseManifest["minzoom"] = 0;
        baseManifest["maxzoom"] = 13;

        var afterAdding = Manifest(bounds: [22.1, 46.1, 22.2, 46.2], levels: [[Range(0, 0, 1, 0)]]);
        afterAdding["version"] = "1.1.0";
        afterAdding["attribution"] = "insert attribution here";
        afterAdding["tiles"] = new JsonArray("{z}/{x}/{y}.terrain?v={version}");
        afterAdding["minzoom"] = 0;
        afterAdding["maxzoom"] = 11;

        var baseBefore = baseManifest.ToJsonString();
        var addedBefore = afterAdding.ToJsonString();

        var merged = TerrainManifest.Merge(baseManifest, afterAdding);

        merged["version"]!.GetValue<string>().ShouldBe("1.1.0");
        merged["attribution"]!.GetValue<string>().ShouldBe("insert attribution here");
        merged["tiles"]![0]!.GetValue<string>().ShouldBe("{z}/{x}/{y}.terrain?v={version}");
        merged["maxzoom"]!.GetValue<int>().ShouldBe(13);
        merged["minzoom"]!.GetValue<int>().ShouldBe(0);

        baseManifest.ToJsonString().ShouldBe(baseBefore);
        afterAdding.ToJsonString().ShouldBe(addedBefore);
    }

    /// <summary>
    /// The stamp that follows the merge, and follows every fresh bake: the credit of what the
    /// pyramid was made from replaces the tool's filler, the name goes with it, the legend nothing
    /// publishes goes, and the version is the one derived from the tiles.
    /// </summary>
    [Fact]
    public void The_stamp_puts_the_credit_and_the_content_version_back_the_way_a_fresh_bake_gets_them()
    {
        var manifest = Manifest(bounds: [22.0, 46.0, 22.5, 46.5], levels: [[Range(0, 0, 1, 0)]]);
        manifest["name"] = "insert name here";
        manifest["description"] = "insert description here";
        manifest["attribution"] = "insert attribution here";
        manifest["legend"] = "legend.png";
        manifest["version"] = "1.1.0";

        TerrainManifest.Stamp(manifest, "Copernicus DEM; A county lidar survey", "1.1.0-0123456789ab");

        manifest["attribution"]!.GetValue<string>().ShouldBe("Copernicus DEM; A county lidar survey");
        manifest["version"]!.GetValue<string>().ShouldBe("1.1.0-0123456789ab");
        manifest["description"]!.GetValue<string>().ShouldBe(TerrainManifest.PlainDescription);
        manifest["name"].ShouldBeNull();
        manifest["legend"].ShouldBeNull();

        // Coverage is untouched by the stamp.
        TerrainManifest.Available(manifest).ShouldBe([[Range(0, 0, 1, 0)]]);

        // With no credit to give, the filler goes and nothing false takes its place.
        var uncredited = Manifest(bounds: null, levels: [[Range(0, 0, 1, 0)]]);
        uncredited["attribution"] = "insert attribution here";
        uncredited["name"] = "insert name here";
        TerrainManifest.Stamp(uncredited, null, "1.1.0-0123456789ab");
        uncredited["attribution"].ShouldBeNull();
        uncredited["name"].ShouldBeNull();
    }

    [Fact]
    public void Bounds_in_any_shape_but_four_numbers_are_read_as_none()
    {
        TerrainManifest.Bounds(new JsonObject()).ShouldBeNull();
        TerrainManifest.Bounds(new JsonObject { ["bounds"] = new JsonArray(1, 2, 3) }).ShouldBeNull();
        TerrainManifest.Bounds(new JsonObject { ["bounds"] = new JsonArray(1, "two", 3, 4) }).ShouldBeNull();
        TerrainManifest.Bounds(new JsonObject { ["bounds"] = "22,46,23,47" }).ShouldBeNull();
        TerrainManifest.Bounds(new JsonObject { ["bounds"] = new JsonArray(22.0, 46.0, 23.0, 47.0) })
            .ShouldBe([22.0, 46.0, 23.0, 47.0]);
    }

    private static TerrainTileRange Range(int startX, int startY, int endX, int endY) =>
        new(startX, startY, endX, endY);

    /// <summary>A manifest as the tile-maker writes one, with the coverage given.</summary>
    private static JsonObject Manifest(double[]? bounds, TerrainTileRange[][] levels)
    {
        var available = new JsonArray();
        foreach (var level in levels)
        {
            var ranges = new JsonArray();
            foreach (var range in level)
            {
                ranges.Add(new JsonObject
                {
                    ["startX"] = range.StartX,
                    ["startY"] = range.StartY,
                    ["endX"] = range.EndX,
                    ["endY"] = range.EndY,
                });
            }

            available.Add(ranges);
        }

        var manifest = new JsonObject
        {
            ["tilejson"] = "2.1.0",
            ["format"] = "quantized-mesh-1.0",
            ["scheme"] = "tms",
            ["tiles"] = new JsonArray("{z}/{x}/{y}.terrain?v={version}"),
            ["available"] = available,
        };

        if (bounds is not null)
        {
            manifest["bounds"] = new JsonArray(bounds[0], bounds[1], bounds[2], bounds[3]);
        }

        return manifest;
    }
}
