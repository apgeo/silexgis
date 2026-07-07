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
    /// Read-visibility fragment over columns owner_user_id/team_id/visibility. Add the
    /// returned parameters to your Dapper query.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) VisibleToFragment(UserContext user, string alias = "")
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        var parameters = new DynamicParameters();
        parameters.Add("vis_user_id", user.UserId);
        parameters.Add("vis_is_admin", user.IsAdmin);
        // A plain Guid[] would be list-expanded by Dapper into a per-row construct
        // (observed 50x slowdown at 50k rows); send a native uuid[] parameter instead.
        parameters.Add("vis_team_ids", new UuidArrayParameter([.. user.TeamIds]));

        var sql = $"""
            (@vis_is_admin
             OR {prefix}owner_user_id = @vis_user_id
             OR {prefix}visibility >= {(short)Visibility.Authenticated}
             OR ({prefix}visibility = {(short)Visibility.Team}
                 AND {prefix}team_id = ANY(@vis_team_ids)))
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
