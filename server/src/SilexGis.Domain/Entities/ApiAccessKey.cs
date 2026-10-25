// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Auth;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A personal access key: a long random secret an account holder makes on the web and pastes
/// into a field device, which then calls the API as that account — but only on the routes that
/// declare the key's purpose.
/// </summary>
/// <remarks>
/// <para>
/// The same storage shape as the other secrets this application hands out: the plaintext exists
/// once, at creation, and only its SHA-256 is kept; a key is withdrawn by stamping it rather than
/// removing it, so the lookup reads the stamp and a revoked key is indistinguishable from one
/// that never existed.
/// </para>
/// <para>
/// Unlike a sign-in token it is looked up on every request, so revoking it, locking the account
/// or changing the password takes effect on the very next call, with no window.
/// </para>
/// <para>
/// When a key was last used is written by a direct update at most once a minute, not through a
/// tracked save, so a busy device does not fill the change history with one row per request.
/// Creation and revocation do go through tracked saves and are audited.
/// </para>
/// </remarks>
public class ApiAccessKey : IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The account the key acts as, and the only account it can ever act as.</summary>
    public Guid UserId { get; set; }

    /// <summary>The holder's own word for which device the key is on.</summary>
    public required string Name { get; set; }

    public AccessKeyPurpose Purpose { get; set; } = AccessKeyPurpose.SurveyUpload;

    /// <summary>The first characters of the key, kept in clear so the holder can tell keys apart.</summary>
    public required string Prefix { get; set; }

    /// <summary>SHA-256 of the whole key text. The key itself is never stored.</summary>
    public required byte[] SecretHash { get; set; }

    /// <summary>When the key stops opening anything. Every key expires; there is no "never".</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The last request the key authenticated, to the minute.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>When the key was withdrawn, or null while it still opens its routes.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// When the key was made — set from the same reading of the clock as <see cref="ExpiresAt"/>,
    /// so a key made for thirty days lasts exactly thirty. Revocation is <see cref="RevokedAt"/> and
    /// use is <see cref="LastUsedAt"/>, so the row carries no general "updated" stamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>Where the key stands at <paramref name="now"/>. A revoked key reads revoked even after it expired.</summary>
    public AccessKeyState StateAt(DateTimeOffset now) =>
        RevokedAt is not null ? AccessKeyState.Revoked
        : ExpiresAt <= now ? AccessKeyState.Expired
        : AccessKeyState.Active;
}
