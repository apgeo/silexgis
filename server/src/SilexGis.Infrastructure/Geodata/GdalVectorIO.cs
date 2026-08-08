// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text.Json;
using MaxRev.Gdal.Core;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using SilexGis.Domain;
using SilexGis.Domain.Import;
using Geometry = NetTopologySuite.Geometries.Geometry;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// GDAL/OGR-backed vector IO (in-process bindings). WKT/WKB are handled with
/// NetTopologySuite directly — they are line-oriented text/hex dumps, not OGR datasets.
/// All geometries are normalized to EPSG:4326 on read.
/// </summary>
public sealed class GdalVectorIO : IVectorIO
{
    static GdalVectorIO()
    {
        GdalBase.ConfigureAll();
        // GPX elevations become geometry Z only with this option; otherwise the driver
        // exposes them as an attribute on the *_points layers we skip. Centerlines (and
        // any imported track) would silently lose altitude without it.
        Gdal.SetConfigOption("GPX_ELE_AS_25D", "YES");
    }

    // GPX datasets expose point/segment layers that duplicate their aggregate layers.
    private static readonly string[] GpxLayers = ["waypoints", "routes", "tracks"];

    /// <summary>
    /// The per-point layers the aggregate read above skips. They are exactly where the times
    /// are, which is why placing a photograph by its clock has to look at them.
    /// </summary>
    private static readonly string[] GpxPointLayers = ["track_points", "route_points"];

    /// <summary>
    /// A ceiling on how much of a track log is read for time matching. A unit logging every
    /// second fills this in about eleven hours, which is a long caving day; beyond it the
    /// matching is no better and the memory is real.
    /// </summary>
    private const int MaxTrackFixes = 40_000;

    public VectorDataset Read(string absolutePath, GeofileFormat format, GeofileSourceOptions? sourceOptions = null)
    {
        if (format is GeofileFormat.Wkt or GeofileFormat.Wkb)
        {
            return ReadWktLines(absolutePath, format);
        }

        if (format == GeofileFormat.Csv)
        {
            return DelimitedVectorReader.Read(absolutePath, sourceOptions?.Delimited);
        }

        // Zipped shapefiles are read in place through GDAL's zip virtual filesystem; a KMZ is
        // the same trick pointed at the document inside it. Naming that document explicitly
        // rather than handing the archive to the driver is deliberate: which KML driver a GDAL
        // build carries (KML or LIBKML) decides whether an archive opens at all, and this way
        // the answer does not depend on how the bindings were compiled.
        var openPath = format switch
        {
            GeofileFormat.Shapefile => "/vsizip/" + absolutePath.Replace('\\', '/'),
            GeofileFormat.Kmz => KmzDocumentPath(absolutePath),
            _ => absolutePath,
        };

        using var dataSource = Ogr.Open(openPath, 0);
        if (dataSource is null)
        {
            throw new VectorIOException("The file could not be opened as a vector dataset.");
        }

        var features = new List<VectorFeature>();
        int? sourceSrid = null;
        var wkbReader = new WKBReader { HandleSRID = false };

        for (var i = 0; i < dataSource.GetLayerCount(); i++)
        {
            var layer = dataSource.GetLayerByIndex(i);
            if (format == GeofileFormat.Gpx && !GpxLayers.Contains(layer.GetName(), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            sourceSrid ??= DetectSrid(layer);
            ReadLayer(layer, wkbReader, features);
        }

        if (features.Count == 0)
        {
            throw new VectorIOException("The dataset contains no readable features.");
        }

        return new VectorDataset(features, sourceSrid);
    }

    /// <summary>
    /// The per-point layers a track log carries, with their times. GPX is the only accepted
    /// format that records one — a KML track's times live in an extension the driver does not
    /// surface as fields, and a shapefile has no clock at all — so everything else answers with
    /// nothing rather than with a plausible-looking guess.
    /// </summary>
    public IReadOnlyList<TrackFix> ReadTrackFixes(string absolutePath, GeofileFormat format)
    {
        if (format != GeofileFormat.Gpx)
        {
            return [];
        }

        using var dataSource = Ogr.Open(absolutePath, 0);
        if (dataSource is null)
        {
            throw new VectorIOException("The file could not be opened as a vector dataset.");
        }

        var fixes = new List<TrackFix>();
        for (var i = 0; i < dataSource.GetLayerCount(); i++)
        {
            var layer = dataSource.GetLayerByIndex(i);
            if (!GpxPointLayers.Contains(layer.GetName(), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            ReadFixes(layer, fixes);
            if (fixes.Count >= MaxTrackFixes)
            {
                break;
            }
        }

        return fixes;
    }

    public byte[] Write(ExportFormat format, string layerName, IReadOnlyList<VectorFeature> features)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"silexgis-vio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            return format switch
            {
                ExportFormat.GeoJson => WriteSingleFile(workDir, "GeoJSON", $"{layerName}.geojson", layerName, wkbGeometryType.wkbUnknown, features, [], []),
                ExportFormat.Kml => WriteSingleFile(workDir, "KML", $"{layerName}.kml", layerName, wkbGeometryType.wkbUnknown, features, [], []),
                ExportFormat.Csv => WriteSingleFile(workDir, "CSV", $"{layerName}.csv", layerName, wkbGeometryType.wkbUnknown, features, [], ["GEOMETRY=AS_WKT"]),
                ExportFormat.Gpx => WriteGpx(workDir, layerName, features),
                ExportFormat.ShapefileZip => WriteShapefileZip(workDir, layerName, features),
                _ => throw new VectorIOException($"Unsupported export format {format}."),
            };
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; the OS temp dir is periodically purged anyway.
            }
        }
    }

    // ---- reading ----

    private static void ReadLayer(Layer layer, WKBReader wkbReader, List<VectorFeature> features)
    {
        var transform = BuildTransformTo4326(layer.GetSpatialRef());
        var definition = layer.GetLayerDefn();
        var fieldCount = definition.GetFieldCount();

        layer.ResetReading();
        Feature? feature;
        while ((feature = layer.GetNextFeature()) is not null)
        {
            using (feature)
            {
                var ogrGeometry = feature.GetGeometryRef();
                if (ogrGeometry is null)
                {
                    continue;
                }

                if (transform is not null)
                {
                    ogrGeometry.Transform(transform);
                }

                var buffer = new byte[ogrGeometry.WkbSize()];
                ogrGeometry.ExportToWkb(buffer, wkbByteOrder.wkbNDR);

                Geometry geometry;
                try
                {
                    geometry = wkbReader.Read(buffer);
                }
                catch (ParseException)
                {
                    continue; // skip rows whose geometry cannot be represented
                }

                // Line work is cleaned rather than rejected. A survey export routinely holds a
                // few zero-length shots (a station written twice); NTS then reports the whole
                // multi-geometry invalid, and dropping the feature threw away an entire cave's
                // centerline over a handful of duplicate points.
                geometry = DropDegenerateLines(geometry);
                if (geometry.IsEmpty || !geometry.IsValid)
                {
                    continue;
                }

                geometry.SRID = 4326;

                // GeometryCollections don't fit the GeoJSON "coordinates" contract used
                // downstream — flatten them into one row per component geometry.
                var geometries = geometry is GeometryCollection collection and not (MultiPoint or MultiLineString or MultiPolygon)
                    ? Enumerable.Range(0, collection.NumGeometries).Select(collection.GetGeometryN).ToArray()
                    : [geometry];

                var properties = new Dictionary<string, object?>();
                for (var f = 0; f < fieldCount; f++)
                {
                    if (!feature.IsFieldSet(f))
                    {
                        continue;
                    }

                    var fieldDefinition = definition.GetFieldDefn(f);
                    var name = fieldDefinition.GetName();
                    properties[name] = fieldDefinition.GetFieldType() switch
                    {
                        FieldType.OFTInteger => feature.GetFieldAsInteger(f),
                        FieldType.OFTInteger64 => feature.GetFieldAsInteger64(f),
                        FieldType.OFTReal => feature.GetFieldAsDouble(f),
                        _ => feature.GetFieldAsString(f),
                    };
                }

                foreach (var component in geometries)
                {
                    component.SRID = 4326;
                    features.Add(new VectorFeature(component, properties));
                }
            }
        }
    }

    /// <summary>
    /// Reads one point layer's timestamped positions. Rows without a readable time are skipped
    /// rather than kept with a guessed one: a fix with no clock behind it cannot place anything,
    /// and keeping it would only make the index look denser than it is.
    /// </summary>
    private static void ReadFixes(Layer layer, List<TrackFix> fixes)
    {
        var transform = BuildTransformTo4326(layer.GetSpatialRef());
        var timeField = layer.GetLayerDefn().GetFieldIndex("time");
        if (timeField < 0)
        {
            return;
        }

        layer.ResetReading();
        Feature? feature;
        while (fixes.Count < MaxTrackFixes && (feature = layer.GetNextFeature()) is not null)
        {
            using (feature)
            {
                if (!feature.IsFieldSet(timeField))
                {
                    continue;
                }

                var ogrGeometry = feature.GetGeometryRef();
                if (ogrGeometry is null || ogrGeometry.GetPointCount() < 1)
                {
                    continue;
                }

                if (transform is not null)
                {
                    ogrGeometry.Transform(transform);
                }

                if (ReadFieldTime(feature, timeField) is not { } time)
                {
                    continue;
                }

                fixes.Add(new TrackFix(time, ogrGeometry.GetX(0), ogrGeometry.GetY(0)));
            }
        }
    }

    /// <summary>
    /// One row's recorded moment. The driver states a GPX time as its own broken-down fields
    /// with a zone flag; a row it cannot break down is read as text instead, because a merged
    /// or hand-edited file writes times the field parser refuses and the text is still an
    /// ISO 8601 instant.
    /// </summary>
    private static DateTimeOffset? ReadFieldTime(Feature feature, int field)
    {
        feature.GetFieldAsDateTime(
            field, out var year, out var month, out var day,
            out var hour, out var minute, out var second, out var timeZone);
        if (year > 0 && month is >= 1 and <= 12 && day >= 1)
        {
            try
            {
                // The driver's zone flag: 0 unknown, 1 local, 100 UTC, and every other value an
                // offset in quarter-hours from UTC counted from 100. An unknown zone is read as
                // UTC — which is what a GPX writes anyway — and the camera-clock offset is what
                // corrects a file whose times turn out to be local.
                var offset = timeZone > 1 ? TimeSpan.FromMinutes((timeZone - 100) * 15) : TimeSpan.Zero;
                var wholeSeconds = (int)second;
                return new DateTimeOffset(
                    year, month, day, hour, minute, Math.Clamp(wholeSeconds, 0, 59), offset);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A field set that does not name a real moment — 31 February, hour 25 — is a
                // damaged row rather than a reason to abandon the track.
                return null;
            }
        }

        var text = feature.GetFieldAsString(field);
        return DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Removes zero-length components from line geometries and returns everything else
    /// untouched. Polygons are left alone deliberately: for them invalidity usually means a
    /// self-intersection, which dropping rings would silently paper over.
    /// </summary>
    private static Geometry DropDegenerateLines(Geometry geometry)
    {
        if (geometry is not MultiLineString multi)
        {
            return geometry;
        }

        var kept = multi.Geometries.Cast<LineString>().Where(HasLength).ToArray();
        return kept.Length == multi.NumGeometries
            ? geometry
            : new MultiLineString(kept) { SRID = geometry.SRID };
    }

    private static bool HasLength(LineString line)
    {
        if (line.IsEmpty || line.Coordinates.Length < 2)
        {
            return false;
        }

        var first = line.Coordinates[0];
        return line.Coordinates.Any(c => c.X != first.X || c.Y != first.Y);
    }

    private static CoordinateTransformation? BuildTransformTo4326(SpatialReference? source)
    {
        // No spatial reference ⇒ trust the data to already be lon/lat (common for GPX/GeoJSON).
        if (source is null)
        {
            return null;
        }

        source.AutoIdentifyEPSG();
        if (source.GetAuthorityCode(null) == "4326")
        {
            return null;
        }

        var target = new SpatialReference("");
        target.ImportFromEPSG(4326);
        // Force lon/lat axis order regardless of authority definitions.
        source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        return new CoordinateTransformation(source, target);
    }

    private static int? DetectSrid(Layer layer)
    {
        var srs = layer.GetSpatialRef();
        if (srs is null)
        {
            return null;
        }

        srs.AutoIdentifyEPSG();
        return int.TryParse(srs.GetAuthorityCode(null), out var epsg) ? epsg : null;
    }

    /// <summary>
    /// The KML document inside a KMZ, as a path GDAL's zip filesystem can open.
    /// <c>doc.kml</c> is the conventional name and what Google Earth writes, but an archive
    /// exported by something else may hold one under any name, so the convention is preferred
    /// and the first KML found is the fallback.
    /// </summary>
    private static string KmzDocumentPath(string absolutePath)
    {
        using var archive = OpenKmz(absolutePath);
        var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "doc.kml", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
            ?? throw new VectorIOException("The archive holds no KML document.");

        return "/vsizip/" + absolutePath.Replace('\\', '/') + "/" + entry.FullName;
    }

    private static ZipArchive OpenKmz(string absolutePath)
    {
        try
        {
            return ZipFile.OpenRead(absolutePath);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            throw new VectorIOException("The file is not a readable KMZ archive.", e);
        }
    }

    private static VectorDataset ReadWktLines(string absolutePath, GeofileFormat format)
    {
        var wktReader = new WKTReader();
        var wkbReader = new WKBReader();
        var features = new List<VectorFeature>();

        foreach (var raw in File.ReadLines(absolutePath))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                var geometry = format == GeofileFormat.Wkt
                    ? wktReader.Read(line)
                    : wkbReader.Read(Convert.FromHexString(line));
                if (!geometry.IsEmpty && geometry.IsValid)
                {
                    geometry.SRID = 4326;
                    features.Add(new VectorFeature(geometry, new Dictionary<string, object?>()));
                }
            }
            catch (Exception e) when (e is ParseException or FormatException or ArgumentException)
            {
                throw new VectorIOException($"Line is not valid {format.ToString().ToUpperInvariant()}: {Truncate(line)}", e);
            }
        }

        if (features.Count == 0)
        {
            throw new VectorIOException("The dataset contains no readable features.");
        }

        return new VectorDataset(features, 4326);
    }

    private static string Truncate(string value) => value.Length <= 60 ? value : value[..60] + "…";

    // ---- writing ----

    private static byte[] WriteSingleFile(
        string workDir,
        string driverName,
        string fileName,
        string layerName,
        wkbGeometryType layerType,
        IReadOnlyList<VectorFeature> features,
        string[] datasetOptions,
        string[] layerOptions)
    {
        var path = Path.Combine(workDir, fileName);
        var driver = Ogr.GetDriverByName(driverName)
            ?? throw new VectorIOException($"OGR driver {driverName} is unavailable.");

        using (var dataSource = driver.CreateDataSource(path, datasetOptions))
        {
            var srs = Epsg4326();
            var layer = dataSource.CreateLayer(layerName, srs, layerType, layerOptions);
            WriteFeatures(layer, features);
            dataSource.FlushCache();
        }

        return File.ReadAllBytes(path);
    }

    private static byte[] WriteGpx(string workDir, string layerName, IReadOnlyList<VectorFeature> features)
    {
        // GPX is schema-bound: points become waypoints, everything linear becomes tracks
        // (polygons are exported as their boundary). Extra attributes go to <extensions>.
        var path = Path.Combine(workDir, $"{layerName}.gpx");
        var driver = Ogr.GetDriverByName("GPX") ?? throw new VectorIOException("OGR driver GPX is unavailable.");

        var waypoints = new List<VectorFeature>();
        var tracks = new List<VectorFeature>();
        foreach (var feature in features)
        {
            switch (feature.Geom)
            {
                case Point or MultiPoint:
                    waypoints.Add(feature);
                    break;
                case Polygon or MultiPolygon:
                    tracks.Add(feature with { Geom = feature.Geom.Boundary });
                    break;
                default:
                    tracks.Add(feature);
                    break;
            }
        }

        using (var dataSource = driver.CreateDataSource(path, ["GPX_USE_EXTENSIONS=YES"]))
        {
            var srs = Epsg4326();
            if (waypoints.Count > 0)
            {
                WriteFeatures(dataSource.CreateLayer("waypoints", srs, wkbGeometryType.wkbPoint, []), waypoints);
            }

            if (tracks.Count > 0)
            {
                WriteFeatures(dataSource.CreateLayer("tracks", srs, wkbGeometryType.wkbMultiLineString, []), tracks);
            }

            dataSource.FlushCache();
        }

        return File.ReadAllBytes(path);
    }

    private static byte[] WriteShapefileZip(string workDir, string layerName, IReadOnlyList<VectorFeature> features)
    {
        // A shapefile holds exactly one geometry class; mixed exports produce one
        // layer per class inside the zip (the convention desktop GIS tools expect).
        var shpDir = Path.Combine(workDir, "shp");
        Directory.CreateDirectory(shpDir);
        var driver = Ogr.GetDriverByName("ESRI Shapefile")
            ?? throw new VectorIOException("OGR driver ESRI Shapefile is unavailable.");

        var groups = features
            .GroupBy(f => f.Geom switch
            {
                Point or MultiPoint => ("points", wkbGeometryType.wkbPoint),
                LineString or MultiLineString => ("lines", wkbGeometryType.wkbLineString),
                _ => ("polygons", wkbGeometryType.wkbPolygon),
            });

        using (var dataSource = driver.CreateDataSource(shpDir, []))
        {
            var srs = Epsg4326();
            foreach (var group in groups)
            {
                var (suffix, geometryType) = group.Key;
                var layer = dataSource.CreateLayer($"{layerName}_{suffix}", srs, geometryType, []);
                WriteFeatures(layer, [.. group]);
            }

            dataSource.FlushCache();
        }

        var zipPath = Path.Combine(workDir, $"{layerName}.zip");
        ZipFile.CreateFromDirectory(shpDir, zipPath);
        return File.ReadAllBytes(zipPath);
    }

    private static void WriteFeatures(Layer layer, IReadOnlyList<VectorFeature> features)
    {
        // Field set = union of property keys, in first-seen order, typed by first value.
        var fields = new List<(string Name, FieldType Type)>();
        var seen = new HashSet<string>();
        foreach (var feature in features)
        {
            foreach (var (key, value) in feature.Properties)
            {
                if (seen.Add(key))
                {
                    fields.Add((key, value switch
                    {
                        int or long => FieldType.OFTInteger64,
                        float or double or decimal => FieldType.OFTReal,
                        _ => FieldType.OFTString,
                    }));
                }
            }
        }

        foreach (var (name, type) in fields)
        {
            using var fieldDefinition = new FieldDefn(name, type);
            layer.CreateField(fieldDefinition, 1);
        }

        var definition = layer.GetLayerDefn();
        foreach (var feature in features)
        {
            using var ogrFeature = new Feature(definition);
            using var ogrGeometry = OSGeo.OGR.Geometry.CreateFromWkb(feature.Geom.AsBinary());
            ogrFeature.SetGeometry(ogrGeometry);

            foreach (var (key, value) in feature.Properties)
            {
                if (value is null)
                {
                    continue;
                }

                var index = definition.GetFieldIndex(key);
                if (index < 0)
                {
                    continue; // shapefile driver may have truncated/renamed the field
                }

                switch (value)
                {
                    case int i:
                        ogrFeature.SetField(index, i);
                        break;
                    case long l:
                        ogrFeature.SetField(index, l);
                        break;
                    case float or double or decimal:
                        ogrFeature.SetField(index, Convert.ToDouble(value));
                        break;
                    case JsonElement json:
                        ogrFeature.SetField(index, json.ToString());
                        break;
                    default:
                        ogrFeature.SetField(index, value.ToString());
                        break;
                }
            }

            layer.CreateFeature(ogrFeature);
        }
    }

    private static SpatialReference Epsg4326()
    {
        var srs = new SpatialReference("");
        srs.ImportFromEPSG(4326);
        srs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        return srs;
    }
}
