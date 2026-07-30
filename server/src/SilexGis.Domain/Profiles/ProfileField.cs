// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>
/// The profile fields whose audience the subject controls, one per stored visibility column.
/// Not persisted — this is the key into <see cref="ProfileVisibilitySettings"/>.
/// </summary>
/// <remarks>
/// Username, display name and avatar are deliberately absent: they are the only handles the
/// team member list, trip participant list, ACL editor, file version list, audit row and
/// history row have to render, so making them hideable would break attribution across six
/// surfaces. A cave's existence is likewise never hidden — only its coordinates.
/// </remarks>
public enum ProfileField
{
    /// <summary>First name and surname together — a surname alone is not a separate disclosure.</summary>
    RealName,

    Bio,

    Email,

    Phone,

    CavingClub,

    /// <summary>The textual part of the home addresses (country, city, street).</summary>
    Address,

    /// <summary>
    /// The optional map point on an address. Strictly more sensitive than "Braşov, Romania",
    /// so it has its own setting and a user can share the city without their doorstep.
    /// </summary>
    AddressPoint,
}
