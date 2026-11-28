// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>How many of a table's rows a run accounts for, and how many of them fill each column.</summary>
public sealed record TestDataTableCoverage(string Table, long Rows, IReadOnlyList<TestDataColumnCoverage> Columns);

public sealed record TestDataColumnCoverage(string Column, long NonNull);

/// <summary>
/// The coverage report's queries: counts only, never a value. A table is measured over the rows
/// a run accounts for — the rows it listed, or the rows that share their key with them or point
/// at them — so a report of one run is not inflated by what else the database holds.
/// </summary>
public static class TestDataCoverageSql
{
    /// <summary>A table the report measures: which listed rows it covers, and through which column.</summary>
    /// <param name="KeyType">The SQL type of the registry table's key, which the registry stores as text: <c>uuid</c> or <c>bigint</c>.</param>
    public sealed record Scope(string Table, string RegistryTable, string KeyColumn, string KeyType = "uuid");

    /// <summary>
    /// The tables measured, each tied to the registry table its rows hang from. Grows with the
    /// generator; a table the generator writes and no scope names is caught by the coverage test.
    /// </summary>
    public static IReadOnlyList<Scope> Scopes { get; } =
    [
        new("features", "features", "id"),
        new("caves", "features", "id"),
        new("cave_entrances", "features", "id"),
        new("feature_hierarchy_edges", "features", "child_id"),
        new("feature_ancestors", "features", "feature_id"),
        new("taggings", "features", "feature_id"),
        new("users", "users", "id"),
        new("user_roles", "users", "user_id"),
        new("user_addresses", "user_addresses", "id"),
        new("user_notification_preferences", "user_notification_preferences", "id", "bigint"),
        new("calendar_feed_tokens", "calendar_feed_tokens", "id"),
        new("api_access_keys", "api_access_keys", "id"),
        new("cavers", "cavers", "id"),
        new("caving_groups", "caving_groups", "id"),
        new("caving_group_memberships", "caving_group_memberships", "id", "bigint"),
        new("permission_groups", "permission_groups", "id"),
        new("permission_group_members", "permission_group_members", "id", "bigint"),
        new("access_entries", "access_entries", "id", "bigint"),
        new("feature_links", "feature_links", "id", "bigint"),
        new("feature_external_ids", "feature_external_ids", "id"),
        new("feature_shares", "feature_shares", "id"),
        new("cave_qr_publications", "cave_qr_publications", "id"),
        new("link_kinds", "link_kinds", "id", "bigint"),
        new("cave_types", "cave_types", "id", "bigint"),
        new("entrance_types", "entrance_types", "id", "bigint"),
        new("rock_types", "rock_types", "id", "bigint"),
        new("feature_types", "feature_types", "id", "bigint"),
        new("cave_surveys", "cave_surveys", "id"),
        new("survey_models", "survey_models", "id"),
        new("survey_sources", "survey_sources", "id"),
        new("cave_depth_places", "cave_depth_places", "id"),
        new("cave_level_bands", "cave_level_bands", "id"),
        new("documents", "documents", "id"),
        new("document_versions", "document_versions", "id"),
        new("files", "files", "id"),
    ];

    public static async Task<IReadOnlyList<TestDataTableCoverage>> MeasureAsync(
        SilexGisDbContext db, Guid runId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var columnsByTable = await ColumnsAsync(db, [.. Scopes.Select(s => s.Table).Distinct()], ct);
        var report = new List<TestDataTableCoverage>();
        foreach (var scope in Scopes)
        {
            if (!columnsByTable.TryGetValue(scope.Table, out var columns) || !columns.Contains(scope.KeyColumn))
            {
                continue;
            }

            report.Add(await MeasureTableAsync(db, runId, scope, columns, ct));
        }

        return report;
    }

    private static async Task<TestDataTableCoverage> MeasureTableAsync(
        SilexGisDbContext db, Guid runId, Scope scope, IReadOnlyList<string> columns, CancellationToken ct)
    {
        // Identifiers come from information_schema, never from a caller, and are quoted besides.
        var select = new StringBuilder("SELECT count(*)");
        foreach (var column in columns)
        {
            select.Append(", count(").Append(Quote(column)).Append(')');
        }

        select.Append(" FROM ").Append(Quote(scope.Table))
            // The registry's text id is cast, never the table's uuid key: a cast key cannot use the
            // key's index, and every scope would read its whole table to count one run's rows.
            .Append(" WHERE ").Append(Quote(scope.KeyColumn)).Append(" IN (")
            .Append("SELECT row_id::").Append(scope.KeyType is "bigint" ? "bigint" : "uuid")
            .Append(" FROM test_data_rows WHERE run_id = @run AND table_name = @registry)");

        await using var command = await CommandAsync(db, ct);
        command.CommandText = select.ToString();
        command.Parameters.Add(new NpgsqlParameter("run", NpgsqlDbType.Uuid) { Value = runId });
        command.Parameters.Add(new NpgsqlParameter("registry", NpgsqlDbType.Text) { Value = scope.RegistryTable });
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var rows = reader.GetInt64(0);
        var measured = columns.Select((c, i) => new TestDataColumnCoverage(c, reader.GetInt64(i + 1))).ToList();
        return new TestDataTableCoverage(scope.Table, rows, measured);
    }

    /// <summary>The columns of each table, in their order.</summary>
    private static async Task<Dictionary<string, IReadOnlyList<string>>> ColumnsAsync(
        SilexGisDbContext db, string[] tables, CancellationToken ct)
    {
        await using var command = await CommandAsync(db, ct);
        command.CommandText = """
            SELECT table_name, column_name FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = ANY(@tables)
              AND is_generated = 'NEVER'
            ORDER BY table_name, ordinal_position
            """;
        command.Parameters.Add(new NpgsqlParameter("tables", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = tables });
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                if (!result.TryGetValue(table, out var list))
                {
                    result[table] = list = [];
                }

                list.Add(reader.GetString(1));
            }
        }

        return result.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static async Task<DbCommand> CommandAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        return connection.CreateCommand();
    }
}
