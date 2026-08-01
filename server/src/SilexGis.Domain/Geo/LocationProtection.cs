// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Server-side protection of sensitive feature locations. A feature marked
/// <c>LocationProtected</c> is a protection root covering itself and every containment
/// descendant; a caller sees exact coordinates only when they hold ViewExactLocation on
/// EVERY protected root above the row (most-restrictive veto — under the DAG a feature
/// can sit beneath two protected areas). Every path that emits protected coordinates
/// (DTOs, map endpoints, exports) must go through this — never rely on the client to
/// hide data.
/// </summary>
public static class LocationProtection
{
    private const double MetersPerDegreeLat = 111_320d;

    /// <summary>Exact-view check against ONE protection root (callers combine roots with <see cref="CanViewExactLocation(Permissions.UserContext?, System.Collections.Generic.IReadOnlyCollection{ProtectionRootGrant})"/>).</summary>
    public static bool CanViewExactLocation(
        UserContext? user, Feature protectionRoot, ObjectPermission aclGranted = ObjectPermission.None) =>
        !protectionRoot.LocationProtected
        || PermissionEvaluator.Can(user, protectionRoot, ObjectPermission.ViewExactLocation, aclGranted);

    /// <summary>
    /// The multi-root veto: exact view requires ViewExactLocation on every protected
    /// root in the row's ancestry. An empty set means the row is unprotected.
    /// </summary>
    public static bool CanViewExactLocation(UserContext? user, IReadOnlyCollection<ProtectionRootGrant> roots) =>
        roots.All(r => CanViewExactLocation(user, r.Root, r.AclGranted));

    /// <summary>
    /// The full row rule, mirrored bit-identically by the SQL exact-view fragment:
    /// admin and the row's own owner always see their row exactly (a foreign protected
    /// area above someone's own cave must not lock the owner out); everyone else needs
    /// ViewExactLocation on EVERY protected root above the row (most-restrictive veto —
    /// under the DAG a feature can sit beneath two protected areas).
    /// </summary>
    public static bool CanViewExactLocation(
        UserContext? user, Feature row, IReadOnlyCollection<ProtectionRootGrant> protectedRoots)
    {
        if (user is null)
        {
            return protectedRoots.Count == 0 && !row.LocationProtected;
        }

        return user.IsAdmin
            || row.OwnerUserId == user.UserId
            || CanViewExactLocation(user, protectedRoots);
    }

    /// <summary>
    /// A locating link on a record with exact coordinates discloses the protected
    /// target's location by proximity — "protected feature X is here". The link must be
    /// hidden wherever the caller may not view the target's exact location, even when
    /// the target record itself is visible to them.
    /// </summary>
    public static bool ShouldRedactLocatingLink(
        UserContext? user, Feature linkedProtected, ObjectPermission aclGranted = ObjectPermission.None) =>
        !CanViewExactLocation(user, linkedProtected, aclGranted);

    /// <summary>Grid cell size in degrees for a protection grid of <paramref name="gridMeters"/>.</summary>
    public static double CellDegrees(double gridMeters) => gridMeters / MetersPerDegreeLat;

    /// <summary>
    /// Snaps a point to the nearest grid intersection of <paramref name="gridMeters"/>,
    /// removing precision deterministically (same input → same output; no jitter to average
    /// away). Uses one cell size in degrees on both axes and away-from-zero rounding so it
    /// is exactly reproducible in SQL as round(x / cell) * cell — the two MUST stay
    /// identical, or combining endpoints would leak location by grid intersection.
    /// </summary>
    public static Point Snap(Point point, double gridMeters)
    {
        var cell = CellDegrees(gridMeters);
        return new Point(SnapValue(point.X, cell), SnapValue(point.Y, cell)) { SRID = point.SRID };
    }

    private static double SnapValue(double value, double cell) =>
        Math.Round(value / cell, MidpointRounding.AwayFromZero) * cell;
}

/// <summary>One protected root above a row plus the caller's ACL flags on that root.</summary>
public readonly record struct ProtectionRootGrant(Feature Root, ObjectPermission AclGranted);
