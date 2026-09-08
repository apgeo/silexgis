// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// The shape of a cave's passage network — how many loops it holds, how its passages meet, how
/// evenly they run — as the figures the karst literature uses to tell one kind of cave from
/// another.
///
/// <para>
/// <b>Withheld entirely from a caller who may read the cave but not place it exactly</b>, and the
/// refusal is answered as "no such cave" rather than as "you may not". The figures are shape rather
/// than position, but they are measured from a survey the caller may not be entitled to know
/// exists, and a 403 would confirm both that the cave exists and that it has been surveyed. It is
/// the same gate the survey files themselves are behind, on purpose: "it is only a number" is not a
/// reason to open a door that the drawing it was measured from keeps shut.
/// </para>
/// <para>
/// <b>Read back rather than computed on demand.</b> The contraction and the betweenness behind
/// these figures are superlinear in the station count, so they are measured when the file is read
/// and stored beside it, written and rewritten inside the same transaction as the stations and
/// shots. A cave whose survey has not been read, or whose file held no network, has no figures
/// rather than figures of zero.
/// </para>
/// <para>
/// <b>Every answer carries what the reading dropped and merged.</b> A network that silently lost
/// legs still produces entirely reasonable-looking numbers, so the two counts travel with the
/// figures rather than being available separately for anyone who thinks to ask.
/// </para>
/// </summary>
public static class CaveTopologyEndpoints
{
    public static RouteGroupBuilder MapCaveTopologyEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/topology", TopologyAsync)
            .WithValidation<CaveTopologyRequest>()
            .WithSummary(
                "The shape of one cave's passage network. Withheld from a caller who may not place "
                + "the cave exactly.");

        return api;
    }

    private static async Task<Results<Ok<CaveTopologyDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        TopologyAsync(
            [AsParameters] CaveTopologyRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await ReadableCaveAsync(db, access, protection, ctx, request.Id, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var row = await SurveyTopologySql.ForCaveAsync(db, ctx, request.Id, ct);
        if (row is null)
        {
            // A cave that exists and that the caller may see, but whose passage network has never
            // been measured — no survey file uploaded, or one holding nothing that joins up. That
            // is a different answer from "no such cave" and gets its own code, because a reader
            // that showed "not found" here would say the cave had gone.
            return ApiProblems.NotFound("cave_topology.not_measured");
        }

        return TypedResults.Ok(new CaveTopologyDto(
            request.Id,
            row.SurveyModelId,
            row.DroppedShotCount,
            row.MergedStationCount,
            row.NodeCount,
            row.EdgeCount,
            row.ComponentCount,
            row.ReducedNodeCount,
            row.ReducedEdgeCount,
            row.ReducedComponentCount,
            row.CyclomaticNumber,
            row.ExtremityCount,
            row.JunctionCount,
            row.Alpha,
            row.Beta,
            row.Gamma,
            row.MeanDegree,
            row.DegreeStandardDeviation,
            row.DegreeCoefficientOfVariation,
            row.CorrelationOfVertexDegree,
            row.BranchCount,
            row.LoopingBranchCount,
            row.MeanBranchLengthM,
            row.BranchLengthCoefficientOfVariation,
            row.MinBranchLengthM,
            row.MaxBranchLengthM,
            row.LengthEntropy,
            row.OrientationEntropy,
            row.MeanTortuosity,
            row.AverageShortestPathLength,
            row.CentralPointDominance,
            row.AverageClusteringCoefficient,
            row.ComputedAt));
    }

    /// <summary>
    /// The cave whose network may be measured, or null when it may not be — which covers a cave
    /// that does not exist, one the caller may not read, and one the caller may read but not place
    /// exactly. All three are one answer on purpose: distinguishing them would say which caves are
    /// being kept from whom.
    /// </summary>
    private static async Task<Feature?> ReadableCaveAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid id,
        CancellationToken ct)
    {
        var feature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

        return feature is not null
            && await SurveyModelAccess.VisibleAsync(access, protection, ctx, feature, ct)
                ? feature
                : null;
    }
}
