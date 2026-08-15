// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Terrain;

/// <summary>
/// The order the steps of a build happen in, and where each one's own progress sits in the whole.
/// </summary>
/// <remarks>
/// <para>
/// Three different questions are asked of a build and the row answers them separately, because
/// conflating any two of them produces a state nobody can act on. <b>Status</b> describes the run —
/// whether it is waiting, working, finished or stopped. <b>Phase</b> describes how far along the
/// chain it got. <b>The active flag</b> describes whether this is the terrain the scene draws. A
/// build that did everything the installation can do today and stopped because the next step does
/// not exist yet is a run that <i>succeeded</i>, at the phase it reached, and is <i>not</i> active.
/// There is deliberately no status meaning "partly done": the phase already says that, and a fifth
/// status would have to be given a number, which the index that finds unfinished builds names
/// literally.
/// </para>
/// <para>
/// The bands exist so that one number can describe a chain of unlike work. Each step reports its
/// own nought-to-a-hundred and is mapped into the slice of the whole it occupies, so a bar that has
/// reached a third of the way across means a third of the build rather than a third of whichever
/// step happens to be running. The slices are rough proportions of wall-clock, not measurements.
/// </para>
/// </remarks>
public static class TerrainPhases
{
    /// <summary>
    /// The steps in the order each consumes what the one before it produced.
    /// </summary>
    /// <remarks>
    /// <see cref="TerrainBuildPhase.Pending"/> is absent: it is the state of a build nobody has
    /// picked up yet, not a step anything runs.
    /// </remarks>
    public static readonly IReadOnlyList<TerrainBuildPhase> Order =
    [
        TerrainBuildPhase.Fetch,
        TerrainBuildPhase.Prepare,
        TerrainBuildPhase.Bake,
        TerrainBuildPhase.Validate,
        TerrainBuildPhase.Publish,
    ];

    private static readonly Dictionary<TerrainBuildPhase, (int From, int To)> Bands = new()
    {
        [TerrainBuildPhase.Pending] = (0, 0),
        [TerrainBuildPhase.Fetch] = (0, 20),
        [TerrainBuildPhase.Prepare] = (20, 45),
        [TerrainBuildPhase.Bake] = (45, 85),
        [TerrainBuildPhase.Validate] = (85, 95),
        [TerrainBuildPhase.Publish] = (95, 100),
    };

    /// <summary>
    /// Where a step's own progress — nought to a hundred — sits in the whole build's nought to a
    /// hundred. Always in range, whatever a tool's own arithmetic answers.
    /// </summary>
    /// <remarks>
    /// Clamped here as well as at the write because the input is a percentage derived from a
    /// tool's output — a count of tiles against an estimate of how many there will be — and such an
    /// estimate is wrong often enough that "103%" is an ordinary thing to be handed. The row's own
    /// range is a database constraint, so an unclamped number would not be a wrong progress bar but
    /// a refused write, thrown from inside the reporting that was meant to be harmless.
    /// </remarks>
    public static int Overall(TerrainBuildPhase phase, int within)
    {
        var (from, to) = Bands.TryGetValue(phase, out var band) ? band : (0, 100);
        var clamped = Math.Clamp(within, 0, 100);
        return Math.Clamp(from + ((to - from) * clamped / 100), 0, 100);
    }
}
