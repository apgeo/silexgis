// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The multi-root exact-location rule: full administrators and the row's owner always
/// see their row; everyone else needs ViewExactLocation — as answered by the access
/// walk per root — on EVERY protected root above it (a feature can sit beneath two
/// protected areas — most-restrictive wins). Mirrored bit-identically by the SQL
/// exact-view fragment; the parity harness pins the two against a live database.
/// The grant source moved to access entries; the veto direction did not: a VEL deny in
/// the walk can only make a root unsatisfied, never satisfy one.
/// </summary>
public class ExactLocationRuleTests
{
    private static readonly Guid OwnerA = Guid.CreateVersion7();
    private static readonly Guid OwnerB = Guid.CreateVersion7();
    private static readonly Guid Caller = Guid.CreateVersion7();

    private static AccessContext User(Guid? id = null, bool fullAdmin = false) =>
        new(id ?? Caller, fullAdmin, [], []);

    private static Feature Protected(Guid owner) => new()
    {
        OwnerUserId = owner,
        Visibility = Visibility.Private,
        LocationProtected = true,
        IsProtectedEffective = true,
    };

    private static Feature Row(Guid owner, bool protectedEffective = true) => new()
    {
        OwnerUserId = owner,
        IsProtectedEffective = protectedEffective,
    };

    private static ProtectionRootGrant Grant(Feature root, bool viewExactLocation = false) =>
        new(root, viewExactLocation);

    [Fact]
    public void Unprotected_row_is_exact_for_everyone_signed_in_and_anonymous()
    {
        var row = Row(OwnerA, protectedEffective: false);

        LocationProtection.CanViewExactLocation(User(), row, []).ShouldBeTrue();
        LocationProtection.CanViewExactLocation(null, row, []).ShouldBeTrue();
    }

    [Fact]
    public void Anonymous_never_sees_a_protected_row_exactly()
    {
        var root = Protected(OwnerA);
        LocationProtection.CanViewExactLocation(null, Row(OwnerA), [Grant(root)]).ShouldBeFalse();
    }

    [Fact]
    public void Full_admin_and_row_owner_always_qualify()
    {
        var foreignRoot = Protected(OwnerB); // someone else's protected area above the row

        LocationProtection.CanViewExactLocation(
            User(fullAdmin: true), Row(OwnerA), [Grant(foreignRoot)]).ShouldBeTrue();

        // The owner-lockout rule: my own cave under a foreign protected area stays
        // exactly visible to me.
        LocationProtection.CanViewExactLocation(
            User(id: OwnerA), Row(OwnerA), [Grant(foreignRoot)]).ShouldBeTrue();
    }

    [Fact]
    public void Every_protected_root_must_grant_two_roots_veto()
    {
        // DAG case: the row sits under two protected areas with different owners.
        var rootA = Protected(OwnerA);
        var rootB = Protected(OwnerB);
        var row = Row(OwnerB); // owned by B, but the caller is a stranger

        // The walk granting only one root is not enough — most-restrictive wins.
        LocationProtection.CanViewExactLocation(User(), row,
            [Grant(rootA, viewExactLocation: true), Grant(rootB)]).ShouldBeFalse();

        // Both roots granted qualify.
        LocationProtection.CanViewExactLocation(User(), row,
            [
                Grant(rootA, viewExactLocation: true),
                Grant(rootB, viewExactLocation: true),
            ]).ShouldBeTrue();
    }

    [Fact]
    public void An_unprotected_ancestor_never_vetoes()
    {
        var plainAncestor = new Feature { OwnerUserId = OwnerB, IsProtectedEffective = true };
        LocationProtection.CanViewExactLocation(User(), Row(OwnerB),
            [Grant(plainAncestor)]).ShouldBeTrue();
    }

    [Fact]
    public void Single_root_case_matches_the_old_cave_rule()
    {
        // One protected cave, exact view only through the walk's grant on it. What the
        // walk consults changed (entries instead of ACL rows, membership now editable
        // seed content); the veto here did not.
        var cave = Protected(OwnerA);

        LocationProtection.CanViewExactLocation(User(), Row(OwnerA), [Grant(cave)]).ShouldBeFalse();
        LocationProtection.CanViewExactLocation(User(), Row(OwnerA),
            [Grant(cave, viewExactLocation: true)]).ShouldBeTrue();
    }

    [Fact]
    public void Snap_floor_only_plain_points_are_snappable()
    {
        GeometryClasses.CanSnap(GeometryClass.Point).ShouldBeTrue();
        GeometryClasses.CanSnap(GeometryClass.MultiPoint).ShouldBeFalse();
        GeometryClasses.CanSnap(GeometryClass.LineString).ShouldBeFalse();
        GeometryClasses.CanSnap(GeometryClass.Polygon).ShouldBeFalse();
        GeometryClasses.CanSnap(GeometryClass.MultiLineString).ShouldBeFalse();
        GeometryClasses.CanSnap(GeometryClass.MultiPolygon).ShouldBeFalse();
    }
}
