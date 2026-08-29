// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The record of a decision to let a cave's printed codes resolve for a visitor who is not
/// signed in. A live row (<see cref="RevokedAt"/> null) is what makes the landing route
/// answer at all; an absent row is not published, which is the state of every cave that has
/// ever existed until somebody says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// It carries no address of its own, and that is the difference between it and a share link.
/// A share link's identity is a high-entropy token nobody can guess, so the token is the
/// credential and the row exists to hold its hash. Here the address is a code printed on a
/// label bolted to a cave wall, generated outside this server from data the code names: it is
/// short, transcribable, reproducible from the same inputs by anybody holding them, and
/// therefore guessable by construction. So it is an address and never a capability, and the
/// only thing standing between the public and the cave is this row — a decision a person took
/// and can take back, not a secret they kept.
/// </para>
/// <para>
/// That is also why the decision is recorded rather than expressed as a flag on the cave. Who
/// published, when, and when it was withdrawn are the facts somebody will want when a label
/// turns up somewhere it should not be, and a boolean would have thrown all three away.
/// Revocation keeps the row and stamps it, so the record survives the withdrawal; publishing
/// again writes a new row rather than clearing the old one's stamp.
/// </para>
/// <para>
/// The unit is the cave, because that is the unit a human decides about. Anything contained in
/// a cave — a place, an area, a station — resolves to the cave over its containment ancestry
/// and inherits the decision; there is deliberately no per-place publication, so no single
/// label can be live under a cave nobody published.
/// </para>
/// </remarks>
public class CaveQrPublication : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The published cave's feature row. Always a cave, never a place inside one.</summary>
    public Guid FeatureId { get; set; }

    /// <summary>The account that took the decision. Kept after revocation.</summary>
    public Guid PublishedBy { get; set; }

    /// <summary>
    /// When the decision was withdrawn; null while it stands. Set once and never cleared — a
    /// second withdrawal keeps the first timestamp, because the moment the codes stopped
    /// resolving is a fact about the world and not about how many times somebody asked.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
