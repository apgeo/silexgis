// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// Where one person's "yes" stands on a trip that has room for a stated number of people: which
/// place in the sign-up order it holds, and whether that place is inside the room there is.
/// </summary>
/// <param name="Place">
/// The one-based position among everybody who has said yes, in the order they said it. It is the
/// sign-up order and only that — being picked out by whoever runs the trip does not move anybody
/// up it, so a hand-chosen team never hides who put their name down first.
/// </param>
/// <param name="Attending">
/// Whether this person counts as being on the trip rather than waiting for a place on it.
/// </param>
public readonly record struct TripAttendancePlace(int Place, bool Attending);

/// <summary>
/// Who is on a trip and who is waiting for a place, worked out from the answers every time it is
/// asked.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is ever written down. A place in a queue stored beside the answers is a second
/// record of the same thing, and it starts disagreeing with the first the moment anybody changes
/// their mind, withdraws, or is asked late — at which point there is no way to tell which of the
/// two is the stale one. Recomputing costs a sort over the people considering one afternoon's
/// caving, which is nothing.
/// </para>
/// <para>
/// The order is when the answer standing now was given, then the row's own key. The key is not
/// decoration: two answers can land in the same instant, and without it the same set of answers
/// would produce two different teams on two readings.
/// </para>
/// <para>
/// The limit never turns anybody away — it is not consulted when an answer is written, only when
/// one is read — so a trip with room for eight can perfectly well hold twelve yeses. That is the
/// point: the four beyond the limit are the record of who else wanted to come.
/// </para>
/// </remarks>
public static class TripAttendance
{
    /// <summary>
    /// Every "yes" on the trip, keyed by row, with the place it holds and whether that place is
    /// inside the limit. Rows that are not a yes are absent: somebody who declined holds no place
    /// and is waiting for nothing.
    /// </summary>
    /// <param name="invitations">Every answer about one trip. Anything that is not a yes is ignored.</param>
    /// <param name="maxParticipants">
    /// How many the trip has room for, or null when it has no stated limit — in which case
    /// everybody who said yes is on it and nobody is waiting.
    /// </param>
    /// <remarks>
    /// <para>
    /// Whoever runs the trip may pick people out of the order, and a picked person is on the trip
    /// wherever they stand in it. That is an override and is treated as one: it is not capped by
    /// the limit either, because a limit that could veto the choice would not be an override but a
    /// suggestion, and the number of places a permit allows is a thing the person holding the
    /// permit knows better than this column does. What the picking cannot do is disturb the order
    /// underneath it, which stays exactly the order people answered in.
    /// </para>
    /// <para>
    /// The places a picked person takes come out of the room first; whoever is left fills what
    /// remains, in order. So picking somebody from the back of the queue displaces the last person
    /// who would otherwise have got in, and not the first.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<long, TripAttendancePlace> Rank(
        IEnumerable<TripInvitation> invitations, int? maxParticipants)
    {
        ArgumentNullException.ThrowIfNull(invitations);

        var yes = InSignUpOrder(invitations.Where(x => x.Response == TripInvitationResponse.Yes));

        var remaining = maxParticipants;
        var picked = new HashSet<long>();

        if (remaining is not null)
        {
            foreach (var row in yes.Where(x => x.SelectedAt is not null))
            {
                picked.Add(row.Id);
                remaining--;
            }
        }

        var places = new Dictionary<long, TripAttendancePlace>(yes.Count);
        for (var index = 0; index < yes.Count; index++)
        {
            var row = yes[index];
            bool attending;
            if (remaining is null)
            {
                attending = true;
            }
            else if (picked.Contains(row.Id))
            {
                attending = true;
            }
            else if (remaining > 0)
            {
                attending = true;
                remaining--;
            }
            else
            {
                attending = false;
            }

            places[row.Id] = new TripAttendancePlace(index + 1, attending);
        }

        return places;
    }

    /// <summary>
    /// These answers in the order people signed up in: when the answer standing now was given,
    /// then the row's own key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one home of the sign-up order, so that the order a list is rendered in and the places
    /// that list reports are the same expression. Written twice they compile, pass, and disagree
    /// the day either is changed alone — at which point a reader sees place 3 above place 2 and
    /// concludes they were passed over out of turn.
    /// </para>
    /// <para>
    /// An answer with no stamp on it sorts last rather than first. Null is not "answered at the
    /// beginning of time"; it means the stamp is missing, and putting a missing stamp at the head
    /// of the queue would hand a place to whoever's record is least complete. The key is not
    /// decoration either: two answers can land in the same instant, and without it the same set of
    /// answers produces two different teams on two readings.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TripInvitation> InSignUpOrder(IEnumerable<TripInvitation> invitations)
    {
        ArgumentNullException.ThrowIfNull(invitations);

        return [.. invitations
            .OrderBy(x => x.RespondedAt is null)
            .ThenBy(x => x.RespondedAt)
            .ThenBy(x => x.Id)];
    }
}
