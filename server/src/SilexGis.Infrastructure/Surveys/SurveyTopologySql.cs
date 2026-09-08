// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The measured shape of one cave's passage network, read back from where the file's reading left
/// it, together with what that reading could not account for.
/// </summary>
/// <param name="SurveyModelId">The survey file the figures were measured from.</param>
/// <param name="DroppedShotCount">Legs the reading could not attach to the station network, or
/// null when the file has not been read. Zero and null are different answers: one says nothing was
/// lost, the other says nobody has looked.</param>
/// <param name="MergedStationCount">Stations that shared a position with an earlier one and so
/// became a single node.</param>
public sealed record SurveyTopologyRow(
    Guid SurveyModelId,
    int? DroppedShotCount,
    int? MergedStationCount,
    int NodeCount,
    int EdgeCount,
    int ComponentCount,
    int ReducedNodeCount,
    int ReducedEdgeCount,
    int ReducedComponentCount,
    int CyclomaticNumber,
    int ExtremityCount,
    int JunctionCount,
    double? Alpha,
    double? Beta,
    double? Gamma,
    double? MeanDegree,
    double? DegreeStandardDeviation,
    double? DegreeCoefficientOfVariation,
    double? CorrelationOfVertexDegree,
    int BranchCount,
    int LoopingBranchCount,
    double? MeanBranchLengthM,
    double? BranchLengthCoefficientOfVariation,
    double? MinBranchLengthM,
    double? MaxBranchLengthM,
    double? LengthEntropy,
    double? OrientationEntropy,
    double? MeanTortuosity,
    double? AverageShortestPathLength,
    double? CentralPointDominance,
    double? AverageClusteringCoefficient,
    DateTime ComputedAt);

/// <summary>
/// Reads the stored topology figures for the one survey model that answers for a cave.
/// </summary>
/// <remarks>
/// The access walk is spliced into the query rather than assumed from a caller-side check, so this
/// cannot become the read that answers past a refusal. Both arms apply: a caller who may see the
/// cave but not place it exactly gets nothing, because the shape of a passage network is measured
/// from the same drawing as its position and is served on the same terms.
/// </remarks>
public static class SurveyTopologySql
{
    public static async Task<SurveyTopologyRow?> ForCaveAsync(
        SilexGisDbContext db, AccessContext ctx, Guid caveFeatureId, CancellationToken ct)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, "f");
        parameters.Add("top_cave_id", caveFeatureId);

        var sql = $"""
            WITH chosen AS (
                {SurveySegmentSql.ChosenModel("@top_cave_id")}
            )
            SELECT t.survey_model_id AS "SurveyModelId",
                   m.dropped_shot_count AS "DroppedShotCount",
                   m.merged_station_count AS "MergedStationCount",
                   t.node_count AS "NodeCount",
                   t.edge_count AS "EdgeCount",
                   t.component_count AS "ComponentCount",
                   t.reduced_node_count AS "ReducedNodeCount",
                   t.reduced_edge_count AS "ReducedEdgeCount",
                   t.reduced_component_count AS "ReducedComponentCount",
                   t.cyclomatic_number AS "CyclomaticNumber",
                   t.extremity_count AS "ExtremityCount",
                   t.junction_count AS "JunctionCount",
                   t.alpha AS "Alpha",
                   t.beta AS "Beta",
                   t.gamma AS "Gamma",
                   t.mean_degree AS "MeanDegree",
                   t.degree_standard_deviation AS "DegreeStandardDeviation",
                   t.degree_coefficient_of_variation AS "DegreeCoefficientOfVariation",
                   t.correlation_of_vertex_degree AS "CorrelationOfVertexDegree",
                   t.branch_count AS "BranchCount",
                   t.looping_branch_count AS "LoopingBranchCount",
                   t.mean_branch_length_m AS "MeanBranchLengthM",
                   t.branch_length_coefficient_of_variation AS "BranchLengthCoefficientOfVariation",
                   t.min_branch_length_m AS "MinBranchLengthM",
                   t.max_branch_length_m AS "MaxBranchLengthM",
                   t.length_entropy AS "LengthEntropy",
                   t.orientation_entropy AS "OrientationEntropy",
                   t.mean_tortuosity AS "MeanTortuosity",
                   t.average_shortest_path_length AS "AverageShortestPathLength",
                   t.central_point_dominance AS "CentralPointDominance",
                   t.average_clustering_coefficient AS "AverageClusteringCoefficient",
                   t.computed_at AS "ComputedAt"
            FROM survey_topology t
            JOIN chosen ON chosen.model_id = t.survey_model_id
            JOIN survey_models m ON m.id = t.survey_model_id
            JOIN features f ON f.id = m.cave_feature_id
            WHERE m.cave_feature_id = @top_cave_id
              AND f.deleted_at IS NULL
              AND {visibleSql}
              AND {exactSql}
            LIMIT 1
            """;

        var connection = db.Database.GetDbConnection();
        return await connection.QuerySingleOrDefaultAsync<SurveyTopologyRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }
}
