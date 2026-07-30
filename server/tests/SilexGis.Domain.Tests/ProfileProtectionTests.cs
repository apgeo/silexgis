// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;

namespace SilexGis.Domain.Tests;

public class ProfileProtectionTests
{
    private static readonly Guid Subject = Guid.CreateVersion7();
    private static readonly Guid Viewer = Guid.CreateVersion7();
    private static readonly Guid SharedTeam = Guid.CreateVersion7();
    private static readonly Guid OtherTeam = Guid.CreateVersion7();

    private static UserContext User(Guid id, params Guid[] teams) =>
        new(id, new HashSet<string>(), teams.ToDictionary(t => t, _ => TeamRole.Member));

    private static UserContext Admin(Guid id) =>
        new(id, new HashSet<string> { GlobalRoles.Admin }, new Dictionary<Guid, TeamRole>());

    private sealed class FakeProfile : IUserProfile
    {
        public Guid Id { get; init; } = Subject;
        public string? UserNameValue { get; init; } = "handle";
        public string? DisplayName { get; init; } = "Ana Pop";
        public string? FirstName { get; init; } = "Ana";
        public string? LastName { get; init; } = "Pop";
        public string? EmailValue { get; init; } = "ana@example.org";
        public string? PhoneNumberValue { get; init; } = "+40 700 111 222";
        public string? CavingClub { get; init; } = "Silex Braşov";
        public string? Bio { get; init; } = "Caver since 2010.";
        public Guid? AvatarFileId { get; init; }
        public string? AvatarPreset { get; init; } = "bat";
        public ProfileVisibility RealNameVisibility { get; init; } = ProfileVisibility.Private;
        public ProfileVisibility BioVisibility { get; init; } = ProfileVisibility.Private;
        public ProfileVisibility EmailVisibility { get; init; } = ProfileVisibility.Private;
        public ProfileVisibility PhoneVisibility { get; init; } = ProfileVisibility.Private;
        public ProfileVisibility CavingClubVisibility { get; init; } = ProfileVisibility.Private;
        public ProfileVisibility AddressVisibility { get; init; } = ProfileVisibility.Private;
        public ProfileVisibility AddressPointVisibility { get; init; } = ProfileVisibility.Private;
    }

    private static UserAddress Address() => new()
    {
        Label = "Home",
        Country = "Romania",
        City = "Braşov",
        AddressText = "Str. Lungă 1",
        Geom = new Point(25.59, 45.65) { SRID = 4326 },
    };

    [Theory]
    // Self sees every setting.
    [InlineData(ProfileViewerRelation.Self, ProfileVisibility.Private, true)]
    [InlineData(ProfileViewerRelation.Self, ProfileVisibility.Team, true)]
    [InlineData(ProfileViewerRelation.Self, ProfileVisibility.Authenticated, true)]
    // Anonymous sees nothing, whatever the setting.
    [InlineData(ProfileViewerRelation.Anonymous, ProfileVisibility.Private, false)]
    [InlineData(ProfileViewerRelation.Anonymous, ProfileVisibility.Team, false)]
    [InlineData(ProfileViewerRelation.Anonymous, ProfileVisibility.Authenticated, false)]
    // A teammate sees Team and above.
    [InlineData(ProfileViewerRelation.SharesTeam, ProfileVisibility.Private, false)]
    [InlineData(ProfileViewerRelation.SharesTeam, ProfileVisibility.Team, true)]
    [InlineData(ProfileViewerRelation.SharesTeam, ProfileVisibility.Authenticated, true)]
    // Any other signed-in user sees only Authenticated.
    [InlineData(ProfileViewerRelation.Authenticated, ProfileVisibility.Private, false)]
    [InlineData(ProfileViewerRelation.Authenticated, ProfileVisibility.Team, false)]
    [InlineData(ProfileViewerRelation.Authenticated, ProfileVisibility.Authenticated, true)]
    public void Can_view_covers_every_relation_and_setting(
        ProfileViewerRelation relation, ProfileVisibility setting, bool expected) =>
        ProfileProtection.CanView(setting, relation).ShouldBe(expected);

    [Fact]
    public void Anonymous_sees_no_field_at_any_setting()
    {
        // The load-bearing invariant: there is no Public level, so nothing a user can choose
        // exposes personal data to a caller who is not signed in.
        foreach (var setting in Enum.GetValues<ProfileVisibility>())
        {
            ProfileProtection.CanView(setting, ProfileViewerRelation.Anonymous).ShouldBeFalse();
        }
    }

    [Fact]
    public void Visibility_values_are_the_schema_contract()
    {
        ((short)ProfileVisibility.Private).ShouldBe((short)0);
        ((short)ProfileVisibility.Team).ShouldBe((short)1);
        ((short)ProfileVisibility.Authenticated).ShouldBe((short)2);
    }

    [Fact]
    public void Default_settings_are_all_private() =>
        Enum.GetValues<ProfileField>()
            .ShouldAllBe(f => ProfileVisibilitySettings.AllPrivate.For(f) == ProfileVisibility.Private);

    [Fact]
    public void Every_governed_field_is_mapped()
    {
        // Keeps the fail-closed default arm of For() unreachable in production: a new field must
        // be wired up rather than silently reading as Private.
        var settings = new ProfileVisibilitySettings(
            ProfileVisibility.Authenticated,
            ProfileVisibility.Authenticated,
            ProfileVisibility.Authenticated,
            ProfileVisibility.Authenticated,
            ProfileVisibility.Authenticated,
            ProfileVisibility.Authenticated,
            ProfileVisibility.Authenticated);

        foreach (var field in Enum.GetValues<ProfileField>())
        {
            settings.For(field).ShouldBe(ProfileVisibility.Authenticated, $"{field} is not mapped");
        }
    }

    [Fact]
    public void Relate_prefers_self_then_shared_team()
    {
        ProfileProtection.Relate(User(Subject, OtherTeam), Subject, new HashSet<Guid>())
            .ShouldBe(ProfileViewerRelation.Self);
        ProfileProtection.Relate(User(Viewer, SharedTeam), Subject, new HashSet<Guid> { SharedTeam })
            .ShouldBe(ProfileViewerRelation.SharesTeam);
        ProfileProtection.Relate(User(Viewer, OtherTeam), Subject, new HashSet<Guid> { SharedTeam })
            .ShouldBe(ProfileViewerRelation.Authenticated);
        ProfileProtection.Relate(null, Subject, new HashSet<Guid> { SharedTeam })
            .ShouldBe(ProfileViewerRelation.Anonymous);
    }

    [Fact]
    public void Relate_gives_an_admin_no_bypass()
    {
        // Contact data is not content: an installation admin reads a member's phone number only
        // if that member published it. Reversing this is one arm here plus this assertion.
        ProfileProtection.Relate(Admin(Viewer), Subject, new HashSet<Guid>())
            .ShouldBe(ProfileViewerRelation.Authenticated);
        ProfileProtection.Relate(Admin(Subject), Subject, new HashSet<Guid>())
            .ShouldBe(ProfileViewerRelation.Self);
    }

    [Fact]
    public void Project_hides_every_private_field_from_a_stranger()
    {
        var profile = new FakeProfile();

        var result = ProfileProtection.Project(
            profile,
            ProfileProtection.SettingsOf(profile),
            [Address()],
            ProfileViewerRelation.Authenticated);

        result.FirstName.ShouldBeNull();
        result.LastName.ShouldBeNull();
        result.Email.ShouldBeNull();
        result.PhoneNumber.ShouldBeNull();
        result.CavingClub.ShouldBeNull();
        result.Bio.ShouldBeNull();
        result.Addresses.ShouldBeEmpty();
        // The identity handles are never hidden — attribution rows depend on them.
        result.DisplayName.ShouldBe("Ana Pop");
        result.Label.ShouldBe("Ana Pop");
        result.AvatarPreset.ShouldBe("bat");
    }

    [Fact]
    public void Project_shows_a_teammate_what_the_subject_shared_with_their_teams()
    {
        var profile = new FakeProfile
        {
            RealNameVisibility = ProfileVisibility.Team,
            EmailVisibility = ProfileVisibility.Private,
            PhoneVisibility = ProfileVisibility.Authenticated,
            AddressVisibility = ProfileVisibility.Team,
        };

        var result = ProfileProtection.Project(
            profile, ProfileProtection.SettingsOf(profile), [Address()], ProfileViewerRelation.SharesTeam);

        result.FirstName.ShouldBe("Ana");
        result.LastName.ShouldBe("Pop");
        result.PhoneNumber.ShouldBe("+40 700 111 222");
        result.Email.ShouldBeNull();
        result.Addresses.Count.ShouldBe(1);
        result.Addresses[0].City.ShouldBe("Braşov");
    }

    [Fact]
    public void Project_withholds_the_map_point_while_showing_the_address_text()
    {
        var profile = new FakeProfile
        {
            AddressVisibility = ProfileVisibility.Authenticated,
            AddressPointVisibility = ProfileVisibility.Private,
        };

        var result = ProfileProtection.Project(
            profile, ProfileProtection.SettingsOf(profile), [Address()], ProfileViewerRelation.Authenticated);

        result.Addresses.Count.ShouldBe(1);
        result.Addresses[0].City.ShouldBe("Braşov");
        result.Addresses[0].Longitude.ShouldBeNull();
        result.Addresses[0].Latitude.ShouldBeNull();
    }

    [Fact]
    public void Project_withholds_the_map_point_when_the_address_itself_is_hidden()
    {
        // The point setting is ANDed with the address setting, never read alone: a visible point
        // beside a hidden address is still the doorstep.
        var profile = new FakeProfile
        {
            AddressVisibility = ProfileVisibility.Private,
            AddressPointVisibility = ProfileVisibility.Authenticated,
        };

        var result = ProfileProtection.Project(
            profile, ProfileProtection.SettingsOf(profile), [Address()], ProfileViewerRelation.Authenticated);

        result.Addresses.ShouldBeEmpty();
    }

    [Fact]
    public void Project_gives_the_subject_everything()
    {
        var profile = new FakeProfile();

        var result = ProfileProtection.Project(
            profile, ProfileProtection.SettingsOf(profile), [Address()], ProfileViewerRelation.Self);

        result.Email.ShouldBe("ana@example.org");
        result.PhoneNumber.ShouldBe("+40 700 111 222");
        result.Addresses[0].Longitude!.Value.ShouldBe(25.59, 1e-9);
        result.Addresses[0].Latitude!.Value.ShouldBe(45.65, 1e-9);
    }

    [Fact]
    public void Label_prefers_the_display_name() =>
        ProfileProtection.Label(Subject, "Ana Pop", "ana@example.org", "ana@example.org")
            .ShouldBe("Ana Pop");

    [Fact]
    public void Label_uses_a_chosen_user_name() =>
        ProfileProtection.Label(Subject, null, "anapop", "ana@example.org").ShouldBe("anapop");

    [Fact]
    public void Label_never_falls_back_to_the_email_address()
    {
        // Accounts are created with the email as the user name, so a plain "display name or user
        // name" fallback would publish the address of everyone who never set a display name.
        var label = ProfileProtection.Label(Subject, null, "ana@example.org", "ana@example.org");

        label.ShouldNotContain("@");
        label.ShouldStartWith(ProfileProtection.AnonymousLabelPrefix);
    }

    [Fact]
    public void Label_ignores_case_when_comparing_the_user_name_to_the_email()
    {
        var label = ProfileProtection.Label(Subject, null, "Ana@Example.org", "ana@example.org");

        label.ShouldNotContain("@");
    }

    [Fact]
    public void Label_is_stable_for_the_same_user() =>
        ProfileProtection.Label(Subject, null, null, null)
            .ShouldBe(ProfileProtection.Label(Subject, null, null, null));
}
