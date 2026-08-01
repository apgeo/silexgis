// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Server-side protection of sensitive feature locations. A feature marked
/// <c>LocationProtected</c> is a protection root covering itself and every containment
/// descendant; a caller sees exact coordinates only when they hold ViewExactLocation on
/// EVERY protected root above the row (most-restrictive veto — under the DAG a feature
/// can sit beneath two protected areas). Every path that emits protected coordinates
/// (DTOs, map endpoints, exports) must go through this — never rely on the client to
/// hide data.
///
/// Whether the caller "holds ViewExactLocation on a root" is answered by the access
/// walk (<see cref="AccessEvaluator"/>) against that root — allow entries reaching the
/// root at any level, minus denies per precedence. The veto direction is independent of
/// that grant source: a VEL deny can only further restrict exact view, never widen it
/// past the protected-roots rule.
/// </summary>
public static class LocationProtection
{
    private const double MetersPerDegreeLat = 111_320d;

    /// <summary>One root's veto: an unprotected root never vetoes; a protected one is
    /// satisfied only by the caller's walk-granted ViewExactLocation on it.</summary>
    public static bool RootSatisfied(ProtectionRootGrant root) =>
        !root.Root.LocationProtected || root.ViewExactLocationGranted;

    /// <summary>
    /// The multi-root veto: exact view requires ViewExactLocation on every protected
    /// root in the row's ancestry. An empty set means the row is unprotected.
    /// </summary>
    public static bool CanViewExactLocation(IReadOnlyCollection<ProtectionRootGrant> roots) =>
        roots.All(RootSatisfied);

    /// <summary>
    /// The full row rule, mirrored bit-identically by the SQL exact-view fragment:
    /// full administrators and the row's own owner always see their row exactly (a
    /// foreign protected area above someone's own cave must not lock the owner out);
    /// everyone else needs ViewExactLocation on EVERY protected root above the row
    /// (most-restrictive veto — under the DAG a feature can sit beneath two protected
    /// areas).
    /// </summary>
    public static bool CanViewExactLocation(
        AccessContext? ctx, Feature row, IReadOnlyCollection<ProtectionRootGrant> protectedRoots)
    {
        if (ctx is null)
        {
            return protectedRoots.Count == 0 && !row.LocationProtected;
        }

        return ctx.IsFullAdmin
            || row.OwnerUserId == ctx.UserId
            || CanViewExactLocation(protectedRoots);
    }

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

/// <summary>One protected root above a row plus whether the access walk grants the
/// caller ViewExactLocation on it.</summary>
public readonly record struct ProtectionRootGrant(Feature Root, bool ViewExactLocationGranted);
