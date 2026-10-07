// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The survey that answers for a cave, reduced to what is needed to compare a station name with
/// it: which model it is and how that model spells its stations.
/// </summary>
/// <param name="Id">The model.</param>
/// <param name="Format">The format it was read from, which decides how its stations are spelled.</param>
/// <param name="RootSurveyName">The name of the file's root survey, where it has one.</param>
public sealed record ChosenSurveyModel(Guid Id, SurveyModelFormat Format, string? RootSurveyName);

/// <summary>
/// The survey marked as a cave's current one, where it is also the one answering for the cave, for
/// a caller who may be told (raw SQL lives only in *Sql.cs files).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a second rule.</b> Which of a cave's surveys answers for it is said in one
/// place, as a sub-select that the figures measured from a survey are all built over. This reads
/// that same sub-select and returns the model it names instead of something measured from it, so a
/// surface that asks "is this station in the cave's survey" is asking about the very survey the
/// cave's lengths and depths were measured over.
/// </para>
/// <para>
/// <b>Only where that survey is the marked one.</b> The sub-select has two fallbacks past the mark,
/// for a cave whose marked upload could not be read or which has no mark at all, so that the cave
/// keeps its figures. A statement made here is shown to people under the words "the cave's current
/// survey", beside a list that shows which upload carries the mark — and a statement about a
/// fallback survey would be about a different upload than the one the page calls current. So the
/// answering model is returned only when it carries the mark; otherwise the answer is null, the
/// same "nothing can be said" a cave with no survey gives. Because the mark is the sub-select's
/// first choice whenever its holder can answer, this is exactly "the marked line plot, once read"
/// without that rule being written a second time.
/// </para>
/// <para>
/// <b>The caller's rights are part of the statement.</b> Both arms are spliced in — Read on the
/// cave, and its exact position open — rather than left to a check made beforehand, because what
/// this answers is survey vocabulary: knowing that a cave has a survey that has been read, and
/// then which names are in it, is served on the terms the survey itself is served on. A caller who
/// may not place the cave gets null, which is also what a cave with no survey answers, and the two
/// cannot be told apart from here.
/// </para>
/// </remarks>
public static class ChosenSurveyModelSql
{
    /// <summary>
    /// The cave's current survey — the marked line plot, where its reading finished — or null when
    /// there is none, when another survey is standing in for it, or when this caller may not be
    /// told about it.
    /// </summary>
    public static async Task<ChosenSurveyModel?> CurrentForCaveAsync(
        SilexGisDbContext db, AccessContext ctx, Guid caveFeatureId, CancellationToken ct)
    {
        var (sql, parameters) = BuildForCave(ctx, caveFeatureId);
        var connection = db.Database.GetDbConnection();
        var row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return row is null ? null : new ChosenSurveyModel(row.Id, (SurveyModelFormat)row.Format, row.RootSurveyName);
    }

    /// <summary>The row as the database sends it: the format as the number its column holds.</summary>
    private sealed class Row
    {
        public Guid Id { get; set; }

        public short Format { get; set; }

        public string? RootSurveyName { get; set; }
    }

    private static (string Sql, DynamicParameters Parameters) BuildForCave(
        AccessContext ctx, Guid caveFeatureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("chosen_cave_id", caveFeatureId);

        // The feature kind is interpolated as the number it is so the planner sees a constant; it
        // is a compile-time enum value, never anything a caller supplies.
        var sql = $"""
            WITH chosen AS (
                {SurveySegmentSql.ChosenModel("@chosen_cave_id")}
            )
            SELECT m.id AS "Id",
                   m.format AS "Format",
                   m.root_survey_name AS "RootSurveyName"
            FROM chosen
            JOIN survey_models m ON m.id = chosen.model_id
            JOIN features f ON f.id = m.cave_feature_id
            WHERE m.cave_feature_id = @chosen_cave_id
              AND m.is_current
              AND f.kind = {(short)FeatureKind.Cave}
              AND f.deleted_at IS NULL
              AND {visibleSql}
              AND {exactSql}
            """;

        return (sql, parameters);
    }
}
