// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The wall distances recorded at a cave's stations, read from the one survey model that answers
/// for it (raw SQL lives only in *Sql.cs files).
///
/// <para>
/// <b>A dimension that was never measured arrives here as null and leaves as null.</b> Both survey
/// formats write a negative number where the surveyor did not reach a wall, and that was turned into
/// nothing on the way in rather than into a zero, because a zero is itself a measurement — a station
/// hard against the wall. Nothing in this query substitutes, defaults or coalesces one of the four
/// columns, and nothing downstream may either: a passage size averaged over unmeasured walls is
/// wrong in one direction always and looks entirely plausible.
/// </para>
/// <para>
/// <b>The station's height comes from the station, not from the reading.</b> The readings are keyed
/// by station name because a station is identified by its name within a model, so the altitude is
/// joined on the model and the name. A reading whose station is not in the line work keeps its
/// dimensions and has no height: it can still be counted in a size distribution and cannot be placed
/// in a vertical slice.
/// </para>
/// <para>
/// <b>The access walk is spliced in rather than assumed from a caller-side check.</b> Both arms
/// apply — the caller must be able to read the cave and to place it exactly — because how wide a
/// passage is at a station is a measurement taken at a cave coordinate.
/// </para>
/// </summary>
public static class SurveyLrudSql
{
    /// <summary>
    /// Every wall reading of the survey model that answers for a cave, or an empty list when the
    /// cave has no parsed survey the caller may see. Line work that only ever existed as a drawing
    /// carries no wall distances at all and answers empty here, which is not the same statement as a
    /// cave whose passages have no size.
    /// </summary>
    public static async Task<IReadOnlyList<CrossSectionReading>> ForCaveAsync(
        SilexGisDbContext db, AccessContext ctx, Guid caveFeatureId, CancellationToken ct)
    {
        var (sql, parameters) = BuildForCave(ctx, caveFeatureId);
        var connection = db.Database.GetDbConnection();
        var rows = await connection.QueryAsync<CrossSectionReading>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return [.. rows];
    }

    /// <summary>
    /// The statement <see cref="ForCaveAsync"/> runs and the parameters it runs it with, built
    /// without a database. Exposed on the same terms as the leg query beside it: the access walk is
    /// spliced over the whole feature table and that is the shape that quietly becomes a sequential
    /// scan.
    /// </summary>
    public static (string Sql, DynamicParameters Parameters) BuildForCave(
        AccessContext ctx, Guid caveFeatureId)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("lrud_cave_id", caveFeatureId);

        // The enum values are interpolated as the numbers they are so the planner sees constants;
        // they are compile-time constants, never anything a caller supplies.
        var sql = $"""
            WITH chosen AS (
                {SurveySegmentSql.ChosenModel("@lrud_cave_id")}
            )
            SELECT
                l.station_name AS "StationName",
                l.left_m AS "LeftM",
                l.right_m AS "RightM",
                l.up_m AS "UpM",
                l.down_m AS "DownM",
                ST_Z(st.position) AS "ElevationM"
            FROM survey_lrud l
            JOIN chosen ON chosen.model_id = l.survey_model_id
            JOIN survey_models m ON m.id = l.survey_model_id
            JOIN features f ON f.id = m.cave_feature_id
            LEFT JOIN survey_stations st
                   ON st.survey_model_id = l.survey_model_id
                  AND st.name = l.station_name
            WHERE m.cave_feature_id = @lrud_cave_id
              AND f.kind = {(short)FeatureKind.Cave}
              AND f.deleted_at IS NULL
              AND {visibleSql}
              AND {exactSql}
            ORDER BY l.station_name, l.id
            """;

        return (sql, parameters);
    }
}
