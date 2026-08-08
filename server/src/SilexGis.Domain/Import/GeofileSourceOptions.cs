// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// How to read a file whose format does not say for itself. Only delimited text needs this:
/// GPX, KML and GeoJSON name their geometry, while a spreadsheet of positions is a table
/// somebody has to point at two columns of.
///
/// <para>
/// Stored on the geofile so a re-read produces the same rows, and so a mapping that was wrong
/// can be corrected and the file read again without re-uploading it.
/// </para>
/// </summary>
public sealed record GeofileSourceOptions
{
    public DelimitedSourceOptions? Delimited { get; init; }
}

/// <summary>Column choices for a delimited file. Every field empty means "work it out".</summary>
public sealed record DelimitedSourceOptions
{
    /// <summary>The field separator. Null sniffs it from the header.</summary>
    public string? Delimiter { get; init; }

    public string? LatitudeColumn { get; init; }

    public string? LongitudeColumn { get; init; }

    public string? ElevationColumn { get; init; }

    /// <summary>
    /// A column holding well-known text. When present it wins over the coordinate pair — it is
    /// how a file exported from this application comes back, and it carries lines and areas a
    /// coordinate pair cannot.
    /// </summary>
    public string? WktColumn { get; init; }

    /// <summary>Header names tried for latitude, best first. Folded before comparison.</summary>
    public static IReadOnlyList<string> LatitudeCandidates { get; } =
        ["latitude", "lat", "latitudine", "y", "north", "northing", "gps_lat", "wgs84_lat"];

    /// <summary>Header names tried for longitude, best first.</summary>
    public static IReadOnlyList<string> LongitudeCandidates { get; } =
        ["longitude", "lon", "lng", "long", "longitudine", "x", "east", "easting", "gps_lon", "wgs84_lon"];

    /// <summary>Header names tried for a well-known-text geometry, best first.</summary>
    public static IReadOnlyList<string> WktCandidates { get; } = ["wkt", "geometry", "geom", "the_geom"];

    /// <summary>Separators sniffed from the header line, in the order they are preferred on a tie.</summary>
    public static IReadOnlyList<char> CandidateDelimiters { get; } = [',', ';', '\t', '|'];
}
