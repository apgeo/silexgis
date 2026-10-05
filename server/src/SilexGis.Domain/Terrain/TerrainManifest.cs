// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilexGis.Domain.Terrain;

/// <summary>
/// What this application does to a pyramid's manifest after the tile-maker has written it: reads
/// what ground it advertises, puts back the coverage an in-place addition throws away, and stamps
/// the credit and the version the tile-maker leaves as filler.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is kept as the document it is rather than mapped onto a type of our own, because
/// most of what is in it belongs to the tile-maker and to the viewer — the tile address template,
/// the format, the scheme, the projection — and a document written back with one of those quietly
/// dropped is a pyramid nothing can draw. Only the fields this class is about are read and written;
/// everything else passes through untouched.
/// </para>
/// <para>
/// Everything here is a pure function of its inputs, with no disk and no clock in it, so that the
/// rule can be tested against the exact shapes the tile-maker produces without a pyramid on disk.
/// </para>
/// </remarks>
public static class TerrainManifest
{
    /// <summary>The ground the pyramid covers, as west, south, east, north in degrees.</summary>
    public const string BoundsField = "bounds";

    /// <summary>The rectangles of tiles the pyramid holds, one list per level.</summary>
    public const string AvailableField = "available";

    /// <summary>What a pyramid says about itself when nothing better is known.</summary>
    public const string PlainDescription = "Elevation model baked for SilexGIS.";

    /// <summary>
    /// The manifest of a pyramid that a raster was added to, with everything the base pyramid
    /// advertised put back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tile-maker's in-place addition keeps every tile and rewrites the manifest from the new
    /// raster alone: its bounds, and at every level only the rectangles the new raster produced.
    /// Every tile of every earlier raster is still on disk and advertised nowhere, so a viewer never
    /// asks for it, and the pyramid draws nothing but the newest patch. Measured on a pilot rather
    /// than read in a manual, which is why this exists at all.
    /// </para>
    /// <para>
    /// The union is taken rectangle by rectangle and the rectangles are kept exactly as they were.
    /// Nothing here coalesces neighbours, trims overlaps or grows a bounding box around a level,
    /// because losing or shrinking a rectangle is precisely the failure being repaired: a rectangle
    /// that is advertised one tile smaller than the tiles it describes is ground that is on disk and
    /// never drawn. The one thing dropped is a rectangle identical to one already kept, which
    /// advertises nothing twice and is what makes merging the same two documents again a no-op —
    /// a run that stopped between merging and recording that it had merged does this over.
    /// </para>
    /// <para>
    /// Everything that is not coverage comes from the document the tile-maker wrote last, because
    /// that is the document describing the tiles as they now are. The credit and the version it
    /// reverted to its own constants are not repaired here; they are stamped afterwards, the way a
    /// fresh bake's are, by <see cref="Stamp"/>.
    /// </para>
    /// </remarks>
    /// <param name="baseManifest">The base pyramid's manifest, as it was advertised before the addition.</param>
    /// <param name="added">The manifest the tile-maker wrote after adding to that pyramid.</param>
    /// <returns>A new document; neither input is changed.</returns>
    public static JsonObject Merge(JsonObject baseManifest, JsonObject added)
    {
        var merged = (JsonObject)added.DeepClone();

        var bounds = UnionOfBounds(Bounds(baseManifest), Bounds(added));
        if (bounds is not null)
        {
            merged[BoundsField] = new JsonArray(bounds[0], bounds[1], bounds[2], bounds[3]);
        }

        merged[AvailableField] = Levels(UnionOfAvailable(Available(baseManifest), Available(added)));

        // The shallowest and deepest levels, where a manifest states them: an advertisement in its
        // own right, and the addition's deepest level is the new raster's rather than the pyramid's.
        var minimum = Lesser(Level(baseManifest, "minzoom"), Level(added, "minzoom"));
        if (minimum is { } min)
        {
            merged["minzoom"] = min;
        }

        var maximum = Greater(Level(baseManifest, "maxzoom"), Level(added, "maxzoom"));
        if (maximum is { } max)
        {
            merged["maxzoom"] = max;
        }

        return merged;
    }

    /// <summary>
    /// Replaces what the tile-maker left behind with what is true, and stamps the version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The credit given for this build replaces whatever is in the manifest, including a real
    /// looking credit left by an earlier bake into the same directory — otherwise ground remade
    /// from different data keeps the first data's licence statement. The name travels with the
    /// credit rather than being left alone, because a name kept from one bake beside a credit
    /// written for another has the pyramid naming one source and crediting a different one.
    /// </para>
    /// <para>
    /// The version is a cache key: every tile address a viewer builds ends with it, and tiles are
    /// cached for a week. The tile-maker writes the same constant into every pyramid it has ever
    /// produced, so without this a pyramid made again is served out of viewers' own caches with no
    /// request reaching the server. A pyramid a raster was added to in place is exactly a pyramid
    /// made again, with the constant put back by the addition.
    /// </para>
    /// </remarks>
    /// <param name="manifest">The document, changed in place.</param>
    /// <param name="credit">The credit of everything this pyramid was made from, or none.</param>
    /// <param name="version">The version derived from the tiles the pyramid actually holds.</param>
    public static void Stamp(JsonObject manifest, string? credit, string version)
    {
        if (credit is not null)
        {
            manifest["attribution"] = credit;
            manifest.Remove("name");
        }
        else
        {
            if (TerrainPyramidCheck.IsFiller(Text(manifest, "attribution")))
            {
                manifest.Remove("attribution");
            }

            if (TerrainPyramidCheck.IsFiller(Text(manifest, "name")))
            {
                manifest.Remove("name");
            }
        }

        if (TerrainPyramidCheck.IsFiller(Text(manifest, "description")))
        {
            manifest["description"] = PlainDescription;
        }

        // A pointer to a legend the tile-maker puts in every manifest and this installation does not
        // publish. Left in, it is an address a viewer may follow to nothing.
        manifest.Remove("legend");

        manifest["version"] = version;
    }

    /// <summary>
    /// What ground the manifest says it holds, level by level.
    /// </summary>
    /// <remarks>
    /// A level is a list of rectangles rather than one, which is what lets a pyramid hold deep tiles
    /// over two separate fine surveys without claiming the coarse ground between them. Anything in
    /// the wrong shape is read as advertising nothing rather than as advertising everything: the
    /// mistake that direction is a refused build, and the other direction is a build published with
    /// tiles nothing will ever ask for.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<TerrainTileRange>> Available(JsonObject manifest)
    {
        if (manifest[AvailableField] is not JsonArray levels)
        {
            return [];
        }

        var advertised = new List<IReadOnlyList<TerrainTileRange>>(levels.Count);

        foreach (var level in levels)
        {
            var ranges = new List<TerrainTileRange>();

            if (level is JsonArray entries)
            {
                foreach (var entry in entries)
                {
                    if (entry is JsonObject range)
                    {
                        ranges.Add(new TerrainTileRange(
                            Ordinate(range, "startX"),
                            Ordinate(range, "startY"),
                            Ordinate(range, "endX"),
                            Ordinate(range, "endY")));
                    }
                }
            }

            advertised.Add(ranges);
        }

        return advertised;
    }

    /// <summary>
    /// The ground the manifest says it covers — west, south, east, north — or null when it does
    /// not say, or says it in a shape that is not four numbers.
    /// </summary>
    public static double[]? Bounds(JsonObject manifest)
    {
        if (manifest[BoundsField] is not JsonArray edges || edges.Count != 4)
        {
            return null;
        }

        var bounds = new double[4];
        for (var i = 0; i < 4; i++)
        {
            // Read from the number's own text rather than through a typed accessor, because a
            // whole-degree edge written as an integer is a number the typed read of a double
            // refuses.
            if (edges[i] is not { } edge
                || edge.GetValueKind() != JsonValueKind.Number
                || !double.TryParse(
                    edge.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out bounds[i])
                || !double.IsFinite(bounds[i]))
            {
                return null;
            }
        }

        return bounds;
    }

    /// <summary>
    /// Both lists of rectangles, level by level, each rectangle kept exactly as it was and an
    /// identical one kept once.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TerrainTileRange>> UnionOfAvailable(
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> first,
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> second)
    {
        var levels = Math.Max(first.Count, second.Count);
        var union = new List<IReadOnlyList<TerrainTileRange>>(levels);

        for (var level = 0; level < levels; level++)
        {
            var ranges = new List<TerrainTileRange>();

            if (level < first.Count)
            {
                ranges.AddRange(first[level]);
            }

            if (level < second.Count)
            {
                foreach (var range in second[level])
                {
                    if (!ranges.Contains(range))
                    {
                        ranges.Add(range);
                    }
                }
            }

            union.Add(ranges);
        }

        return union;
    }

    /// <summary>The smallest rectangle holding both, or whichever one there is.</summary>
    public static double[]? UnionOfBounds(double[]? first, double[]? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return
        [
            Math.Min(first[0], second[0]),
            Math.Min(first[1], second[1]),
            Math.Max(first[2], second[2]),
            Math.Max(first[3], second[3]),
        ];
    }

    private static JsonArray Levels(IReadOnlyList<IReadOnlyList<TerrainTileRange>> available)
    {
        var levels = new JsonArray();

        foreach (var level in available)
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

            levels.Add(ranges);
        }

        return levels;
    }

    private static int? Level(JsonObject manifest, string name) =>
        manifest[name] is { } value && value.GetValueKind() == JsonValueKind.Number
            ? value.GetValue<int>()
            : null;

    private static int? Lesser(int? first, int? second) =>
        first is null ? second : second is null ? first : Math.Min(first.Value, second.Value);

    private static int? Greater(int? first, int? second) =>
        first is null ? second : second is null ? first : Math.Max(first.Value, second.Value);

    private static int Ordinate(JsonObject range, string name) =>
        range[name] is { } value && value.GetValueKind() == JsonValueKind.Number
            ? value.GetValue<int>()
            : 0;

    private static string? Text(JsonObject manifest, string name) =>
        manifest[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
