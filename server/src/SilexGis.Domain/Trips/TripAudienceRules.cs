// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// What a trip is being written for at the moment it is created, and nothing more. It is not a
/// lifecycle state, it is not stored, and no read of the trip ever asks it again — the only
/// question it answers is which audience the trip starts with when the request names none.
/// </summary>
public enum TripCreationIntent
{
    /// <summary>A trip that already happened, written up afterwards.</summary>
    Report = 0,

    /// <summary>A trip somebody is preparing, which has not happened yet.</summary>
    Plan = 1,
}

/// <summary>
/// Who can read a trip that was created without its author saying so.
/// </summary>
/// <remarks>
/// <para>
/// A trip written up after the event is one person's account of something already over, and it
/// starts private: nobody else is waiting on it and the author decides when it is worth reading.
/// A trip being prepared is the opposite — it is a proposal to the people who might come on it,
/// and a proposal only they can read is a proposal to nobody. So a plan starts visible to the
/// author's caving group.
/// </para>
/// <para>
/// The fallback is deliberately the narrow answer rather than "every signed-in account". A plan
/// names the cave it is for, and handing that to the whole installation because the author
/// happens to belong to no group would be a widening nobody asked for; a plan nobody but its
/// author can read is merely useless, and is fixed by naming an audience. The same reasoning
/// covers belonging to several groups: nothing here can say which of them the trip is for, and
/// guessing would show it to a club that has nothing to do with it. A group-visible row must also
/// name the group it is for, because one that names none admits nobody, which is why the two
/// values are decided together and travel together.
/// </para>
/// <para>
/// Lives here rather than at the write because two callers need the same answer: the write that
/// applies it, and the read that lets a form name the audience before the trip exists. A form
/// that guessed instead would be guessing about who can see a cave.
/// </para>
/// </remarks>
public static class TripAudienceRules
{
    public static (Visibility Visibility, Guid? CavingGroupId) DefaultAudience(
        TripCreationIntent intent, IReadOnlyList<Guid> cavingGroupIds)
    {
        if (intent != TripCreationIntent.Plan)
        {
            return (Visibility.Private, null);
        }

        var ownGroupId = cavingGroupIds.Count == 1 ? cavingGroupIds[0] : (Guid?)null;
        return ownGroupId is null
            ? (Visibility.Private, null)
            : (Visibility.CavingGroup, ownGroupId);
    }
}
