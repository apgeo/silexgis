// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SilexGis.Domain.Statistics;
using SilexGis.Infrastructure.Statistics;

namespace SilexGis.Api.Features.Statistics;

/// <summary>
/// The four ways a registry question may be narrowed, spelled once so the conversion into the
/// query's own scope happens in one place for every route.
/// </summary>
/// <remarks>
/// Each request record repeats the four properties because a bound parameter record cannot nest
/// another one. What must not be repeated is the meaning: naming an area makes the answer a
/// statement about where caves are, and that single fact decides which access rule the statement
/// carries. It is derived from the scope object, so it is derived once.
/// </remarks>
public interface IRegistryScopeRequest
{
    /// <summary>A karst area to answer within, by the declared containment chain.</summary>
    Guid? AreaId { get; }

    /// <summary>One kind of cave.</summary>
    long? CaveTypeId { get; }

    /// <summary>One rock.</summary>
    long? RockTypeId { get; }

    /// <summary>One named region, matched exactly.</summary>
    string? Region { get; }
}

/// <summary>Turns a bound request into the scope the query layer understands.</summary>
public static class RegistryScopeRequestExtensions
{
    public static RegistryStatisticsScope ToScope(this IRegistryScopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RegistryStatisticsScope(
            request.AreaId,
            request.CaveTypeId,
            request.RockTypeId,
            string.IsNullOrWhiteSpace(request.Region) ? null : request.Region);
    }
}

/// <param name="Measure">
/// Which measured column the histogram is drawn over, by the name the answer uses for it.
/// </param>
/// <param name="Bins">
/// Sectors asked for, before the publication rule joins any that hold too few caves. Defaults to a
/// number that reads well on a screen rather than to the finest permitted: a caller who wants the
/// finest has to say so.
/// </param>
/// <param name="MinimumBinCaveCount">
/// The floor a published sector must clear, which a caller may raise and may not lower. It is bound
/// and validated rather than fixed inside the statement so that the one rule deciding what may be
/// said is stated where every other rule about a bad request is stated — and so that a caller
/// asking for something finer than the registry will publish is told so, in the same language as
/// any other refusal, instead of being quietly served a different answer than the one they asked
/// for.
/// </param>
/// <param name="Percentiles">
/// Quantiles to publish, as fractions between nought and one, comma-separated and ascending.
/// Defaults to what a box plot is drawn from.
/// </param>
/// <remarks>
/// Every property names its query string explicitly, because a parameter record bound without one
/// publishes the C# spelling into the contract and the generated client would then send
/// <c>Measure</c> and <c>Bins</c> where every other route in this API is asked in lower camel case.
/// </remarks>
public sealed record RegistryDistributionRequest(
    [property: FromQuery(Name = "measure")] string? Measure,
    [property: FromQuery(Name = "bins")] int? Bins,
    [property: FromQuery(Name = "minimumBinCaveCount")] int? MinimumBinCaveCount,
    [property: FromQuery(Name = "percentiles")] string? Percentiles,
    [property: FromQuery(Name = "areaId")] Guid? AreaId,
    [property: FromQuery(Name = "caveTypeId")] long? CaveTypeId,
    [property: FromQuery(Name = "rockTypeId")] long? RockTypeId,
    [property: FromQuery(Name = "region")] string? Region) : IRegistryScopeRequest;

/// <param name="X">The measure read along the horizontal axis, by name.</param>
/// <param name="Y">The measure read along the vertical one, by name.</param>
/// <param name="Logarithmic">
/// Whether to fit over the logarithms of both, which is the form a length against a depth actually
/// takes. Defaults to true for that reason.
/// </param>
public sealed record RegistryCorrelationRequest(
    [property: FromQuery(Name = "x")] string? X,
    [property: FromQuery(Name = "y")] string? Y,
    [property: FromQuery(Name = "logarithmic")] bool? Logarithmic,
    [property: FromQuery(Name = "areaId")] Guid? AreaId,
    [property: FromQuery(Name = "caveTypeId")] long? CaveTypeId,
    [property: FromQuery(Name = "rockTypeId")] long? RockTypeId,
    [property: FromQuery(Name = "region")] string? Region) : IRegistryScopeRequest;

/// <summary>The scope alone, for the breakdown that takes no measure.</summary>
public sealed record RegistryRegionsRequest(
    [property: FromQuery(Name = "areaId")] Guid? AreaId,
    [property: FromQuery(Name = "caveTypeId")] long? CaveTypeId,
    [property: FromQuery(Name = "rockTypeId")] long? RockTypeId,
    [property: FromQuery(Name = "region")] string? Region) : IRegistryScopeRequest;

/// <summary>
/// Reading a measure's name off a request.
/// </summary>
/// <remarks>
/// <para>
/// The name arrives as text and is matched without regard to case, because the answer names its
/// own measures in lower camel case and a caller who copies a name out of one answer into the next
/// request must be understood. Bound as the enumeration itself, the framework would match only the
/// exact declared spelling and would fail the request before any rule of this API had a chance to
/// refuse it in the API's own words.
/// </para>
/// <para>
/// A number is not a name. The wire vocabulary is the set of names, so a caller writing the
/// enumeration's underlying value is refused rather than quietly served: the numbers are an
/// implementation detail and accepting them would make them a contract nobody meant to publish.
/// </para>
/// <para>
/// Which is why the text is matched against the declared names themselves rather than handed to
/// the framework's own text-to-enumeration parse. That parse accepts three spellings this
/// vocabulary does not have, and each of them publishes something nobody meant to: an underlying
/// number (<c>2</c>), the same number behind a sign (<c>+2</c>, which a first-character digit test
/// does not see), and a comma-separated list, which combines the values it names by bit and
/// answers with whichever measure the combination happens to land on. Matching names closes all
/// three in one step, and it closes them by construction rather than by a guard that has to
/// anticipate the next spelling.
/// </para>
/// </remarks>
public static class RegistryMeasures
{
    public static bool TryParse(string? text, out RegistryMeasure measure)
    {
        measure = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var written = text.Trim();
        foreach (var declared in Enum.GetValues<RegistryMeasure>())
        {
            if (string.Equals(declared.ToString(), written, StringComparison.OrdinalIgnoreCase))
            {
                measure = declared;
                return true;
            }
        }

        return false;
    }

    /// <summary>Every accepted name, for the message that refuses a request naming none of them.</summary>
    public static string Accepted { get; } = string.Join(", ", Enum.GetNames<RegistryMeasure>());
}

/// <summary>
/// Reading the quantile fractions off a request, in one place so the rule that refuses a bad list
/// and the code that answers a good one cannot come to disagree about what the list said.
/// </summary>
public static class RegistryPercentiles
{
    /// <summary>
    /// Parses a comma-separated list of fractions. A missing or blank list is the default set and
    /// is valid; anything present must be a strictly ascending run of fractions strictly inside
    /// nought and one, no longer than the registry will publish.
    /// </summary>
    /// <remarks>
    /// Nought and one are excluded rather than clamped. They are the smallest and largest recorded
    /// values, which are the two order statistics that name an individual cave outright — and both
    /// are already published as the range the histogram is drawn over, where they read as bounds
    /// rather than as somebody's cave.
    /// </remarks>
    public static bool TryParse(string? text, out IReadOnlyList<double> fractions)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            fractions = RegistryStatisticsLimits.DefaultPercentiles;
            return true;
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<double>(parts.Length);
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value)
                || value <= 0
                || value >= 1)
            {
                fractions = [];
                return false;
            }

            // Ascending and distinct, because a published median that sits before its own published
            // first quartile is read as a broken registry rather than as a badly-ordered request.
            if (parsed.Count > 0 && value <= parsed[^1])
            {
                fractions = [];
                return false;
            }

            parsed.Add(value);
        }

        if (parsed.Count == 0 || parsed.Count > RegistryStatisticsLimits.MaximumPercentileCount)
        {
            fractions = [];
            return false;
        }

        fractions = parsed;
        return true;
    }
}

public sealed class RegistryDistributionRequestValidator : AbstractValidator<RegistryDistributionRequest>
{
    public RegistryDistributionRequestValidator()
    {
        RuleFor(x => x.Measure)
            .Must(text => RegistryMeasures.TryParse(text, out _))
            .WithMessage($"measure must name one of: {RegistryMeasures.Accepted}.");

        RuleFor(x => x.Bins)
            .GreaterThanOrEqualTo(RegistryStatisticsLimits.MinimumBinCount)
            .LessThanOrEqualTo(RegistryStatisticsLimits.MaximumBinCount)
            .When(x => x.Bins is not null)
            .WithMessage(
                $"bins must be between {RegistryStatisticsLimits.MinimumBinCount} and "
                + $"{RegistryStatisticsLimits.MaximumBinCount}. A histogram finer than that over a "
                + "registry of a few hundred caves is a way of asking for the values one at a time.");

        // Raised, never lowered. The floor is what keeps a sector from describing one cave closely
        // enough to name it, so a request naming a smaller one is refused rather than rounded up:
        // an answer quietly computed against a rule other than the one asked for is a figure whose
        // label is wrong.
        RuleFor(x => x.MinimumBinCaveCount)
            .GreaterThanOrEqualTo(RegistryStatisticsLimits.MinimumBinCaveCount)
            .LessThanOrEqualTo(RegistryStatisticsLimits.MaximumBinCaveCount)
            .When(x => x.MinimumBinCaveCount is not null)
            .WithMessage(
                "minimumBinCaveCount may be raised above "
                + $"{RegistryStatisticsLimits.MinimumBinCaveCount} and not lowered below it: a "
                + "sector holding fewer caves than that describes them closely enough to identify "
                + "them.");

        RuleFor(x => x.Percentiles)
            .Must(text => RegistryPercentiles.TryParse(text, out _))
            .WithMessage(
                "percentiles must be an ascending, comma-separated list of at most "
                + $"{RegistryStatisticsLimits.MaximumPercentileCount} fractions strictly between 0 "
                + "and 1.");
    }
}

public sealed class RegistryCorrelationRequestValidator : AbstractValidator<RegistryCorrelationRequest>
{
    public RegistryCorrelationRequestValidator()
    {
        RuleFor(x => x.X)
            .Must(text => RegistryMeasures.TryParse(text, out _))
            .WithMessage($"x must name one of: {RegistryMeasures.Accepted}.");
        RuleFor(x => x.Y)
            .Must(text => RegistryMeasures.TryParse(text, out _))
            .WithMessage($"y must name one of: {RegistryMeasures.Accepted}.");

        // A measure against itself fits a line of slope one through every cave and calls it a
        // correlation of one. It is not wrong arithmetic, it is a question with no content, and
        // publishing a perfect fit for it invites the number to be quoted. Compared after both
        // names are read, so two spellings of one measure are still one measure.
        RuleFor(request => request)
            .Must(request =>
                !RegistryMeasures.TryParse(request.X, out var x)
                || !RegistryMeasures.TryParse(request.Y, out var y)
                || x != y)
            .WithMessage("x and y must be different measures.");
    }
}

public sealed class RegistryRegionsRequestValidator : AbstractValidator<RegistryRegionsRequest>
{
    public RegistryRegionsRequestValidator()
    {
        // Nothing to bound: the breakdown takes no number, and its scope fields are either an
        // identifier that exists or one that matches nothing. The validator exists so the route
        // has the same shape as its siblings and so a bound added later has somewhere to go.
        RuleFor(x => x.Region).MaximumLength(200).When(x => x.Region is not null);
    }
}

/// <summary>
/// How two measures move together over the caves in scope, with what it was computed over.
/// </summary>
/// <param name="Basis">
/// In words, because two people with different access get different figures for the same registry
/// and both are right. A figure with no basis beside it invites the reader to take it for the whole
/// truth and somebody else's copy for a contradiction.
/// </param>
public sealed record RegistryCorrelationDto(
    RegistryMeasure X,
    RegistryMeasure Y,
    int Count,
    double? Slope,
    double? Intercept,
    double? RSquared,
    double? Correlation,
    bool Logarithmic,
    string Basis);

/// <summary>How many caves stand under each region in scope.</summary>
/// <param name="CaveCount">
/// The total the rows are taken from — which is not their sum, because a cave the caller may read
/// and may not place is counted here and stands under no region.
/// </param>
public sealed record RegistryRegionBreakdownDto(
    int CaveCount, IReadOnlyList<RegistryRegionRow> Regions, string Basis);

/// <summary>
/// Reading a list of measure names off a request, in one place so the rule that refuses a bad list
/// and the code that answers a good one cannot disagree about what the list said.
/// </summary>
public static class RegistryMeasureList
{
    /// <summary>
    /// Parses a comma-separated list of measure names. Every entry must name a measure, no measure
    /// may be named twice, and the list must be neither empty nor longer than the registry will
    /// group over.
    /// </summary>
    /// <remarks>
    /// A repeat is refused rather than collapsed. A measure named twice would be counted twice in
    /// every distance, which is a weighting the caller did not ask for and could not see in the
    /// answer; silently deduplicating it would answer a different question from the one asked.
    /// </remarks>
    public static bool TryParse(string? text, out IReadOnlyList<RegistryMeasure> measures)
    {
        measures = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<RegistryMeasure>(parts.Length);
        foreach (var part in parts)
        {
            if (!RegistryMeasures.TryParse(part, out var measure) || parsed.Contains(measure))
            {
                return false;
            }

            parsed.Add(measure);
        }

        if (parsed.Count == 0
            || parsed.Count > RegistryStatisticsLimits.MaximumClusteringMeasureCount)
        {
            return false;
        }

        measures = parsed;
        return true;
    }
}

/// <param name="Measures">
/// The measures the grouping is taken over, by name, comma-separated. It is the caller's choice
/// because it decides the population as much as the answer: a measure only a tenth of the registry
/// records turns the grouping into a statement about that tenth, and the only way a reader can
/// weigh that trade is to be able to make it and see what each choice leaves.
/// </param>
/// <param name="Clusters">
/// How many groups to produce. Bounded at both ends: one group is the population, and past a
/// handful the reader is handed a list of caves rather than a description of a set.
/// </param>
public sealed record RegistryClusteringRequest(
    [property: FromQuery(Name = "measures")] string? Measures,
    [property: FromQuery(Name = "clusters")] int? Clusters,
    [property: FromQuery(Name = "areaId")] Guid? AreaId,
    [property: FromQuery(Name = "caveTypeId")] long? CaveTypeId,
    [property: FromQuery(Name = "rockTypeId")] long? RockTypeId,
    [property: FromQuery(Name = "region")] string? Region) : IRegistryScopeRequest;

public sealed class RegistryClusteringRequestValidator : AbstractValidator<RegistryClusteringRequest>
{
    public RegistryClusteringRequestValidator()
    {
        RuleFor(x => x.Measures)
            .Must(text => RegistryMeasureList.TryParse(text, out _))
            .WithMessage(
                "measures must be a comma-separated list of at most "
                + $"{RegistryStatisticsLimits.MaximumClusteringMeasureCount} distinct names from: "
                + $"{RegistryMeasures.Accepted}.");

        RuleFor(x => x.Clusters)
            .GreaterThanOrEqualTo(MetricClustering.MinimumClusterCount)
            .LessThanOrEqualTo(MetricClustering.MaximumClusterCount)
            .When(x => x.Clusters is not null)
            .WithMessage(
                $"clusters must be between {MetricClustering.MinimumClusterCount} and "
                + $"{MetricClustering.MaximumClusterCount}. One group is the population, and past a "
                + "handful the grouping describes individual caves rather than a set.");
    }
}

/// <summary>
/// How much of the population recorded one measure, and what asking for it cost.
/// </summary>
/// <param name="SoleReason">
/// Caves excluded from the grouping that would have been in it had this one measure not been
/// asked for. It is the answer to "what does this column cost me", and it is the figure a reader
/// has to see before the groups mean anything.
/// </param>
public sealed record RegistryClusterCoverageDto(
    RegistryMeasure Measure, int Recorded, int Missing, int SoleReason);

/// <summary>
/// Who was grouped and who was not. Published above the groups rather than beside them, because a
/// grouping over the best-surveyed tenth of a registry is internally consistent and reads exactly
/// like a grouping over the registry.
/// </summary>
/// <param name="Considered">Caves in scope the caller may read.</param>
/// <param name="Eligible">Those that recorded every named measure, and were therefore grouped.</param>
/// <param name="Excluded">
/// The rest. Published rather than left to be subtracted, because a figure a reader has to compute
/// is a figure a reader skips.
/// </param>
public sealed record RegistryClusterPopulationDto(
    int Considered,
    int Eligible,
    int Excluded,
    IReadOnlyList<RegistryClusterCoverageDto> Measures);

/// <summary>
/// What one measure was centred and divided by before any distance was taken.
/// </summary>
/// <remarks>
/// Published because the standardisation is not a detail: a length in metres and a ratio between
/// nought and one are not comparable distances, and without it the measure with the widest raw
/// spread would decide every group by itself. A reader who can see what each column was divided by
/// can see that it happened.
/// </remarks>
/// <param name="StandardDeviation">
/// Zero when every grouped cave recorded the same value, in which case that measure contributed
/// nothing to any distance. Reported rather than dropped, so a caller can see that a measure they
/// chose did no work.
/// </param>
public sealed record RegistryClusterScalingDto(
    RegistryMeasure Measure, double Mean, double StandardDeviation);

/// <summary>One group.</summary>
/// <param name="Index">
/// Its number. <b>A label, not a rank and not a score.</b> Group 2 is not larger, better or more
/// interesting than group 1; the numbering falls out of the order the starting positions were
/// chosen in and carries no meaning a reader may lean on.
/// </param>
/// <param name="Count">Caves in it.</param>
/// <param name="Centre">
/// Its middle, in the units the measures arrived in. <b>Null for a group holding fewer caves than
/// <c>minimumPublishableClusterSize</c></b>: a mean over two caves is those two caves' readings
/// with one arithmetic step in front of them, and anybody who knows one of them reads the other
/// off it. The count is still published — a thin group is itself a finding — but its measurements
/// are not.
/// </param>
/// <param name="ScaledCentre">
/// The same middle in the standardised space the distances were actually taken in, withheld under
/// the same rule. It is published beside the readable one because every spread figure here is
/// measured in that space, and a reader comparing the two would otherwise be comparing different
/// quantities.
/// </param>
/// <param name="MeanDistanceToCentre">
/// The group's own width, standardised, and withheld under the same rule. <b>Read it against
/// <c>separation.meanBetweenDistance</c></b>: a group as wide as the distance between groups is
/// not a group.
/// </param>
public sealed record RegistryClusterDto(
    int Index,
    int Count,
    IReadOnlyList<double>? Centre,
    IReadOnlyList<double>? ScaledCentre,
    double? MeanDistanceToCentre);

/// <summary>Which group one cave fell in.</summary>
/// <param name="CaveId">The cave.</param>
/// <param name="Cluster">The group's <see cref="RegistryClusterDto.Index"/>.</param>
/// <param name="DistanceToCentre">Its distance to that group's middle, standardised.</param>
public sealed record RegistryClusterAssignmentDto(
    Guid CaveId, int Cluster, double DistanceToCentre);

/// <summary>
/// How far apart the groups stand compared with how wide they are — the one figure in this answer
/// that can say the grouping found nothing.
/// </summary>
/// <param name="Ratio">
/// <paramref name="MeanWithinDistance"/> over <paramref name="MeanBetweenDistance"/>. <b>Small
/// means the groups are real; near or above one means they are not.</b> The algorithm returns
/// exactly the number of groups it was asked for on any data whatever, including noise, so the
/// existence of a grouping is evidence of nothing and this ratio is the only thing here that can
/// contradict it. Null when the middles do not separate at all, which is the strongest possible
/// reading of "there is no structure here".
/// </param>
public sealed record RegistryClusterSeparationDto(
    double MeanWithinDistance, double MeanBetweenDistance, double? Ratio);

/// <summary>
/// Which caves in scope resemble each other, over the measures the caller named — together with
/// everything a reader needs in order to know what it is a grouping of.
/// </summary>
/// <param name="Clusters">
/// The groups. <b>Empty when too few caves recorded every named measure to group at all</b>, with
/// the population account still filled in: the answer to "nothing could be grouped" is the account
/// of why, not an error.
/// </param>
/// <param name="MinimumEligibleCount">
/// The fewest grouped caves a grouping is attempted over, served so a caller reading an empty
/// answer can see how far short the population fell.
/// </param>
/// <param name="MinimumPublishableClusterSize">
/// The fewest caves a group may hold before its measurements are published. Served rather than
/// assumed, so a reader seeing a group with a count and no centre knows which rule withheld it.
/// </param>
/// <param name="Converged">
/// False when the refinement was still moving caves when it hit its cap, which makes the grouping
/// arbitrary in its details rather than wrong.
/// </param>
/// <param name="Basis">
/// What it was computed over, in words, because two people with different access get different
/// groupings of the same registry and both are right.
/// </param>
public sealed record RegistryClusteringDto(
    IReadOnlyList<RegistryMeasure> Measures,
    int RequestedClusterCount,
    RegistryClusterPopulationDto Population,
    IReadOnlyList<RegistryClusterScalingDto> Scaling,
    IReadOnlyList<RegistryClusterDto> Clusters,
    IReadOnlyList<RegistryClusterAssignmentDto> Assignments,
    RegistryClusterSeparationDto? Separation,
    int MinimumEligibleCount,
    int MinimumPublishableClusterSize,
    int Iterations,
    bool Converged,
    string Basis);
