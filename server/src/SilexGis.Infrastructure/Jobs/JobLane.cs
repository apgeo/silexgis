// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// The slice of the queue one worker claims from.
/// </summary>
/// <remarks>
/// The two lanes are complements of one another over a single list of kinds: one claims only the
/// kinds it names, the other claims everything else. Building both from the same list is what
/// makes them a partition of the table. A lane carrying a list of its own would silently stop
/// claiming every kind added after it was written, and two lists that drifted apart would leave a
/// kind claimed by both workers, or by neither — and nothing about the queue would look wrong.
/// </remarks>
/// <param name="Kinds">The job kinds the lane is defined by.</param>
/// <param name="Excluded">
/// True when those kinds are the ones this lane does <b>not</b> take (the general lane); false
/// when they are the only ones it takes.
/// </param>
public sealed record JobLane(IReadOnlyList<string> Kinds, bool Excluded)
{
    /// <summary>Everything except the kinds that have a worker of their own.</summary>
    public static JobLane General { get; } = new(ProcessingJobKinds.TerrainLane, Excluded: true);

    /// <summary>Terrain builds, and nothing else.</summary>
    public static JobLane Terrain { get; } = new(ProcessingJobKinds.TerrainLane, Excluded: false);

    /// <summary>
    /// The general lane as this installation configures it: map-sheet drawing moves to the heavy
    /// lane when <c>GeoreferencedMaps:SheetJobsOnHeavyLane</c> is set.
    /// </summary>
    public static JobLane GeneralFor(IConfiguration configuration) => new(HeavyKinds(configuration), Excluded: true);

    /// <summary>The heavy lane as this installation configures it; the complement of <see cref="GeneralFor"/>.</summary>
    public static JobLane TerrainFor(IConfiguration configuration) => new(HeavyKinds(configuration), Excluded: false);

    /// <summary>
    /// The kinds the heavy lane takes. Sheets draw in seconds to about a minute, so by default they
    /// stay on the general lane rather than queue behind hour-long terrain builds; an installation
    /// whose sheets are large enough to hold up imports moves them across.
    /// </summary>
    public static IReadOnlyList<string> HeavyKinds(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetValue<bool>("GeoreferencedMaps:SheetJobsOnHeavyLane")
            ? [.. ProcessingJobKinds.TerrainLane, ProcessingJobKinds.MapSheetRender, ProcessingJobKinds.MapPlacementWarp]
            : ProcessingJobKinds.TerrainLane;
    }
}
