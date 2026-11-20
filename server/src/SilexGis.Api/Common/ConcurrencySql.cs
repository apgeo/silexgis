// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>Allow-listed tables for the concurrency-version lookup (never user input).</summary>
public enum VersionedTable
{
    /// <summary>
    /// The feature supertype row. Every feature kind — caves, entrances, centerlines and
    /// generic features — versions here: a subtype edit also touches its feature row, so one
    /// token covers the whole aggregate and a concurrent edit of any part is detected.
    /// </summary>
    Features,
    TripLogs,
    Geofiles,
    GeoreferencedMaps,
    MapViews,
    SurveyModels,
    Files,
    Expeditions,
    Events,
    Checklists,
    Documents,
}

/// <summary>
/// Raw SQL for optimistic concurrency (raw SQL lives only in *Sql.cs files). PostgreSQL's
/// xmin system column is the version token — it changes on every row update without any
/// schema change; reading it explicitly avoids provider-specific model mapping.
/// </summary>
public static class ConcurrencySql
{
    private static readonly IReadOnlyDictionary<VersionedTable, string> Tables =
        new Dictionary<VersionedTable, string>
        {
            [VersionedTable.Features] = "features",
            [VersionedTable.Geofiles] = "geofiles",
            [VersionedTable.GeoreferencedMaps] = "georeferenced_maps",
            [VersionedTable.MapViews] = "map_views",
            [VersionedTable.SurveyModels] = "survey_models",
            [VersionedTable.Files] = "files",
            [VersionedTable.Expeditions] = "expeditions",
            [VersionedTable.Events] = "events",
            [VersionedTable.Checklists] = "checklists",
            [VersionedTable.Documents] = "documents",
        };

    /// <summary>
    /// The advisory-lock class every whole-set replacement takes its lock in ("SXWS" in ASCII),
    /// kept apart from the other advisory locks the application takes.
    /// </summary>
    private const int WholeSetLockClass = 0x53585753;

    /// <summary>
    /// A trip's version, as a statement of its own rather than a row in the table of names: a
    /// deleted trip is kept in this table, hidden from every read the model makes, and a
    /// statement written by hand passes through none of that. So it says so itself — a deleted
    /// trip has no version to hand out and none to compare a precondition against, which is the
    /// same answer a trip that never existed gives.
    /// </summary>
    private const string LiveTripLogVersion =
        "SELECT xmin::text::bigint FROM trip_logs WHERE id = @id AND deleted_at IS NULL";

    /// <summary>
    /// The row's version, taking its lock until the surrounding transaction ends, or null when
    /// the row does not exist (a deleted trip counts as absent). Under the default isolation a
    /// statement that waited for another writer's lock reads the version that writer committed,
    /// not the one it started from. Needs a transaction on the context.
    /// </summary>
    public static async Task<long?> LockVersionAsync(
        SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct) =>
        await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                VersionSql(table) + " FOR UPDATE",
                new { id },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));

    // xid has no direct integer cast; text round-trip is the documented conversion.
    private static string VersionSql(VersionedTable table) =>
        table == VersionedTable.TripLogs
            ? LiveTripLogVersion
            : $"SELECT xmin::text::bigint FROM {Tables[table]} WHERE id = @id";

    /// <summary>Current row version, or null when the row does not exist.</summary>
    public static async Task<long?> VersionAsync(
        SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct)
    {
        return await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                VersionSql(table),
                new { id },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
    }

    /// <summary>
    /// The versions of several rows in one statement, for a listing whose rows each carry the
    /// version an edit opened from that row sends back. A row that does not exist (a deleted trip
    /// included) is absent from the answer.
    /// </summary>
    /// <remarks>
    /// Read in the same snapshot as the rows themselves — the caller's transaction at repeatable
    /// read — or a write landing between the two statements would pair a row's older values with
    /// its newer version, and an edit opened from that row would overwrite the change unseen.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<Guid, long>> VersionsAsync(
        SilexGisDbContext db, VersionedTable table, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        var sql = table == VersionedTable.TripLogs
            ? "SELECT id AS Id, xmin::text::bigint AS Version FROM trip_logs WHERE id = ANY(@ids) AND deleted_at IS NULL"
            : $"SELECT id AS Id, xmin::text::bigint AS Version FROM {Tables[table]} WHERE id = ANY(@ids)";
        var rows = await db.Database.GetDbConnection().QueryAsync<(Guid Id, long Version)>(
            new CommandDefinition(
                sql,
                new { ids = ids.ToArray() },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
        return rows.ToDictionary(x => x.Id, x => x.Version);
    }

    /// <summary>
    /// One version for a set of access rules that is read and replaced as a whole — a permission
    /// group's rules, the rules authored on one object. It changes when any member is updated, added or
    /// removed. A digest of each member's own row version, for the same reasons as a series run's
    /// (two changes cannot cancel out, a row dropping out cannot leave the figure where it was);
    /// an empty set has a version too, the digest of nothing, so that a set read empty and filled
    /// since is refused like any other.
    /// </summary>
    /// <remarks>
    /// The caller names the members it read, so the version describes exactly those rows; it reads
    /// both in one snapshot (or under <see cref="LockWholeSetAsync"/>) so the two cannot disagree.
    /// A member deleted since is absent from the digest, which is what makes the version move.
    /// Access rules are keyed by a number rather than a uuid, which is why they are not one of the
    /// tables a single row is held in.
    /// </remarks>
    public static async Task<string> AccessRuleSetVersionAsync(
        SilexGisDbContext db, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        const string sql = """
            SELECT md5(coalesce(string_agg(id::text || ':' || xmin::text, ',' ORDER BY id), ''))
            FROM access_entries WHERE id = ANY(@ids)
            """;
        return await db.Database.GetDbConnection().QuerySingleAsync<string>(
            new CommandDefinition(
                sql,
                new { ids = ids.ToArray() },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
    }

    /// <summary>
    /// Serialises the whole-set replacements of one set until the surrounding transaction ends.
    /// A row lock cannot do it: a set's members can be added, and an empty set has no row to lock
    /// at all. Two replacements made from the same reading would otherwise both find the version
    /// they were opened at and both write, the second silently undoing the first. Taken first,
    /// before the set is read, so the second replacement reads what the first left and is refused.
    /// </summary>
    /// <param name="set">Names the set: its kind and the id of what it hangs on.</param>
    public static Task LockWholeSetAsync(SilexGisDbContext db, string set, CancellationToken ct) =>
        db.Database.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                "SELECT pg_advisory_xact_lock(@cls, hashtext(@set))",
                new { cls = WholeSetLockClass, set },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));

    /// <summary>
    /// One version for a run of a series' occurrences: this day and every later one. It changes
    /// when any of them is updated, when one is added or removed, and when one is moved into or
    /// out of the run, which is exactly the set an edit of "this and the following" writes.
    /// </summary>
    /// <remarks>
    /// A digest of each member's own row version rather than a sum or a maximum, so that two
    /// changes cannot cancel one another and a row dropping out cannot leave the figure where it
    /// was. Hexadecimal, so it survives being carried as an entity tag. The series has at least
    /// the occurrence it was asked about; a series that has no rows answers null.
    /// </remarks>
    /// <param name="lockRows">
    /// Locks the run's rows until the surrounding transaction ends, so that the version returned
    /// stays true while the caller writes. Needs a transaction on the context; without one the
    /// lock is released as soon as the statement ends.
    /// </param>
    public static async Task<string?> EventSeriesFollowingVersionAsync(
        SilexGisDbContext db, Guid seriesId, DateOnly fromDay, CancellationToken ct,
        bool lockRows = false)
    {
        var sql = lockRows
            ? """
              SELECT md5(string_agg(id::text || ':' || xmin::text, ',' ORDER BY id))
              FROM (SELECT id, xmin FROM events
                    WHERE series_id = @seriesId AND start_date >= @fromDay
                    ORDER BY id FOR UPDATE) run
              """
            : """
              SELECT md5(string_agg(id::text || ':' || xmin::text, ',' ORDER BY id))
              FROM events
              WHERE series_id = @seriesId AND start_date >= @fromDay
              """;
        return await db.Database.GetDbConnection().QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(
                sql,
                new { seriesId, fromDay },
                transaction: db.Database.CurrentTransaction?.GetDbTransaction(),
                cancellationToken: ct));
    }
}
