// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;

namespace SilexGis.Api.Common;

/// <summary>
/// SQL twin of <see cref="PermissionQueryExtensions.VisibleTo{T}"/> for Dapper queries
/// (05-auth-permissions.md §3). A parity test guarantees the two never diverge — change
/// them together or not at all.
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
        parameters.Add("vis_team_ids", user.TeamIds.ToArray());

        var sql = $"""
            (@vis_is_admin
             OR {prefix}owner_user_id = @vis_user_id
             OR {prefix}visibility >= {(short)Visibility.Authenticated}
             OR ({prefix}visibility = {(short)Visibility.Team}
                 AND {prefix}team_id = ANY(@vis_team_ids)))
            """;

        return (sql, parameters);
    }
}
