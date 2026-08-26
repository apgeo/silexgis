// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Events;

/// <summary>
/// Who can read an event that was created without its author saying so.
/// </summary>
/// <remarks>
/// <para>
/// An event has no equivalent of a trip's write-it-up-afterwards case, and so no choice to make
/// about which way it starts: it is a thing a group is being told about, and one only its author
/// can read is a notice to nobody. So it starts visible to the author's club, and falls back to
/// private — never to every signed-in account — when there is no single club to name.
/// </para>
/// <para>
/// Lives here rather than at the write because two callers need the same answer: the write that
/// applies it, and the read that lets a form show the audience before the event exists.
/// </para>
/// </remarks>
public static class EventAudienceRules
{
    public static (Visibility Visibility, Guid? CavingGroupId) DefaultAudience(
        IReadOnlyList<Guid> cavingGroupIds) => AudienceDefaults.OwnGroupOrPrivate(cavingGroupIds);
}
