// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

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
/// Which club, and what happens when there is not exactly one, is
/// <see cref="AudienceDefaults.OwnGroupOrPrivate"/>'s to answer and is answered there for every
/// kind of content at once: the narrow answer rather than "every signed-in account". It matters
/// especially for a plan, which names the cave it is for — handing that to the whole installation
/// because the author happens to belong to no club would be a widening nobody asked for.
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

        return AudienceDefaults.OwnGroupOrPrivate(cavingGroupIds);
    }
}
