// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class LocationProtectionTests
{
    [Fact]
    public void Snap_is_deterministic_and_grid_aligned()
    {
        var point = new Point(25.44721, 45.53127) { SRID = 4326 };

        var a = LocationProtection.Snap(point, 5000);
        var b = LocationProtection.Snap(point, 5000);

        a.ShouldBe(b);
        var cell = LocationProtection.CellDegrees(5000);
        (a.X / cell).ShouldBe(Math.Round(a.X / cell), 1e-9);
        (a.Y / cell).ShouldBe(Math.Round(a.Y / cell), 1e-9);
    }

    [Fact]
    public void Snap_moves_the_point_at_most_one_cell_diagonal()
    {
        var point = new Point(25.44721, 45.53127) { SRID = 4326 };
        var snapped = LocationProtection.Snap(point, 5000);

        var cell = LocationProtection.CellDegrees(5000);
        Math.Abs(snapped.X - point.X).ShouldBeLessThanOrEqualTo(cell / 2 + 1e-12);
        Math.Abs(snapped.Y - point.Y).ShouldBeLessThanOrEqualTo(cell / 2 + 1e-12);
    }

    [Fact]
    public void Nearby_points_share_a_cell_so_averaging_cannot_recover_the_location()
    {
        var cell = LocationProtection.CellDegrees(5000);
        var basePoint = new Point(25.4, 45.5) { SRID = 4326 };
        var nudged = new Point(25.4 + cell / 10, 45.5 + cell / 10) { SRID = 4326 };

        LocationProtection.Snap(basePoint, 5000).ShouldBe(LocationProtection.Snap(nudged, 5000));
    }
}
