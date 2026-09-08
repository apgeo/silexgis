// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Statistics;

/// <summary>
/// The bounds a registry statistic is answered within, in one place so the rule that refuses a
/// request and the arithmetic that answers it cannot come to disagree about what is allowed.
/// </summary>
/// <remarks>
/// The floor below is the one that is not a matter of taste. The others bound work; that one bounds
/// what may be said, and it is checked as validation rather than being buried in a query so a
/// caller who asks for a histogram fine enough to isolate a single cave is told no in the same
/// language as any other bad request.
/// </remarks>
public static class RegistryStatisticsLimits
{
    /// <summary>Fewest sectors a histogram may be asked for.</summary>
    public const int MinimumBinCount = 2;

    /// <summary>
    /// Most sectors a histogram may be asked for. A hundred sectors over a registry of a few
    /// hundred caves is already finer than the data supports; past that the request is a way of
    /// asking for the values one at a time.
    /// </summary>
    public const int MaximumBinCount = 100;

    /// <summary>Sectors a histogram is drawn with when the caller names no number.</summary>
    public const int DefaultBinCount = 20;

    /// <summary>
    /// Fewest caves a published sector may hold. A sector holding one or two is joined to its
    /// neighbour — never removed, because a gap in an even axis announces that something rare sits
    /// exactly there, and on an axis of lengths that is narrow enough to name the cave.
    /// </summary>
    /// <remarks>
    /// Three rather than two: two caves of nearly equal length in one narrow sector identify each
    /// other to anybody who knows one of them, which is the disclosure the floor exists to prevent.
    /// </remarks>
    public const int MinimumBinCaveCount = 3;

    /// <summary>
    /// The highest floor a caller may name. Past it the rule stops being a protection and becomes a
    /// second way of asking for a coarse histogram, which the sector count already offers — and a
    /// floor above the registry's own size collapses every answer to a single sector whatever was
    /// asked for.
    /// </summary>
    public const int MaximumBinCaveCount = 50;

    /// <summary>
    /// Most quantiles one request may ask for. Quantiles are order statistics: enough of them over
    /// a small registry reconstructs the sorted values, and the sorted values are the registry.
    /// </summary>
    public const int MaximumPercentileCount = 21;

    /// <summary>The quantiles published when the caller names none: the quartiles and the deciles at
    /// each end, which is what a box plot is drawn from.</summary>
    public static IReadOnlyList<double> DefaultPercentiles { get; } =
        [0.1, 0.25, 0.5, 0.75, 0.9];
}
