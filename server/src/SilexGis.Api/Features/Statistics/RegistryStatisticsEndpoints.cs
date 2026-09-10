// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Statistics;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Statistics;

namespace SilexGis.Api.Features.Statistics;

/// <summary>
/// The registry read as a dataset: how a measured column is distributed over the caves the caller
/// may read, how two of them move together, and how the caves fall across regions.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is askable in the sense the listing screens are. A figure may be looked at and it
/// may be narrowed by the four declared scope fields, and that is all: a total that moved under an
/// arbitrary filter would answer questions about individual caves as surely as returning them
/// would, one narrow scope at a time.
/// </para>
/// <para>
/// Each question answers twice — once as figures on a screen, once as a file — and <b>both go
/// through one private answering method</b>. A file is read once and then kept, forwarded and
/// opened long after the rules that produced it changed, so a report assembled by its own path is
/// exactly how a saved copy comes to state what the screen refuses. The refusals are reproduced on
/// the export route letter for letter for the same reason: a file existing where a page says
/// nothing is the page's answer given anyway.
/// </para>
/// <para>
/// There is deliberately <b>no right of its own</b> over these figures. They are already cut to what
/// the caller may read, and a right over them would be a right to a number nobody is otherwise
/// entitled to. What can be refused is a scope: naming an area the caller may not read, or may read
/// and not place, is answered as an area that is not there — the same words either way, because a
/// refusal that distinguished the two would itself say which caves are protected.
/// </para>
/// </remarks>
public static class RegistryStatisticsEndpoints
{
    public static RouteGroupBuilder MapRegistryStatisticsEndpoints(this RouteGroupBuilder api)
    {
        var registry = api.MapGroup("/stats/registry").WithTags("Statistics");

        registry.MapGet("/distribution", DistributionAsync)
            .WithValidation<RegistryDistributionRequest>()
            .WithSummary("How one measured column is distributed over the caves the caller may read.");
        registry.MapGet("/distribution/export", ExportDistributionAsync)
            .WithValidation<RegistryDistributionRequest>()
            .WithSummary("The same distribution, as a spreadsheet.");

        registry.MapGet("/correlation", CorrelationAsync)
            .WithValidation<RegistryCorrelationRequest>()
            .WithSummary("How two measured columns move together over the caves the caller may read.");
        registry.MapGet("/correlation/export", ExportCorrelationAsync)
            .WithValidation<RegistryCorrelationRequest>()
            .WithSummary("The same fit, as a spreadsheet.");

        // No export sibling: a spreadsheet of per-cave group labels is a list of caves, which is
        // the one thing this family does not answer.
        registry.MapGet("/clustering", ClusteringAsync)
            .WithValidation<RegistryClusteringRequest>()
            .WithSummary(
                "Which caves the caller may read resemble each other over the measures they named.");

        registry.MapGet("/regions", RegionsAsync)
            .WithValidation<RegistryRegionsRequest>()
            .WithSummary("How many caves stand under each region, over those the caller may place.");
        registry.MapGet("/regions/export", ExportRegionsAsync)
            .WithValidation<RegistryRegionsRequest>()
            .WithSummary("The same breakdown, as a spreadsheet.");

        return api;
    }

    private static async Task<Results<Ok<RegistryDistribution>, UnauthorizedHttpResult, ProblemHttpResult>>
        DistributionAsync(
            [AsParameters] RegistryDistributionRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var answer = await AnswerDistributionAsync(request, db, accessAccessor, protection, ct);
        return Seen(answer);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportDistributionAsync(
            [AsParameters] RegistryDistributionRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct)
    {
        var answer = await AnswerDistributionAsync(request, db, accessAccessor, protection, ct);
        return Saved(answer, sheets, "distribution", RegistryStatisticsWorkbook.DistributionRows);
    }

    private static async Task<Results<Ok<RegistryCorrelationDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        CorrelationAsync(
            [AsParameters] RegistryCorrelationRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var answer = await AnswerCorrelationAsync(request, db, accessAccessor, protection, ct);
        return Seen(answer);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportCorrelationAsync(
            [AsParameters] RegistryCorrelationRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct)
    {
        var answer = await AnswerCorrelationAsync(request, db, accessAccessor, protection, ct);
        return Saved(answer, sheets, "correlation", RegistryStatisticsWorkbook.CorrelationRows);
    }

    private static async Task<Results<Ok<RegistryClusteringDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ClusteringAsync(
            [AsParameters] RegistryClusteringRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var answer = await AnswerClusteringAsync(request, db, accessAccessor, protection, ct);
        return Seen(answer);
    }

    private static async Task<Results<Ok<RegistryRegionBreakdownDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        RegionsAsync(
            [AsParameters] RegistryRegionsRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var answer = await AnswerRegionsAsync(request, db, accessAccessor, protection, ct);
        return Seen(answer);
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        ExportRegionsAsync(
            [AsParameters] RegistryRegionsRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            ISpreadsheetWriter sheets,
            CancellationToken ct)
    {
        var answer = await AnswerRegionsAsync(request, db, accessAccessor, protection, ct);
        return Saved(answer, sheets, "regions", RegistryStatisticsWorkbook.RegionRows);
    }

    /// <summary>
    /// The permission ladder and the one query behind the histogram, its quantiles and its fits.
    /// Both surfaces come through here so neither can acquire a rule the other lacks.
    /// </summary>
    private static async Task<Answer<RegistryDistribution>> AnswerDistributionAsync(
        RegistryDistributionRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var scope = request.ToScope();
        var (ctx, refusal) = await GuardAsync(scope, db, accessAccessor, protection, ct);
        if (ctx is null)
        {
            return refusal.As<RegistryDistribution>();
        }

        // Both the measure's name and the list of quantiles were already found well-formed by the
        // validator, and both are read here by the same code that judged them. Reading either one
        // a second way would be a second opinion about what the request said, and the two opinions
        // would differ on exactly the inputs nobody thought to test.
        if (!RegistryMeasures.TryParse(request.Measure, out var measure)
            || !RegistryPercentiles.TryParse(request.Percentiles, out var fractions))
        {
            return new Answer<RegistryDistribution>(false, Malformed(), null);
        }

        var distribution = await RegistryStatisticsQuery.DistributionAsync(
            db,
            ctx,
            scope,
            measure,
            request.Bins ?? RegistryStatisticsLimits.DefaultBinCount,
            request.MinimumBinCaveCount ?? RegistryStatisticsLimits.MinimumBinCaveCount,
            fractions,
            ct);

        return new Answer<RegistryDistribution>(false, null, distribution);
    }

    /// <summary>The ladder and the one statement behind the fit, for both surfaces.</summary>
    private static async Task<Answer<RegistryCorrelationDto>> AnswerCorrelationAsync(
        RegistryCorrelationRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var scope = request.ToScope();
        var (ctx, refusal) = await GuardAsync(scope, db, accessAccessor, protection, ct);
        if (ctx is null)
        {
            return refusal.As<RegistryCorrelationDto>();
        }

        // Logarithmic unless the caller says otherwise: a length against a depth is a straight line
        // only on logarithmic axes, and a line fitted through the linear form of it is a number
        // that means nothing and looks like it means something.
        if (!RegistryMeasures.TryParse(request.X, out var x)
            || !RegistryMeasures.TryParse(request.Y, out var y))
        {
            return new Answer<RegistryCorrelationDto>(false, Malformed(), null);
        }

        var logarithmic = request.Logarithmic ?? true;
        var fit = await RegistryStatisticsQuery.CorrelationAsync(
            db, ctx, scope, x, y, logarithmic, ct);

        var basis = scope.IsSpatiallyScoped
            ? RegistryStatisticsQuery.AreaBasis
            : RegistryStatisticsQuery.ReadableBasis;

        return new Answer<RegistryCorrelationDto>(
            false,
            null,
            new RegistryCorrelationDto(
                x,
                y,
                fit.Count,
                fit.Slope,
                fit.Intercept,
                fit.RSquared,
                fit.Correlation,
                fit.Logarithmic,
                basis));
    }

    /// <summary>
    /// The ladder, the one statement behind the grouping, and the rule that decides which of its
    /// figures may be published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A group holding fewer caves than the floor is published as a count and nothing else.</b>
    /// Its middle is a mean over two or three caves — arithmetic thin enough that anybody who knows
    /// one of them reads the others off it — so the measurements are withheld while the count
    /// stands, because removing the group entirely would make the counts stop adding up to the
    /// eligible total and invite a reader to conclude nothing had been left out. The floor itself
    /// is published beside the groups so a reader meeting a group with no centre knows why.
    /// </para>
    /// <para>
    /// The per-cave assignments carry no measurement, only which group a readable cave fell in;
    /// the readings behind it are the cave's own and are already readable to anybody this answer
    /// was computed for. Nothing here says where a cave is.
    /// </para>
    /// </remarks>
    private static async Task<Answer<RegistryClusteringDto>> AnswerClusteringAsync(
        RegistryClusteringRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var scope = request.ToScope();
        var (ctx, refusal) = await GuardAsync(scope, db, accessAccessor, protection, ct);
        if (ctx is null)
        {
            return refusal.As<RegistryClusteringDto>();
        }

        if (!RegistryMeasureList.TryParse(request.Measures, out var measures))
        {
            return new Answer<RegistryClusteringDto>(false, Malformed(), null);
        }

        var clusterCount = request.Clusters ?? RegistryStatisticsLimits.DefaultClusterCount;

        RegistryClustering grouping;
        try
        {
            grouping = await RegistryStatisticsQuery.ClusteringAsync(
                db, ctx, scope, measures, clusterCount, ct);
        }
        catch (RegistryScopeTooLargeException tooLarge)
        {
            // Refused rather than answered over part of the scope: a grouping quietly taken from
            // the first so many caves would wear the whole scope's label and leave no trace of it.
            return new Answer<RegistryClusteringDto>(
                false,
                ApiProblems.BadRequest(
                    "statistics.scope_too_large",
                    $"a grouping is answered over at most {tooLarge.Limit} caves; narrow the scope."),
                null);
        }

        var model = grouping.Model;
        var coverage = new List<RegistryClusterCoverageDto>(measures.Count);
        var scaling = new List<RegistryClusterScalingDto>(measures.Count);
        for (var i = 0; i < measures.Count; i++)
        {
            coverage.Add(new RegistryClusterCoverageDto(
                measures[i],
                model.Population.Columns[i].Recorded,
                model.Population.Columns[i].Missing,
                model.Population.Columns[i].SoleReason));
            scaling.Add(new RegistryClusterScalingDto(
                measures[i], model.Scaling[i].Mean, model.Scaling[i].StandardDeviation));
        }

        var clusters = model.Clusters
            .Select(c => c.Count >= MetricClustering.MinimumPublishableClusterSize
                ? new RegistryClusterDto(
                    c.Index, c.Count, c.Centre, c.ScaledCentre, c.MeanDistanceToCentre)
                : new RegistryClusterDto(c.Index, c.Count, null, null, null))
            .ToList();

        var assignments = model.Assignments
            .Select(a => new RegistryClusterAssignmentDto(a.SubjectId, a.Cluster, a.DistanceToCentre))
            .ToList();

        return new Answer<RegistryClusteringDto>(
            false,
            null,
            new RegistryClusteringDto(
                grouping.Measures,
                model.RequestedClusterCount,
                new RegistryClusterPopulationDto(
                    model.Population.Considered,
                    model.Population.Eligible,
                    model.Population.Excluded,
                    coverage),
                scaling,
                clusters,
                assignments,
                model.Separation is { } s
                    ? new RegistryClusterSeparationDto(
                        s.MeanWithinDistance, s.MeanBetweenDistance, s.Ratio)
                    : null,
                MetricClustering.MinimumEligibleCount,
                MetricClustering.MinimumPublishableClusterSize,
                model.Iterations,
                model.Converged,
                grouping.Basis));
    }

    /// <summary>The ladder and the two statements behind the breakdown, for both surfaces.</summary>
    private static async Task<Answer<RegistryRegionBreakdownDto>> AnswerRegionsAsync(
        RegistryRegionsRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var scope = request.ToScope();
        var (ctx, refusal) = await GuardAsync(scope, db, accessAccessor, protection, ct);
        if (ctx is null)
        {
            return refusal.As<RegistryRegionBreakdownDto>();
        }

        // The total and the rows are asked under different rules on purpose. The total counts every
        // cave in scope the caller may read; the rows count only those they may also place. Their
        // difference is how many caves in scope stand under no named region here, which is a figure
        // about the answer and not about any cave.
        var caveCount = await RegistryStatisticsQuery.CaveCountAsync(db, ctx, scope, ct);
        var regions = await RegistryStatisticsQuery.RegionsAsync(db, ctx, scope, ct);

        // Which of those two sentences is true depends on the scope, so the basis is derived from
        // the same predicate the total was: a request naming an area takes the unplaceable cave out
        // of the total as well, and saying otherwise would invite a reader to check the rows against
        // the total, find them reconciled, and read that as proof nothing was left out.
        var basis = scope.IsSpatiallyScoped
            ? RegistryStatisticsQuery.AreaBasis
            : RegistryStatisticsQuery.PlaceableBasis;

        return new Answer<RegistryRegionBreakdownDto>(
            false, null, new RegistryRegionBreakdownDto(caveCount, regions, basis));
    }

    /// <summary>
    /// Who is asking, and whether the scope they named is one they may be answered about.
    /// </summary>
    /// <remarks>
    /// An area the caller cannot read and an area they can read but cannot place are refused with
    /// the same words, and those are the words for an area that does not exist. Distinguishing them
    /// would turn the refusal into the probe: asked over a list of candidate areas it would report
    /// which ones are protected and which are merely unknown.
    /// </remarks>
    private static async Task<(AccessContext? Context, Answer<object> Refusal)> GuardAsync(
        RegistryStatisticsScope scope,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return (null, new Answer<object>(true, null, null));
        }

        if (scope.AreaId is { } areaId)
        {
            var readable = await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .AnyAsync(f => f.Id == areaId, ct);
            if (!readable)
            {
                return (null, new Answer<object>(false, ApiProblems.NotFound("feature.not_found"), null));
            }

            var placeable = await protection.ExactViewIdsAsync(ctx, [areaId], ct);
            if (!placeable.Contains(areaId))
            {
                return (null, new Answer<object>(false, ApiProblems.NotFound("feature.not_found"), null));
            }
        }

        return (ctx, default);
    }

    /// <summary>
    /// The refusal for a request that got past the validator and still could not be read.
    /// </summary>
    /// <remarks>
    /// It should be unreachable, and it carries the validator's own code rather than a code of its
    /// own so that a rule falling out of step between the two is a refusal a caller can act on and
    /// not a failure they have to guess at.
    /// </remarks>
    private static ProblemHttpResult Malformed() =>
        ApiProblems.BadRequest("validation.failed", "the request could not be read.");

    private static Results<Ok<T>, UnauthorizedHttpResult, ProblemHttpResult> Seen<T>(Answer<T> answer)
        where T : class =>
        answer.Unauthorized ? TypedResults.Unauthorized()
            : answer.Problem is { } problem ? problem
            : TypedResults.Ok(answer.Payload!);

    private static Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult> Saved<T>(
        Answer<T> answer,
        ISpreadsheetWriter sheets,
        string slug,
        Func<T, IReadOnlyList<IReadOnlyList<SheetCell>>> rows)
        where T : class
    {
        // The same refusal the screen gets, letter for letter, and reached before a single cell is
        // written: a file that existed where a page said nothing would be the answer the page
        // declined to give.
        if (answer.Unauthorized)
        {
            return TypedResults.Unauthorized();
        }

        if (answer.Problem is { } problem)
        {
            return problem;
        }

        var bytes = sheets.Write(RegistryStatisticsWorkbook.SheetName, rows(answer.Payload!));

        // Named by what it is about and when it was taken. Nothing in the name comes from the
        // registry: a file name is not somewhere to decide a second time what a caller may be
        // shown of a region's or an area's name.
        var fileName =
            $"registry-statistics-{slug}-{DateTime.UtcNow:yyyyMMdd}." + sheets.Extension;
        return TypedResults.File(bytes, sheets.ContentType, fileName);
    }

    /// <summary>
    /// Either a refusal or the figures — never both, and never neither.
    /// </summary>
    private readonly record struct Answer<T>(bool Unauthorized, ProblemHttpResult? Problem, T? Payload)
        where T : class
    {
        /// <summary>The same refusal, carried into the shape the caller's route returns.</summary>
        internal Answer<TOther> As<TOther>()
            where TOther : class => new(Unauthorized, Problem, null);
    }
}
