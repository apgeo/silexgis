// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// The position conditions of a world other than features, each resolved to the ids of the rows
/// that answer it before the world's predicate is compiled.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, and neither belongs to the world asking. What a condition measures from is resolved
/// by the feature world, through the one placement gate every anchor passes. Which of the world's
/// rows answer is the world's own rule about the position its reader is shown — a picture's
/// disclosed fix, a model's cave, a trip's listed caves, a map's footprint — handed in here as
/// <c>members</c>.
/// </para>
/// <para>
/// Resolved to ids first, as the feature world's own conditions are, so a negated condition is
/// decided on the same set: a row whose position the reader is not shown is in no set, which "not
/// within" then reads exactly as it reads a row with no position at all.
/// </para>
/// </remarks>
public static class PositionConditions
{
    /// <summary>Nothing resolved — a filter with no position condition.</summary>
    public static IReadOnlyDictionary<ConditionNode, Guid[]> None { get; } = new Dictionary<ConditionNode, Guid[]>();

    public static async Task<IReadOnlyDictionary<ConditionNode, Guid[]>> ResolveAsync(
        FeatureFilterWorld anchors,
        AccessContext caller,
        FilterNode? where,
        Func<SpatialAsk, Task<IEnumerable<Guid>>> members,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(members);
        Dictionary<ConditionNode, Guid[]>? resolved = null;
        foreach (var condition in SpatialConditions.ConditionsOf(where))
        {
            resolved ??= [];
            if (resolved.ContainsKey(condition))
            {
                continue;
            }

            var ask = await anchors.ResolveAskAsync(caller, condition, ct);
            resolved[condition] = ask is null ? [] : [.. (await members(ask)).Distinct()];
        }

        return resolved ?? None;
    }

    /// <summary>The ids a resolved condition stands for — a condition the walk did not reach is a fault.</summary>
    public static Guid[] Of(IReadOnlyDictionary<ConditionNode, Guid[]> resolved, ConditionNode condition) =>
        resolved.TryGetValue(condition, out var ids)
            ? ids
            : throw new InvalidOperationException(
                "A position condition reached the compiler unresolved; it would match nothing silently.");
}
