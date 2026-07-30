// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Profiles;

namespace SilexGis.Infrastructure.Identity;

/// <summary>
/// Application user — ASP.NET Identity with uuid v7 keys plus profile fields.
/// Lives in Infrastructure so Domain stays framework-free;
/// domain code references users by Guid only.
/// </summary>
/// <remarks>
/// Deliberately not auditable: the audit diff of a user row would copy personal data into a table
/// whose only redaction rule covers cave coordinates.
/// </remarks>
public class SilexGisUser : IdentityUser<Guid>, ITimestamped, IUserProfile
{
    public SilexGisUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string? DisplayName { get; set; }

    public string? Bio { get; set; }

    public string Locale { get; set; } = "en";

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? CavingClub { get; set; }

    public Guid? AvatarFileId { get; set; }

    /// <summary>Id of a built-in avatar the user picked instead of uploading one.</summary>
    public string? AvatarPreset { get; set; }

    /// <summary>
    /// Address requested by a change that has not been confirmed yet. The live
    /// <see cref="IdentityUser{TKey}.Email"/> is untouched until the emailed token comes back.
    /// </summary>
    public string? PendingEmail { get; set; }

    /// <summary>When the pending-change message was last sent; throttles resends.</summary>
    public DateTimeOffset? PendingEmailRequestedAt { get; set; }

    public ProfileVisibility RealNameVisibility { get; set; } = ProfileVisibility.Private;

    public ProfileVisibility BioVisibility { get; set; } = ProfileVisibility.Private;

    public ProfileVisibility EmailVisibility { get; set; } = ProfileVisibility.Private;

    public ProfileVisibility PhoneVisibility { get; set; } = ProfileVisibility.Private;

    public ProfileVisibility CavingClubVisibility { get; set; } = ProfileVisibility.Private;

    public ProfileVisibility AddressVisibility { get; set; } = ProfileVisibility.Private;

    public ProfileVisibility AddressPointVisibility { get; set; } = ProfileVisibility.Private;

    /// <summary>Master switch for notification email; individual categories sit beside it.</summary>
    public bool NotifyEmailEnabled { get; set; } = true;

    public NotificationDigest NotifyDigest { get; set; } = NotificationDigest.Immediate;

    /// <summary>
    /// Client-owned interface preferences (appearance, density, reduced motion) as a JSON object.
    /// The server stores and returns it without interpreting the keys — see the preferences
    /// endpoint for why this one thing is deliberately schemaless.
    /// </summary>
    public string UiPreferences { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // IUserProfile forwards to the Identity-owned properties, whose names it cannot reuse.
    string? IUserProfile.UserNameValue => UserName;

    string? IUserProfile.EmailValue => Email;

    string? IUserProfile.PhoneNumberValue => PhoneNumber;
}
