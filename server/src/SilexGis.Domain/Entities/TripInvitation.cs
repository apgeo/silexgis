// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What one person has said about coming on one trip.
/// </summary>
/// <remarks>
/// <para>
/// A fixed vocabulary rather than a club-extensible one, because every value has code behind
/// it: how many people are in and how many are waiting is computed from which value a row
/// holds, so a value nobody wrote code for would silently be counted as one of the others or
/// as none of them.
/// </para>
/// <para>
/// <see cref="Pending"/> is a first-class value and not an absent one. Invited and silent is a
/// state somebody reads and acts on, and a null column would have to mean both "has not
/// answered yet" and "took an answer back" — two different facts about the same person.
/// </para>
/// </remarks>
public enum TripInvitationResponse : short
{
    /// <summary>Asked, or on the list, and has not said either way.</summary>
    Pending = 0,

    Yes = 1,

    No = 2,

    /// <summary>Interested and not committed — a real answer, and not a slower <see cref="Yes"/>.</summary>
    Maybe = 3,
}

/// <summary>
/// One person's standing answer about one trip: whether they were asked, what they have said,
/// and when they said it.
/// </summary>
/// <remarks>
/// <para>
/// One row carries both the invitation and the answer, because somebody who was never invited
/// still answers — a member who sees a trip their club is running and says they are coming. If
/// the invitation were the parent row those answers would have nowhere to hang, so the
/// invitation's own fields are nullable on this row instead and an uninvited answer is simply a
/// row whose <see cref="InvitedAt"/> is null.
/// </para>
/// <para>
/// One person holds one answer about one trip, which is why the row is unique on the trip and
/// the person together and on nothing else. The roster beside it is unique on the trip, the job
/// and the person — one row per job, so being the leader and the surveyor is two rows — and
/// that key cannot carry a standing answer either way round: put the answer in the key and one
/// person legally holds "yes" and "no" at once, put it in a column and somebody who is both a
/// participant and a driver carries two answers free to disagree.
/// </para>
/// <para>
/// Intent is not attendance. This table says who meant to come; the roster says who was there.
/// Keeping them apart is what leaves every reader that counts people on a trip — statistics,
/// the camp write-up, the head counts, the notification that tells the trip's people it has
/// been announced — counting the people who actually went, without any of them being changed.
/// </para>
/// <para>
/// <see cref="CaverId"/> is the subject and <see cref="RespondedByUserId"/> is the actor. Most
/// cavers never sign in, so the person an answer is about has to be a roster entry rather than
/// an account; but "Ana said yes" and "the leader wrote down that Ana said yes" are different
/// facts, and on a plan whose callout may be read in an emergency the difference matters.
/// </para>
/// </remarks>
public class TripInvitation : ITimestamped, IAuditable, IAuditChild
{
    public long Id { get; set; }

    public Guid TripLogId { get; set; }

    /// <summary>Who the answer is about — a roster entry, which most people have and few accounts.</summary>
    public Guid CaverId { get; set; }

    public TripInvitationResponse Response { get; set; } = TripInvitationResponse.Pending;

    /// <summary>Who put this person on the list, or null once that account is gone.</summary>
    public Guid? InvitedByUserId { get; set; }

    /// <summary>
    /// When they were asked, or null when nobody asked them — a person who answered a trip
    /// they simply saw is on the list without ever having been invited to it.
    /// </summary>
    public DateTimeOffset? InvitedAt { get; set; }

    /// <summary>
    /// When the answer this row now carries was given — restamped by every change of mind, and
    /// never inherited from when the row was created. Somebody who said "maybe" in March and
    /// "yes" in June joined the queue in June, and a stamp that stayed at the row's creation
    /// would put them ahead of everyone who said yes in between.
    /// </summary>
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Who recorded the answer — the person themselves, or somebody writing it down for them.</summary>
    public Guid? RespondedByUserId { get; set; }

    /// <summary>
    /// When whoever runs the trip picked this person for it, or null while they have not been
    /// picked. It sits beside the order people answered in rather than replacing it, so a
    /// hand-chosen team never hides who signed up first.
    /// </summary>
    public DateTimeOffset? SelectedAt { get; set; }

    /// <summary>Whatever the answer needed saying beside it — "only if we start late", "can drive four".</summary>
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Being asked, and answering, are facts about the trip: they surface on its timeline rather
    // than in a history of their own that nobody would think to open.
    public string RootEntityType => nameof(TripLog);

    public string RootEntityId => TripLogId.ToString();
}
