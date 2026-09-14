// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A publication link for one trip's live tracking: the address somebody without an account
/// follows the party at. The URL identity is a high-entropy opaque token — never the trip id —
/// stored only as a hash (the plaintext is shown once at mint) and revocable.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no mode, no expiry and no scope on the row. Everything a link opens is
/// decided by what the published read builds, and that decision is taken again on every read
/// rather than remembered from the moment the link was made — protection can be switched on
/// after a link has been handed out, and a page that trusted a stored verdict would keep
/// answering past it.
/// </para>
/// <para>
/// A link whose trip goes away goes with it (cascade): a token that resolves to nothing would
/// otherwise be the one token whose answer differs from every other unknown one.
/// </para>
/// </remarks>
public class TripTrackingShare : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TripLogId { get; set; }

    /// <summary>SHA-256 of the URL token, base64url. The plaintext token is never stored.</summary>
    public required string TokenHash { get; set; }

    public Guid CreatedBy { get; set; }

    /// <summary>Set once and kept: a second revocation is the same fact, not a new one.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
