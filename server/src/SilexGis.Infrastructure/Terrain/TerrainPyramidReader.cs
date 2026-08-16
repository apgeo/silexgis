// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>What one pyramid on disk turned out to be.</summary>
/// <param name="Manifest">
/// The manifest as it was found, whole. Kept as it was rather than mapped onto a type of our own
/// because most of what is in it belongs to the tile-maker and to the viewer, and something written
/// back with fields quietly dropped is a pyramid nothing can draw.
/// </param>
/// <param name="TileCount">How many tiles are on disk.</param>
/// <param name="TileBytes">What they occupy.</param>
/// <param name="Digest">A hash over every tile's place and contents, in a fixed order.</param>
/// <param name="MeshCount">How many are meshes as written.</param>
/// <param name="CompressedCount">How many are meshes inside a compressed wrapper.</param>
/// <param name="Damaged">The ones that are neither, in the order they were read.</param>
/// <param name="Unadvertised">The ones the manifest does not say are there.</param>
/// <param name="EmptyLevels">The levels the manifest says are there and that hold nothing.</param>
internal sealed record TerrainPyramidReport(
    JsonObject Manifest,
    int TileCount,
    long TileBytes,
    string Digest,
    int MeshCount,
    int CompressedCount,
    IReadOnlyList<string> Damaged,
    IReadOnlyList<string> Unadvertised,
    IReadOnlyList<int> EmptyLevels);

/// <summary>
/// Reads a pyramid off the disk and says what is wrong with it.
/// </summary>
/// <remarks>
/// Every tile is opened. That sounds expensive and is not: tiles are a few kilobytes each, the whole
/// of a region's worth is read once, and the alternative — trusting that a program which reports
/// success produced what it said it did — is the thing this entire step exists to stop doing. The
/// same read serves three purposes at once, which is why it is one walk: it says what each tile is,
/// it feeds the hash that becomes the pyramid's version, and it collects the addresses that are
/// checked against what the manifest advertises.
/// </remarks>
internal static class TerrainPyramidReader
{
    /// <summary>
    /// Everything worth knowing about a pyramid, or nothing at all if its manifest cannot be read.
    /// </summary>
    public static TerrainPyramidReport? Read(string tilesDirectory)
    {
        var manifest = ReadManifest(tilesDirectory);
        if (manifest is null)
        {
            return null;
        }

        var available = Advertised(manifest);
        var counts = new Dictionary<int, int>();
        var damaged = new List<string>();
        var unadvertised = new List<string>();
        var head = new byte[TerrainPyramidCheck.TileHeaderBytes];
        var meshes = 0;
        var compressed = 0;
        var tiles = 0;
        var bytes = 0L;

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var relative in TilePaths(tilesDirectory))
        {
            var path = Path.Combine(tilesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            var contents = File.ReadAllBytes(path);

            tiles++;
            bytes += contents.LongLength;

            // The tile's place is folded in as well as its contents, so that the answer depends on
            // what the pyramid holds and not on the order a directory happened to be read in — and
            // with the forward slash whatever separator the host uses, so the same pyramid gives the
            // same version on a developer's machine and inside a container.
            digest.AppendData(Encoding.UTF8.GetBytes(relative));
            digest.AppendData(contents);

            Array.Clear(head);
            contents.AsSpan(0, Math.Min(head.Length, contents.Length)).CopyTo(head);

            switch (TerrainPyramidCheck.Classify(head, contents.LongLength))
            {
                case TerrainTileKind.Mesh:
                    meshes++;
                    break;
                case TerrainTileKind.Compressed:
                    compressed++;
                    break;
                default:
                    damaged.Add(relative);
                    break;
            }

            if (!TerrainPyramidCheck.TryReadAddress(relative, out var address))
            {
                // A tile somewhere other than level, column, row. Nothing will ever ask for it,
                // which is the same failure as one advertised nowhere and is reported as one.
                unadvertised.Add(relative);
                continue;
            }

            counts[address.Level] = counts.GetValueOrDefault(address.Level) + 1;

            if (!TerrainPyramidCheck.IsAdvertised(available, address))
            {
                unadvertised.Add(relative);
            }
        }

        return new TerrainPyramidReport(
            manifest,
            tiles,
            bytes,
            Convert.ToHexStringLower(digest.GetHashAndReset()),
            meshes,
            compressed,
            damaged,
            unadvertised,
            TerrainPyramidCheck.LevelsAdvertisedAndEmpty(available, counts));
    }

    /// <summary>Every tile under a pyramid, by its place under the root, in a fixed order.</summary>
    private static IEnumerable<string> TilePaths(string tilesDirectory)
    {
        if (!Directory.Exists(tilesDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(tilesDirectory, "*" + TerrainPyramid.TileExtension, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(tilesDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// The manifest, or nothing if it is absent or is not one.
    /// </summary>
    /// <remarks>
    /// Unreadable and absent are one answer on purpose: both mean nothing can find a single tile in
    /// this directory, whatever else is in it, and both are fixed the same way.
    /// </remarks>
    private static JsonObject? ReadManifest(string tilesDirectory)
    {
        var path = Path.Combine(tilesDirectory, TerrainPyramid.ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
    private static IReadOnlyList<IReadOnlyList<TerrainTileRange>> Advertised(JsonObject manifest)
    {
        if (manifest["available"] is not JsonArray levels)
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

    private static int Ordinate(JsonObject range, string name) =>
        range[name] is { } value && value.GetValueKind() == JsonValueKind.Number
            ? value.GetValue<int>()
            : 0;
}
