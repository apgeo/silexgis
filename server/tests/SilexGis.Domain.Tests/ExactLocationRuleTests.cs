// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The multi-root exact-location rule: admin and the row's owner always see their row;
/// everyone else needs ViewExactLocation on EVERY protected root above it (a feature can
/// sit beneath two protected areas — most-restrictive wins). Mirrored bit-identically by
/// the SQL exact-view fragment; the parity harness pins the two against a live database.
/// </summary>
public class ExactLocationRuleTests
{
    private static readonly Guid OwnerA = Guid.CreateVersion7();
    private static readonly Guid OwnerB = Guid.CreateVersion7();
    private static readonly Guid Caller = Guid.CreateVersion7();
    private static readonly Guid CavingGroupId = Guid.CreateVersion7();

    private static UserContext User(
        Guid? id = null, string? role = null, (Guid CavingGroup, CavingGroupRole Role)? cavingGroup = null) => new(
        id ?? Caller,
        role is null ? [] : new HashSet<string> { role },
        cavingGroup is null ? [] : new Dictionary<Guid, CavingGroupRole> { [cavingGroup.Value.CavingGroup] = cavingGroup.Value.Role });

    private static Feature Protected(Guid owner, Guid? cavingGroupId = null) => new()
    {
        OwnerUserId = owner,
        CavingGroupId = cavingGroupId,
        Visibility = Visibility.Private,
        LocationProtected = true,
        IsProtectedEffective = true,
    };

    private static Feature Row(Guid owner, bool protectedEffective = true) => new()
    {
        OwnerUserId = owner,
        IsProtectedEffective = protectedEffective,
    };

    private static ProtectionRootGrant Grant(Feature root, ObjectPermission acl = ObjectPermission.None) =>
        new(root, acl);

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
    public void Admin_and_row_owner_always_qualify()
    {
        var foreignRoot = Protected(OwnerB); // someone else's protected area above the row

        LocationProtection.CanViewExactLocation(
            User(role: GlobalRoles.Admin), Row(OwnerA), [Grant(foreignRoot)]).ShouldBeTrue();

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

        // Grant on only one root is not enough — most-restrictive wins.
        LocationProtection.CanViewExactLocation(User(), row,
            [Grant(rootA, ObjectPermission.ViewExactLocation), Grant(rootB)]).ShouldBeFalse();

        // Grants on both roots qualify.
        LocationProtection.CanViewExactLocation(User(), row,
            [
                Grant(rootA, ObjectPermission.ViewExactLocation),
                Grant(rootB, ObjectPermission.ViewExactLocation),
            ]).ShouldBeTrue();
    }

    [Fact]
    public void Root_owner_and_root_caving_group_membership_satisfy_that_root()
    {
        // Owning one root satisfies it; the other still needs a grant.
        var myRoot = Protected(Caller);
        var foreignRoot = Protected(OwnerB);

        LocationProtection.CanViewExactLocation(User(), Row(OwnerB),
            [Grant(myRoot), Grant(foreignRoot)]).ShouldBeFalse();
        LocationProtection.CanViewExactLocation(User(), Row(OwnerB),
            [Grant(myRoot), Grant(foreignRoot, ObjectPermission.ViewExactLocation)]).ShouldBeTrue();

        // CavingGroup membership on the root's caving group implies exact view (kept code semantics,
        // revisited by the ruleset redesign).
        var cavingGroupRoot = Protected(OwnerB, CavingGroupId);
        LocationProtection.CanViewExactLocation(
            User(cavingGroup: (CavingGroupId, CavingGroupRole.Member)), Row(OwnerB), [Grant(cavingGroupRoot)]).ShouldBeTrue();
    }

    [Fact]
    public void Single_root_case_matches_the_old_cave_rule()
    {
        // Pre-supertype behavior: one protected cave, exact view via explicit grant.
        var cave = Protected(OwnerA);

        LocationProtection.CanViewExactLocation(User(), Row(OwnerA), [Grant(cave)]).ShouldBeFalse();
        LocationProtection.CanViewExactLocation(User(), Row(OwnerA),
            [Grant(cave, ObjectPermission.ViewExactLocation)]).ShouldBeTrue();
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
