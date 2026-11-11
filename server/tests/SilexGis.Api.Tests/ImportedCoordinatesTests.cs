// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text.RegularExpressions;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// What an imported geofile or map raster is stored as is longitude and latitude, wherever it
/// came from: a file in a projected system lands where an independent projection puts it, and a
/// file whose coordinates cannot be degrees, or whose edge will not convert to them, is refused
/// rather than stored off the globe.
///
/// <para>
/// The reference positions were computed once by PostGIS, a different program from the library
/// these readers use, from the same coordinates and the same authority codes. Every file here is
/// written by the test itself.
/// </para>
/// </summary>
[Collection(RasterScratchCollection.Name)]
public sealed class ImportedCoordinatesTests : IDisposable
{
    private const double MetresPerDegree = 111_320;

    private readonly string root = Path.Combine(
        TestScratch.Root, "silexgis-coordinates-" + Guid.NewGuid().ToString("N"));

    public ImportedCoordinatesTests()
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

    /// <summary>
    /// Stereo 70 is the national grid most Romanian survey data is drawn in, and its datum is not
    /// WGS 84, so it exercises the datum shift as well as the projection; a UTM zone on WGS 84 is
    /// the control with no datum to shift. A swapped axis or a missed transformation puts a point
    /// hundreds of kilometres away, so a metre is a tolerance that separates the two cleanly.
    /// </summary>
    [Theory]
    [InlineData(3844, 500_000d, 500_000d, 24.998445775149445, 45.99971818665659)]
    [InlineData(3844, 585_000d, 512_000d, 26.09805944545341, 46.10243634382225)]
    [InlineData(32635, 400_000d, 5_100_000d, 25.707467964172864, 46.046265455836014)]
    public void A_shapefile_in_a_projected_system_lands_where_an_independent_projection_puts_it(
        int epsg, double x, double y, double longitude, double latitude)
    {
        using var reference = new SpatialReference("");
        reference.ImportFromEPSG(epsg);
        var archive = ShapefileArchive("points", reference, (x, y));

        var point = SinglePoint(archive);

        MetresBetween(point, longitude, latitude).ShouldBeLessThan(1.0, $"read as {point.X}, {point.Y}");
    }

    /// <summary>
    /// A .prj written by desktop software often carries no authority code, so the reader cannot
    /// look the system up by number and has to work from the definition itself.
    /// </summary>
    [Fact]
    public void A_shapefile_whose_projection_file_names_no_authority_lands_in_the_same_place()
    {
        using var reference = new SpatialReference("");
        reference.ImportFromEPSG(3844);
        reference.ExportToWkt(out var wkt, null);
        var anonymous = Regex.Replace(wkt, @",AUTHORITY\[""[^""]*"",""[^""]*""\]", string.Empty);
        anonymous.ShouldNotContain("AUTHORITY");

        var archive = ShapefileArchive("points", reference, (585_000d, 512_000d), projectionFile: anonymous);

        var point = SinglePoint(archive);

        MetresBetween(point, 26.09805944545341, 46.10243634382225)
            .ShouldBeLessThan(1.0, $"read as {point.X}, {point.Y}");
    }

    /// <summary>
    /// A WKT file states no coordinate system and GeoJSON without one is taken to be in degrees,
    /// so a file of Stereo 70 metres read through either used to be stored as numbers in the
    /// hundreds of thousands stamped as longitude and latitude.
    /// </summary>
    [Theory]
    [InlineData(GeofileFormat.Wkt, "sites.wkt", "POINT (585000 512000)", "POINT (26.098 46.102)")]
    [InlineData(GeofileFormat.GeoJson, "sites.geojson", "[585000, 512000]", "[26.098, 46.102]")]
    public void Metres_read_from_a_file_that_states_no_system_are_refused_while_degrees_import(
        GeofileFormat format, string name, string metres, string degrees)
    {
        string Content(string coordinates) => format == GeofileFormat.Wkt
            ? coordinates
            : "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{},"
              + "\"geometry\":{\"type\":\"Point\",\"coordinates\":" + coordinates + "}}]}";

        var good = Path.Combine(root, "good-" + name);
        File.WriteAllText(good, Content(degrees));
        var read = new GdalVectorIO().Read(good, format);
        read.Features.Count.ShouldBe(1);
        read.Features[0].Geom.Coordinate.X.ShouldBe(26.098, 1e-9);

        var bad = Path.Combine(root, "bad-" + name);
        File.WriteAllText(bad, Content(metres));
        var refusal = Should.Throw<VectorIOException>(() => new GdalVectorIO().Read(bad, format));
        refusal.Message.ShouldContain("projected coordinate system");
    }

    /// <summary>
    /// A file in a projected system one of whose features lies beyond the area the system can
    /// describe — a mistyped easting, say. That feature cannot be converted, and importing the
    /// rest as though the file were whole would leave the importer believing every feature had
    /// landed; the file is refused instead, while the same file without it imports.
    /// </summary>
    [Fact]
    public void A_projected_file_one_of_whose_features_will_not_convert_is_refused_whole()
    {
        using var reference = new SpatialReference("");
        reference.ImportFromEPSG(3035);
        (double X, double Y) inside = (4_321_000, 3_210_000);
        (double X, double Y) outside = (4_321_000 + 20_000_000, 3_210_000);

        var whole = new GdalVectorIO().Read(
            ShapefileArchive("points", reference, [inside]), GeofileFormat.Shapefile);
        whole.Features.Count.ShouldBe(1);

        var refusal = Should.Throw<VectorIOException>(() => new GdalVectorIO().Read(
            ShapefileArchive("points", reference, [inside, outside]), GeofileFormat.Shapefile));
        refusal.Message.ShouldContain("longitude and latitude");
    }

    /// <summary>
    /// A raster placed beyond the area its coordinate system can describe: the library reports
    /// such a point as an infinite coordinate, which used to be stored in the map's footprint and
    /// then break every catalogue page that listed it.
    /// </summary>
    [Fact]
    public void A_raster_whose_edge_leaves_its_systems_area_is_refused_while_one_inside_converts()
    {
        var inside = GeoTiff(Path.Combine(root, "inside.tif"), 32635, 400_000, 5_100_000, 100);
        var info = new RasterCogService().ConvertToCog(inside, Path.Combine(root, "inside-cog.tif"));
        var envelope = info.Bbox4326.EnvelopeInternal;
        envelope.MinX.ShouldBe(25.707, 0.01);
        envelope.MaxY.ShouldBe(46.046, 0.01);
        envelope.Width.ShouldBeGreaterThan(0);

        // Lambert azimuthal equal-area covers a disc round its centre; this one starts far
        // outside it.
        var outside = GeoTiff(Path.Combine(root, "outside.tif"), 3035, 4_321_000 + 20_000_000, 3_210_000, 100_000);
        var refusal = Should.Throw<VectorIOException>(
            () => new RasterCogService().ConvertToCog(outside, Path.Combine(root, "outside-cog.tif")));
        refusal.Message.ShouldContain("longitude and latitude");
    }

    private static NetTopologySuite.Geometries.Point SinglePoint(string archive)
    {
        var read = new GdalVectorIO().Read(archive, GeofileFormat.Shapefile);
        read.Features.Count.ShouldBe(1);
        return read.Features[0].Geom.ShouldBeOfType<NetTopologySuite.Geometries.Point>();
    }

    private static double MetresBetween(NetTopologySuite.Geometries.Point point, double longitude, double latitude)
    {
        var east = (point.X - longitude) * MetresPerDegree * Math.Cos(latitude * Math.PI / 180);
        var north = (point.Y - latitude) * MetresPerDegree;
        return Math.Sqrt((east * east) + (north * north));
    }

    private string ShapefileArchive(
        string layerName, SpatialReference reference, (double X, double Y) at, string? projectionFile = null) =>
        ShapefileArchive(layerName, reference, [at], projectionFile);

    private string ShapefileArchive(
        string layerName, SpatialReference reference, IReadOnlyList<(double X, double Y)> points,
        string? projectionFile = null)
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        using (var dataSource = Ogr.GetDriverByName("ESRI Shapefile").CreateDataSource(directory, null))
        using (var layer = dataSource.CreateLayer(layerName, reference, wkbGeometryType.wkbPoint, null))
        {
            foreach (var at in points)
            {
                using var feature = new Feature(layer.GetLayerDefn());
                using var geometry = new Geometry(wkbGeometryType.wkbPoint);
                geometry.AddPoint_2D(at.X, at.Y);
                feature.SetGeometry(geometry);
                layer.CreateFeature(feature).ShouldBe(Ogr.OGRERR_NONE);
            }
        }

        if (projectionFile is not null)
        {
            File.WriteAllText(Path.Combine(directory, layerName + ".prj"), projectionFile);
        }

        var archive = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(directory, archive);
        return archive;
    }

    private static string GeoTiff(string path, int epsg, double west, double north, double pixelSize)
    {
        using (var dataset = Gdal.GetDriverByName("GTiff").Create(path, 16, 16, 1, DataType.GDT_Byte, null))
        {
            dataset.SetGeoTransform([west, pixelSize, 0, north, 0, -pixelSize]);
            using (var reference = new SpatialReference(""))
            {
                reference.ImportFromEPSG(epsg);
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
