// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Vector formats accepted for geofile upload/export. Stored as smallint.</summary>
public enum GeofileFormat : short
{
    Gpx = 0,
    Kml = 1,
    GeoJson = 2,
    Shapefile = 3,
    Wkt = 4,
    Wkb = 5,

    /// <summary>Zipped KML, as Google Earth saves it.</summary>
    Kmz = 6,

    /// <summary>
    /// Delimited text with coordinate columns — the format a spreadsheet of positions arrives
    /// in, and the only one whose geometry has to be assembled rather than read.
    /// </summary>
    Csv = 7,
}

/// <summary>Lifecycle of an uploaded geofile's server-side import. Stored as smallint.</summary>
public enum GeofileImportStatus : short
{
    Uploaded = 0,
    Importing = 1,
    Imported = 2,
    Failed = 3,
}
