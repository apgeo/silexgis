// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Resolves an EPSG code to its PROJ.4 definition. A seam for the same reason
/// <see cref="IVectorIO"/> is one: the PROJ binding stays out of the feature slices.
/// </summary>
public interface ICrsRegistry
{
    /// <summary>
    /// The PROJ.4 definition string of one EPSG code, or null when the bundled PROJ database
    /// does not know the code or cannot express it as PROJ.4. Never throws for a bad code.
    /// </summary>
    string? Proj4(int epsgCode);
}
