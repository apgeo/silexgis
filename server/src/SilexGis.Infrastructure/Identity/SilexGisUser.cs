// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain;
using SilexGis.Domain.Auth;
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

    public Guid? CavingClubId { get; set; }

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

    /// <summary>
    /// Number awaiting confirmation. Mirrors the email rule: <see cref="IdentityUser{TKey}.PhoneNumber"/>
    /// only takes the new value once a texted code comes back, so a mistyped number cannot quietly
    /// become the destination for sign-in codes.
    /// </summary>
    public string? PendingPhoneNumber { get; set; }

    /// <summary>When the phone verification code was last texted; throttles resends.</summary>
    public DateTimeOffset? PendingPhoneRequestedAt { get; set; }

    /// <summary>
    /// The three second factors, tracked separately because Identity's own
    /// <see cref="IdentityUser{TKey}.TwoFactorEnabled"/> is a single flag with no room for which
    /// method. That flag stays as the master switch it has to be — Identity consults it to decide
    /// a sign-in needs a second step — and is kept equal to "any of these three".
    /// </summary>
    public bool TwoFactorAuthenticatorEnabled { get; set; }

    public bool TwoFactorEmailEnabled { get; set; }

    public bool TwoFactorSmsEnabled { get; set; }

    /// <summary>Method offered first at sign-in. Null means "whichever is strongest".</summary>
    public TwoFactorMethod? PreferredTwoFactorMethod { get; set; }

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

    /// <summary>Gathers what the two-factor rules need, so they can stay free of Identity types.</summary>
    public TwoFactorState ToTwoFactorState() => new(
        TwoFactorAuthenticatorEnabled,
        TwoFactorEmailEnabled,
        TwoFactorSmsEnabled,
        EmailConfirmed,
        PhoneNumberConfirmed,
        PreferredTwoFactorMethod);

    // IUserProfile forwards to the Identity-owned properties, whose names it cannot reuse.
    string? IUserProfile.UserNameValue => UserName;

    string? IUserProfile.EmailValue => Email;

    string? IUserProfile.PhoneNumberValue => PhoneNumber;
}
