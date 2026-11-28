// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// Which generated rows something the run did not write refers to. Asked of every foreign key the
/// model declares onto a table, so a table that later gains a reference to accounts, cavers or
/// clubs is covered without anybody remembering to add it here — the generator's removal can then
/// never delete, or cascade away, a row that somebody's own data hangs on.
/// </summary>
public static class TestDataReferencesSql
{
    /// <summary>
    /// Tables whose rows belong to the row they point at and go with it — an account's roles and
    /// sign-in records, a feature's place in the ancestry — so a row of theirs never makes the
    /// row it points at "in use".
    /// </summary>
    public static IReadOnlySet<string> BelongingTables { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "user_roles", "user_claims", "user_logins", "user_tokens", "feature_ancestors", "taggings",
        "trip_type_schemas", "checklist_items",
    };

    /// <summary>
    /// The ids among <paramref name="ids"/> (rows of <paramref name="principalTable"/>) that a row
    /// not listed by run <paramref name="runId"/> refers to through a foreign key. A referring row
    /// whose table has a key of more than one column cannot be looked up in the registry, and
    /// counts as somebody's own unless its table is one of <see cref="BelongingTables"/>.
    /// </summary>
    public static async Task<HashSet<string>> ReferencedByOthersAsync(
        SilexGisDbContext db, Guid runId, string principalTable, IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ids);
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return found;
        }

        var principal = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == principalTable && e.BaseType is null)
            ?? throw new InvalidOperationException($"No entity is mapped to {principalTable}.");
        var principalKey = principal.FindPrimaryKey()!;
        if (principalKey.Properties.Count != 1)
        {
            throw new InvalidOperationException($"{principalTable} has a key of more than one column.");
        }

        var keyType = Nullable.GetUnderlyingType(principalKey.Properties[0].ClrType) ?? principalKey.Properties[0].ClrType;
        var (arrayType, typedIds) = keyType == typeof(Guid)
            ? (NpgsqlDbType.Array | NpgsqlDbType.Uuid, (object)ids.Select(Guid.Parse).ToArray())
            : keyType == typeof(long)
                ? (NpgsqlDbType.Array | NpgsqlDbType.Bigint, ids.Select(i => long.Parse(i, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
                : throw new InvalidOperationException($"{principalTable}'s key is neither a uuid nor a bigint.");

        var references = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredForeignKeys())
            .Where(fk => fk.PrincipalEntityType == principal && fk.PrincipalKey.IsPrimaryKey() && fk.Properties.Count == 1)
            .Select(fk => Reference(fk))
            .Where(r => r is not null && !BelongingTables.Contains(r.Table))
            .Distinct()
            .ToList();

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        foreach (var reference in references)
        {
            // Identifiers come from the model, never from a caller, and are quoted besides.
            var sql = new StringBuilder("SELECT DISTINCT d.").Append(Quote(reference!.Column)).Append("::text FROM ")
                .Append(Quote(reference.Table)).Append(" d WHERE d.").Append(Quote(reference.Column)).Append(" = ANY(@ids)");
            if (reference.KeyColumn is { } key)
            {
                sql.Append(" AND NOT EXISTS (SELECT 1 FROM test_data_rows r WHERE r.run_id = @run AND r.table_name = @table AND r.row_id = d.")
                    .Append(Quote(key)).Append("::text)");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = sql.ToString();
            command.Parameters.Add(new NpgsqlParameter("ids", arrayType) { Value = typedIds });
            command.Parameters.Add(new NpgsqlParameter("run", NpgsqlDbType.Uuid) { Value = runId });
            command.Parameters.Add(new NpgsqlParameter("table", NpgsqlDbType.Text) { Value = reference.Table });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                found.Add(reader.GetString(0));
            }
        }

        return found;
    }

    private sealed record ReferenceColumn(string Table, string Column, string? KeyColumn);

    private static ReferenceColumn? Reference(IForeignKey fk)
    {
        var dependent = fk.DeclaringEntityType;
        var table = dependent.GetTableName();
        if (table is null)
        {
            return null;
        }

        var store = StoreObjectIdentifier.Table(table, dependent.GetSchema());
        var column = fk.Properties[0].GetColumnName(store);
        if (column is null)
        {
            return null;
        }

        var key = dependent.FindPrimaryKey();
        var keyColumn = key is { Properties.Count: 1 } ? key.Properties[0].GetColumnName(store) : null;
        return new ReferenceColumn(table, column, keyColumn);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
