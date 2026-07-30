// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>The subject's chosen audience for each governed profile field.</summary>
public sealed record ProfileVisibilitySettings(
    ProfileVisibility RealName,
    ProfileVisibility Bio,
    ProfileVisibility Email,
    ProfileVisibility Phone,
    ProfileVisibility CavingClub,
    ProfileVisibility Address,
    ProfileVisibility AddressPoint)
{
    /// <summary>
    /// The safe default every account starts on, and the value the stored columns default to.
    /// Existing accounts must land here on upgrade: any other default would retroactively
    /// publish personal data that was never offered to anyone.
    /// </summary>
    public static ProfileVisibilitySettings AllPrivate { get; } = new(
        ProfileVisibility.Private,
        ProfileVisibility.Private,
        ProfileVisibility.Private,
        ProfileVisibility.Private,
        ProfileVisibility.Private,
        ProfileVisibility.Private,
        ProfileVisibility.Private);

    /// <summary>
    /// The setting governing <paramref name="field"/>. An unmapped field returns Private rather
    /// than throwing: a field added to the enum but forgotten here must fail closed (hidden),
    /// never leak. A unit test asserts every member is mapped, so that arm stays unreachable.
    /// </summary>
    public ProfileVisibility For(ProfileField field) => field switch
    {
        ProfileField.RealName => RealName,
        ProfileField.Bio => Bio,
        ProfileField.Email => Email,
        ProfileField.Phone => Phone,
        ProfileField.CavingClub => CavingClub,
        ProfileField.Address => Address,
        ProfileField.AddressPoint => AddressPoint,
        _ => ProfileVisibility.Private,
    };
}
