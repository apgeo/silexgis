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
