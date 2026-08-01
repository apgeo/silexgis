// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;

namespace SilexGis.Domain.Access;

/// <summary>
/// The caller's authorization context, resolved once per request: identity from the
/// bearer token; memberships and the reachable access entries from the database.
/// Tokens never carry entries — they go stale the moment a ruleset is edited.
/// Pure data plus a cached flattening; the evaluator and both filter twins consume it.
/// </summary>
public sealed class AccessContext(
    Guid userId,
    bool isFullAdmin,
    IReadOnlyList<Guid> cavingGroupIds,
    IReadOnlyList<AccessEntrySnapshot> entries)
{
    private readonly ConcurrentDictionary<(AccessDomain Domain, AccessAction Action), AccessFilterSet> filterSets = new();

    public Guid UserId { get; } = userId;

    /// <summary>Member of the protected Full Administrators group (directly or through
    /// a caving group). The evaluator short-circuits on this before any entry is
    /// consulted, which is what makes the group unreachable by deny.</summary>
    public bool IsFullAdmin { get; } = isFullAdmin;

    /// <summary>The caving groups the caller belongs to, via their caver row. An
    /// account-less caver contributes nothing here — there is deliberately no path from
    /// a caver row to this context.</summary>
    public IReadOnlyList<Guid> CavingGroupIds { get; } = cavingGroupIds;

    /// <summary>Every access entry that reaches the caller: direct entries naming them
    /// or one of their caving groups, plus every entry of the permission groups they are
    /// in — directly, through a caving group, or the implicit All Users membership.</summary>
    public IReadOnlyList<AccessEntrySnapshot> Entries { get; } = entries;

    /// <summary>The flattened (domain, action) slice for the filter twins, cached per
    /// context. <paramref name="action"/> must be a single flag.</summary>
    public AccessFilterSet For(AccessDomain domain, AccessAction action) =>
        filterSets.GetOrAdd((domain, action), key => AccessFilterSet.Build(Entries, key.Domain, key.Action));
}

/// <summary>Resolves the current request's <see cref="AccessContext"/> (null when
/// anonymous — anonymous callers reach no evaluator).</summary>
public interface IAccessContextAccessor
{
    Task<AccessContext?> GetAsync(CancellationToken ct = default);
}
