// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// The one elevation dataset this application can obtain by itself: Copernicus DEM GLO-30, from
/// the open-data bucket that publishes it without an account, a key or a signature.
/// </summary>
/// <remarks>
/// <para>
/// Thirty-metre coverage of the whole land surface, one cloud-optimised GeoTIFF per one-degree
/// cell, free for any use including commercial provided the credit below travels with it. It is
/// the coarse fill every build starts from; anything finer is data somebody already holds and
/// arrives by upload or from a directory the operator names, because no national lidar portal can
/// be fetched programmatically.
/// </para>
/// <para>
/// The arithmetic here has one home because it is stated twice — once by the command-line tool an
/// operator can still run by hand, once by the application — and the two must name the same cell
/// for the same box or a rectangle drawn across a boundary would download the wrong ground and
/// nothing would look wrong: the tiles would be real, they would simply be somewhere else.
/// </para>
/// </remarks>
public static class CopernicusCoverage
{
    /// <summary>What to call this data where a person reads it.</summary>
    public const string Name = "Copernicus DEM GLO-30";

    /// <summary>
    /// The credit this licence requires, which ends up stamped on the pyramid.
    /// </summary>
    /// <remarks>
    /// Held here rather than in a setting an operator fills in, because it belongs to this one
    /// dataset and to nothing else: a build made from somebody's lidar carries the credit that
    /// data requires, given when the source is declared. A pyramid stamped with a credit belonging
    /// to data it does not contain is a false licence statement displayed on screen to everyone
    /// who looks at the scene, with nothing anywhere to say it is wrong.
    /// </remarks>
    public const string Attribution =
        "Copernicus DEM GLO-30 — © DLR e.V. 2010-2014 and © Airbus Defence and "
        + "Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA";

    /// <summary>The terms in a sentence, for the record kept beside the build.</summary>
    public const string Licence = "Free for any use, including commercial, with attribution.";

    /// <summary>The bucket the cells are published in.</summary>
    public const string Bucket = "https://copernicus-dem-30m.s3.amazonaws.com";

    /// <summary>
    /// The one-degree cells covering a west/south/east/north box, as this dataset names them.
    /// </summary>
    /// <remarks>
    /// A cell is named for its <b>south-west</b> corner — latitude in two digits, longitude in
    /// three — so the cell spanning 46 to 47 degrees north is N46, and the one spanning one degree
    /// south to the equator is S01. A corner at exactly zero belongs to the northern and eastern
    /// names, which is why the sign test is "less than zero" and not "less than or equal": the
    /// bucket publishes S01_00_E015_00 and has no S00_00_W000_00 at all.
    ///
    /// <para>
    /// The bounds are floor of the south and west edge and ceiling of the north and east, so a box
    /// that crosses a cell boundary by a fraction of a degree takes both cells. Anything else
    /// leaves a strip of the requested area with no elevation under it, and a hole in terrain is
    /// not visible as a hole — it is drawn as the smooth ellipsoid, which reads as flat ground.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Cells(double west, double south, double east, double north)
    {
        var cells = new List<string>();
        var lastLat = (int)Math.Ceiling(north);
        var lastLon = (int)Math.Ceiling(east);

        for (var lat = (int)Math.Floor(south); lat < lastLat; lat++)
        {
            for (var lon = (int)Math.Floor(west); lon < lastLon; lon++)
            {
                var ns = lat < 0 ? 'S' : 'N';
                var ew = lon < 0 ? 'W' : 'E';
                cells.Add($"{ns}{Math.Abs(lat):D2}_00_{ew}{Math.Abs(lon):D3}_00");
            }
        }

        return cells;
    }

    /// <summary>Where one named cell is published.</summary>
    /// <remarks>The directory repeats the file's own name; that is how the bucket is laid out.</remarks>
    public static string Url(string cell)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cell);
        var name = $"Copernicus_DSM_COG_10_{cell}_DEM";
        return $"{Bucket}/{name}/{name}.tif";
    }

    /// <summary>What a downloaded cell is called on disk.</summary>
    /// <remarks>
    /// The cell's own name rather than the remote one, so a directory of them reads as a map
    /// rather than as a list of identical-looking product identifiers.
    /// </remarks>
    public static string FileName(string cell) => $"{cell}.tif";
}
