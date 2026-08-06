// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// What the heights in a terrain source's tiles are measured from.
///
/// <para>
/// This is a property of the source and of nothing else. A 3D globe draws every terrain tile as
/// height above the WGS84 ellipsoid, whatever the numbers in it actually meant, so the same
/// surveyed cave altitude needs a different correction depending on which source is attached —
/// and getting that wrong moves a whole cave by the geoid undulation, which is around forty
/// metres over Romanian karst. There is deliberately no installation-wide value: an installation
/// that swaps its terrain source and keeps its correction has silently moved every cave it draws.
/// </para>
/// </summary>
public enum TerrainHeightDatum
{
    /// <summary>
    /// Heights above the geoid — mean sea level, the surface a levelling run and a national map
    /// both measure against. This is what an elevation model is usually published as (Copernicus
    /// GLO-30 is EGM2008 orthometric), and what the common hosted terrain services serve.
    ///
    /// <para>
    /// A globe draws these numbers as if they were ellipsoidal, so the drawn ground sits the geoid
    /// undulation below where it really is — and a surveyed altitude, which is orthometric too,
    /// lands on it correctly with no correction at all. The two agree because they are wrong in
    /// the same direction by the same amount, which is what matters for showing a cave inside its
    /// hillside. This is the default because it is what an unconverted elevation model produces.
    /// </para>
    /// </summary>
    Orthometric = 0,

    /// <summary>
    /// Heights above the WGS84 ellipsoid, because the geoid undulation was added when the tiles
    /// were baked. The drawn ground is then in its true place, and a surveyed orthometric altitude
    /// has to be raised by the undulation before it will sit on that ground.
    /// </summary>
    Ellipsoidal = 1,
}
