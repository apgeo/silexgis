// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// The audience a row gets when it is created and its author names none.
/// </summary>
/// <remarks>
/// <para>
/// One home, because two kinds of content answering this question separately is how they come to
/// answer it differently — and the difference would be who can read something, found by whoever
/// is surprised by it rather than by whoever decided it.
/// </para>
/// <para>
/// The fallback is deliberately the narrow answer rather than "every signed-in account". Handing
/// a row to the whole installation because its author happens to belong to no club would be a
/// widening nobody asked for; a row nobody but its author can read is merely useless, and is
/// fixed by naming an audience. The same reasoning covers belonging to several clubs: nothing
/// here can say which of them the row is for, and guessing would show it to a club that has
/// nothing to do with it. A group-visible row must also name the group it is for, because one
/// that names none admits nobody — which is why the two values are decided together and travel
/// together.
/// </para>
/// </remarks>
public static class AudienceDefaults
{
    /// <summary>
    /// The author's own club when they have exactly one, private otherwise.
    /// </summary>
    public static (Visibility Visibility, Guid? CavingGroupId) OwnGroupOrPrivate(
        IReadOnlyList<Guid> cavingGroupIds)
    {
        var ownGroupId = cavingGroupIds.Count == 1 ? cavingGroupIds[0] : (Guid?)null;
        return ownGroupId is null
            ? (Visibility.Private, null)
            : (Visibility.CavingGroup, ownGroupId);
    }
}
