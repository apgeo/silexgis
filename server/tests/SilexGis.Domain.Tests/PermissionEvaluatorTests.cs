// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;

namespace SilexGis.Domain.Tests;

public class PermissionEvaluatorTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Stranger = Guid.CreateVersion7();
    private static readonly Guid CavingGroupId = Guid.CreateVersion7();

    private static UserContext User(
        Guid? id = null, string? role = null, (Guid CavingGroup, CavingGroupRole Role)? cavingGroup = null) => new(
        id ?? Stranger,
        role is null ? new HashSet<string>() : new HashSet<string> { role },
        cavingGroup is null ? new Dictionary<Guid, CavingGroupRole>() : new Dictionary<Guid, CavingGroupRole> { [cavingGroup.Value.CavingGroup] = cavingGroup.Value.Role });

    // The evaluator works on IProtectedEntity; a cave feature is the canonical instance.
    private static Feature Cave(Visibility visibility, Guid? cavingGroupId = null, bool locationProtected = false) => new()
    {
        Kind = FeatureKind.Cave,
        Name = "x",
        OwnerUserId = Owner,
        CavingGroupId = cavingGroupId,
        Visibility = visibility,
        LocationProtected = locationProtected,
    };

    [Fact]
    public void Anonymous_gets_nothing()
    {
        PermissionEvaluator.Can(null, Cave(Visibility.Public), ObjectPermission.Read).ShouldBeFalse();
    }

    [Fact]
    public void Admin_and_owner_get_everything()
    {
        foreach (var permission in new[]
        {
            ObjectPermission.Read, ObjectPermission.Write, ObjectPermission.Delete,
            ObjectPermission.Share, ObjectPermission.ManagePermissions, ObjectPermission.ViewExactLocation,
        })
        {
            PermissionEvaluator.Can(User(role: GlobalRoles.Admin), Cave(Visibility.Private), permission).ShouldBeTrue();
            PermissionEvaluator.Can(User(id: Owner), Cave(Visibility.Private), permission).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(Visibility.Public, true)]
    [InlineData(Visibility.Authenticated, true)]
    [InlineData(Visibility.CavingGroup, false)]
    [InlineData(Visibility.Private, false)]
    public void Visibility_governs_read_for_unrelated_users(Visibility visibility, bool expected)
    {
        PermissionEvaluator.Can(User(), Cave(visibility), ObjectPermission.Read).ShouldBe(expected);
        // Visibility never grants write.
        PermissionEvaluator.Can(User(), Cave(visibility), ObjectPermission.Write).ShouldBeFalse();
    }

    [Fact]
    public void CavingGroup_member_reads_and_writes_caving_group_objects_but_cannot_delete()
    {
        var member = User(cavingGroup: (CavingGroupId, CavingGroupRole.Member));
        var cave = Cave(Visibility.CavingGroup, CavingGroupId);

        PermissionEvaluator.Can(member, cave, ObjectPermission.Read).ShouldBeTrue();
        PermissionEvaluator.Can(member, cave, ObjectPermission.Write).ShouldBeTrue();
        PermissionEvaluator.Can(member, cave, ObjectPermission.ViewExactLocation).ShouldBeTrue();
        PermissionEvaluator.Can(member, cave, ObjectPermission.Delete).ShouldBeFalse();
        PermissionEvaluator.Can(member, cave, ObjectPermission.ManagePermissions).ShouldBeFalse();
    }

    [Fact]
    public void CavingGroup_admin_gets_full_caving_group_object_control()
    {
        var cavingGroupAdmin = User(cavingGroup: (CavingGroupId, CavingGroupRole.Admin));
        var cave = Cave(Visibility.CavingGroup, CavingGroupId);

        PermissionEvaluator.Can(cavingGroupAdmin, cave, ObjectPermission.Delete).ShouldBeTrue();
        PermissionEvaluator.Can(cavingGroupAdmin, cave, ObjectPermission.ManagePermissions).ShouldBeTrue();
    }

    [Fact]
    public void Membership_in_another_caving_group_grants_nothing()
    {
        var otherCavingGroupMember = User(cavingGroup: (Guid.CreateVersion7(), CavingGroupRole.Owner));
        var cave = Cave(Visibility.CavingGroup, CavingGroupId);

        PermissionEvaluator.Can(otherCavingGroupMember, cave, ObjectPermission.Read).ShouldBeFalse();
    }
}
