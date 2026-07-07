// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Server-side protection of sensitive cave locations (05-auth-permissions.md §5).
/// Every path that emits cave coordinates (DTOs, map endpoints, exports) must go through
/// this — never rely on the client to hide data.
/// </summary>
public static class LocationProtection
{
    private const double MetersPerDegreeLat = 111_320d;

    public static bool CanViewExactLocation(UserContext? user, Cave cave) =>
        !cave.LocationProtected
        || PermissionEvaluator.Can(user, cave, ObjectPermission.ViewExactLocation);

    /// <summary>Grid cell size in degrees for a protection grid of <paramref name="gridMeters"/>.</summary>
    public static double CellDegrees(double gridMeters) => gridMeters / MetersPerDegreeLat;

    /// <summary>
    /// Snaps a point to the nearest grid intersection of <paramref name="gridMeters"/>,
    /// removing precision deterministically (same input → same output; no jitter to average
    /// away). Uses one cell size in degrees on both axes so it is exactly reproducible in
    /// SQL as ST_SnapToGrid — the two MUST stay identical, or combining endpoints would
    /// leak location by grid intersection.
    /// </summary>
    public static Point Snap(Point point, double gridMeters)
    {
        var cell = CellDegrees(gridMeters);
        return new Point(Math.Round(point.X / cell) * cell, Math.Round(point.Y / cell) * cell)
        {
            SRID = point.SRID,
        };
    }
}
