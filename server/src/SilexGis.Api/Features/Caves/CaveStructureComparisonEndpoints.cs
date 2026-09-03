// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Whether a cave's passages follow the structure mapped around them.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of the comparison are gathered by two different rules because they are two
/// different things. The cave is withheld <i>whole</i> from a caller who may read it but not place
/// it exactly, and the refusal is spelled "no such cave" — the same answer its statistics and its
/// heights give, so that a caller cannot learn from the set of routes that answer which of them is
/// being kept from them. The mapped traces are gated one at a time: a trace whose position is
/// closed to this caller is not in the rose, not in the count and not in the divergence, exactly as
/// a doline outline is withheld from the shape table.
/// </para>
/// <para>
/// A trace's bearing is a position once its shape is known, and unlike a point there is no snapped
/// form of it, so there is one rule here and not a second, weaker one.
/// </para>
/// </remarks>
public static class CaveStructureComparisonEndpoints
{
    private const string CaveNotFoundCode = "cave.not_found";
    private const string AreaNotFoundCode = "feature.not_found";

    public static RouteGroupBuilder MapCaveStructureComparisonEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/caves").WithTags("Caves")
            .MapGet("/{id:guid}/structure-comparison", StructureComparisonAsync)
            .WithValidation<CaveStructureComparisonRequest>()
            .WithSummary(
                "The cave's passage rose against the length-weighted rose of the fracture traces "
                + "mapped within reach of it, with how far apart the two are.");

        api.MapGroup("/features").WithTags("Features")
            .MapGet("/{id:guid}/structure-comparison", AreaStructureComparisonAsync)
            .WithValidation<AreaStructureComparisonRequest>()
            .WithSummary(
                "The rose of the depression long axes under one area against the length-weighted "
                + "rose of the fracture traces mapped in it, with how far apart the two are.");

        return api;
    }

    private static async Task<Results<Ok<CaveStructureComparisonDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        StructureComparisonAsync(
            [AsParameters] CaveStructureComparisonRequest request,
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

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.Kind == FeatureKind.Cave, ct);

        if (cave is null || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        // A named area must itself be readable, or the question is refused as the cave one is: an
        // area that answers for a caller who may not read it would let the set of traces under it
        // be probed through any cave they can see.
        if (request.AreaId is { } scopeAreaId
            && !await db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .AnyAsync(f => f.Id == scopeAreaId, ct))
        {
            return ApiProblems.NotFound(AreaNotFoundCode);
        }

        var radius = request.RadiusMetres ?? CaveStructureComparisonLimits.DefaultRadiusMetres;

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);

        // A leg the file marks as already surveyed on another trip is the same passage measured
        // twice, and leaving it in would weight its trend twice over because two teams walked it.
        var passageSamples = set.Segments
            .Where(s => !s.IsDuplicate && s.AzimuthDegrees is not null)
            .Select(s => new OrientationSample(s.AzimuthDegrees!.Value, s.SlopeLengthM ?? s.PlanLengthM))
            .ToList();

        var passage = passageSamples.Count > 0
            ? OrientationStatistics.Summarize(passageSamples)
            : null;

        // Resolved by code rather than by a name a person could edit: a kind that has been renamed
        // makes this answer "no structure was mapped near this cave", which is a sentence somebody
        // would believe.
        var typeId = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == FeatureTypeSeeds.FractureLine)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        var structureRows = typeId is null
            ? []
            : request.AreaId is { } areaId
                ? await StructureLineSql.InAreaAsync(
                    db, ctx, areaId, typeId.Value,
                    CaveStructureComparisonLimits.MaxStructureFeatures, ct)
                : await StructureLineSql.NearCaveAsync(
                    db, ctx, request.Id, typeId.Value, radius,
                    CaveStructureComparisonLimits.MaxStructureFeatures, ct);

        // A piece whose two ends coincide has no bearing and no length; a piece of zero length
        // would be refused by the rose arithmetic as a weight that bends every figure in it.
        var structureSamples = structureRows
            .Where(r => r.AzimuthDegrees is not null && r.LengthM > 0d)
            .Select(r => new OrientationSample(r.AzimuthDegrees!.Value, r.LengthM))
            .ToList();

        var structure = structureSamples.Count > 0
            ? OrientationStatistics.Summarize(structureSamples)
            : null;

        // By length on both sides. A cave is many short legs and a fracture map is a few long
        // traces, so counting observations would let the density of the surveyor's stations decide
        // how strongly the cave appears to agree with the rock.
        var divergence = passage is not null && structure is not null
            ? RoseComparison.Compare(
                passage.Bins,
                structure.Bins,
                byLength: true,
                passage.ByLength.MeanAxisDegrees,
                structure.ByLength.MeanAxisDegrees)
            : null;

        return TypedResults.Ok(new CaveStructureComparisonDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            radius,
            structureRows.Select(r => r.FeatureId).Distinct().Count(),
            passage,
            structure,
            divergence));
    }

    private static async Task<Results<Ok<AreaStructureComparisonDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        AreaStructureComparisonAsync(
            [AsParameters] AreaStructureComparisonRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IOptions<SpatialOptions> spatial,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The area itself must be readable or the whole question is refused, so that the set of
        // features under an area cannot be probed through an area the caller was never shown.
        if (!await db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .AnyAsync(f => f.Id == request.Id, ct))
        {
            return ApiProblems.NotFound(AreaNotFoundCode);
        }

        var axisRows = await DolineAxisSql.InAreaAsync(
            db,
            ctx,
            request.Id,
            request.FeatureTypeId,
            CaveStructureComparisonLimits.MaxDolineCandidates,
            CaveStructureComparisonLimits.MaxDolines,
            spatial.Value.WorkingSrid,
            ct);

        // Weighted by how much longer the outline is than it is wide. A round depression's long
        // axis is whatever direction the outline's least noise happened to favour, and a weight
        // that falls to nothing as the shape approaches equidimensional keeps those readings from
        // filling the rose without anybody having to defend a cut-off elongation.
        var dolineSamples = axisRows
            .Where(r => r.AxisAzimuthDegrees is not null
                && r.LongAxisM is not null
                && r.ShortAxisM is not null
                && r.LongAxisM.Value - r.ShortAxisM.Value > 0d)
            .Select(r => new OrientationSample(
                r.AxisAzimuthDegrees!.Value, r.LongAxisM!.Value - r.ShortAxisM!.Value))
            .ToList();

        var dolines = dolineSamples.Count > 0 ? OrientationStatistics.Summarize(dolineSamples) : null;

        var typeId = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == FeatureTypeSeeds.FractureLine)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        var structureRows = typeId is null
            ? []
            : await StructureLineSql.InAreaAsync(
                db, ctx, request.Id, typeId.Value,
                CaveStructureComparisonLimits.MaxStructureFeatures, ct);

        var structureSamples = structureRows
            .Where(r => r.AzimuthDegrees is not null && r.LengthM > 0d)
            .Select(r => new OrientationSample(r.AzimuthDegrees!.Value, r.LengthM))
            .ToList();

        var structure = structureSamples.Count > 0
            ? OrientationStatistics.Summarize(structureSamples)
            : null;

        // By weight on both sides, for the reason the cave comparison is: a fracture map is a few
        // long traces and a doline field is many small outlines, and counting observations would
        // let how finely each was mapped decide how strongly the two appear to agree.
        var divergence = dolines is not null && structure is not null
            ? RoseComparison.Compare(
                dolines.Bins,
                structure.Bins,
                byLength: true,
                dolines.ByLength.MeanAxisDegrees,
                structure.ByLength.MeanAxisDegrees)
            : null;

        return TypedResults.Ok(new AreaStructureComparisonDto(
            request.Id,
            dolineSamples.Count,
            structureRows.Select(r => r.FeatureId).Distinct().Count(),
            dolines,
            structure,
            divergence));
    }
}
