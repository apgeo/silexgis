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
    private static readonly Guid TeamId = Guid.CreateVersion7();

    private static UserContext User(
        Guid? id = null, string? role = null, (Guid Team, TeamRole Role)? team = null) => new(
        id ?? Stranger,
        role is null ? new HashSet<string>() : new HashSet<string> { role },
        team is null ? new Dictionary<Guid, TeamRole>() : new Dictionary<Guid, TeamRole> { [team.Value.Team] = team.Value.Role });

    private static Cave Cave(Visibility visibility, Guid? teamId = null, bool locationProtected = false) => new()
    {
        Name = "x",
        OwnerUserId = Owner,
        TeamId = teamId,
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
    [InlineData(Visibility.Team, false)]
    [InlineData(Visibility.Private, false)]
    public void Visibility_governs_read_for_unrelated_users(Visibility visibility, bool expected)
    {
        PermissionEvaluator.Can(User(), Cave(visibility), ObjectPermission.Read).ShouldBe(expected);
        // Visibility never grants write.
        PermissionEvaluator.Can(User(), Cave(visibility), ObjectPermission.Write).ShouldBeFalse();
    }

    [Fact]
    public void Team_member_reads_and_writes_team_objects_but_cannot_delete()
    {
        var member = User(team: (TeamId, TeamRole.Member));
        var cave = Cave(Visibility.Team, TeamId);

        PermissionEvaluator.Can(member, cave, ObjectPermission.Read).ShouldBeTrue();
        PermissionEvaluator.Can(member, cave, ObjectPermission.Write).ShouldBeTrue();
        PermissionEvaluator.Can(member, cave, ObjectPermission.ViewExactLocation).ShouldBeTrue();
        PermissionEvaluator.Can(member, cave, ObjectPermission.Delete).ShouldBeFalse();
        PermissionEvaluator.Can(member, cave, ObjectPermission.ManagePermissions).ShouldBeFalse();
    }

    [Fact]
    public void Team_admin_gets_full_team_object_control()
    {
        var teamAdmin = User(team: (TeamId, TeamRole.Admin));
        var cave = Cave(Visibility.Team, TeamId);

        PermissionEvaluator.Can(teamAdmin, cave, ObjectPermission.Delete).ShouldBeTrue();
        PermissionEvaluator.Can(teamAdmin, cave, ObjectPermission.ManagePermissions).ShouldBeTrue();
    }

    [Fact]
    public void Membership_in_another_team_grants_nothing()
    {
        var otherTeamMember = User(team: (Guid.CreateVersion7(), TeamRole.Owner));
        var cave = Cave(Visibility.Team, TeamId);

        PermissionEvaluator.Can(otherTeamMember, cave, ObjectPermission.Read).ShouldBeFalse();
    }
}
