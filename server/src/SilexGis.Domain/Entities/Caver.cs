// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A person in the roster. Most cavers never sign in — they exist so trips, statistics and
/// discovery credits can name them — and at most one holds a user account.
/// </summary>
/// <remarks>
/// <para>
/// A caver row carries <b>no authorization meaning whatsoever</b>: there is deliberately no path
/// from a caver to a permission decision, because a person without an account has nothing to
/// exercise one with. Everything access-related keys on the user account instead, and a caving
/// group's members reach the permission machinery only through the accounts among them.
/// </para>
/// <para>
/// The account link is severable in one direction only: deleting an account clears
/// <see cref="UserId"/> and leaves the person — and their trip history — intact. Contact details
/// stay with the orphaned row and simply fall under the stricter disclosure tier for cavers
/// without an account.
/// </para>
/// </remarks>
public class Caver : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The roster label. Required even for account holders, whose profile may hide their real name.</summary>
    public required string FullName { get; set; }

    /// <summary>
    /// What a club's own log calls this person — a first name, a nickname, an initial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept beside the roster name rather than instead of it, because the two answer different
    /// questions. A roster has to say who somebody is; a log written at a cave entrance, and the
    /// marker drawn over a survey, have room for what their party calls them. Writing the short
    /// form into <see cref="FullName"/> to make the drawing readable loses the only field that
    /// identifies them, and writing the long form everywhere makes a party of nine illegible.
    /// </para>
    /// <para>
    /// Null where nobody has given one, and then every surface falls back to
    /// <see cref="FullName"/> — so this is an improvement to a drawing, never a precondition for
    /// one.
    /// </para>
    /// </remarks>
    public string? ShortName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>Roster-keeping remarks. Visible only to whoever may edit the roster.</summary>
    public string? Notes { get; set; }

    /// <summary>The account this person signs in with, when they have one. Unique across cavers.</summary>
    public Guid? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
