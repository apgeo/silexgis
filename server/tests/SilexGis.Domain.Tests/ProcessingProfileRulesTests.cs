// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Who sees and who changes a processing profile: a personal one is its author's alone, a group one
/// is read by the group's members, an installation one by everyone, and changing a shared one is
/// the scan-profiles right. Every refusal is paired with the case that is allowed.
/// </summary>
public sealed class ProcessingProfileRulesTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Club = Guid.NewGuid();

    [Fact]
    public void A_personal_profile_is_read_and_changed_by_its_author_only()
    {
        var profile = Profile(ProcessingProfileScope.Personal, Author, null);
        var author = Context(Author);
        var other = Context(Other, Grant(AccessAction.Read | AccessAction.Write | AccessAction.Delete));

        ProcessingProfileRules.MayRead(author, profile).ShouldBeTrue();
        ProcessingProfileRules.MayChange(author, profile, AccessAction.Write).ShouldBeTrue();

        // The right over shared profiles does not reach somebody's personal one.
        ProcessingProfileRules.MayRead(other, profile).ShouldBeFalse();
        ProcessingProfileRules.MayChange(other, profile, AccessAction.Write).ShouldBeFalse();
    }

    [Fact]
    public void A_group_profile_is_read_by_members_and_by_holders_of_read()
    {
        var profile = Profile(ProcessingProfileScope.CavingGroup, Author, Club);

        ProcessingProfileRules.MayRead(Context(Other, groups: [Club]), profile).ShouldBeTrue();
        ProcessingProfileRules.MayRead(Context(Other, Grant(AccessAction.Read)), profile).ShouldBeTrue();
        ProcessingProfileRules.MayRead(Context(Other), profile).ShouldBeFalse();
        ProcessingProfileRules.MayRead(Context(Other, Grant(AccessAction.Write)), profile).ShouldBeFalse();
    }

    [Fact]
    public void A_shared_profile_is_changed_with_the_right_and_not_by_its_author_alone()
    {
        var profile = Profile(ProcessingProfileScope.Installation, Author, null);

        ProcessingProfileRules.MayRead(Context(Other), profile).ShouldBeTrue();
        ProcessingProfileRules.MayChange(Context(Other, Grant(AccessAction.Write)), profile, AccessAction.Write).ShouldBeTrue();
        ProcessingProfileRules.MayChange(Context(Other, Grant(AccessAction.Write)), profile, AccessAction.Delete).ShouldBeFalse();
        // Its author, once without the right, no longer manages what is now shared policy.
        ProcessingProfileRules.MayChange(Context(Author), profile, AccessAction.Write).ShouldBeFalse();
        ProcessingProfileRules.MayManage(Context(Author), ProcessingProfileScope.Installation, Author, AccessAction.Create).ShouldBeFalse();
        ProcessingProfileRules.MayManage(Context(Author), ProcessingProfileScope.Personal, Author, AccessAction.Create).ShouldBeTrue();
    }

    [Fact]
    public void A_built_in_profile_is_changed_by_nobody_not_even_a_full_administrator()
    {
        var profile = Profile(ProcessingProfileScope.Installation, null, null);
        profile.Origin = ProcessingProfileOrigins.BuiltIn;
        var fullAdmin = new AccessContext(Other, true, [], []);

        ProcessingProfileRules.MayRead(fullAdmin, profile).ShouldBeTrue();
        ProcessingProfileRules.MayChange(fullAdmin, profile, AccessAction.Write).ShouldBeFalse();

        profile.Origin = ProcessingProfileOrigins.Edited;
        ProcessingProfileRules.MayChange(fullAdmin, profile, AccessAction.Write).ShouldBeTrue();
    }

    [Fact]
    public void An_imported_origin_keeps_only_the_file_name()
    {
        ProcessingProfileOrigins.Imported("/home/someone/HDMapping_params_1.0_2026.toml")
            .ShouldBe("imported:HDMapping_params_1.0_2026.toml");
        ProcessingProfileOrigins.Imported(new string('a', 400)).Length
            .ShouldBe(ProcessingProfileOrigins.ImportedPrefix.Length + ProcessingProfileOrigins.MaxFileNameLength);
    }

    private static ProcessingProfile Profile(ProcessingProfileScope scope, Guid? owner, Guid? group) => new()
    {
        StepId = "pointcloud.estimate",
        Name = "p",
        Scope = scope,
        OwnerUserId = owner,
        CavingGroupId = group,
    };

    private static AccessEntrySnapshot Grant(AccessAction actions) =>
        new(1, null, AccessSubjectKind.User, Other, AccessEffect.Allow, AccessDomain.ScanProfiles, actions,
            AccessScopeKind.All, null, null, null, null);

    private static AccessContext Context(Guid user, AccessEntrySnapshot? grant = null, IReadOnlyList<Guid>? groups = null) =>
        new(user, false, groups ?? [], grant is null ? [] : [grant]);
}
