// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Globalization;

namespace SilexGis.Domain.Terrain;

/// <summary>What a file in a pyramid turned out to be.</summary>
public enum TerrainTileKind
{
    /// <summary>Not a tile a browser could draw: truncated, empty, or something else entirely.</summary>
    Damaged = 0,

    /// <summary>A mesh, as it is written.</summary>
    Mesh = 1,

    /// <summary>A mesh inside a compressed wrapper.</summary>
    Compressed = 2,
}

/// <summary>Where one tile sits: its level, and its column and row within that level.</summary>
public readonly record struct TerrainTileAddress(int Level, int X, int Y);

/// <summary>
/// A rectangle of tiles a pyramid says it holds at one level.
/// </summary>
/// <remarks>
/// A level carries a list of these rather than one rectangle, and that is the whole reason this
/// pipeline hands the tile-maker separate rasters instead of one merged sheet: two fine surveys
/// either side of a coarse region are advertised as two rectangles, and a viewer therefore never
/// asks for the deep tiles in the gap between them.
/// </remarks>
public sealed record TerrainTileRange(int StartX, int StartY, int EndX, int EndY)
{
    /// <summary>Whether this rectangle covers a given column and row.</summary>
    public bool Holds(int x, int y) => Holds(x, y, 0);

    /// <summary>
    /// Whether this rectangle covers a given column and row once it has been grown outwards by a
    /// given number of tiles on every side.
    /// </summary>
    public bool Holds(int x, int y, int margin) =>
        x >= StartX - margin && x <= EndX + margin && y >= StartY - margin && y <= EndY + margin;
}

/// <summary>
/// Whether a pyramid on disk is one that can be drawn, decided from the bytes rather than from
/// anything the program that wrote it said about itself.
/// </summary>
/// <remarks>
/// Every failure this catches is invisible on a screen. A pyramid with damaged tiles, with a level
/// it advertises and has nothing at, or with tiles it holds and advertises nowhere, is answered with
/// a smooth plausible globe and no error at all — in the browser, in the server's log, anywhere. So
/// the checks are made here, once, before anything is allowed to call a build finished.
/// </remarks>
public static class TerrainPyramidCheck
{
    /// <summary>
    /// How much of a tile has to be read to know what it is: a fixed header and the vertex count
    /// after it, which is also the smallest a mesh can possibly be.
    /// </summary>
    public const int TileHeaderBytes = 92;

    /// <summary>Three coordinates a vertex, two bytes each, immediately after the count.</summary>
    private const int BytesPerVertex = 6;

    /// <summary>More vertices than any tile carries; a fragment read as a count usually exceeds it.</summary>
    private const int VertexCountBound = 1 << 22;

    /// <summary>Comfortably outside the earth, for a coordinate measured from its centre.</summary>
    private const double EarthRadiusBoundMetres = 6.6e6;

    /// <summary>Generously outside the range the earth's own surface occupies.</summary>
    private const double ElevationBoundMetres = 15_000;

    /// <summary>
    /// How far outside an advertised rectangle a tile may sit and still be accounted for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured rather than chosen. The tile-maker writes a ring one tile wide around every
    /// rectangle it advertises, at every level: a pyramid whose manifest says columns 1154 to 1157
    /// and rows 767 to 770 has tiles for columns 1153 to 1158 and rows 766 to 771 on disk, and the
    /// same holds at every other level of the same pyramid and in every pyramid it has produced.
    /// Those are the neighbouring tiles the mesher needed in order to make the edges of the
    /// advertised area join up with the ground beside them; leaving them written costs a few
    /// kilobytes and nothing ever asks for them. A containment test taken literally therefore
    /// refuses every pyramid this pipeline is able to produce — reproducibly, on every bake — which
    /// is a check that condemns good work rather than one that catches bad.
    /// </para>
    /// <para>
    /// One tile and no more, deliberately. The failure this check exists for is a pyramid added to
    /// in place, whose manifest comes back describing only the newest raster and quietly stops
    /// advertising everything that was there before: that leaves whole rectangles outside what is
    /// advertised, an island's worth of tiles away, and a margin of one still catches every one of
    /// them.
    /// </para>
    /// </remarks>
    public const int AdvertisedMarginTiles = 1;

    /// <summary>The first two bytes of a compressed stream.</summary>
    private const byte CompressedFirstByte = 0x1f;

    /// <inheritdoc cref="CompressedFirstByte"/>
    private const byte CompressedSecondByte = 0x8b;

    /// <summary>
    /// What a file is, from its opening bytes and its length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious test — does it begin with the two bytes a compressed stream begins with — is not
    /// good enough, and the reason is worth writing down because getting it wrong produces a failure
    /// that reads as a broken pyramid rather than as a broken test. A mesh has no marker of its own
    /// at the front: it begins with the tile's centre as a little-endian double, whose low two bytes
    /// are essentially random. So about one tile in every sixty-five thousand begins with those two
    /// bytes by coincidence, which over a region's worth of tiles is a near certainty — and the
    /// consequence would be to call that pyramid mixed and refuse it, or worse to conclude the whole
    /// directory is compressed and have it served under a header describing bytes that are not
    /// there. Making it again does not help: those bytes are a function of where the tile is, so the
    /// same coincidence comes back every time, for ever, on that one tile.
    /// </para>
    /// <para>
    /// So a mesh is identified positively instead, from the numbers in its header that have to be
    /// what they are — a centre inside the earth, two heights within the range the earth's surface
    /// occupies with the lower not above the higher, and a vertex count the file is long enough to
    /// hold. A compressed stream read that way is a point some unimaginable distance from anywhere;
    /// a run of zeros left behind by a disk that filled declares no vertices at all; and a tile cut
    /// off part way through declares more than are there. Only once all of that has failed is the
    /// compressed test reached, and it asks for the whole fixed header rather than for two bytes.
    /// </para>
    /// </remarks>
    public static TerrainTileKind Classify(ReadOnlySpan<byte> head, long sizeBytes)
    {
        if (sizeBytes < TileHeaderBytes || head.Length < TileHeaderBytes)
        {
            return TerrainTileKind.Damaged;
        }

        if (LooksLikeMesh(head, sizeBytes))
        {
            return TerrainTileKind.Mesh;
        }

        return LooksCompressed(head) ? TerrainTileKind.Compressed : TerrainTileKind.Damaged;
    }

    /// <summary>
    /// The levels a pyramid says it holds tiles at and has none of on disk.
    /// </summary>
    /// <remarks>
    /// A level that advertises rectangles and produced no files is a run that stopped part way —
    /// the shape a machine that ran out of disk leaves behind. Nothing else notices it: the rest of
    /// the pyramid is perfectly readable, every tile in it is valid, and a viewer asking for one of
    /// the missing tiles is answered with nothing and quietly draws the coarser tile above it
    /// instead. Ground at the wrong resolution, presented as ground.
    /// <para>
    /// Only "advertised and entirely absent" is reported. Counting how many tiles each rectangle
    /// implies and comparing would be stricter, and would also condemn a pyramid whose maker
    /// advertises its rectangles optimistically — which is not a judgement this check is in a
    /// position to make.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> LevelsAdvertisedAndEmpty(
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> available,
        IReadOnlyDictionary<int, int> tileCounts)
    {
        var empty = new List<int>();

        for (var level = 0; level < available.Count; level++)
        {
            if (available[level].Count > 0
                && (!tileCounts.TryGetValue(level, out var count) || count == 0))
            {
                empty.Add(level);
            }
        }

        return empty;
    }

    /// <summary>
    /// Whether a tile that exists is one the manifest says exists.
    /// </summary>
    /// <remarks>
    /// The opposite direction to a level advertised and empty, and the one that matters most,
    /// because it is the single shape that passes every other check and draws nothing. Tiles on disk
    /// that the manifest does not advertise are never asked for: a viewer reads the manifest, sees
    /// no coverage there, and does not send the request. The ground is missing and the pyramid looks
    /// perfect. It is the exact failure of adding a raster to a finished pyramid in place — the
    /// tiles are written correctly and the manifest comes back describing only the new raster —
    /// and it costs nothing to check, because every tile's path is already being read.
    ///
    /// <para>
    /// Asked of each rectangle grown by <see cref="AdvertisedMarginTiles"/>, because the tile-maker
    /// always writes a ring that wide around what it advertises and a build refused for that would
    /// be every build.
    /// </para>
    /// </remarks>
    public static bool IsAdvertised(
        IReadOnlyList<IReadOnlyList<TerrainTileRange>> available, TerrainTileAddress tile)
    {
        if (tile.Level < 0 || tile.Level >= available.Count)
        {
            return false;
        }

        foreach (var range in available[tile.Level])
        {
            if (range.Holds(tile.X, tile.Y, AdvertisedMarginTiles))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a tile's place in the pyramid from where it sits under the pyramid's root.
    /// </summary>
    /// <remarks>
    /// Written with the forward slash whatever separator the host uses, so that the same pyramid
    /// gives the same answers on a developer's machine and inside a container.
    /// </remarks>
    public static bool TryReadAddress(string relativePath, out TerrainTileAddress address)
    {
        address = default;

        var parts = relativePath.Split('/');
        if (parts.Length != 3)
        {
            return false;
        }

        var name = parts[2];
        if (!name.EndsWith(TerrainPyramid.TileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var row = name[..^TerrainPyramid.TileExtension.Length];

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var column)
            || !int.TryParse(row, NumberStyles.None, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        address = new TerrainTileAddress(level, column, y);
        return true;
    }

    /// <summary>
    /// The version a pyramid publishes for itself, from a digest of the tiles it actually holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a cache key and it has to be one. Every tile a viewer asks for carries this string on
    /// the end of its address, and tiles are worth caching for a long time. The tile-maker writes a
    /// constant there — the same one in every pyramid it has ever produced — which makes two
    /// different pyramids answer at byte-identical addresses, and re-making a pyramid is an ordinary
    /// thing to do. Measured rather than reasoned about: a pyramid remade with every height changed
    /// was served entirely out of a browser's own cache, zero bytes over the network, and would have
    /// been for a week.
    /// </para>
    /// <para>
    /// Taken from the tiles rather than from the clock so that making the same pyramid twice leaves
    /// caches warm, while a change to any tile changes every address. Shaped like a version because
    /// that is nominally what the field is for; nothing reads it as one.
    /// </para>
    /// </remarks>
    public static string VersionFrom(string digestHex) =>
        "1.1.0-" + (digestHex.Length <= 12 ? digestHex : digestHex[..12]);

    /// <summary>
    /// Whether a value in a manifest is the tile-maker's own filler rather than a fact.
    /// </summary>
    /// <remarks>
    /// It writes the words "insert attribution here" into every pyramid it produces, and a scene
    /// that shows its terrain's credit shows exactly that. Filler is worse than nothing, because it
    /// is displayed.
    /// </remarks>
    public static bool IsFiller(string? value) =>
        value is null || value.StartsWith("insert ", StringComparison.Ordinal);

    /// <summary>
    /// The one credit a pyramid carries, from the credits of everything that went into it.
    /// </summary>
    /// <remarks>
    /// Composed from what this build was actually made from, never from a setting: a constant would
    /// name whichever dataset this was first written against, and would keep naming it in pyramids
    /// baked from something else entirely. Repeats are folded together because ten cells of one
    /// dataset are one credit, and the order the sources were recorded in is kept, so the same build
    /// credits its data the same way every time it is checked.
    /// </remarks>
    public static string? CreditFrom(IEnumerable<string?> attributions)
    {
        var kept = new List<string>();

        foreach (var attribution in attributions)
        {
            var trimmed = attribution?.Trim();
            if (string.IsNullOrEmpty(trimmed) || kept.Contains(trimmed, StringComparer.Ordinal))
            {
                continue;
            }

            kept.Add(trimmed);
        }

        return kept.Count == 0 ? null : string.Join("; ", kept);
    }

    private static bool LooksLikeMesh(ReadOnlySpan<byte> head, long sizeBytes)
    {
        for (var offset = 0; offset < 24; offset += 8)
        {
            var ordinate = BinaryPrimitives.ReadDoubleLittleEndian(head[offset..]);
            if (!double.IsFinite(ordinate) || Math.Abs(ordinate) > EarthRadiusBoundMetres)
            {
                return false;
            }
        }

        var lowest = BinaryPrimitives.ReadSingleLittleEndian(head[24..]);
        var highest = BinaryPrimitives.ReadSingleLittleEndian(head[28..]);

        if (!float.IsFinite(lowest)
            || !float.IsFinite(highest)
            || lowest < -ElevationBoundMetres
            || highest > ElevationBoundMetres
            || lowest > highest)
        {
            return false;
        }

        var vertices = BinaryPrimitives.ReadUInt32LittleEndian(head[88..]);

        return vertices > 0
            && vertices <= VertexCountBound
            && sizeBytes >= TileHeaderBytes + ((long)vertices * BytesPerVertex);
    }

    private static bool LooksCompressed(ReadOnlySpan<byte> head) =>
        head[0] == CompressedFirstByte
        && head[1] == CompressedSecondByte

        // The one compression method the format has ever defined, and the top three flag bits are
        // reserved and have to be clear. Two coincidental bytes rarely survive both.
        && head[2] == 0x08
        && (head[3] & 0xe0) == 0;
}
