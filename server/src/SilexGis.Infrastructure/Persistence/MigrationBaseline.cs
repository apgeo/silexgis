// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Moves a database built by the long chain of migrations onto the single baseline that replaced
/// them, without running the baseline against tables that already exist.
/// </summary>
/// <remarks>
/// <para>
/// The schema was once written by about a hundred migrations; it is now written by one, and a
/// database the old chain built already holds everything that one would create. Left to itself the
/// migrator would see the baseline as unapplied and try to create every table again, so this runs
/// first: it brings a database at one of the chain's last steps up to its final step — with the
/// tool's own idempotent script of those steps — and then records the baseline as applied in place
/// of the old history, in one transaction.
/// </para>
/// <para>
/// A database the old chain left further back is refused rather than guessed at: the steps it is
/// missing no longer exist to be run. Update it first to a release from before the baseline, which
/// still carries them. A fresh database has no history, and the migrator applies the baseline to it
/// as usual.
/// </para>
/// </remarks>
public static class MigrationBaseline
{
    /// <summary>The migration that replaced the chain.</summary>
    public const string BaselineId = "20261017161527_Baseline";

    /// <summary>The chain's final step: a database here needs only its history rewritten.</summary>
    public const string ChainHead = "20261016125428_MapLayerOnSharedLinks";

    /// <summary>
    /// The chain's steps a database may stand at and still be moved: the final one, and the ones the
    /// catch-up script starts from. The earliest is where the installations running at the time of
    /// the squash stood.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedLastSteps =
    [
        "20261016021043_JobRetryAndProgress",
        "20261016090838_SavedFilters",
        ChainHead,
    ];

    public const string RefusedCode = "migrations.before_baseline";

    /// <summary>What happened, for the start-up log.</summary>
    public enum Outcome { NothingToDo, Adopted }

    /// <exception cref="InvalidOperationException">
    /// The database was last migrated by a step of the old chain this release cannot continue from.
    /// </exception>
    public static async Task<Outcome> AdoptAsync(DbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!await db.Database.SqlQueryRaw<bool>(MigrationBaselineSql.HistoryTableExists).SingleAsync(ct))
        {
            return Outcome.NothingToDo;
        }

        var applied = await db.Database.SqlQueryRaw<string>(MigrationBaselineSql.AppliedIds).ToListAsync(ct);
        if (applied.Count == 0 || applied.Contains(BaselineId))
        {
            return Outcome.NothingToDo;
        }

        var last = applied[^1];
        if (!AcceptedLastSteps.Contains(last))
        {
            throw new InvalidOperationException(
                $"{RefusedCode}: this database was last migrated to {last}, which this release cannot "
                + $"continue from. Update it first to a release from before the migration baseline "
                + $"(its last step is {ChainHead}), then to this one.");
        }

        var version = db.GetService<IMigrationsAssembly>().Migrations.ContainsKey(BaselineId)
            ? ProductInfo.GetVersion()
            : throw new InvalidOperationException($"The baseline {BaselineId} is not in this build.");

        // Through the connection rather than EF's raw-SQL helpers: those read braces as parameter
        // placeholders, and the catch-up script writes JSON defaults such as '{}'::jsonb.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        if (last != ChainHead)
        {
            await using var catchUp = connection.CreateCommand();
            catchUp.Transaction = transaction.GetDbTransaction();
            catchUp.CommandText = MigrationBaselineSql.CatchUp;
            await catchUp.ExecuteNonQueryAsync(ct);
        }

        await using (var replace = connection.CreateCommand())
        {
            replace.Transaction = transaction.GetDbTransaction();
            replace.CommandText = MigrationBaselineSql.ReplaceHistory;
            replace.Parameters.Add(new NpgsqlParameter("id", BaselineId));
            replace.Parameters.Add(new NpgsqlParameter("version", version));
            await replace.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return Outcome.Adopted;
    }
}
