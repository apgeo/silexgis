// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using MaxRev.Gdal.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OSGeo.GDAL;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// The drawing and warping services on their own, with no database: what a document's page is
/// drawn into, how a placement publishes it, and what each installation setting changes.
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class MapSheetRenderingTests(ITestOutputHelper output) : IDisposable
{
    private readonly string work = Directory.CreateDirectory(
        Path.Combine(TestScratch.Root, $"silexgis-sheet-render-{Guid.NewGuid():N}")).FullName;

    private static readonly ICrsRegistry Crs = new ProjCrsRegistry();

    [Fact]
    public void A_georeferenced_page_is_drawn_and_published_with_its_own_place()
    {
        var pdf = Write("geo.pdf", PdfSheetFixtures.GeoPdf(3844, 500000, 500000, 0.5));
        var options = new GeoreferencedMapOptions();
        var clock = Stopwatch.StartNew();

        var drawn = new PdfSheetRenderService().Render(new SheetRenderRequest(pdf, 1, null, null, Dir("render")), options, default);
        output.WriteLine($"drawn {drawn.Width}x{drawn.Height} at {drawn.Dpi:0} dpi in {clock.ElapsedMilliseconds} ms");
        drawn.Info.Embedded.ShouldNotBeNull();
        drawn.Dpi.ShouldBe(options.MaxAutoDpi);
        drawn.Warnings.ShouldBeNull();

        var placement = PdfSheetRenderService.EmbeddedPlacement(drawn.Info.Embedded!, drawn.Info.PointsWide, drawn.Info.PointsHigh);
        output.WriteLine($"placement crs {placement.Crs[..Math.Min(40, placement.Crs.Length)]}, mask {placement.Mask?.Count}");
        placement.Crs.ShouldBe("EPSG:3844");

        clock.Restart();
        var cog = Path.Combine(work, "placed.tif");
        var outcome = new MapWarpService(Crs).Publish(drawn.MasterPath, placement, cog, Dir("warp"), options, default);
        output.WriteLine($"published in {clock.ElapsedMilliseconds} ms, rms {outcome.RmsMetres}");
        outcome.Bbox4326.EnvelopeInternal.MinX.ShouldBe(25.0, 0.01);
        outcome.RmsMetres!.Value.ShouldBeLessThan(0.05);

        GdalBase.ConfigureAll();
        using var published = Gdal.Open(cog, Access.GA_ReadOnly);
        published.GetMetadataItem("COMPRESSION", "IMAGE_STRUCTURE").ShouldBe("WEBP");
    }

    [Fact]
    public void Deflate_is_used_when_the_installation_chooses_it()
    {
        var pdf = Write("plain.pdf", PdfSheetFixtures.PlainPdf());
        var options = new GeoreferencedMapOptions { CogCompression = "deflate", DefaultDpiUnplaced = 72 };
        var drawn = new PdfSheetRenderService().Render(new SheetRenderRequest(pdf, 1, null, null, Dir("render")), options, default);
        drawn.Info.Embedded.ShouldBeNull();
        drawn.Width.ShouldBe(400);

        var placement = new MapPlacement(1, MapTransformations.Affine, "EPSG:4326",
            [new(0, 0, 24, 45.5), new(1, 0, 24.01, 45.5), new(0, 1, 24, 45.49)], null);
        var cog = Path.Combine(work, "deflate.tif");
        new MapWarpService(Crs).Publish(drawn.MasterPath, placement, cog, Dir("warp"), options, default);

        GdalBase.ConfigureAll();
        using var published = Gdal.Open(cog, Access.GA_ReadOnly);
        published.GetMetadataItem("COMPRESSION", "IMAGE_STRUCTURE").ShouldBe("DEFLATE");
    }

    [Fact]
    public void An_outline_makes_the_rest_of_the_sheet_transparent()
    {
        var pdf = Write("masked.pdf", PdfSheetFixtures.PlainPdf());
        var options = new GeoreferencedMapOptions { DefaultDpiUnplaced = 72 };
        var drawn = new PdfSheetRenderService().Render(new SheetRenderRequest(pdf, 1, null, null, Dir("render")), options, default);
        var placement = new MapPlacement(1, MapTransformations.Affine, "EPSG:4326",
            [new(0, 0, 24, 45.5), new(1, 0, 24.01, 45.5), new(0, 1, 24, 45.49)],
            [[0.0, 0.0], [0.5, 0.0], [0.5, 1.0], [0.0, 1.0]]);
        var preview = new MapWarpService(Crs).Preview(drawn.PreviewPath, placement, Dir("preview"), 512);
        preview.Png.Length.ShouldBeGreaterThan(0);

        // Half the picture kept, so roughly half the published pixels are see-through.
        var png = Path.Combine(work, "masked.png");
        File.WriteAllBytes(png, preview.Png);
        GdalBase.ConfigureAll();
        using var picture = Gdal.Open(png, Access.GA_ReadOnly);
        picture.RasterCount.ShouldBe(4);
        picture.GetRasterBand(4).ComputeStatistics(false, out _, out _, out var mean, out _, null, null);
        mean.ShouldBeInRange(255 * 0.3, 255 * 0.7);
    }

    [Fact]
    public void Every_setting_out_of_range_refuses_start_up_naming_the_setting()
    {
        var validator = new GeoreferencedMapOptionsValidator();
        validator.Validate(null, new GeoreferencedMapOptions()).Succeeded.ShouldBeTrue();

        (string Name, Action<GeoreferencedMapOptions> Break)[] cases =
        [
            ("CogCompression", o => o.CogCompression = "jpeg"),
            ("CogWebpQuality", o => o.CogWebpQuality = 0),
            ("CogDeflateLevel", o => o.CogDeflateLevel = 10),
            ("DefaultDpiUnplaced", o => o.DefaultDpiUnplaced = 10),
            ("TargetGroundResolutionMetres", o => o.TargetGroundResolutionMetres = 0),
            ("MinDpi", o => o.MinDpi = 700),
            ("MaxDpi", o => o.MaxDpi = 5000),
            ("PixelBudgetMegapixels", o => o.PixelBudgetMegapixels = 1),
            ("RenderBudgetMegabytes", o => o.RenderBudgetMegabytes = 1),
            ("RenderTimeLimitMinutes", o => o.RenderTimeLimitMinutes = 0),
            ("PreviewMaxPixels", o => o.PreviewMaxPixels = 10),
            ("PreviewConcurrency", o => o.PreviewConcurrency = 0),
            ("MaxControlPoints", o => o.MaxControlPoints = 1),
            ("MaxAutoDpi", o => o.MaxAutoDpi = 10),
            ("PlacementCrsCodes", o => o.PlacementCrsCodes = [0]),
        ];
        foreach (var (name, breakIt) in cases)
        {
            var options = new GeoreferencedMapOptions();
            breakIt(options);
            var result = validator.Validate(null, options);
            result.Failed.ShouldBeTrue(name);
            result.FailureMessage.ShouldContain(name);
        }
    }

    [Fact]
    public void Sheet_jobs_move_to_the_heavy_lane_only_when_the_installation_says_so()
    {
        var off = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var on = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GeoreferencedMaps:SheetJobsOnHeavyLane"] = "true" })
            .Build();
        SilexGis.Infrastructure.Jobs.JobLane.HeavyKinds(off).ShouldNotContain(SilexGis.Domain.Entities.ProcessingJobKinds.MapSheetRender);
        SilexGis.Infrastructure.Jobs.JobLane.HeavyKinds(on).ShouldContain(SilexGis.Domain.Entities.ProcessingJobKinds.MapSheetRender);
        SilexGis.Infrastructure.Jobs.JobLane.HeavyKinds(on).ShouldContain(SilexGis.Domain.Entities.ProcessingJobKinds.MapPlacementWarp);
        SilexGis.Infrastructure.Jobs.JobLane.HeavyKinds(on).ShouldContain(SilexGis.Domain.Entities.ProcessingJobKinds.TerrainBuild);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(work, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Dir(string name) => Directory.CreateDirectory(Path.Combine(work, name)).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException)
        {
            // Scratch; best effort.
        }
    }
}
