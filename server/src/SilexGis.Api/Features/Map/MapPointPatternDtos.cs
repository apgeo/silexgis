// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// Whether the entrances sit closer together than chance would put them, as a nearest-neighbour
/// index with its test.
/// </summary>
/// <param name="MeanNearestNeighbourM">Mean distance from an entrance to its nearest neighbour.</param>
/// <param name="ExpectedMeanM">What that mean would be for the same n scattered at random over the
/// same ground.</param>
/// <param name="Index">
/// The ratio of the two. One is chance; below one is clustered; above one is more evenly spaced
/// than chance.
/// </param>
/// <param name="ZScore">Standard errors from chance. Negative is clustered.</param>
/// <param name="PValue">Two-sided probability of a departure this large under chance alone.</param>
public sealed record ClarkEvansDto(
    double MeanNearestNeighbourM,
    double ExpectedMeanM,
    double Index,
    double ZScore,
    double PValue);

/// <summary>
/// One distance on a Ripley curve. Under complete spatial randomness the curve follows
/// <c>L = radius</c>, so the diagonal is the reference and the band is what random scatters of the
/// same points over the same window actually produced.
/// </summary>
/// <param name="LowerL">
/// The bottom of the simulated band, or null when no simulation ran — because none was asked for,
/// or because the window was too thin to scatter points into. Null rather than the observed value:
/// a zero-width band drawn on the observed curve reads as "exactly what chance would give", which
/// is the opposite of "chance was never tested".
/// </param>
/// <param name="UpperL">The top of it, or null for the same reason.</param>
public sealed record RipleyStepDto(double RadiusM, double ObservedL, double? LowerL, double? UpperL);

/// <summary>
/// Ripley's L across distances, with the envelope its simulations drew.
/// </summary>
/// <param name="Simulations">
/// How many random patterns actually ran, which is what the band is the extremes of. It is not
/// necessarily the number requested — zero is a legitimate request, and a window too thin to
/// scatter points into stops the loop — and a reader needs the number that happened, since the
/// significance the band is read at is derived from it.
/// </param>
/// <param name="Seed">
/// The seed the simulations were drawn from — stated, and settable, so the same data gives the same
/// band every time it is asked for. An envelope from an unstated seed changes on every reload, and
/// two readers comparing notes cannot tell a real difference from a different draw.
/// </param>
public sealed record RipleyDto(
    int Simulations,
    int Seed,
    double MaxRadiusM,
    IReadOnlyList<RipleyStepDto> Steps);

/// <summary>
/// How the cave entrances in a window are arranged: clustered, random, or spaced out.
/// </summary>
/// <param name="FeatureCount">
/// How many entrances contributed — which is how many the caller may both read and place exactly.
/// It is deliberately not a count of the entrances in the window: an entrance whose position is
/// closed to this caller is in neither statistic, because a spacing measured from a coordinate
/// rounded onto the protection grid would measure the rounding rather than the caves.
/// </param>
/// <param name="StudyAreaKm2">
/// The ground both statistics were computed over: the window, or the part of the named outline
/// inside it. Neither number means anything without it — both compare the observed spacing against
/// what the same n over this much ground would give.
/// </param>
/// <param name="ClarkEvans">Null when there were too few entrances for a nearest neighbour to
/// mean anything.</param>
/// <param name="Ripley">Null for the same reason, or when no radius range could be formed.</param>
/// <param name="Alignment">
/// Which way the lines joining pairs of entrances run — the directional half of the reading, which
/// neither spacing statistic can see. Null when no pair fell inside the separation range.
/// </param>
public sealed record PointPatternDto(
    int FeatureCount,
    int MinimumFeatureCount,
    double StudyAreaKm2,
    Guid? StudyAreaId,
    ClarkEvansDto? ClarkEvans,
    RipleyDto? Ripley,
    PairAlignmentDto? Alignment);

/// <summary>
/// The bearings of the lines joining pairs of entrances, binned into the same sectors a cave's
/// passage trends are, so the same rose draws both.
/// </summary>
/// <param name="PairCount">
/// How many joining lines the rose is built from. It is not <c>n(n-1)/2</c>: pairs outside the
/// separation range are left out, and a reader needs to know how much of the window survived.
/// </param>
/// <param name="MinSeparationM">
/// The shortest joining line that counted. Two entrances a few metres apart share a doline rather
/// than a direction, and their bearing is noise.
/// </param>
/// <param name="MaxSeparationM">
/// The longest one. Across a wide window most pairs join caves in unrelated massifs, and kept they
/// swamp the local structure with a background whose shape is the window's.
/// </param>
/// <param name="Rose">
/// The sector histogram and both weightings' trend statistics, in exactly the shape the passage
/// rose uses. Length weighting here means separation weighting: a kilometre-long alignment is more
/// evidence of a lineament than a thirty-metre one.
/// </param>
public sealed record PairAlignmentDto(
    int PairCount,
    double MinSeparationM,
    double MaxSeparationM,
    OrientationSummary Rose);

/// <param name="Bbox">The window, as <c>west,south,east,north</c> in degrees.</param>
/// <param name="AreaId">
/// An outline to measure inside instead of the whole window. The caller must be able to read it and
/// to place it exactly.
/// </param>
/// <param name="MaxRadiusMetres">
/// The largest distance the Ripley curve counts neighbours within. Defaults to a quarter of the
/// window's shorter side, beyond which the estimate is mostly edge correction.
/// </param>
/// <param name="Simulations">How many random patterns the envelope is drawn from.</param>
/// <param name="Seed">The seed for those patterns. Defaults to a fixed one, so the answer is stable.</param>
/// <param name="MaxPairSeparationMetres">
/// The longest line joining two entrances that counts towards the alignment rose. Defaults to the
/// Ripley curve's maximum radius, so the two readings describe the same neighbourhood.
/// </param>
/// <param name="MinPairSeparationMetres">
/// The shortest one. Defaults to the location-protection grid, below which two entrances are not
/// reliably in a known direction from each other in the first place.
/// </param>
/// <remarks>
/// Every property names its query string explicitly, for the reason given on the density request:
/// without it the contract advertises the C# spelling and the generated client sends a capitalised
/// parameter name these three routes alone would use.
/// </remarks>
public sealed record MapPointPatternRequest(
    [property: FromQuery(Name = "bbox")] string? Bbox,
    [property: FromQuery(Name = "areaId")] Guid? AreaId,
    [property: FromQuery(Name = "maxRadiusMetres")] double? MaxRadiusMetres,
    [property: FromQuery(Name = "steps")] int? Steps,
    [property: FromQuery(Name = "simulations")] int? Simulations,
    [property: FromQuery(Name = "seed")] int? Seed,
    [property: FromQuery(Name = "maxPairSeparationMetres")] double? MaxPairSeparationMetres,
    [property: FromQuery(Name = "minPairSeparationMetres")] double? MinPairSeparationMetres);

/// <summary>
/// The bounds on a point-pattern request, in one place so the validator and the handler cannot
/// drift apart.
/// </summary>
public static class MapPointPatternLimits
{
    /// <summary>
    /// The fewest entrances that produce a statistic rather than a coincidence. Below this the
    /// answer carries its count and no numbers, rather than a nearest-neighbour index computed off
    /// three points and read as if it meant something.
    /// </summary>
    public const int MinPoints = 5;

    /// <summary>
    /// The most entrances one request measures. Both statistics compare every pair, and the
    /// envelope repeats that for every simulation, so this is what bounds the work; a larger set is
    /// refused rather than sampled down, because a statistic over an arbitrary subset of the window
    /// is not the statistic that was asked for.
    /// </summary>
    public const int MaxPoints = 1_000;

    public const int DefaultSteps = 12;
    public const int MaxSteps = 24;

    /// <summary>
    /// Ninety-nine random patterns put the observed curve outside the band under chance about two
    /// times in a hundred, which is the significance the band is read at.
    /// </summary>
    public const int DefaultSimulations = 99;
    public const int MaxSimulations = 199;

    /// <summary>The seed used when the caller does not name one.</summary>
    public const int DefaultSeed = 1;

    /// <summary>Beyond this a radius is wider than any karst window worth asking about.</summary>
    public const double MaxRadiusMetres = 100_000d;

    /// <summary>
    /// The total work one request may ask for, as
    /// <c>points squared, times radii, times curves</c>.
    ///
    /// <para>
    /// The three limits above bound each dimension on its own, and that is not the same thing as
    /// bounding the request: the cost is their <i>product</i>, because every simulation walks every
    /// pair at every radius, so taking every maximum at once asks for several times the work any of
    /// them was set to permit. This ceiling is deliberately generous enough for the defaults at the
    /// largest permitted point set and mean enough that the maximum of everything is refused with a
    /// code, rather than served slowly while a core is held.
    /// </para>
    /// </summary>
    public const long MaxWorkUnits = 1_500_000_000L;

    /// <summary>
    /// What a request would cost, in the units <see cref="MaxWorkUnits"/> is stated in. The
    /// observed curve is one more curve than the simulations, hence the plus one.
    /// </summary>
    public static long WorkUnits(int points, int steps, int simulations) =>
        (long)points * points * steps * (simulations + 1);
}

public sealed class MapPointPatternRequestValidator : AbstractValidator<MapPointPatternRequest>
{
    public MapPointPatternRequestValidator()
    {
        RuleFor(x => x.Bbox).NotEmpty();

        RuleFor(x => x.MaxRadiusMetres)
            .GreaterThan(0)
            .LessThanOrEqualTo(MapPointPatternLimits.MaxRadiusMetres)
            .When(x => x.MaxRadiusMetres is not null);

        RuleFor(x => x.Steps)
            .InclusiveBetween(1, MapPointPatternLimits.MaxSteps)
            .When(x => x.Steps is not null);

        // Zero is allowed and means "the curve without a band" — a legitimate request when a reader
        // wants the observed shape quickly and the significance is not the question.
        RuleFor(x => x.Simulations)
            .InclusiveBetween(0, MapPointPatternLimits.MaxSimulations)
            .When(x => x.Simulations is not null);

        RuleFor(x => x.MaxPairSeparationMetres)
            .GreaterThan(0)
            .LessThanOrEqualTo(MapPointPatternLimits.MaxRadiusMetres)
            .When(x => x.MaxPairSeparationMetres is not null);

        RuleFor(x => x.MinPairSeparationMetres)
            .GreaterThanOrEqualTo(0)
            .LessThan(x => x.MaxPairSeparationMetres ?? MapPointPatternLimits.MaxRadiusMetres)
            .When(x => x.MinPairSeparationMetres is not null);
    }
}
