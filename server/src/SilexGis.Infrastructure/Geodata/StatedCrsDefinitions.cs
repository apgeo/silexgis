// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// PROJ.4 definitions this application states itself rather than taking from the PROJ database's
/// export, for the codes where that export loses the datum shift.
///
/// <para>
/// Modern PROJ keeps a coordinate system and the transformation to WGS 84 apart and chooses the
/// transformation when it transforms, so a system exported as a PROJ.4 string often comes back
/// without its <c>+towgs84</c>. Inside PROJ that costs nothing; a consumer that only has the
/// string — the 3D survey viewer, a published trip's reader — projects with no shift at all.
/// For the Romanian national grid that put every survey about 125 m from where the database,
/// the map and the coordinate formats put it.
/// </para>
///
/// <para>
/// Stereo 70: the shift is EPSG transformation 15995, "Pulkovo 1942(58) to WGS 84 (19)", for
/// Romania, stated accuracy 3 m — the parameters the National Agency for Cadastre and Land
/// Registration publishes for ETRS89, which the register takes as WGS 84 at that accuracy. It is
/// also what PROJ itself chooses for EPSG:3844, so this string and the database's ST_Transform
/// agree to the millimetre. The register states the rotations in the coordinate-frame
/// convention; <c>+towgs84</c> reads them in the position-vector one, hence the turned signs.
/// EPSG:31700 is the register's deprecated code for the same grid on a datum that was never
/// realised; survey files still name it and mean the grid as used, so it gets the same answer.
/// The client's coordinate formats carry the identical string, and a test on each side holds
/// both to one shared copy of it and of the points that pin it.
/// </para>
/// </summary>
internal static class StatedCrsDefinitions
{
    public const string Stereo70 =
        "+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass "
        + "+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs";

    public static string? For(int epsgCode) => epsgCode is 3844 or 31700 ? Stereo70 : null;
}
