// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Domain;
using SilexGis.Domain.Access;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// SQL twin of <see cref="AccessEvaluator"/> and of the EF filter
/// (<c>AccessQueryExtensions.VisibleTo</c>) for Dapper queries — the level walk as a
/// flat CASE chain whose bands short-circuit exactly like the walk. Parity tests pin
/// the three forms against a live database; change them together or not at all.
/// Flat throughout: subtree matching is an array overlap on ancestor_ids, feature-set
/// matching one membership EXISTS, the visibility cascade one EXISTS over the ancestor
/// rows — nothing recurses. Every id set rides a native uuid[]/smallint[]/bigint[]
/// parameter (Dapper's list expansion was measured 50x slower at 50k rows).
/// </summary>
public static class AccessSql
{
    /// <summary>Shared caller parameters every fragment consumes.</summary>
    private const string UserIdParam = "acc_user_id";
    private const string IsFullAdminParam = "acc_is_full_admin";
    private const string CavingGroupIdsParam = "acc_caving_group_ids";

    /// <summary>
    /// Read-visibility fragment over the features table. The caller must additionally
    /// filter soft delete (<c>{alias}.deleted_at IS NULL</c>); the EF side gets that
    /// from the global query filter.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) FeatureVisibleToFragment(
        AccessContext ctx, string alias)
    {
        RequireAlias(alias);
        var parameters = BaseParameters(ctx);
        var sql = FeatureWalk(
            ctx.For(AccessDomain.Features, AccessAction.Read),
            alias,
            "acc_r",
            parameters,
            withVisibilityArm: true);
        return (sql, parameters);
    }

    /// <summary>
    /// Exact-location fragment over a feature row, bit-identical to
    /// <c>LocationProtection.CanViewExactLocation(ctx, row, roots)</c>: full admins and
    /// the row's own owner always qualify; everyone else must hold ViewExactLocation —
    /// per the access walk, evaluated against each root — on EVERY protected root above
    /// the row. Flat form; soft-deleted roots still veto (no deleted_at filter on the
    /// root lookup), matching the batch evaluator.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) ExactViewFragment(
        AccessContext ctx, string alias)
    {
        RequireAlias(alias);
        var parameters = BaseParameters(ctx);
        return (ExactViewSql(ctx, alias, parameters), parameters);
    }

    /// <summary>
    /// Both feature fragments over one parameter set, for the map paths that filter by
    /// visibility and decide exact-vs-snapped in the same statement.
    /// </summary>
    /// <remarks>
    /// They must be built together rather than merged afterwards: each fragment carries
    /// the same caller parameters (id, full-admin flag, caving groups), and Dapper's
    /// <c>AddDynamicParams</c> merges by dictionary insert — so combining two
    /// independently-built sets throws on the first shared name. Sharing one set also
    /// means the two fragments cannot drift onto different values for the same caller.
    /// </remarks>
    public static (string VisibleSql, string ExactSql, DynamicParameters Parameters) FeatureLayerFragments(
        AccessContext ctx, string alias)
    {
        RequireAlias(alias);
        var parameters = BaseParameters(ctx);
        var visibleSql = FeatureWalk(
            ctx.For(AccessDomain.Features, AccessAction.Read),
            alias,
            "acc_r",
            parameters,
            withVisibilityArm: true);
        return (visibleSql, ExactViewSql(ctx, alias, parameters), parameters);
    }

    private static string ExactViewSql(AccessContext ctx, string alias, DynamicParameters parameters)
    {
        var velWalk = FeatureWalk(
            ctx.For(AccessDomain.Features, AccessAction.ViewExactLocation),
            "root",
            "acc_vel",
            parameters,
            withVisibilityArm: false);
        return $"""
            (@{IsFullAdminParam}
             OR {alias}.owner_user_id = @{UserIdParam}
             OR NOT EXISTS (SELECT 1 FROM features root
                            WHERE root.id = ANY({alias}.ancestor_ids)
                              AND root.location_protected
                              AND NOT {velWalk}))
            """;
    }

    /// <summary>
    /// Read-visibility fragment for NON-feature protected tables (trips, geofiles,
    /// rasters, views, documents): the walk without the arms that only exist in the
    /// feature world (subtree, set, kind/type), visibility consulting the row alone.
    /// The aliased table must expose <c>id</c>, <c>owner_user_id</c>,
    /// <c>caving_group_id</c> and <c>visibility</c>; no soft-delete arm is emitted, so a
    /// table that soft-deletes must add its own guard the way the feature callers do.
    /// </summary>
    /// <param name="reachedByAttachment">
    /// Ids the caller reaches through an object the row's content is attached to, resolved
    /// for Read by the caller — the flat form of the attachment built-in. It joins the
    /// same ELSE arm as ownership and visibility so it widens only what no entry decided;
    /// hoisted out of the CASE it would override a deny. Empty for domains without
    /// attachments.
    /// </param>
    public static (string Sql, DynamicParameters Parameters) VisibleToFragment(
        AccessContext ctx, AccessDomain domain, string alias, IReadOnlyCollection<Guid>? reachedByAttachment = null)
    {
        RequireAlias(alias);
        var parameters = BaseParameters(ctx);
        var set = ctx.For(domain, AccessAction.Read);
        var prefix = "acc_r";
        Guid[] reached = reachedByAttachment is null ? [] : [.. reachedByAttachment];
        parameters.Add($"{prefix}_attachment_reach", UuidArray(reached));
        var reachArm = $"OR {alias}.id = ANY(@{prefix}_attachment_reach)";
        var visibilityArm = $"""
            OR {alias}.visibility >= {(short)Visibility.Authenticated}
                   OR ({alias}.visibility = {(short)Visibility.CavingGroup}
                       AND {alias}.caving_group_id IS NOT NULL
                       AND {alias}.caving_group_id = ANY(@{CavingGroupIdsParam}))
                   {reachArm}
            """;

        if (set.IsEmpty)
        {
            return ($"""
                (@{IsFullAdminParam}
                 OR {alias}.owner_user_id = @{UserIdParam}
                 {visibilityArm})
                """, parameters);
        }

        parameters.Add($"{prefix}_deny_obj", UuidArray(set.DenyObjectIds));
        parameters.Add($"{prefix}_allow_obj", UuidArray(set.AllowObjectIds));
        parameters.Add($"{prefix}_deny_all", set.DenyAll);
        parameters.Add($"{prefix}_allow_all", set.AllowAll);
        parameters.Add($"{prefix}_deny_own", set.DenyOwn);
        parameters.Add($"{prefix}_allow_own", set.AllowOwn);
        parameters.Add($"{prefix}_deny_cg_ids", UuidArray(set.DenyCavingGroupIds));
        parameters.Add($"{prefix}_allow_cg_ids", UuidArray(set.AllowCavingGroupIds));

        var sql = $"""
            (@{IsFullAdminParam} OR
             CASE
               WHEN {alias}.id = ANY(@{prefix}_deny_obj)  THEN false
               WHEN {alias}.id = ANY(@{prefix}_allow_obj) THEN true
               WHEN @{prefix}_deny_all
                    OR (@{prefix}_deny_own AND {alias}.owner_user_id = @{UserIdParam})
                    OR ({alias}.caving_group_id IS NOT NULL
                        AND {alias}.caving_group_id = ANY(@{prefix}_deny_cg_ids)) THEN false
               ELSE @{prefix}_allow_all
                    OR (@{prefix}_allow_own AND {alias}.owner_user_id = @{UserIdParam})
                    OR ({alias}.caving_group_id IS NOT NULL
                        AND {alias}.caving_group_id = ANY(@{prefix}_allow_cg_ids))
                    OR {alias}.owner_user_id = @{UserIdParam}
                    {visibilityArm}
             END)
            """;
        return (sql, parameters);
    }

    /// <summary>
    /// The feature-domain walk for one (action) slice as a CASE chain over
    /// <paramref name="alias"/>. Adds this slice's parameters under
    /// <paramref name="prefix"/>; the shared caller parameters are added by the public
    /// entry points. With an empty slice the walk collapses to the built-ins — the lean
    /// plan the EXPLAIN pins guard for entry-less callers.
    /// </summary>
    private static string FeatureWalk(
        AccessFilterSet set, string alias, string prefix, DynamicParameters parameters, bool withVisibilityArm)
    {
        // The D1d read-time cascade: a row is visibility-readable when its own row or
        // any ancestor admits the caller. ancestor_ids includes self; subtree-stamped
        // soft delete makes the deleted_at guard equivalent on both twins.
        var visibilityArm = withVisibilityArm
            ? $"""

                    OR EXISTS (SELECT 1 FROM features anc
                               WHERE anc.id = ANY({alias}.ancestor_ids)
                                 AND anc.deleted_at IS NULL
                                 AND (anc.visibility >= {(short)Visibility.Authenticated}
                                      OR (anc.visibility = {(short)Visibility.CavingGroup}
                                          AND anc.caving_group_id IS NOT NULL
                                          AND anc.caving_group_id = ANY(@{CavingGroupIdsParam}))))
               """
            : string.Empty;

        if (set.IsEmpty)
        {
            return $"""
                (@{IsFullAdminParam}
                 OR {alias}.owner_user_id = @{UserIdParam}{visibilityArm})
                """;
        }

        parameters.Add($"{prefix}_deny_obj", UuidArray(set.DenyObjectIds));
        parameters.Add($"{prefix}_allow_obj", UuidArray(set.AllowObjectIds));
        parameters.Add($"{prefix}_deny_subtree_roots", UuidArray(set.DenySubtreeRoots));
        parameters.Add($"{prefix}_allow_subtree_roots", UuidArray(set.AllowSubtreeRoots));
        parameters.Add($"{prefix}_deny_set_ids", UuidArray(set.DenySetIds));
        parameters.Add($"{prefix}_allow_set_ids", UuidArray(set.AllowSetIds));
        parameters.Add($"{prefix}_deny_all", set.DenyAll);
        parameters.Add($"{prefix}_allow_all", set.AllowAll);
        parameters.Add($"{prefix}_deny_own", set.DenyOwn);
        parameters.Add($"{prefix}_allow_own", set.AllowOwn);
        parameters.Add($"{prefix}_deny_cg_ids", UuidArray(set.DenyCavingGroupIds));
        parameters.Add($"{prefix}_allow_cg_ids", UuidArray(set.AllowCavingGroupIds));
        parameters.Add($"{prefix}_deny_all_kinds", SmallintArray(set.DenyAllKinds));
        parameters.Add($"{prefix}_allow_all_kinds", SmallintArray(set.AllowAllKinds));
        parameters.Add($"{prefix}_deny_own_kinds", SmallintArray(set.DenyOwnKinds));
        parameters.Add($"{prefix}_allow_own_kinds", SmallintArray(set.AllowOwnKinds));
        parameters.Add($"{prefix}_deny_all_type_ids", BigintArray(set.DenyAllTypeIds));
        parameters.Add($"{prefix}_allow_all_type_ids", BigintArray(set.AllowAllTypeIds));
        parameters.Add($"{prefix}_deny_own_type_ids", BigintArray(set.DenyOwnTypeIds));
        parameters.Add($"{prefix}_allow_own_type_ids", BigintArray(set.AllowOwnTypeIds));

        return $"""
            (@{IsFullAdminParam} OR
             CASE
               WHEN {alias}.id = ANY(@{prefix}_deny_obj)  THEN false
               WHEN {alias}.id = ANY(@{prefix}_allow_obj) THEN true
               WHEN {alias}.ancestor_ids && @{prefix}_deny_subtree_roots
                    OR EXISTS (SELECT 1 FROM feature_set_members fsm
                               WHERE fsm.feature_id = {alias}.id
                                 AND fsm.feature_set_id = ANY(@{prefix}_deny_set_ids)) THEN false
               WHEN {alias}.ancestor_ids && @{prefix}_allow_subtree_roots
                    OR EXISTS (SELECT 1 FROM feature_set_members fsm
                               WHERE fsm.feature_id = {alias}.id
                                 AND fsm.feature_set_id = ANY(@{prefix}_allow_set_ids)) THEN true
               WHEN @{prefix}_deny_all
                    OR (@{prefix}_deny_own AND {alias}.owner_user_id = @{UserIdParam})
                    OR ({alias}.caving_group_id IS NOT NULL
                        AND {alias}.caving_group_id = ANY(@{prefix}_deny_cg_ids))
                    OR {alias}.kind = ANY(@{prefix}_deny_all_kinds)
                    OR ({alias}.feature_type_id IS NOT NULL
                        AND {alias}.feature_type_id = ANY(@{prefix}_deny_all_type_ids))
                    OR ({alias}.owner_user_id = @{UserIdParam}
                        AND ({alias}.kind = ANY(@{prefix}_deny_own_kinds)
                             OR ({alias}.feature_type_id IS NOT NULL
                                 AND {alias}.feature_type_id = ANY(@{prefix}_deny_own_type_ids)))) THEN false
               ELSE @{prefix}_allow_all
                    OR (@{prefix}_allow_own AND {alias}.owner_user_id = @{UserIdParam})
                    OR ({alias}.caving_group_id IS NOT NULL
                        AND {alias}.caving_group_id = ANY(@{prefix}_allow_cg_ids))
                    OR {alias}.kind = ANY(@{prefix}_allow_all_kinds)
                    OR ({alias}.feature_type_id IS NOT NULL
                        AND {alias}.feature_type_id = ANY(@{prefix}_allow_all_type_ids))
                    OR ({alias}.owner_user_id = @{UserIdParam}
                        AND ({alias}.kind = ANY(@{prefix}_allow_own_kinds)
                             OR ({alias}.feature_type_id IS NOT NULL
                                 AND {alias}.feature_type_id = ANY(@{prefix}_allow_own_type_ids))))
                    OR {alias}.owner_user_id = @{UserIdParam}{visibilityArm}
             END)
            """;
    }

    private static void RequireAlias(string alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            // Correlated subqueries reference the outer id column; without an alias the
            // bare "id" would resolve to the subquery's own table inside EXISTS arms.
            throw new ArgumentException("The access fragments require a table alias.", nameof(alias));
        }
    }

    private static DynamicParameters BaseParameters(AccessContext ctx)
    {
        var parameters = new DynamicParameters();
        parameters.Add(UserIdParam, ctx.UserId);
        parameters.Add(IsFullAdminParam, ctx.IsFullAdmin);
        parameters.Add(CavingGroupIdsParam, UuidArray([.. ctx.CavingGroupIds]));
        return parameters;
    }

    /// <summary>
    /// A native <c>uuid[]</c> parameter for <c>= ANY(...)</c> tests. Dapper's own list
    /// handling expands an array into one placeholder per element, which was measured
    /// 50x slower on large result sets — pass id sets through this instead.
    /// </summary>
    public static SqlMapper.ICustomQueryParameter UuidArray(Guid[] values) =>
        new ArrayParameter<Guid>(values, NpgsqlDbType.Uuid);

    public static SqlMapper.ICustomQueryParameter SmallintArray(short[] values) =>
        new ArrayParameter<short>(values, NpgsqlDbType.Smallint);

    public static SqlMapper.ICustomQueryParameter BigintArray(long[] values) =>
        new ArrayParameter<long>(values, NpgsqlDbType.Bigint);

    private sealed class ArrayParameter<T>(T[] values, NpgsqlDbType elementType)
        : SqlMapper.ICustomQueryParameter
    {
        public void AddParameter(IDbCommand command, string name)
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elementType)
            {
                Value = values,
            });
        }
    }
}
