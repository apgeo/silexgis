// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OSGeo.GDAL;
using Shouldly;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Storing an elevation tile, whose position is written in its file name and nowhere else.
///
/// <para>
/// The store issues every name it uses, because a name somebody outside chose is a path. For every
/// other raster that costs nothing — the raster library identifies those by their contents. A
/// <c>.hgt</c> is the exception: it is a headerless square of samples that the library places by
/// matching the file's name, so a name of the store's own leaves it not merely mis-placed but
/// unreadable, reported minutes later and in another step as a file that is not elevation data.
/// </para>
///
/// <para>
/// So the assertions here are about coordinates, not about the absence of an exception. A tile
/// stored under the wrong corner produces a raster that opens, draws and meshes, and there is
/// nothing downstream that could notice.
/// </para>
/// </summary>
public sealed class TerrainTileUploadTests : IDisposable
{
    /// <summary>
    /// The number of samples along the side of a three-arc-second tile. The format states its width
    /// nowhere: the library works it out from the file's length, so a test tile has to be one of the
    /// real sizes and this is the smallest.
    /// </summary>
    private const int Side = 1201;

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "silexgis-tile-upload-" + Guid.NewGuid().ToString("N"));

    private readonly TerrainUploads uploads;

    public TerrainTileUploadTests()
    {
        Directory.CreateDirectory(root);
        uploads = new TerrainUploads(new TerrainWorkspace(
            Options.Create(new TerrainBuildOptions
            {
                BuildRoot = Path.Combine(root, "builds"),
                SpoolRoot = Path.Combine(root, "spool"),
                PublishRoot = Path.Combine(root, "published"),
            }),
            NullLogger<TerrainWorkspace>.Instance));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that outlives the run is not worth failing a test over.
        }
    }

    /// <remarks>
    /// The heart of it: the corner the uploader's name described has to survive into a file stored
    /// under a name that no longer describes anything.
    /// </remarks>
    [Fact]
    public async Task A_tile_keeps_the_ground_its_name_described_after_the_name_is_gone()
    {
        var stored = await uploads.SaveAsync(Tile(), "N45E024.hgt", CancellationToken.None);

        var path = uploads.PathOf(stored.Reference).ShouldNotBeNull();

        GdalBase.ConfigureAll();
        using var raster = Gdal.Open(path, Access.GA_ReadOnly);

        var grid = new double[6];
        raster.GetGeoTransform(grid);

        // The tile's samples sit on their corners, so the outer half-pixel of the grid falls outside
        // the square the name describes. Compared against the degree it covers, to a tolerance well
        // under one pixel.
        grid[0].ShouldBe(24.0, 0.001);
        grid[3].ShouldBe(46.0, 0.001);
        (grid[0] + (grid[1] * raster.RasterXSize)).ShouldBe(25.0, 0.001);
        (grid[3] + (grid[5] * raster.RasterYSize)).ShouldBe(45.0, 0.001);

        raster.RasterXSize.ShouldBe(Side);
        raster.RasterYSize.ShouldBe(Side);
    }

    /// <remarks>
    /// The whole point of converting on the way in: what comes out is identified by its contents, so
    /// the pipeline that reads it never has to know the name it arrived under.
    /// </remarks>
    [Fact]
    public async Task What_is_stored_is_a_raster_that_states_its_own_position()
    {
        var stored = await uploads.SaveAsync(Tile(), "N45E024.hgt", CancellationToken.None);

        Path.GetExtension(stored.Reference).ShouldBe(".tif");
        stored.SizeBytes.ShouldBeGreaterThan(0);
        TerrainRasterFiles.IsRaster(stored.Reference).ShouldBeTrue();

        var path = uploads.PathOf(stored.Reference).ShouldNotBeNull();
        new FileInfo(path).Length.ShouldBe(stored.SizeBytes);

        GdalBase.ConfigureAll();
        using var raster = Gdal.Open(path, Access.GA_ReadOnly);
        raster.GetProjection().ShouldNotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// The elevations themselves, because a conversion that placed the tile correctly and wrote the
    /// wrong samples into it would pass every other assertion here.
    /// </remarks>
    [Fact]
    public async Task The_samples_are_the_ones_that_were_sent()
    {
        var stored = await uploads.SaveAsync(Tile(), "N45E024.hgt", CancellationToken.None);
        var path = uploads.PathOf(stored.Reference).ShouldNotBeNull();

        GdalBase.ConfigureAll();
        using var raster = Gdal.Open(path, Access.GA_ReadOnly);

        var read = new short[4];
        raster.GetRasterBand(1).ReadRaster(0, 0, 2, 2, read, 2, 2, 0, 0);

        read[0].ShouldBe(Sample(0, 0));
        read[1].ShouldBe(Sample(1, 0));
        read[2].ShouldBe(Sample(0, 1));
        read[3].ShouldBe(Sample(1, 1));
    }

    /// <remarks>
    /// The store's own rule, which the conversion must not have quietly become an exception to: the
    /// name is a key this application invented, and nothing an uploader chose is any part of it.
    /// </remarks>
    [Theory]
    [InlineData("N45E024.hgt")]
    [InlineData("N45E025.SRTMGL1.hgt")]
    public async Task No_part_of_the_uploader_s_name_becomes_part_of_a_path(string sent)
    {
        var stored = await uploads.SaveAsync(Tile(), sent, CancellationToken.None);

        var stem = Path.GetFileNameWithoutExtension(stored.Reference);
        stem.Length.ShouldBe(32);
        stem.ShouldAllBe(c => char.IsAsciiHexDigitLower(c));
        stored.Reference.ShouldNotContain("N45", Case.Insensitive);

        uploads.PathOf(stored.Reference).ShouldNotBeNull();
    }

    /// <remarks>
    /// Two people sending the same square must not write over one another, and neither may leave
    /// anything behind: the working directory is named so that nothing can read one as an upload,
    /// but a store that accumulates them is still a store filling a disk.
    /// </remarks>
    [Fact]
    public async Task The_same_square_sent_twice_is_stored_twice_and_leaves_nothing_behind()
    {
        var first = await uploads.SaveAsync(Tile(), "N45E024.hgt", CancellationToken.None);
        var second = await uploads.SaveAsync(Tile(), "N45E024.hgt", CancellationToken.None);

        second.Reference.ShouldNotBe(first.Reference);
        uploads.PathOf(first.Reference).ShouldNotBeNull();
        uploads.PathOf(second.Reference).ShouldNotBeNull();

        Directory.GetDirectories(uploads.Directory).ShouldBeEmpty();
        Directory.GetFiles(uploads.Directory).Length.ShouldBe(2);
    }

    [Fact]
    public async Task A_name_that_describes_no_square_is_refused()
    {
        var refused = await Should.ThrowAsync<InvalidDataException>(
            () => uploads.SaveAsync(Tile(), "elevation.hgt", CancellationToken.None));

        refused.Message.ShouldContain("N45E024.hgt");
        Directory.Exists(uploads.Directory).ShouldBeTrue();
        Directory.GetFileSystemEntries(uploads.Directory).ShouldBeEmpty();
    }

    /// <remarks>
    /// What a transfer cut short looks like: the name is a square and the length is not one, which
    /// the library reports by declining to open the file at all.
    /// </remarks>
    [Fact]
    public async Task A_tile_whose_length_is_not_a_square_is_refused_and_stored_nowhere()
    {
        using var truncated = new MemoryStream(new byte[Side * 2]);

        await Should.ThrowAsync<InvalidDataException>(
            () => uploads.SaveAsync(truncated, "N45E024.hgt", CancellationToken.None));

        Directory.GetFileSystemEntries(uploads.Directory).ShouldBeEmpty();
    }

    /// <remarks>
    /// Everything that is not a tile goes on being stored byte for byte under the extension it
    /// arrived with. The conversion is for the one format that cannot survive being renamed.
    /// </remarks>
    [Fact]
    public async Task A_raster_that_carries_its_own_position_is_stored_untouched()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var content = new MemoryStream(bytes);

        var stored = await uploads.SaveAsync(content, "survey.TIF", CancellationToken.None);

        Path.GetExtension(stored.Reference).ShouldBe(".tif");
        stored.SizeBytes.ShouldBe(bytes.Length);

        var path = uploads.PathOf(stored.Reference).ShouldNotBeNull();
        File.ReadAllBytes(path).ShouldBe(bytes);
    }

    /// <summary>
    /// A tile as it arrives: a headerless square of big-endian 16-bit samples, and nothing else.
    /// </summary>
    private static MemoryStream Tile()
    {
        var bytes = new byte[Side * Side * 2];

        for (var row = 0; row < Side; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                var sample = Sample(column, row);
                var at = ((row * Side) + column) * 2;

                // Big-endian, which is what the format is and what the machine running this is not.
                bytes[at] = (byte)((sample >> 8) & 0xFF);
                bytes[at + 1] = (byte)(sample & 0xFF);
            }
        }

        return new MemoryStream(bytes);
    }

    /// <summary>Something that varies both ways, so a transposed or mirrored grid shows up.</summary>
    private static short Sample(int column, int row) => (short)(200 + (row * 2) + column);
}
