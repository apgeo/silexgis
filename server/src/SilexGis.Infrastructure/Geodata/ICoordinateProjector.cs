// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Geodata;

/// <summary>Where a projected coordinate lands, and how its grid is turned against the world there.</summary>
/// <param name="Longitude">Degrees east, WGS 84.</param>
/// <param name="Latitude">Degrees north, WGS 84.</param>
/// <param name="ConvergenceDeg">
/// The angle from true north to the grid's north at this point, positive eastwards.
///
/// <para>
/// A projected grid is only aligned with true north on its own central meridian; everywhere else
/// its north is turned by a degree or two, and the further from the meridian the more. That turn is
/// invisible at a point and grows with distance, so a survey dropped into the world without
/// correcting it is right at its centre and wrong at its ends — measured at 3.3 m over one of the
/// sample caves and 18.3 m over another.
/// </para>
/// </param>
public readonly record struct ProjectedPoint(double Longitude, double Latitude, double ConvergenceDeg);

/// <summary>
/// Turns coordinates in a projected system into geographic ones. A seam beside
/// <see cref="ICrsRegistry"/>, and for the same reason: the PROJ binding stays in one place.
/// </summary>
public interface ICoordinateProjector
{
    /// <summary>
    /// Where an easting/northing in <paramref name="epsgCode"/> is in the world, or null when the
    /// code is unknown or the point cannot be transformed. Never throws for bad input.
    /// </summary>
    ProjectedPoint? ToWgs84(int epsgCode, double easting, double northing);
}
