// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Where the raster library is allowed to put the scratch files it works through.
///
/// <para>
/// The cloud-optimised writer cannot know a raster's overviews until it has written the image once,
/// so it works in a scratch file and moves it into place at the end. Where that goes is one setting,
/// and its default is the library's own fallback: the process's current directory. On a developer's
/// machine that is writable and the whole question is invisible. In a container it is the directory
/// the application was copied into — owned by root, while the process runs as somebody else — so the
/// fallback is a refusal, reported as a permission error against a scratch name nobody recognises,
/// from a step that looks like it is reading rather than writing.
/// </para>
///
/// <para>
/// The tests below reproduce that on any machine by pointing the setting somewhere unusable and
/// asserting the work still finishes, which it can only do by overriding it.
/// </para>
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class GdalScratchDirectoryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "silexgis-scratch-" + Guid.NewGuid().ToString("N"));

    private readonly GdalTerrainRasterPreparer preparer = new();

    public GdalScratchDirectoryTests()
    {
        Directory.CreateDirectory(root);
        GdalRuntime.Configure();
    }

    public void Dispose()
    {
        Gdal.SetConfigOption("CPL_TMPDIR", Path.GetTempPath());

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
    public void Bringing_the_library_up_settles_somewhere_writable_to_work()
    {
        Gdal.GetConfigOption("CPL_TMPDIR", null).ShouldNotBeNullOrWhiteSpace();
        Directory.Exists(Gdal.GetConfigOption("CPL_TMPDIR", null)).ShouldBeTrue();
    }

    [Fact]
    public void A_scratch_directory_is_in_force_only_while_it_is_held()
    {
        var before = Gdal.GetConfigOption("CPL_TMPDIR", null);
        var chosen = Path.Combine(root, "chosen");
        Directory.CreateDirectory(chosen);

        using (GdalScratchDirectory.At(chosen))
        {
            Gdal.GetConfigOption("CPL_TMPDIR", null).ShouldBe(Path.GetFullPath(chosen));
        }

        // Restored rather than cleared: clearing would put back the library's own fallback, which is
        // the current directory and is the failure this exists to prevent.
        Gdal.GetConfigOption("CPL_TMPDIR", null).ShouldBe(before);
    }

    /// <remarks>
    /// The container's failure, made to happen anywhere: the setting names a directory that cannot
    /// be written, exactly as it names an unwritable one there. A conversion that honoured it would
    /// fail, and the whole build with it.
    /// </remarks>
    [Fact]
    public void A_build_converts_its_rasters_even_when_the_machine_offers_nowhere_to_work()
    {
        var source = Raster("survey.tif", 24.0, 46.0, 0.001, 64, 64);
        Gdal.SetConfigOption("CPL_TMPDIR", Path.Combine(root, "does-not-exist"));

        var prepared = preparer
            .Prepare(
                new TerrainRasterPrepareRequest(
                    [source],
                    Path.Combine(root, "prepared"),
                    new TerrainArea(24.01, 45.95, 24.05, 45.99)),
                CancellationToken.None)
            .ShouldHaveSingleItem();

        File.Exists(prepared.Path).ShouldBeTrue();
        new FileInfo(prepared.Path).Length.ShouldBeGreaterThan(0);
    }

    /// <remarks>
    /// Where the scratch file goes and not merely that it goes somewhere. It is the size of the
    /// output — gigabytes over any real amount of ground — so it belongs on the disk the operator
    /// sized for terrain, which is the one the finished raster is already going to.
    /// </remarks>
    [Fact]
    public void A_build_works_beside_what_it_is_writing_rather_than_wherever_the_machine_says()
    {
        var source = Raster("survey.tif", 24.0, 46.0, 0.001, 64, 64);
        var elsewhere = Path.Combine(root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Gdal.SetConfigOption("CPL_TMPDIR", elsewhere);

        // Watched rather than looked at afterwards: the library tidies its scratch file away when it
        // is finished, so a directory that is empty at the end is empty whether or not gigabytes
        // passed through it.
        var intruders = new List<string>();
        using var watcher = new FileSystemWatcher(elsewhere)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };
        watcher.Created += (_, e) =>
        {
            lock (intruders)
            {
                intruders.Add(e.Name ?? string.Empty);
            }
        };

        var output = Path.Combine(root, "prepared");
        preparer.Prepare(
            new TerrainRasterPrepareRequest(
                [source], output, new TerrainArea(24.01, 45.95, 24.05, 45.99)),
            CancellationToken.None);

        lock (intruders)
        {
            intruders.ShouldBeEmpty();
        }

        // And nothing of the working file is left beside the finished one either.
        Directory.GetFiles(output).Select(Path.GetFileName)
            .ShouldAllBe(name => !name!.Contains(TerrainRasterFiles.PartialSuffix));
    }

    /// <summary>A small placed raster, which is all these tests need one to be.</summary>
    private string Raster(string name, double originX, double originY, double pixel, int width, int height)
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(root, name);
        var driver = Gdal.GetDriverByName("GTiff");

        using var dataset = driver.Create(path, width, height, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([originX, pixel, 0, originY, 0, -pixel]);

        using (var reference = new SpatialReference(null))
        {
            reference.ImportFromEPSG(4326);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var samples = new float[width * height];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 500 + (i % 97);
        }

        dataset.GetRasterBand(1).WriteRaster(0, 0, width, height, samples, width, height, 0, 0);
        dataset.FlushCache();

        return path;
    }
}
