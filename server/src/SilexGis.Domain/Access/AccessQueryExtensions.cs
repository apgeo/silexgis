// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Access;

/// <summary>
/// The EF expression twin of <see cref="AccessEvaluator"/> — the level walk compiled
/// into a nested conditional so point-checks and list filters cannot disagree (bands
/// short-circuit exactly like the walk). SQL twin: <c>AccessSql</c>; parity tests pin
/// all three forms — change them together or not at all. Flat throughout: subtree
/// matching is an array overlap on the row's ancestor ids, set matching one membership
/// EXISTS, the visibility cascade one EXISTS over the ancestor rows.
/// </summary>
public static class AccessQueryExtensions
{
    /// <summary>
    /// Read filter over features. <paramref name="allFeatures"/> is the unfiltered
    /// live feature set (the ancestor rows the visibility cascade consults — soft
    /// delete is subtree-stamped, so ancestors of a live row are live);
    /// <paramref name="setMembers"/> the feature-set membership set. Callers pass the
    /// context's own DbSets — the correlated subqueries stay single EXISTS arms in the
    /// translated SQL.
    /// </summary>
    public static IQueryable<Feature> VisibleTo(
        this IQueryable<Feature> query,
        AccessContext ctx,
        IQueryable<Feature> allFeatures,
        IQueryable<FeatureSetMember> setMembers)
    {
        if (ctx.IsFullAdmin)
        {
            return query;
        }

        var set = ctx.For(AccessDomain.Features, AccessAction.Read);
        var userId = ctx.UserId;
        var cavingGroupIds = ctx.CavingGroupIds.ToArray();

        if (set.IsEmpty)
        {
            // No entry contributes — only the built-ins remain. Kept as a separate,
            // simpler predicate so the common no-entries caller gets the lean plan the
            // EXPLAIN pins guard.
            return query.Where(f =>
                f.OwnerUserId == userId
                || allFeatures.Any(a => f.AncestorIds.Contains(a.Id)
                    && (a.Visibility >= Visibility.Authenticated
                        || (a.Visibility == Visibility.CavingGroup
                            && a.CavingGroupId != null
                            && cavingGroupIds.Contains(a.CavingGroupId.Value)))));
        }

        var denyObj = set.DenyObjectIds;
        var allowObj = set.AllowObjectIds;
        var denySub = set.DenySubtreeRoots;
        var allowSub = set.AllowSubtreeRoots;
        var denySet = set.DenySetIds;
        var allowSet = set.AllowSetIds;
        var denyCg = set.DenyCavingGroupIds;
        var allowCg = set.AllowCavingGroupIds;
        var denyAll = set.DenyAll;
        var allowAll = set.AllowAll;
        var denyOwn = set.DenyOwn;
        var allowOwn = set.AllowOwn;
        var denyAllKinds = set.DenyAllKinds;
        var allowAllKinds = set.AllowAllKinds;
        var denyOwnKinds = set.DenyOwnKinds;
        var allowOwnKinds = set.AllowOwnKinds;
        var denyAllTypes = set.DenyAllTypeIds;
        var allowAllTypes = set.AllowAllTypeIds;
        var denyOwnTypes = set.DenyOwnTypeIds;
        var allowOwnTypes = set.AllowOwnTypeIds;

        // The walk as one conditional chain: level 1 deny → false, level 1 allow →
        // true, level 2 deny, level 2 allow, level 3 deny, else level 3 allows and the
        // built-ins. Mirrors AccessEvaluator.Decide band for band.
        return query.Where(f =>
            denyObj.Contains(f.Id) ? false
            : allowObj.Contains(f.Id) ? true
            : f.AncestorIds.Any(id => denySub.Contains(id))
              || setMembers.Any(m => m.FeatureId == f.Id && denySet.Contains(m.FeatureSetId)) ? false
            : f.AncestorIds.Any(id => allowSub.Contains(id))
              || setMembers.Any(m => m.FeatureId == f.Id && allowSet.Contains(m.FeatureSetId)) ? true
            : denyAll
              || (denyOwn && f.OwnerUserId == userId)
              || (f.CavingGroupId != null && denyCg.Contains(f.CavingGroupId.Value))
              || denyAllKinds.Contains((short)f.Kind)
              || (f.FeatureTypeId != null && denyAllTypes.Contains(f.FeatureTypeId.Value))
              || (f.OwnerUserId == userId
                  && (denyOwnKinds.Contains((short)f.Kind)
                      || (f.FeatureTypeId != null && denyOwnTypes.Contains(f.FeatureTypeId.Value)))) ? false
            : allowAll
              || (allowOwn && f.OwnerUserId == userId)
              || (f.CavingGroupId != null && allowCg.Contains(f.CavingGroupId.Value))
              || allowAllKinds.Contains((short)f.Kind)
              || (f.FeatureTypeId != null && allowAllTypes.Contains(f.FeatureTypeId.Value))
              || (f.OwnerUserId == userId
                  && (allowOwnKinds.Contains((short)f.Kind)
                      || (f.FeatureTypeId != null && allowOwnTypes.Contains(f.FeatureTypeId.Value))))
              || f.OwnerUserId == userId
              || allFeatures.Any(a => f.AncestorIds.Contains(a.Id)
                  && (a.Visibility >= Visibility.Authenticated
                      || (a.Visibility == Visibility.CavingGroup
                          && a.CavingGroupId != null
                          && cavingGroupIds.Contains(a.CavingGroupId.Value)))));
    }

    /// <summary>
    /// Read filter over non-feature protected rows (trip logs, geofiles, rasters, views,
    /// documents). Drops the arms that only exist in the feature world — subtree, set,
    /// kind/type — and the visibility built-in consults the row alone (nothing above a
    /// trip log to inherit from). Every band left here keys on a column such a row
    /// actually carries, which is why a domain reaches this overload only when its rows
    /// carry the owner/caving-group/visibility trio.
    /// </summary>
    /// <param name="reachedByAttachment">
    /// Ids the caller reaches through an object the row's content is attached to, resolved
    /// for Read by whoever builds the list — the flat form of the attachment built-in.
    /// It rides the same ELSE arm as ownership and visibility, so it widens only what no
    /// entry decided; passing it in the outer predicate instead would let it override a
    /// deny. Empty for the domains that have no attachments at all.
    /// </param>
    public static IQueryable<T> VisibleTo<T>(
        this IQueryable<T> query,
        AccessContext ctx,
        AccessDomain domain,
        IReadOnlyCollection<Guid>? reachedByAttachment = null)
        where T : class, IProtectedEntity
    {
        if (ctx.IsFullAdmin)
        {
            return query;
        }

        var set = ctx.For(domain, AccessAction.Read);
        var userId = ctx.UserId;
        var cavingGroupIds = ctx.CavingGroupIds.ToArray();
        Guid[] reached = reachedByAttachment is null ? [] : [.. reachedByAttachment];

        if (set.IsEmpty)
        {
            return query.Where(e =>
                e.OwnerUserId == userId
                || e.Visibility >= Visibility.Authenticated
                || (e.Visibility == Visibility.CavingGroup
                    && e.CavingGroupId != null
                    && cavingGroupIds.Contains(e.CavingGroupId.Value))
                || reached.Contains(e.Id));
        }

        var denyObj = set.DenyObjectIds;
        var allowObj = set.AllowObjectIds;
        var denyCg = set.DenyCavingGroupIds;
        var allowCg = set.AllowCavingGroupIds;
        var denyAll = set.DenyAll;
        var allowAll = set.AllowAll;
        var denyOwn = set.DenyOwn;
        var allowOwn = set.AllowOwn;

        return query.Where(e =>
            denyObj.Contains(e.Id) ? false
            : allowObj.Contains(e.Id) ? true
            : denyAll
              || (denyOwn && e.OwnerUserId == userId)
              || (e.CavingGroupId != null && denyCg.Contains(e.CavingGroupId.Value)) ? false
            : allowAll
              || (allowOwn && e.OwnerUserId == userId)
              || (e.CavingGroupId != null && allowCg.Contains(e.CavingGroupId.Value))
              || e.OwnerUserId == userId
              || e.Visibility >= Visibility.Authenticated
              || (e.Visibility == Visibility.CavingGroup
                  && e.CavingGroupId != null
                  && cavingGroupIds.Contains(e.CavingGroupId.Value))
              || reached.Contains(e.Id));
    }
}
