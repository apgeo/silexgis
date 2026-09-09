// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Statistics;

namespace SilexGis.Infrastructure.Statistics;

/// <summary>
/// Which measured column a registry statistic is taken over.
/// </summary>
/// <remarks>
/// A closed set, and it is closed for two reasons. A column name cannot be a parameter, so the only
/// safe way to put one into a statement is for it never to have come from outside; and not every
/// number on a cave may be summarised. <b>Altitude is deliberately absent.</b> A height above sea
/// level is a coordinate: asked one range at a time it narrows to a value, and a value plus the
/// little else a registry gives away places a cave whose position is closed to the caller. Every
/// measure below is a measurement of the cave itself and locates nothing.
/// </remarks>
public enum RegistryMeasure
{
    /// <summary>Surveyed passage length, in metres.</summary>
    SurveyedLength = 0,

    /// <summary>Length as estimated rather than surveyed, in metres.</summary>
    EstimatedLength = 1,

    /// <summary>Vertical extent, in metres — not a height above sea level.</summary>
    Depth = 2,

    /// <summary>Extent above the entrance, in metres.</summary>
    PositiveDepth = 3,

    /// <summary>Extent below the entrance, in metres.</summary>
    NegativeDepth = 4,

    /// <summary>Straight-line extent of the surveyed cave, in metres.</summary>
    RealExtension = 5,

    /// <summary>Extent of the cave projected onto the horizontal, in metres.</summary>
    ProjectedExtension = 6,

    /// <summary>Enclosed volume, in cubic metres.</summary>
    Volume = 7,

    /// <summary>Plan area, in square metres.</summary>
    Area = 8,

    /// <summary>Ramification index — passage length over extent, a pure number.</summary>
    RamificationIndex = 9,
}

/// <summary>
/// Which caves a registry statistic is taken over.
/// </summary>
/// <param name="AreaId">A karst area, if the question is about one. Membership is the declared
/// containment chain somebody entered, never a spatial test — the same rule the per-area statistics
/// already publish, and for the same two reasons: a spatial test would be a second opinion about
/// what is in the area, and it would make every area's count a probe for where a protected cave
/// sits. <b>Scoping to an area makes the answer spatial</b>, which is what excludes caves the caller
/// may read but may not place; see the query's own remarks.</param>
/// <param name="CaveTypeId">One kind of cave, if the question is about one.</param>
/// <param name="RockTypeId">One rock, if the question is about one.</param>
/// <param name="Region">One named region, matched exactly. The region is not withheld from anybody
/// who may read the cave — the cave page and the listing both show it — so narrowing by it
/// discloses nothing the caller was not already shown.</param>
public sealed record RegistryStatisticsScope(
    Guid? AreaId = null,
    long? CaveTypeId = null,
    long? RockTypeId = null,
    string? Region = null)
{
    /// <summary>
    /// True when the scope names a place, and therefore when the answer is about where caves are
    /// rather than only about how big they are.
    /// </summary>
    public bool IsSpatiallyScoped => AreaId is not null;
}

/// <summary>The extent of a measure over a scope, before any histogram is drawn over it.</summary>
/// <param name="CaveCount">Caves in the scope, whether or not they record the measure.</param>
/// <param name="MeasuredCount">Caves that record it — the denominator every fraction here uses, and
/// the number that tells a small total from a mostly-unrecorded one.</param>
/// <param name="Minimum">Smallest recorded value, null when none is recorded.</param>
/// <param name="Maximum">Largest recorded value.</param>
public sealed record RegistryMeasureBoundsRow(
    int CaveCount, int MeasuredCount, double? Minimum, double? Maximum);

/// <summary>One sector of a histogram as the database counted it, before the publication rule.</summary>
public sealed record RegistryBinRow(int Bucket, int Count);

/// <summary>
/// One cave's readings of the measures a grouping was asked over, straight off the statement.
/// </summary>
/// <param name="Id">The cave.</param>
/// <param name="Values">One entry per named measure, in the order they were named. A null is a
/// measure this cave does not record — carried rather than dropped, because how many caves lack
/// which measure is half of the answer a grouping owes its reader.</param>
public sealed record RegistryMetricVectorRow(Guid Id, double?[] Values);

/// <summary>
/// A grouping of the caves in scope by their measures, with the account of who it left out.
/// </summary>
/// <param name="Measures">The measures the distances were taken over, in the order asked.</param>
/// <param name="Model">The grouping itself.</param>
/// <param name="Basis">What it was computed over, in words.</param>
public sealed record RegistryClustering(
    IReadOnlyList<RegistryMeasure> Measures,
    MetricClusterModel Model,
    string Basis);

/// <summary>
/// Raised when the scope holds more caves than one grouping is answered over.
/// </summary>
/// <remarks>
/// A refusal rather than a truncation on purpose. Grouping the first so many caves and labelling
/// the answer with the whole scope would be the one misreading this surface is built to prevent,
/// and unlike a missing measure it would leave no trace in the answer for a reader to notice.
/// </remarks>
public sealed class RegistryScopeTooLargeException(int limit)
    : Exception($"A grouping is answered over at most {limit} caves.")
{
    /// <summary>The most caves a grouping is answered over.</summary>
    public int Limit { get; } = limit;
}

/// <summary>A measure's quantiles, as fractions of the way through the recorded values.</summary>
/// <param name="Fraction">Between zero and one.</param>
/// <param name="Value">The value at that fraction, interpolated between the two observations it
/// falls between rather than snapped to one of them.</param>
public sealed record RegistryPercentileRow(double Fraction, double? Value);

/// <summary>
/// How two measures move together, over the caves that record both.
/// </summary>
/// <param name="Count">Caves contributing a pair. Everything else here is null below two of them.</param>
/// <param name="Slope">Slope of the least-squares line of the second measure on the first.</param>
/// <param name="Intercept">Where that line meets the axis.</param>
/// <param name="RSquared">The fraction of the variation the line accounts for.</param>
/// <param name="Correlation">The correlation coefficient, which carries the sign the squared figure
/// loses.</param>
/// <param name="Logarithmic">True when the fit was taken over the logarithms of both measures. A
/// length against a depth is a straight line only on logarithmic axes, and the slope of that line —
/// how many times longer a cave gets for each doubling of its depth — is the figure worth reading.</param>
public sealed record RegistryCorrelationRow(
    int Count,
    double? Slope,
    double? Intercept,
    double? RSquared,
    double? Correlation,
    bool Logarithmic);

/// <summary>How many caves stand under each region in the scope.</summary>
/// <param name="Region">Null for caves with no region recorded, which are a row of their own rather
/// than dropped: how much of a registry is unrecorded is part of what a breakdown says.</param>
public sealed record RegistryRegionRow(string? Region, int CaveCount);

/// <summary>
/// A measure summarised over a scope: its extent, its histogram after the publication rule, its
/// quantiles, and the two distribution fits.
/// </summary>
/// <param name="Basis">What the figures were computed over, in words, so a reader who compares two
/// people's copies of the same answer can see why they differ.</param>
public sealed record RegistryDistribution(
    RegistryMeasure Measure,
    int CaveCount,
    int MeasuredCount,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<DistributionBin> Bins,
    IReadOnlyList<RegistryPercentileRow> Percentiles,
    LognormalFit? Lognormal,
    ParetoTailFit? ParetoTail,
    string Basis);
