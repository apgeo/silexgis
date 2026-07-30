// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;

namespace SilexGis.Domain.Profiles;

/// <summary>How a viewer relates to the user whose profile is being read.</summary>
public enum ProfileViewerRelation
{
    /// <summary>No signed-in user.</summary>
    Anonymous,

    /// <summary>Signed in, shares no team with the subject.</summary>
    Authenticated,

    /// <summary>Signed in and shares at least one team with the subject.</summary>
    SharesTeam,

    /// <summary>The subject reading their own profile.</summary>
    Self,
}

/// <summary>
/// Server-side protection of user profile data. Every path that emits another user's profile
/// (DTOs, pickers, member lists, attribution rows, exports, email bodies) must go through this —
/// never rely on the client to hide data.
/// </summary>
/// <remarks>
/// <para>
/// Administrators are deliberately <b>not</b> given a bypass. Personal contact data is not
/// content, and an installation admin already has every content permission without needing to
/// read a member's home address. Granting one would be a single extra arm here plus a test, and
/// should then be audit-logged the way exact-location grants are.
/// </para>
/// <para>
/// The paired <see cref="ProfileQueryExtensions.WhereFieldVisibleTo{T}"/> is the SQL-side twin of
/// <see cref="CanView"/>. Gating a projection while leaving a WHERE clause open turns the filter
/// into an oracle, so the two must change together or not at all; a parity test enforces it.
/// </para>
/// </remarks>
public static class ProfileProtection
{
    /// <summary>
    /// Placed on a profile that has no usable name of its own. Prefixed rather than bare so a
    /// generated label can never be mistaken for a chosen one.
    /// </summary>
    public const string AnonymousLabelPrefix = "user-";

    /// <summary>
    /// How the viewer relates to <paramref name="subjectUserId"/>. <paramref name="subjectTeamIds"/>
    /// are the teams the subject belongs to. Self is checked first, so a user always sees their
    /// own profile in full.
    /// </summary>
    public static ProfileViewerRelation Relate(
        UserContext? viewer, Guid subjectUserId, IReadOnlySet<Guid> subjectTeamIds)
    {
        if (viewer is null)
        {
            return ProfileViewerRelation.Anonymous;
        }

        if (viewer.UserId == subjectUserId)
        {
            return ProfileViewerRelation.Self;
        }

        foreach (var teamId in viewer.Teams.Keys)
        {
            if (subjectTeamIds.Contains(teamId))
            {
                return ProfileViewerRelation.SharesTeam;
            }
        }

        return ProfileViewerRelation.Authenticated;
    }

    /// <summary>The whole rule: may a viewer in this relation see a field set to this audience?</summary>
    public static bool CanView(ProfileVisibility setting, ProfileViewerRelation relation) => relation switch
    {
        ProfileViewerRelation.Self => true,
        // Load-bearing and unconditional: because ProfileVisibility has no Public level, no
        // setting value can expose personal data to a caller who is not signed in.
        ProfileViewerRelation.Anonymous => false,
        ProfileViewerRelation.SharesTeam => setting >= ProfileVisibility.Team,
        ProfileViewerRelation.Authenticated => setting >= ProfileVisibility.Authenticated,
        _ => false,
    };

    /// <summary>
    /// The name to show for a user in attribution rows and pickers, for viewers who may not see
    /// their real name or email.
    /// </summary>
    /// <remarks>
    /// The user name is skipped when it equals the email address. Registration, the admin
    /// bootstrap and external federation all create accounts with the email as the user name, so
    /// a plain "display name, or else user name" fallback would render the email address of every
    /// user who never set a display name — defeating the email setting on six surfaces at once.
    /// The comparison is exact rather than a "looks like an email" guess.
    /// </remarks>
    public static string Label(Guid userId, string? displayName, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        if (!string.IsNullOrWhiteSpace(userName)
            && !string.Equals(userName, email, StringComparison.OrdinalIgnoreCase))
        {
            return userName;
        }

        return AnonymousLabelPrefix + userId.ToString("N")[..8];
    }

    /// <summary>
    /// Projects a profile down to what <paramref name="relation"/> may see. Hidden fields come
    /// back null; hidden addresses come back as an empty list.
    /// </summary>
    public static PublicProfile Project(
        IUserProfile subject,
        ProfileVisibilitySettings settings,
        IReadOnlyList<UserAddress> addresses,
        ProfileViewerRelation relation)
    {
        var realName = CanView(settings.For(ProfileField.RealName), relation);
        var showAddresses = CanView(settings.For(ProfileField.Address), relation);
        // ANDed, never checked alone: a visible point beside a hidden address is still the doorstep.
        var showPoints = showAddresses && CanView(settings.For(ProfileField.AddressPoint), relation);

        return new PublicProfile(
            subject.Id,
            Label(subject.Id, subject.DisplayName, subject.UserNameValue, subject.EmailValue),
            subject.DisplayName,
            subject.AvatarFileId,
            subject.AvatarPreset,
            realName ? subject.FirstName : null,
            realName ? subject.LastName : null,
            CanView(settings.For(ProfileField.Email), relation) ? subject.EmailValue : null,
            CanView(settings.For(ProfileField.Phone), relation) ? subject.PhoneNumberValue : null,
            CanView(settings.For(ProfileField.CavingClub), relation) ? subject.CavingClub : null,
            CanView(settings.For(ProfileField.Bio), relation) ? subject.Bio : null,
            showAddresses
                ? [.. addresses.OrderBy(a => a.SortOrder).Select(a => ToPublic(a, showPoints))]
                : []);
    }

    /// <summary>The settings carried by an Identity user, as one record.</summary>
    public static ProfileVisibilitySettings SettingsOf(IUserProfile subject) => new(
        subject.RealNameVisibility,
        subject.BioVisibility,
        subject.EmailVisibility,
        subject.PhoneVisibility,
        subject.CavingClubVisibility,
        subject.AddressVisibility,
        subject.AddressPointVisibility);

    private static PublicAddress ToPublic(UserAddress address, bool withPoint) => new(
        address.Id,
        address.Label,
        address.Country,
        address.City,
        address.AddressText,
        withPoint ? address.Geom?.X : null,
        withPoint ? address.Geom?.Y : null);
}
