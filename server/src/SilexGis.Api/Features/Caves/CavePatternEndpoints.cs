// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// What kind of cave a survey's shape suggests, with the rules that said so.
///
/// <para>
/// <b>The reasoning is the answer and the label is a summary of it.</b> This route never returns a
/// pattern alone: it returns every rule, whether it fired, what figures it read and what those
/// figures were, and what each pattern scored. A reader can then disagree with a rule rather than
/// with a verdict, and a label the trace does not support is visible as such rather than being taken
/// on trust.
/// </para>
/// <para>
/// <b>Nothing here is measured a second time.</b> The network counts, the bearings, the profile and
/// the cross-section shape are each read from the one place that defines them, over the one survey
/// model that answers for the cave. Two figures about one cave describe the same passage only if
/// they were measured over the same file.
/// </para>
/// <para>
/// <b>Withheld entirely from a caller who may read the cave but not place it exactly</b>, on the
/// same terms as every other figure derived from the line work, and refused as "no such cave".
/// </para>
/// </summary>
public static class CavePatternEndpoints
{
    public static RouteGroupBuilder MapCavePatternEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/{id:guid}/pattern", PatternAsync)
            .WithValidation<CavePatternRequest>()
            .WithSummary(
                "What kind of cave one survey's shape suggests, with every rule applied to reach "
                + "it. Withheld from a caller who may not place the cave exactly.");

        return api;
    }

    private static async Task<Results<Ok<CavePatternDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        PatternAsync(
            [AsParameters] CavePatternRequest request,
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

        if (!await CaveCrossSectionEndpoints.ReadableCaveAsync(db, access, protection, ctx, request.Id, ct))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var set = await SurveySegmentSource.ForCaveAsync(db, ctx, request.Id, ct);

        // A leg the file marks as passage already surveyed on another trip is the same piece of cave
        // measured a second time. It is excluded from every figure below for the reason it is
        // excluded from a length: it would weight one trend, one loop and one steepness twice over
        // because two teams walked it, which is a fact about the surveying and not about the cave.
        var measurable = set.Segments.Where(s => !s.IsDuplicate).ToList();

        var network = PassageNetwork.Measure(
            measurable.Select(s => (s.FromStationName, s.ToStationName)));

        var bearings = OrientationStatistics.Summarize(measurable
            .Where(s => s.AzimuthDegrees is not null)
            .Select(s => new OrientationSample(
                s.AzimuthDegrees!.Value, s.SlopeLengthM ?? s.PlanLengthM)));

        // Refused, not reported as level, for line work with no altitudes: the reduction that
        // produces a drawn centreline's segments substitutes zero for a missing altitude so the
        // result can be stored, and summarising that would describe a plan drawing as a cave that is
        // flat everywhere with nothing in the answer to say otherwise.
        var dip = set.HasZ
            ? DipStatistics.Summarize(measurable
                .Where(s => s.DipDegrees is not null)
                .Select(s => new DipSample(
                    s.DipDegrees!.Value, s.SlopeLengthM ?? s.PlanLengthM)))
            : null;

        var indices = CaveSurveyIndices.Compute(SurveySegments.AsPassage(set.Segments));

        var readings = await SurveyLrudSql.ForCaveAsync(db, ctx, request.Id, ct);
        var sections = CrossSectionMorphometry.Stations(readings);
        var ratio = CrossSectionMorphometry.Distribution(
            sections.Where(s => s.WidthHeightRatio is not null).Select(s => s.WidthHeightRatio!.Value));

        var (dropped, merged) = await CompletenessAsync(db, set.SurveyModelId, ct);

        var suggestion = SpeleogeneticPattern.Classify(new PatternEvidence(
            set.Basis,
            ReducedNodeCount: network?.ReducedNodeCount,
            CyclomaticNumber: network?.CyclomaticNumber,
            ExtremityCount: network?.ExtremityCount,
            Clustering: network?.Clustering,
            OrientationEntropy: bearings.ByLength.EntropyNormalized,
            HasAltitudes: set.HasZ,
            Verticality: set.HasZ ? indices.Verticality : null,
            MeanAbsoluteDipDegrees: dip?.MeanAbsoluteDipDegrees,
            MinimumDipDegrees: dip?.MinimumDipDegrees,
            MaximumDipDegrees: dip?.MaximumDipDegrees,
            MedianWidthHeightRatio: ratio?.Median,
            DroppedShotCount: dropped,
            MergedStationCount: merged));

        return TypedResults.Ok(new CavePatternDto(
            request.Id,
            set.Basis,
            set.Basis == SurveySegmentBasis.SkeletonHeuristic,
            set.SurveyModelId,
            set.HasZ,
            suggestion,
            network));
    }

    /// <summary>
    /// What reading the survey file lost, or nulls when no file answered.
    /// </summary>
    /// <remarks>
    /// Null is not zero here and the difference is the point: a file that has not been read says
    /// nothing about what was lost, which is not the same statement as nothing having been lost. A
    /// network measured over less than the file contained still yields figures that look entirely
    /// reasonable, and this is the only thing that says otherwise.
    /// </remarks>
    private static async Task<(int? Dropped, int? Merged)> CompletenessAsync(
        SilexGisDbContext db, Guid? surveyModelId, CancellationToken ct)
    {
        if (surveyModelId is not { } id)
        {
            return (null, null);
        }

        var row = await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.DroppedShotCount, m.MergedStationCount })
            .FirstOrDefaultAsync(ct);

        return (row?.DroppedShotCount, row?.MergedStationCount);
    }
}
