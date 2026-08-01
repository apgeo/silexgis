// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>
/// SQL twin of <see cref="PermissionQueryExtensions.VisibleTo{T}"/> (and of the Domain
/// exact-view rule) for Dapper queries. Parity tests guarantee the twins never diverge —
/// change them together or not at all.
/// </summary>
public static class PermissionSql
{
    /// <summary>
    /// Read-visibility fragment over the features table (columns owner_user_id/team_id/
    /// visibility on <paramref name="alias"/>), including the explicit object_acl Read
    /// grants keyed by the feature FK. Grants are deliberately non-cascading — the
    /// hierarchy-cascade semantics belong to the planned ruleset permission system.
    /// The caller must additionally filter soft delete (<c>{alias}.deleted_at IS NULL</c>);
    /// the EF side gets that from the global query filter.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) FeatureVisibleToFragment(
        UserContext user, string alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            // The ACL EXISTS correlates on the outer id column; without an alias the bare
            // "id" would resolve to object_acl.id inside the subquery.
            throw new ArgumentException("The feature fragment requires a table alias.", nameof(alias));
        }

        var parameters = BaseParameters(user);
        var sql = $"""
            (@vis_is_admin
             OR {alias}.owner_user_id = @vis_user_id
             OR {alias}.visibility >= {(short)Visibility.Authenticated}
             OR ({alias}.visibility = {(short)Visibility.Team}
                 AND {alias}.team_id = ANY(@vis_team_ids))
             OR EXISTS (SELECT 1 FROM object_acl acl
                        WHERE acl.feature_id = {alias}.id
                          AND ((acl.subject_kind = 0 AND acl.subject_id = @vis_user_id)
                               OR (acl.subject_kind = 1 AND acl.subject_id = ANY(@vis_team_ids)))
                          AND (acl.permissions & {(int)ObjectPermission.Read}) <> 0))
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// Exact-location fragment over a feature row, bit-identical to
    /// <c>LocationProtection.CanViewExactLocation(user, row, roots)</c>: admin and the
    /// row's own owner always qualify; everyone else must hold ViewExactLocation on
    /// EVERY protected root above the row. Flat form — the roots are resolved from the
    /// row's ancestor array, no recursion. True for unprotected rows (no protected
    /// ancestor exists to veto).
    /// </summary>
    public static string ExactViewFragment(string alias) => $"""
        (@vis_is_admin
         OR {alias}.owner_user_id = @vis_user_id
         OR NOT EXISTS (SELECT 1 FROM features root
                        WHERE root.id = ANY({alias}.ancestor_ids)
                          AND root.location_protected
                          AND NOT (root.owner_user_id = @vis_user_id
                                   OR (root.team_id IS NOT NULL AND root.team_id = ANY(@vis_team_ids))
                                   OR EXISTS (SELECT 1 FROM object_acl acl
                                              WHERE acl.feature_id = root.id
                                                AND ((acl.subject_kind = 0 AND acl.subject_id = @vis_user_id)
                                                     OR (acl.subject_kind = 1 AND acl.subject_id = ANY(@vis_team_ids)))
                                                AND (acl.permissions & {(int)ObjectPermission.ViewExactLocation}) <> 0))))
        """;

    /// <summary>
    /// Read-visibility fragment for NON-feature tables (trips, geofiles, rasters, views),
    /// with the explicit object_acl Read-grant layer via the polymorphic pair when
    /// <paramref name="aclEntityType"/> is given (the entity's id column must be
    /// <c>{alias}.id</c>). Add the returned parameters to your Dapper query.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) VisibleToFragment(
        UserContext user, string alias = "", Domain.Entities.AttachedEntityType? aclEntityType = null)
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        var parameters = BaseParameters(user);

        // Mirrors the ACL branch of the polymorphic VisibleTo overload. Subject kinds
        // 0=user, 1=team; Read flag = 1 — schema-contract values, locked by tests.
        var aclSql = string.Empty;
        if (aclEntityType is not null)
        {
            if (string.IsNullOrEmpty(alias))
            {
                throw new ArgumentException("The ACL-aware fragment requires a table alias.", nameof(alias));
            }

            parameters.Add("vis_acl_entity_type", (short)aclEntityType.Value);
            aclSql = $"""

                 OR EXISTS (SELECT 1 FROM object_acl acl
                            WHERE acl.entity_type = @vis_acl_entity_type
                              AND acl.entity_id = {prefix}id
                              AND ((acl.subject_kind = 0 AND acl.subject_id = @vis_user_id)
                                   OR (acl.subject_kind = 1 AND acl.subject_id = ANY(@vis_team_ids)))
                              AND (acl.permissions & {(int)ObjectPermission.Read}) <> 0)
                """;
        }

        var sql = $"""
            (@vis_is_admin
             OR {prefix}owner_user_id = @vis_user_id
             OR {prefix}visibility >= {(short)Visibility.Authenticated}
             OR ({prefix}visibility = {(short)Visibility.Team}
                 AND {prefix}team_id = ANY(@vis_team_ids)){aclSql})
            """;

        return (sql, parameters);
    }

    private static DynamicParameters BaseParameters(UserContext user)
    {
        var parameters = new DynamicParameters();
        parameters.Add("vis_user_id", user.UserId);
        parameters.Add("vis_is_admin", user.IsAdmin);
        // A plain Guid[] would be list-expanded by Dapper into a per-row construct
        // (observed 50x slowdown at 50k rows); send a native uuid[] parameter instead.
        parameters.Add("vis_team_ids", new UuidArrayParameter([.. user.TeamIds]));
        return parameters;
    }

    /// <summary>
    /// A native <c>uuid[]</c> parameter for <c>= ANY(...)</c> tests. Dapper's own list handling
    /// expands an array into one placeholder per element, which was measured 50x slower on large
    /// result sets — pass id sets through this instead.
    /// </summary>
    public static SqlMapper.ICustomQueryParameter UuidArray(Guid[] values) => new UuidArrayParameter(values);

    private sealed class UuidArrayParameter(Guid[] values) : SqlMapper.ICustomQueryParameter
    {
        public void AddParameter(IDbCommand command, string name)
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = values,
            });
        }
    }
}
