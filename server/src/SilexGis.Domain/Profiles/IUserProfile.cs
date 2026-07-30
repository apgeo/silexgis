// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>
/// The profile shape of an application user, so this layer can decide and filter visibility
/// without referencing the Identity type (which lives in Infrastructure). The Identity user
/// implements this the same way it already implements <see cref="ITimestamped"/>.
/// </summary>
/// <remarks>
/// The <c>*Value</c> suffixes exist to avoid colliding with the properties ASP.NET Identity
/// already declares (<c>UserName</c>, <c>Email</c>, <c>PhoneNumber</c>); implementations
/// forward to those.
/// </remarks>
public interface IUserProfile
{
    Guid Id { get; }

    string? UserNameValue { get; }

    string? DisplayName { get; }

    string? FirstName { get; }

    string? LastName { get; }

    string? EmailValue { get; }

    string? PhoneNumberValue { get; }

    string? CavingClub { get; }

    string? Bio { get; }

    Guid? AvatarFileId { get; }

    string? AvatarPreset { get; }

    ProfileVisibility RealNameVisibility { get; }

    ProfileVisibility BioVisibility { get; }

    ProfileVisibility EmailVisibility { get; }

    ProfileVisibility PhoneVisibility { get; }

    ProfileVisibility CavingClubVisibility { get; }

    ProfileVisibility AddressVisibility { get; }

    ProfileVisibility AddressPointVisibility { get; }
}
