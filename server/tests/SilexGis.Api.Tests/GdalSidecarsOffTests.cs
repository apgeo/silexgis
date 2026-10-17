// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The raster library writes nothing beside a raster it only reads, while the terrain chain is
/// reading it.
///
/// <para>
/// A terrain build reads an operator's directory of rasters where it lies, and that directory must
/// come back exactly as it was. The library's habit is to keep what it learned about a file in an
/// <c>.aux.xml</c> beside it, even for a file it opened read-only. The first test provokes exactly
/// that with the habit left on, so that the second — the same act with it switched off — proves
/// something rather than passing because the act never wrote anything.
/// </para>
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class GdalSidecarsOffTests : IDisposable
{
    private readonly string root = Path.Combine(
        TestScratch.Root, "silexgis-sidecars-" + Guid.NewGuid().ToString("N"));

    public GdalSidecarsOffTests()
    {
        Directory.CreateDirectory(root);
        GdalRuntime.Configure();
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

    [Fact]
    public void Left_to_itself_the_library_writes_a_sidecar_beside_a_raster_it_only_read()
    {
        var raster = Raster("left-on.tif");

        ReadStatistics(raster);

        File.Exists(raster + ".aux.xml").ShouldBeTrue(
            "the fixture no longer provokes a sidecar, so the test beside this one proves nothing");
    }

    [Fact]
    public void Held_off_the_same_read_leaves_the_directory_as_it_was_and_the_setting_is_restored()
    {
        var raster = Raster("held-off.tif");
        var before = Directory.GetFiles(root).Order(StringComparer.Ordinal).ToList();

        using (GdalSidecarsOff.OnThisThread())
        {
            ReadStatistics(raster);
        }

        Directory.GetFiles(root).Order(StringComparer.Ordinal).ShouldBe(before);
        Gdal.GetThreadLocalConfigOption("GDAL_PAM_ENABLED", null).ShouldBeNull();
    }

    [Fact]
    public void Preparing_a_raster_writes_only_where_it_was_asked_to()
    {
        var sources = Path.Combine(root, "operator");
        Directory.CreateDirectory(sources);
        var raster = Raster(Path.Combine("operator", "ground.tif"));
        var before = Snapshot(sources);

        var preparer = new GdalTerrainRasterPreparer();
        var output = Path.Combine(root, "prepared");
        var request = new TerrainRasterPrepareRequest(
            [raster], output, new TerrainArea(25.0, 46.0, 25.05, 46.05),
            Names: new Dictionary<string, string> { [raster] = "ground-1.tif" });

        preparer.Prepare(request, CancellationToken.None).Count.ShouldBe(1);
        preparer.DescribePrepared(request).ShouldNotBeNull();

        Snapshot(sources).ShouldBe(before);

        // Prepared under the name it was given rather than its own, which is how two rasters of one
        // name in two of an operator's folders are both kept.
        File.Exists(Path.Combine(output, "ground-1.tif")).ShouldBeTrue();
        File.Exists(Path.Combine(output, "ground.tif")).ShouldBeFalse();
    }

    private static List<string> Snapshot(string directory) =>
        [.. Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => $"{f}|{new FileInfo(f).Length}|{File.GetLastWriteTimeUtc(f).Ticks}")
            .Order(StringComparer.Ordinal)];

    private static void ReadStatistics(string raster)
    {
        using var dataset = Gdal.Open(raster, Access.GA_ReadOnly);
        dataset.GetRasterBand(1).ComputeStatistics(false, out _, out _, out _, out _, null, null);
    }

    private string Raster(string name)
    {
        var path = Path.Combine(root, name);
        using var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([25.0, 0.004, 0, 46.06, 0, -0.004]);

        using (var reference = new SpatialReference(""))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var pixels = Enumerable.Range(0, 256).Select(i => 300f + i).ToArray();
        dataset.GetRasterBand(1).WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
        dataset.FlushCache();
        return path;
    }
}
