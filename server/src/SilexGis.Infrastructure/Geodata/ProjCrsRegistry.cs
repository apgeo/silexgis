// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using MaxRev.Gdal.Core;
using OSGeo.OSR;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// EPSG → PROJ.4 lookups answered from the PROJ database that ships with the bundled GDAL
/// build. Entirely offline: an installation with no route to the internet resolves any code
/// PROJ knows, which is the point — the alternative in use elsewhere is a runtime request to a
/// public web service, and that both leaks what an operator is looking at and silently loses
/// georeferencing on an air-gapped install.
///
/// <para>
/// Registered as a singleton and safe for concurrent use the same way the rest of the GDAL
/// wrapping here is: no PROJ object is shared between calls, only the resulting string is.
/// Definitions are immutable — a given EPSG code means one thing forever — so results are
/// cached for the lifetime of the process, misses included, and the cache is bounded by the
/// input validation the caller performs before reaching this.
/// </para>
/// </summary>
public sealed class ProjCrsRegistry : ICrsRegistry
{
    static ProjCrsRegistry() => GdalBase.ConfigureAll();

    private readonly ConcurrentDictionary<int, string?> cache = new();

    public string? Proj4(int epsgCode) => cache.GetOrAdd(epsgCode, Resolve);

    private static string? Resolve(int epsgCode)
    {
        try
        {
            using var srs = new SpatialReference("");
            // The bindings are used in return-code mode throughout this assembly (nothing calls
            // Osr.UseExceptions), but PROJ can still raise on malformed input, so both are handled
            // — a nonsense code must come back as "unknown", never as a failed request.
            if (srs.ImportFromEPSG(epsgCode) != 0)
            {
                return null;
            }

            if (srs.ExportToProj4(out var proj4) != 0 || string.IsNullOrWhiteSpace(proj4))
            {
                return null;
            }

            return proj4.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
