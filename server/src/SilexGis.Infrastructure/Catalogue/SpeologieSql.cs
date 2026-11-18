// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Catalogue;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Catalogue;

/// <summary>A cave this installation already holds for a catalogue entry.</summary>
public sealed record SpeologieMatch(int SpeologieId, Guid FeatureId);

/// <summary>
/// Raw SQL for the catalogue import (raw SQL lives only in *Sql.cs files).
///
/// <para>
/// One question, asked in two places and answered the same way both times: which caves here came
/// from which catalogue entries. The search screen asks it to mark a row as already imported;
/// the confirmation asks it to decide between creating a cave and refreshing one. Asking it in
/// SQL rather than through the object model is not an optimisation — the catalogue id lives
/// inside a jsonb column, and the index that makes it findable is an index on an expression,
/// which only an expression written the same way can use.
/// </para>
/// </summary>
public static class SpeologieSql
{
    /// <summary>
    /// The expression the index is built on. Written once so the query and the migration cannot
    /// drift apart: an expression index is used only by a query whose expression matches it
    /// exactly, and a query that stops matching does not fail — it silently becomes a scan of
    /// every feature in the installation.
    /// </summary>
    public const string IdExpression = "properties->>'" + SpeologieMapping.Keys.Id + "'";

    /// <summary>The first key of the advisory locks taken per catalogue entry ("SPL1").</summary>
    private const int CatalogueEntryLockClass = 0x53504C31;

    /// <summary>
    /// Takes, for the rest of the current transaction, a lock per catalogue entry, so that two
    /// confirmations naming the same entry cannot both decide it is new.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A confirmation looks for the cave an entry already has before it writes one, and two
    /// confirmations running together would otherwise both look before either wrote: both find
    /// nothing, and the entry becomes two caves. Nothing in the schema stops that — the index on the
    /// catalogue id is not unique, and cannot be made so on an installation that may already hold a
    /// duplicate — so the confirmation that comes second has to wait here until the first has
    /// committed, and then look. Each look after this lock is a fresh statement at read committed,
    /// so it sees what the first one wrote.
    /// </para>
    /// <para>
    /// Taken in ascending order of id, one statement each: two confirmations whose selections
    /// overlap then queue on their lowest shared entry instead of each holding one the other wants.
    /// Transaction-scoped, so a confirmation that fails or is rolled back cannot leave the next one
    /// waiting, and a confirmation replayed inside somebody else's transaction holds them until that
    /// transaction ends.
    /// </para>
    /// </remarks>
    public static async Task LockEntriesAsync(
        SilexGisDbContext db, IReadOnlyCollection<int> speologieIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(speologieIds);

        foreach (var id in speologieIds.Distinct().Order())
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0}, {1})", [CatalogueEntryLockClass, id], ct);
        }
    }

    /// <summary>
    /// Of the given catalogue ids, the ones that already have a cave here, with that cave.
    ///
    /// <para>
    /// Deliberately not filtered by what the caller may see. This decides whether a second import
    /// would duplicate a cave, and a cave the caller cannot see is still a cave that would be
    /// duplicated; hiding it here would mean the one thing the registry knows for certain — that
    /// this catalogue entry is already in it — is withheld precisely when it matters. What the
    /// caller may then <em>do</em> with the match is decided separately and per row, and refusing
    /// there is what keeps this from disclosing anything about the cave itself.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<SpeologieMatch>> MatchAsync(
        SilexGisDbContext db, IReadOnlyCollection<int> speologieIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(speologieIds);

        if (speologieIds.Count == 0)
        {
            return [];
        }

        var ids = speologieIds.Distinct().Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var sql = $"""
            SELECT ({IdExpression})::int AS "{nameof(SpeologieMatch.SpeologieId)}",
                   id                    AS "{nameof(SpeologieMatch.FeatureId)}"
            FROM features
            WHERE kind = @kind
              AND deleted_at IS NULL
              AND {IdExpression} = ANY(@ids)
            """;

        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<SpeologieMatch>(
            new CommandDefinition(sql, new { kind = (short)FeatureKind.Cave, ids }, cancellationToken: ct));

        // One catalogue entry should map to one cave, but nothing in the schema forbids two —
        // two installations merged, or a cave copied. The first by id is taken so the answer is
        // stable between calls rather than whatever the planner returned first.
        return rows
            .GroupBy(r => r.SpeologieId)
            .Select(g => g.OrderBy(r => r.FeatureId).First())
            .ToArray();
    }
}
