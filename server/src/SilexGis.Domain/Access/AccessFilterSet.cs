// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// One (domain, action) slice of the caller's entries, flattened into the arrays the
/// EF and SQL filter twins splice into queries. Level 1 = the object arrays, level 2 =
/// subtree roots + set ids + cabinet ids, level 3 = the scalars and the
/// narrowed-conjunction arrays.
/// Every narrowed array keeps its conjunction (<c>AllowOwnKinds = [cave]</c> means
/// "own AND kind = cave", never two independent facts) — an entry narrows by kind OR by
/// type, never both, so one array element is always one whole conjunction.
/// </summary>
public sealed record AccessFilterSet
{
    public static readonly AccessFilterSet Empty = new();

    public Guid[] DenyObjectIds { get; init; } = [];

    public Guid[] AllowObjectIds { get; init; } = [];

    public Guid[] DenySubtreeRoots { get; init; } = [];

    public Guid[] AllowSubtreeRoots { get; init; } = [];

    public Guid[] DenySetIds { get; init; } = [];

    public Guid[] AllowSetIds { get; init; } = [];

    /// <summary>Cabinet roots an entry names; a row matches when one of them reaches it,
    /// which for a document means it is filed at or below that cabinet.</summary>
    public Guid[] DenyCabinetIds { get; init; } = [];

    public Guid[] AllowCabinetIds { get; init; } = [];

    public bool DenyAll { get; init; }

    public bool AllowAll { get; init; }

    public bool DenyOwn { get; init; }

    public bool AllowOwn { get; init; }

    public short[] DenyAllKinds { get; init; } = [];

    public short[] AllowAllKinds { get; init; } = [];

    public short[] DenyOwnKinds { get; init; } = [];

    public short[] AllowOwnKinds { get; init; } = [];

    public long[] DenyAllTypeIds { get; init; } = [];

    public long[] AllowAllTypeIds { get; init; } = [];

    public long[] DenyOwnTypeIds { get; init; } = [];

    public long[] AllowOwnTypeIds { get; init; } = [];

    public Guid[] DenyCavingGroupIds { get; init; } = [];

    public Guid[] AllowCavingGroupIds { get; init; } = [];

    /// <summary>True when no entry contributes to this slice — the filters can skip
    /// every entry arm and fall straight through to the built-ins.</summary>
    public bool IsEmpty =>
        !DenyAll && !AllowAll && !DenyOwn && !AllowOwn
        && DenyObjectIds.Length == 0 && AllowObjectIds.Length == 0
        && DenySubtreeRoots.Length == 0 && AllowSubtreeRoots.Length == 0
        && DenySetIds.Length == 0 && AllowSetIds.Length == 0
        && DenyCabinetIds.Length == 0 && AllowCabinetIds.Length == 0
        && DenyAllKinds.Length == 0 && AllowAllKinds.Length == 0
        && DenyOwnKinds.Length == 0 && AllowOwnKinds.Length == 0
        && DenyAllTypeIds.Length == 0 && AllowAllTypeIds.Length == 0
        && DenyOwnTypeIds.Length == 0 && AllowOwnTypeIds.Length == 0
        && DenyCavingGroupIds.Length == 0 && AllowCavingGroupIds.Length == 0;

    /// <summary>
    /// Flattens the entries that carry <paramref name="action"/> in
    /// <paramref name="domain"/>. <paramref name="action"/> must be a single flag.
    /// In the feature domain Object-scope entries anchor on scope_feature_id; everywhere
    /// else on scope_id — the two never mix because a set is always per-domain.
    /// </summary>
    public static AccessFilterSet Build(
        IReadOnlyList<AccessEntrySnapshot> entries, AccessDomain domain, AccessAction action)
    {
        List<Guid> denyObj = [], allowObj = [], denySub = [], allowSub = [];
        List<Guid> denySet = [], allowSet = [], denyCg = [], allowCg = [];
        List<Guid> denyCab = [], allowCab = [];
        List<short> denyAllKinds = [], allowAllKinds = [], denyOwnKinds = [], allowOwnKinds = [];
        List<long> denyAllTypes = [], allowAllTypes = [], denyOwnTypes = [], allowOwnTypes = [];
        bool denyAll = false, allowAll = false, denyOwn = false, allowOwn = false;

        foreach (var entry in entries)
        {
            if (entry.Domain != domain || (entry.Actions & action) == 0)
            {
                continue;
            }

            var deny = entry.Effect == AccessEffect.Deny;
            switch (entry.ScopeKind)
            {
                case AccessScopeKind.Object:
                    var objectId = domain == AccessDomain.Features ? entry.ScopeFeatureId : entry.ScopeId;
                    if (objectId is { } o)
                    {
                        (deny ? denyObj : allowObj).Add(o);
                    }

                    break;

                case AccessScopeKind.Subtree:
                    if (entry.ScopeFeatureId is { } root)
                    {
                        (deny ? denySub : allowSub).Add(root);
                    }

                    break;

                case AccessScopeKind.FeatureSet:
                    if (entry.ScopeId is { } set)
                    {
                        (deny ? denySet : allowSet).Add(set);
                    }

                    break;

                case AccessScopeKind.Cabinet:
                    if (entry.ScopeId is { } cabinet)
                    {
                        (deny ? denyCab : allowCab).Add(cabinet);
                    }

                    break;

                case AccessScopeKind.CavingGroup:
                    if (entry.ScopeId is { } cavingGroup)
                    {
                        (deny ? denyCg : allowCg).Add(cavingGroup);
                    }

                    break;

                case AccessScopeKind.All:
                    if (entry.FeatureKind is { } allKind)
                    {
                        (deny ? denyAllKinds : allowAllKinds).Add((short)allKind);
                    }
                    else if (entry.FeatureTypeId is { } allType)
                    {
                        (deny ? denyAllTypes : allowAllTypes).Add(allType);
                    }
                    else if (deny)
                    {
                        denyAll = true;
                    }
                    else
                    {
                        allowAll = true;
                    }

                    break;

                case AccessScopeKind.Own:
                    if (entry.FeatureKind is { } ownKind)
                    {
                        (deny ? denyOwnKinds : allowOwnKinds).Add((short)ownKind);
                    }
                    else if (entry.FeatureTypeId is { } ownType)
                    {
                        (deny ? denyOwnTypes : allowOwnTypes).Add(ownType);
                    }
                    else if (deny)
                    {
                        denyOwn = true;
                    }
                    else
                    {
                        allowOwn = true;
                    }

                    break;

                default:
                    break;
            }
        }

        return new AccessFilterSet
        {
            DenyObjectIds = [.. denyObj],
            AllowObjectIds = [.. allowObj],
            DenySubtreeRoots = [.. denySub],
            AllowSubtreeRoots = [.. allowSub],
            DenySetIds = [.. denySet],
            AllowSetIds = [.. allowSet],
            DenyCabinetIds = [.. denyCab],
            AllowCabinetIds = [.. allowCab],
            DenyCavingGroupIds = [.. denyCg],
            AllowCavingGroupIds = [.. allowCg],
            DenyAll = denyAll,
            AllowAll = allowAll,
            DenyOwn = denyOwn,
            AllowOwn = allowOwn,
            DenyAllKinds = [.. denyAllKinds],
            AllowAllKinds = [.. allowAllKinds],
            DenyOwnKinds = [.. denyOwnKinds],
            AllowOwnKinds = [.. allowOwnKinds],
            DenyAllTypeIds = [.. denyAllTypes],
            AllowAllTypeIds = [.. allowAllTypes],
            DenyOwnTypeIds = [.. denyOwnTypes],
            AllowOwnTypeIds = [.. allowOwnTypes],
        };
    }
}
