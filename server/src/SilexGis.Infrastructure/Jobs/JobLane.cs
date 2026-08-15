// SPDX-License-Identifier: AGPL-3.0-or-later
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
}
