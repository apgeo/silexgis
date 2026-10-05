// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One subscription address for an account's own calendar — the thing a phone or a desktop
/// calendar polls, carrying the trips, camps and club dates that person is on.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as a feature share and an album share, deliberately: 32 random bytes become the
/// address, only their SHA-256 is kept, the plaintext exists once at mint and nowhere afterwards,
/// and a row is revoked by stamping it rather than removing it, so the lookup predicate reads the
/// stamp. A feed address is pasted into third-party calendar services that store it, sync it and
/// log it for years, which is the case a plaintext token is worst for.
/// </para>
/// <para>
/// <b>It resolves to a person, not to a named object</b>, and that is what makes it different from
/// every other anonymous token this application mints. A share opens one feature or one album; a
/// follow link opens one trip; the opt-out token flips one preference row. This one is turned into
/// an account's whole grant set — resolved freshly on every poll, never baked into the row — and
/// then narrowed to what that account is on. Nothing about the grants is stored here, so a grant
/// withdrawn after the mint stops a row appearing on the next poll.
/// </para>
/// <para>
/// Several per account, so a phone and a desktop can each be revoked on their own — and so losing
/// one device does not mean re-subscribing every other one. The label is the holder's own word for
/// which device a row is, and is read by nobody else.
/// </para>
/// </remarks>
public class CalendarFeedToken : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The account the feed is about, and the only account it can ever be about.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the URL token, base64url. The plaintext token is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Which device or calendar this address was made for, in the holder's own words.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// When the address was withdrawn, or null while it still answers. Read in the lookup
    /// predicate, so a revoked address is indistinguishable from one that never existed.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public bool IsRevoked => RevokedAt is not null;
}
