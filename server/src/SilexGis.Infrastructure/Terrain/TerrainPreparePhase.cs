// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The second step: turning the pile of rasters a build gathered into the one form everything after
/// it reads.
/// </summary>
/// <remarks>
/// <para>
/// What arrives from the first step is whatever the world publishes — tiles in one coordinate
/// system and a survey in another, one file saying <c>-32768</c> for nothing-known and the next
/// saying <c>0</c>, pixels a metre across beside pixels thirty metres across. Nothing downstream has
/// any way to be told about those differences, so they are settled once, here: every raster comes
/// out in one reference with one value standing for a hole.
/// </para>
/// <para>
/// What is <i>not</i> settled here is which raster covers which ground. They stay separate files,
/// each keeping its own pixel size, because that is what lets whatever meshes them go deep over a
/// fine local survey and no deeper than a coarse regional fill supports elsewhere. Merged into one
/// sheet they could not: a single grid across a mixed set either throws the fine detail away or
/// stretches the coarse data into detail it never had.
/// </para>
/// <para>
/// The result is kept rather than thrown away with the run. It is the honest record of what a build
/// was made from — the sources themselves may be edited, moved or deleted afterwards — and it is
/// what makes building the mesh again cost minutes instead of hours.
/// </para>
/// </remarks>
public sealed class TerrainPreparePhase(
    ITerrainRasterPreparer preparer,
    TerrainRasterIndex index,
    ILogger<TerrainPreparePhase> logger) : ITerrainPhase
{
    public TerrainBuildPhase Phase => TerrainBuildPhase.Prepare;

    /// <summary>
    /// Whether every raster this step would write is already there and whole.
    /// </summary>
    /// <remarks>
    /// Asked of the disk and then asked again of each file, and of all of them rather than of one:
    /// a run that died partway leaves some of the set converted and the rest missing, and a run that
    /// died while writing leaves a file of the right name in the right place and the wrong length.
    /// The check opens each one, confirms it is in the reference and carries the value for a hole
    /// that this pipeline settled on, and asks for a pixel out of its far corner — the part a file
    /// cut short has not got. Getting this wrong costs nothing visible: ground assembled from a
    /// truncated raster is smooth and plausible exactly where the data ran out.
    /// </remarks>
    public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct)
    {
        var inputs = Sources(context);
        if (inputs.Count == 0)
        {
            // Nothing to compare against. The run that follows says so properly, with a code.
            return Task.FromResult(false);
        }

        var existing = preparer.DescribePrepared(RequestFor(context, inputs));
        if (existing is not null)
        {
            logger.LogInformation(
                "Terrain build {BuildId} already has {Count} prepared raster(s)",
                context.Build.Id, existing.Count);
        }

        return Task.FromResult(existing is not null);
    }

    public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
    {
        var inputs = Sources(context);
        if (inputs.Count == 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.NoRasters,
                "There is nothing to prepare: no elevation raster reached this build.");
        }

        await context.ReportAsync(
            5,
            inputs.Count == 1 ? "Preparing 1 raster" : $"Preparing {inputs.Count} rasters",
            ct);

        // Synchronous, and deliberately not handed to a background thread: this runs on a worker
        // that takes one terrain build at a time, and the raster library's handles must not be
        // touched from more than one thread — doing so faults the process in a way no catch block
        // in this language can see.
        var prepared = preparer.Prepare(RequestFor(context, inputs), ct);

        logger.LogInformation(
            "Terrain build {BuildId} prepared {Prepared} of {Sources} raster(s), {Bytes} bytes",
            context.Build.Id, prepared.Count, inputs.Count, prepared.Sum(p => p.SizeBytes));

        foreach (var raster in prepared)
        {
            await context.LogAsync(
                $"Prepared {Path.GetFileName(raster.Path)}: {raster.Width}x{raster.Height} pixels "
                + $"covering {Round(raster.West)}, {Round(raster.South)} to "
                + $"{Round(raster.East)}, {Round(raster.North)} "
                + $"({FormattableString.Invariant($"{raster.PixelSizeDegrees:G6}")} degrees per pixel).",
                ct);
        }

        // Said plainly rather than left to be inferred from a count of lines. A raster left out for
        // covering none of the ground asked for is an ordinary thing that happens when a directory
        // holds a whole country, and it is also what a mis-drawn rectangle looks like.
        if (prepared.Count < inputs.Count)
        {
            await context.LogAsync(
                $"Prepared {prepared.Count} of {inputs.Count} raster(s); the rest cover none of the "
                + "area this build asked for.",
                ct);
        }

        // What was just written is described once and kept, so that reading a height later does not
        // have to open every one of these files to find out which of them holds the point. Done
        // here rather than left to the first reader because this step already has the files open
        // and warm, and because a build that finishes at three in the morning should not make
        // whoever asks the first question of the day pay for the description.
        await index.RefreshAsync(context.Build.Id, ct);

        await context.ReportAsync(100, null, ct);
    }

    private static List<string> Sources(TerrainBuildContext context) =>
        TerrainPreparedSet.Sources(context);

    private static TerrainRasterPrepareRequest RequestFor(
        TerrainBuildContext context, IReadOnlyList<string> inputs) =>
        TerrainPreparedSet.RequestFor(context, inputs);

    /// <summary>
    /// A coordinate as a line of a log wants it: enough places to say which valley, not enough to
    /// suggest the number means more than a reprojection without transformation grids can promise.
    /// </summary>
    private static string Round(double degrees) =>
        FormattableString.Invariant($"{Math.Round(degrees, 5)}");
}
