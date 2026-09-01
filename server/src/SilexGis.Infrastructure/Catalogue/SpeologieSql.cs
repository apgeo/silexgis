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
