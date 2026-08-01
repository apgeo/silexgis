// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>
/// One user's profile as a particular viewer is allowed to see it. Fields the viewer may not
/// see are simply <c>null</c>.
/// </summary>
/// <remarks>
/// Deliberately carries no visibility settings. The viewer never learns another user's choices,
/// which structurally prevents a client from becoming the enforcer (it has nothing to filter
/// on) and avoids the second-order disclosure of "she hides her phone" — which itself confirms
/// a phone number exists. For the same reason there is no list of redacted field names: unlike
/// a change history, where the property set is fixed and already public, the set of
/// populated-but-hidden profile fields is itself information about the subject.
/// </remarks>
public sealed record PublicProfile(
    Guid Id,
    string Label,
    string? DisplayName,
    Guid? AvatarFileId,
    string? AvatarPreset,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    Guid? CavingClubId,
    string? Bio,
    IReadOnlyList<PublicAddress> Addresses);

/// <summary>
/// One of a user's addresses as a viewer may see it. <see cref="Longitude"/>/<see cref="Latitude"/>
/// are present only when the viewer may see the map point as well as the address text.
/// </summary>
public sealed record PublicAddress(
    Guid Id,
    string Label,
    string? Country,
    string? City,
    string? AddressText,
    double? Longitude,
    double? Latitude);
