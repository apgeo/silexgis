// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using Dapper;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Sends one statement with planner settings that hold for it alone — the one home of every such
/// setting, for the wide map statements measured to be planned worse without them.
/// </summary>
/// <remarks>
/// <para>
/// Each setting is transaction-scoped (<c>set_config(…, true)</c>) and sent in the same batch as
/// the statement, which PostgreSQL runs as one implicit transaction: it holds for that statement,
/// ends with it, never outlives it on a pooled connection and leaves every other statement to the
/// server's own configuration. Inside an explicit transaction it would last until that transaction
/// ended, which is why it is only ever sent this way.
/// </para>
/// <para>
/// Both settings answer the same misjudgement. The planner prices the per-row access checks spliced
/// into a wide statement — the visibility cascade's probe of a row's ancestors, the exact-view walk —
/// as if every ancestor array held ten ids it had to fetch from disk, when it holds two or three and
/// they are in cache.
/// </para>
/// </remarks>
public static class PlannerSql
{
    /// <summary>
    /// Switches JIT compilation off for the statement.
    /// </summary>
    /// <remarks>
    /// The inflated estimate puts a wide window in the millions of cost units, far past the
    /// thresholds at which the planner compiles with inlining and optimisation, while the statement
    /// itself runs in a few hundred milliseconds. Measured over a hundred thousand entrances,
    /// compiling the cluster statement took 600–700 ms of every request on top of 230–420 ms of
    /// execution, and it is CPU work, so on a busy machine it grew further. PostgreSQL keeps no
    /// compiled code from one execution to the next, so every request paid it again.
    /// </remarks>
    public const string WithoutJit = "SELECT set_config('jit', 'off', true)";

    /// <summary>
    /// Keeps the planner from answering a per-row probe of a handful of ids with a bitmap scan.
    /// </summary>
    /// <remarks>
    /// Expecting ten rows, the planner builds a bitmap for each ancestor probe and then visits the
    /// heap through it — a fixed cost that, for the two rows really there, is about twice that of
    /// walking the index directly. Paid once per candidate row by every reader who does not own the
    /// rows, it was the larger part of a country-wide cluster request: over a hundred thousand
    /// entrances, 970 ms of execution with bitmap probes against 630–690 ms with index probes. The
    /// statement's own window scan is an index scan either way.
    /// </remarks>
    public const string WithoutBitmapScans = "SELECT set_config('enable_bitmapscan', 'off', true)";

    /// <summary>The statement as it is sent: behind each setting, in one batch.</summary>
    public static string Prefixed(string sql, params IReadOnlyList<string> settings) =>
        string.Join(";\n", [.. settings, sql]);

    /// <summary>Runs <paramref name="sql"/> under <paramref name="settings"/> and reads its rows.</summary>
    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        DbConnection connection,
        string sql,
        object? parameters,
        IReadOnlyList<string> settings,
        CancellationToken ct)
    {
        await using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(Prefixed(sql, settings), parameters, cancellationToken: ct));
        foreach (var _ in settings)
        {
            await results.ReadSingleAsync<string>();
        }

        return [.. await results.ReadAsync<T>()];
    }
}
