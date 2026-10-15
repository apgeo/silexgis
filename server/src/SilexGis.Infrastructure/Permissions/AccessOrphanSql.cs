// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>An access row whose named row exists in none of the tables its arm allows.</summary>
public sealed record AccessOrphan(AccessAnchorArm Arm, long RowId, Guid AnchorId);

/// <summary>
/// Finds access entries and permission-group members whose polymorphic reference names a row
/// that no longer exists (raw SQL lives only in *Sql.cs files).
/// </summary>
/// <remarks>
/// <para>
/// One statement, built from <see cref="AccessAnchors.Arms"/>: one branch per arm, a
/// <c>NOT EXISTS</c> per table the arm allows. The table and key names come from the model, so a
/// renamed table cannot leave a branch looking in the wrong place, and the discriminators ride as
/// parameters. Nothing a caller supplies reaches the text.
/// </para>
/// <para>
/// Deliberately written against the tables rather than through the model's query filters: a
/// soft-deleted trip, document or cave is still in its table, and the rules on it are kept so
/// that putting it back gives back its access. Read through the filters, every rule on a binned
/// row would look dangling — and the sweep built on this would delete them.
/// </para>
/// <para>
/// One statement is one snapshot, so a row created together with its rule in a transaction that
/// commits meanwhile is seen with its rule or not at all, never as a rule without its row.
/// </para>
/// </remarks>
public static class AccessOrphanSql
{
    public static async Task<IReadOnlyList<AccessOrphan>> FindAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var (sql, parameters, arms) = Build(db);
        var rows = await db.Database.GetDbConnection().QueryAsync<OrphanRow>(new CommandDefinition(
            sql, parameters, db.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: ct));
        return [.. rows.Select(r => new AccessOrphan(arms[r.Arm], r.RowId, r.AnchorId))];
    }

    /// <summary>The statement, its parameters and the arms its branch numbers index.</summary>
    public static (string Sql, DynamicParameters Parameters, IReadOnlyList<AccessAnchorArm> Arms) Build(DbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        var helper = db.GetService<ISqlGenerationHelper>();
        var arms = AccessAnchors.Arms.Where(a => !a.HasForeignKey).ToList();
        var parameters = new DynamicParameters();
        parameters.Add("ao_object_scope", (short)Domain.Access.AccessScopeKind.Object);

        var branches = new List<string>();
        for (var i = 0; i < arms.Count; i++)
        {
            var arm = arms[i];
            var (from, column, condition) = arm.Column switch
            {
                AccessReferenceColumn.ObjectScope =>
                    ("access_entries", "scope_id", $"x.scope_kind = @ao_object_scope AND x.domain = @ao_{i}"),
                AccessReferenceColumn.ScopeId =>
                    ("access_entries", "scope_id", $"x.scope_kind = @ao_{i}"),
                AccessReferenceColumn.Subject =>
                    ("access_entries", "subject_id", $"x.subject_kind = @ao_{i}"),
                AccessReferenceColumn.PermissionMember =>
                    ("permission_group_members", "member_id", $"x.member_kind = @ao_{i}"),
                _ => throw new InvalidOperationException($"{arm.Name} has no polymorphic column to search."),
            };
            parameters.Add($"ao_{i}", arm.Column switch
            {
                AccessReferenceColumn.ObjectScope => (short)arm.Domain,
                AccessReferenceColumn.ScopeId => (short)arm.ScopeKind,
                _ => (short)arm.SubjectKind,
            });

            var present = string.Join(" OR ", arm.Targets.Select(target =>
            {
                var entity = db.Model.FindEntityType(target)
                    ?? throw new InvalidOperationException($"{target.Name} is not mapped.");
                var table = helper.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                var key = helper.DelimitIdentifier(entity.FindPrimaryKey()!.Properties.Single().GetColumnName());
                return $"EXISTS (SELECT 1 FROM {table} t WHERE t.{key} = x.{column})";
            }));

            branches.Add(
                $"SELECT {i} AS arm, x.id AS rowid, x.{column} AS anchorid FROM {from} x " +
                $"WHERE {condition} AND x.{column} IS NOT NULL AND NOT ({present})");
        }

        var sql = new StringBuilder()
            .AppendJoin("\nUNION ALL\n", branches)
            .Append("\nORDER BY arm, rowid")
            .ToString();
        return (sql, parameters, arms);
    }

    internal sealed record OrphanRow(int Arm, long RowId, Guid AnchorId);
}
