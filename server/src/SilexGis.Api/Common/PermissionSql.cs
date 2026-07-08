// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;

namespace SilexGis.Api.Common;

/// <summary>
/// SQL twin of <see cref="PermissionQueryExtensions.VisibleTo{T}"/> for Dapper queries.
/// A parity test guarantees the two never diverge — change them together or not at all.
/// </summary>
public static class PermissionSql
{
    /// <summary>
    /// Read-visibility fragment over columns owner_user_id/team_id/visibility, plus the
    /// explicit object_acl Read-grant layer when <paramref name="aclEntityType"/> is
    /// given (the entity's id column must be <c>{alias}.id</c>). Add the returned
    /// parameters to your Dapper query.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) VisibleToFragment(
        UserContext user, string alias = "", Domain.Entities.AttachedEntityType? aclEntityType = null)
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        var parameters = new DynamicParameters();
        parameters.Add("vis_user_id", user.UserId);
        parameters.Add("vis_is_admin", user.IsAdmin);
        // A plain Guid[] would be list-expanded by Dapper into a per-row construct
        // (observed 50x slowdown at 50k rows); send a native uuid[] parameter instead.
        parameters.Add("vis_team_ids", new UuidArrayParameter([.. user.TeamIds]));

        // Mirrors the ACL branch of PermissionQueryExtensions.VisibleTo: an explicit
        // Read grant to the user or one of their teams. Subject kinds 0=user, 1=team;
        // Read flag = 1 — schema-contract values, locked by tests.
        var aclSql = string.Empty;
        if (aclEntityType is not null)
        {
            if (string.IsNullOrEmpty(alias))
            {
                // The EXISTS correlates on the outer id column; without an alias the bare
                // "id" would resolve to object_acl.id inside the subquery.
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
