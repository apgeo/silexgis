// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// An uploaded geofile or map raster is opened only by the drivers of the format it was accepted
/// as, and the raster library cannot reach the network at all.
///
/// <para>
/// The library picks a driver from a file's content, not from its name, and two of its drivers
/// read documents that name other files: a vector or raster "virtual" document (an XML file of a
/// few lines) can point at any file the server can read, or at an address only the server can
/// reach, and opening it reads that source as if it were the upload. Every hostile file here is
/// written by the test itself. Each refusal test first opens the same file the way the readers
/// used to — with every driver — and shows it really does read the file it names, so the refusal
/// beside it proves the restriction rather than a fixture that never worked.
/// </para>
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class UploadedGeodataDriverTests : IDisposable
{
    private readonly string root = Path.Combine(
        TestScratch.Root, "silexgis-drivers-" + Guid.NewGuid().ToString("N"));

    private readonly string marker = "server-only-" + Guid.NewGuid().ToString("N");

    public UploadedGeodataDriverTests()
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
    public void Every_driver_an_accepted_format_needs_is_in_the_bundled_build_and_so_is_the_one_refused()
    {
        foreach (var name in new[] { "GPX", "KML", "GeoJSON", "ESRI Shapefile", "CSV" })
        {
            Ogr.GetDriverByName(name).ShouldNotBeNull($"vector driver {name}");
        }

        foreach (var name in new[] { "GTiff", "COG" })
        {
            Gdal.GetDriverByName(name).ShouldNotBeNull($"raster driver {name}");
        }

        // The refusals below would prove nothing in a build that never had these.
        Ogr.GetDriverByName("OGR_VRT").ShouldNotBeNull();
        Gdal.GetDriverByName("VRT").ShouldNotBeNull();
    }

    [Theory]
    [InlineData(GeofileFormat.Gpx, "track.gpx")]
    [InlineData(GeofileFormat.Kml, "places.kml")]
    [InlineData(GeofileFormat.GeoJson, "features.geojson")]
    public void A_vector_virtual_document_under_an_accepted_name_is_refused(GeofileFormat format, string name)
    {
        var upload = Path.Combine(root, name);
        File.WriteAllText(upload, VectorVirtualDocument(ServerOnlyTable()));

        ReadsTheNamedFileWithEveryDriver(upload).ShouldBeTrue("the fixture no longer reads the file it names");

        Should.Throw<VectorIOException>(() => new GdalVectorIO().Read(upload, format));
    }

    [Fact]
    public void A_vector_virtual_document_inside_a_kmz_is_refused()
    {
        var upload = Path.Combine(root, "places.kmz");
        Zip(upload, ("doc.kml", VectorVirtualDocument(ServerOnlyTable())));

        ReadsTheNamedFileWithEveryDriver("/vsizip/" + upload.Replace('\\', '/') + "/doc.kml")
            .ShouldBeTrue("the fixture no longer reads the file it names");

        Should.Throw<VectorIOException>(() => new GdalVectorIO().Read(upload, GeofileFormat.Kmz));
    }

    [Fact]
    public void A_vector_virtual_document_inside_a_shapefile_archive_is_refused()
    {
        var upload = Path.Combine(root, "layer.zip");
        Zip(upload, ("layer.vrt", VectorVirtualDocument(ServerOnlyTable())));

        ReadsTheNamedFileWithEveryDriver("/vsizip/" + upload.Replace('\\', '/'))
            .ShouldBeTrue("the fixture no longer reads the file it names");

        Should.Throw<VectorIOException>(() => new GdalVectorIO().Read(upload, GeofileFormat.Shapefile));
    }

    [Fact]
    public void A_track_log_that_is_a_virtual_document_gives_no_times()
    {
        var upload = Path.Combine(root, "clock.gpx");
        File.WriteAllText(upload, VectorVirtualDocument(ServerOnlyTable(), layer: "track_points"));

        ReadsTheNamedFileWithEveryDriver(upload).ShouldBeTrue("the fixture no longer reads the file it names");

        Should.Throw<VectorIOException>(() => new GdalVectorIO().ReadTrackFixes(upload, GeofileFormat.Gpx));
    }

    [Fact]
    public void A_raster_virtual_document_named_as_a_geotiff_is_refused_while_a_real_geotiff_converts()
    {
        var serverOnly = GeoTiff(Path.Combine(root, "server-only.tif"));
        var upload = Path.Combine(root, "map.tif");
        File.WriteAllText(upload, RasterVirtualDocument(serverOnly));

        using (var everyDriver = Gdal.Open(upload, Access.GA_ReadOnly))
        {
            everyDriver.ShouldNotBeNull();
            everyDriver.GetDriver().ShortName.ShouldBe("VRT", "the fixture is no longer read as a virtual raster");
        }

        var service = new RasterCogService();
        Should.Throw<VectorIOException>(() => service.ConvertToCog(upload, Path.Combine(root, "out", "refused.tif")));
        File.Exists(Path.Combine(root, "out", "refused.tif")).ShouldBeFalse();

        service.ConvertToCog(serverOnly, Path.Combine(root, "out", "accepted.tif")).Width.ShouldBe(16);
    }

    [Theory]
    [InlineData("/vsicurl/")]
    [InlineData("/vsicurl_streaming/")]
    public void The_library_reaches_no_address_through_its_network_file_systems(string prefix)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        var contacted = Listen(listener);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var address = $"{prefix}http://127.0.0.1:{port}/map.tif";
        Gdal.PushErrorHandler("CPLQuietErrorHandler");
        try
        {
            using var dataset = OpenQuietly(address);
            dataset.ShouldBeNull();
        }
        finally
        {
            Gdal.PopErrorHandler();
        }

        // A connection the library made is accepted by the time it gave up on it; a short grace
        // covers the accept loop being scheduled after the library's own failure.
        Thread.Sleep(200);
        contacted.Count.ShouldBe(0, $"the library connected to the address behind {prefix}");
    }

    /// <summary>
    /// GeoJSON may name its coordinate system by an address to fetch the definition from, and
    /// the library fetches it through its own HTTP client rather than through a file system, so
    /// switching off the network file systems alone does not stop it. Such a file is refused, and
    /// the server must not have contacted the address it names.
    /// </summary>
    [Theory]
    [InlineData("link", "href")]
    [InlineData("url", "url")]
    [InlineData("name", "name")]
    public void A_geojson_naming_its_coordinate_system_by_address_makes_no_request(string type, string member)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        var contacted = Listen(listener);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var upload = Path.Combine(root, "features.geojson");
        File.WriteAllText(
            upload,
            "{\"type\":\"FeatureCollection\","
            + $"\"crs\":{{\"type\":\"{type}\",\"properties\":{{\"{member}\":\"http://127.0.0.1:{port}/crs\",\"type\":\"proj4\"}}}},"
            + "\"features\":[{\"type\":\"Feature\",\"properties\":{},"
            + "\"geometry\":{\"type\":\"Point\",\"coordinates\":[25.1,46.1]}}]}");

        // The same file naming its system the ordinary way is an ordinary upload, so a refusal
        // below is about the address and not about the fixture.
        var plain = Path.Combine(root, "plain.geojson");
        File.WriteAllText(
            plain,
            "{\"type\":\"FeatureCollection\","
            + "\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}},"
            + "\"features\":[{\"type\":\"Feature\",\"properties\":{},"
            + "\"geometry\":{\"type\":\"Point\",\"coordinates\":[25.1,46.1]}}]}");
        new GdalVectorIO().Read(plain, GeofileFormat.GeoJson).Features.Count.ShouldBe(1);

        Should.Throw<VectorIOException>(() => new GdalVectorIO().Read(upload, GeofileFormat.GeoJson))
            .Message.ShouldContain("address");

        Thread.Sleep(200);
        contacted.Count.ShouldBe(0, "reading the upload fetched the address its coordinate system names");
    }

    /// <summary>Counts every connection made to the listener until it stops.</summary>
    private static ConnectionCount Listen(TcpListener listener)
    {
        listener.Start();
        var contacted = new ConnectionCount();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    contacted.Add();
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The listener stopped with the test.
            }
        });

        return contacted;
    }

    private sealed class ConnectionCount
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Add() => Interlocked.Increment(ref count);
    }

    private static Dataset? OpenQuietly(string path)
    {
        try
        {
            return Gdal.OpenEx(path, (uint)(GdalConst.OF_RASTER | GdalConst.OF_READONLY), null, null, null);
        }
        catch (ApplicationException)
        {
            return null;
        }
    }

    /// <summary>Opens a file as the readers used to, with every driver, and says whether the marker came back.</summary>
    private bool ReadsTheNamedFileWithEveryDriver(string path)
    {
        using var dataSource = Ogr.Open(path, 0);
        if (dataSource is null)
        {
            return false;
        }

        for (var i = 0; i < dataSource.GetLayerCount(); i++)
        {
            var layer = dataSource.GetLayerByIndex(i);
            layer.ResetReading();
            for (var feature = layer.GetNextFeature(); feature is not null; feature = layer.GetNextFeature())
            {
                using (feature)
                {
                    for (var f = 0; f < feature.GetFieldCount(); f++)
                    {
                        if (feature.GetFieldAsString(f) == marker)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>A table standing in for a file the server can read and the uploader must not.</summary>
    private string ServerOnlyTable()
    {
        var path = Path.Combine(root, "server-only.csv");
        File.WriteAllText(path, $"x,y,secret\n25.1,46.1,{marker}\n");
        return path;
    }

    private static string VectorVirtualDocument(string source, string layer = "tracks") =>
        $"""
        <OGRVRTDataSource>
          <OGRVRTLayer name="{layer}">
            <SrcDataSource relativeToVRT="0">{source}</SrcDataSource>
            <SrcLayer>server-only</SrcLayer>
            <GeometryType>wkbPoint</GeometryType>
            <LayerSRS>EPSG:4326</LayerSRS>
            <GeometryField encoding="PointFromColumns" x="x" y="y"/>
          </OGRVRTLayer>
        </OGRVRTDataSource>
        """;

    private static string RasterVirtualDocument(string source) =>
        $"""
        <VRTDataset rasterXSize="16" rasterYSize="16">
          <SRS>EPSG:4326</SRS>
          <GeoTransform>25.0, 0.004, 0, 46.06, 0, -0.004</GeoTransform>
          <VRTRasterBand dataType="Byte" band="1">
            <SimpleSource>
              <SourceFilename relativeToVRT="0">{source}</SourceFilename>
              <SourceBand>1</SourceBand>
            </SimpleSource>
          </VRTRasterBand>
        </VRTDataset>
        """;

    private static void Zip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private static string GeoTiff(string path)
    {
        using (var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Byte, null))
        {
            dataset.SetGeoTransform([25.0, 0.004, 0, 46.06, 0, -0.004]);
            using (var reference = new SpatialReference(""))
            {
                reference.ImportFromEPSG(4326);
                reference.ExportToWkt(out var wkt, null);
                dataset.SetProjection(wkt);
            }

            var pixels = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            dataset.GetRasterBand(1).WriteRaster(0, 0, 16, 16, pixels, 16, 16, 0, 0);
            dataset.FlushCache();
        }

        return path;
    }
}
