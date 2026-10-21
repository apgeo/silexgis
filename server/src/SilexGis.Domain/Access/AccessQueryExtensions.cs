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
/// <para>
/// The cascade is written as "some id of the row's ancestor array names a row that admits the
/// caller" — the array walked, each id looked up by primary key — and not as "some feature whose id
/// the array contains". The two mean the same; the second translates to <c>ancestor_ids @>
/// ARRAY[a.id]</c>, a test on the outer row that no index on the inner one can answer, so the
/// planner scans the whole feature table once per candidate row. Measured on 100k entrances it
/// cost four seconds for a window of 137, for every reader who owned none of them.
/// </para>
/// <para>
/// A row's ancestor array holds the row itself, and a live row is one the cascade's own lookup
/// would find. So a live row is asked about itself on the columns it already carries, and its
/// own id is passed over in the walk — one key lookup fewer for every row, which for a reader
/// who owns nothing is about half of what an unwindowed count costs. A deleted row (read only
/// by the callers that look at deletions, whose ancestor set may or may not hold deleted rows)
/// is still looked up like any other ancestor, so the answer is the walk's in every case.
/// </para>
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
                || (f.DeletedAt == null
                    && (f.Visibility >= Visibility.Authenticated
                        || (f.Visibility == Visibility.CavingGroup
                            && f.CavingGroupId != null
                            && cavingGroupIds.Contains(f.CavingGroupId.Value))))
                || f.AncestorIds.Any(ancestorId => (ancestorId != f.Id || f.DeletedAt != null)
                    && allFeatures.Any(a => a.Id == ancestorId
                        && (a.Visibility >= Visibility.Authenticated
                            || (a.Visibility == Visibility.CavingGroup
                                && a.CavingGroupId != null
                                && cavingGroupIds.Contains(a.CavingGroupId.Value))))));
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
              || (f.DeletedAt == null
                  && (f.Visibility >= Visibility.Authenticated
                      || (f.Visibility == Visibility.CavingGroup
                          && f.CavingGroupId != null
                          && cavingGroupIds.Contains(f.CavingGroupId.Value))))
              || f.AncestorIds.Any(ancestorId => (ancestorId != f.Id || f.DeletedAt != null)
                  && allFeatures.Any(a => a.Id == ancestorId
                      && (a.Visibility >= Visibility.Authenticated
                          || (a.Visibility == Visibility.CavingGroup
                              && a.CavingGroupId != null
                              && cavingGroupIds.Contains(a.CavingGroupId.Value))))));
    }

    /// <summary>
    /// Read filter over non-feature protected rows (trip logs, geofiles, rasters, views,
    /// documents). Drops the arms that only exist in the feature world — subtree, set,
    /// kind/type — and the visibility built-in consults the row alone (nothing above a
    /// trip log to inherit from). Every band left here keys on a column such a row
    /// actually carries, which is why a domain reaches this overload only when its rows
    /// carry the owner/caving-group/visibility trio.
    /// <para>
    /// One collection band does apply here: documents can be filed in cabinets, and a
    /// cabinet entry decides at level 2 like any other collection. It is an entry band, not
    /// a built-in — it sits inside the conditional chain so a cabinet deny stops a
    /// domain-wide allow, and a per-document entry still overrules it.
    /// </para>
    /// </summary>
    /// <param name="reachedByAttachment">
    /// Ids the caller reaches through an object the row's content is attached to, resolved
    /// for Read by whoever builds the list — the flat form of the attachment built-in.
    /// It rides the same ELSE arm as ownership and visibility, so it widens only what no
    /// entry decided; passing it in the outer predicate instead would let it override a
    /// deny. Empty for the domains that have no attachments at all.
    /// </param>
    /// <param name="filings">
    /// The cabinet filings and the cabinet rows they point at — the context's own DbSets, so
    /// the cabinet band stays a single correlated EXISTS. Only the document domain can carry
    /// cabinet entries; passing nothing where one reaches the caller is refused rather than
    /// ignored, because silently dropping the band would turn a cabinet deny into an allow.
    /// </param>
    public static IQueryable<T> VisibleTo<T>(
        this IQueryable<T> query,
        AccessContext ctx,
        AccessDomain domain,
        IReadOnlyCollection<Guid>? reachedByAttachment = null,
        (IQueryable<CabinetDocument> Memberships, IQueryable<Cabinet> Cabinets)? filings = null)
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
        var denyCab = set.DenyCabinetIds;
        var allowCab = set.AllowCabinetIds;
        var denyAll = set.DenyAll;
        var allowAll = set.AllowAll;
        var denyOwn = set.DenyOwn;
        var allowOwn = set.AllowOwn;

        if (denyCab.Length == 0 && allowCab.Length == 0)
        {
            // No cabinet entry reaches this caller — every domain but documents, and most
            // document callers. Emitted without the collection band so those queries keep
            // the plan they have today rather than carrying a subquery that can never
            // match. The band below is the same walk with level 2 spliced in; the two
            // chains are one rule and change together.
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

        if (filings is not { } filed)
        {
            // Dropping the band silently would read a cabinet deny as an allow, so a list
            // built without the filings is a defect in the caller, not a narrower query.
            throw new InvalidOperationException(
                "A cabinet-scoped entry reaches this caller; the read filter needs the cabinet filings to evaluate it.");
        }

        var memberships = filed.Memberships;
        var cabinets = filed.Cabinets;

        return query.Where(e =>
            denyObj.Contains(e.Id) ? false
            : allowObj.Contains(e.Id) ? true
            : memberships.Any(m => m.DocumentId == e.Id
                && cabinets.Any(c => c.Id == m.CabinetId
                    && c.AncestorIds.Any(a => denyCab.Contains(a)))) ? false
            : memberships.Any(m => m.DocumentId == e.Id
                && cabinets.Any(c => c.Id == m.CabinetId
                    && c.AncestorIds.Any(a => allowCab.Contains(a)))) ? true
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
