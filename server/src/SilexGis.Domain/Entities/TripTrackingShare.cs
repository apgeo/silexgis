// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A publication link for one trip's live tracking: the address somebody without an account
/// follows the party at. The URL identity is a high-entropy opaque token — never the trip id —
/// stored only as a hash (the plaintext is shown once at mint) and revocable.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no mode and no scope on the row. <em>What</em> a link opens is decided by
/// what the published read builds, and that decision is taken again on every read rather than
/// remembered from the moment the link was made — protection can be switched on after a link has
/// been handed out, and a page that trusted a stored verdict would keep answering past it.
/// </para>
/// <para>
/// <see cref="ExpiresAt"/> is the exception, and it is a fact rather than a verdict: it records
/// when this link was decided to lapse, not what it may show. The reasoning for having one at all
/// — and for it being only one of three things that end a publication — is in
/// <see cref="SilexGis.Domain.Trips.TripPublicationWindow"/>, which is where the question is asked
/// and where every surface asks it.
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

    /// <summary>
    /// When this link stops opening the page, whatever else happens. Fixed at mint and never
    /// extended by being read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not null, and deliberately not nullable: a link with no expiry is the state this column
    /// exists to make unrepresentable. The value is computed once from the trip's own dates and the
    /// installation's configured window — see
    /// <see cref="SilexGis.Domain.Trips.TripPublicationWindow.ExpiresAtFor"/> — so a publication
    /// outlives the trip by one window and no longer.
    /// </para>
    /// <para>
    /// It is the backstop rather than the usual cause of a link ending. Nearly every real
    /// publication is ended by the watch closing, which happens days or weeks earlier; this column
    /// is what covers the case nothing else does — a watch nobody ever closed, on a trip that went
    /// fine, with the address sitting in a club's article being crawled.
    /// </para>
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
