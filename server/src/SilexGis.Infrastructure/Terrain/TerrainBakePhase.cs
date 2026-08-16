// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The third step: turning the prepared rasters into the pyramid of tiles a 3D scene draws.
/// </summary>
/// <remarks>
/// <para>
/// The program that does the meshing is a command-line tool that runs once and exits, and this
/// application is deliberately given no way to start containers of its own — that is a much larger
/// privilege than making tiles is worth, and it would be a privilege every part of this process
/// then held. So the two meet on a directory they share: this step leaves a request there and
/// waits, and a small service watching that directory runs the tool and leaves its answer beside
/// it. That service is optional, absent unless an operator starts it, and this step tells the three
/// answers apart — nothing to bake with, something that is not answering, and something that ran
/// and would not do it — because they are three different things to do about it and only one of
/// them is anybody's mistake.
/// </para>
/// <para>
/// The one thing this step exists to catch is that the tool does not fail when it runs short of
/// memory. It stops refining and writes coarser tiles, and says so only in its own log. The
/// pyramid that comes out is complete and valid and describes ground at the wrong resolution, and
/// nothing anywhere reports it. So the log is read, every time, and a bake that says it degraded is
/// a failed bake however cleanly the tool exited.
/// </para>
/// </remarks>
public sealed class TerrainBakePhase(
    ITerrainRasterPreparer preparer,
    TerrainWorkspace workspace,
    IOptions<TerrainBuildOptions> options,
    ILogger<TerrainBakePhase> logger) : ITerrainPhase
{
    /// <summary>
    /// How often the answer is looked for. The service on the other side looks for work about this
    /// often too, so anything shorter only costs directory reads.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How often the build's own progress line is rewritten while the bake runs.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many of the tool's own last lines are kept when a bake ends badly. Enough to carry a
    /// stack trace's first few frames; far short of the bounded column they are stored in.
    /// </summary>
    private const int KeptLogLines = 15;

    private readonly TerrainBuildOptions settings = options.Value;

    public TerrainBuildPhase Phase => TerrainBuildPhase.Bake;

    /// <summary>
    /// Whether this build already has a pyramid that some run of this step has stood behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pyramid on disk is asked for in both its halves, because either one alone draws nothing
    /// and reports nothing: a manifest with no tiles behind it, and tiles no manifest advertises,
    /// are the two shapes a meshing run that stopped part way leaves behind. Whether what is there
    /// is <i>good</i> is the next step's whole purpose and is not second-guessed here.
    /// </para>
    /// <para>
    /// What a pyramid on disk is not, on its own, is a bake anybody read the tool's log for — and
    /// that log is the only place the one failure this step exists to catch is ever written. This
    /// application and the thing doing the meshing are separate programs: this one can be restarted
    /// mid-bake — a deployment, a host that rebooted — while the other carries on, finishes, and
    /// leaves a complete pyramid and its answer behind, having said in its log and nowhere else
    /// that it ran short of memory and stopped refining. The run that comes back finds exactly what
    /// a good bake leaves. So the directory the two sides met in is what decides, and it says three
    /// different things:
    /// </para>
    /// <list type="bullet">
    /// <item>An answer nobody has read: this run reads it now, log and all, and either clears the
    /// pyramid and fails or accepts what the earlier bake produced.</item>
    /// <item>A request with no answer beside it: something may be meshing into this very directory
    /// right now, so there is nothing finished to accept and the step is run, which waits for it.</item>
    /// <item>Nothing at all: the only thing that removes it is a run of this step that read the log
    /// and found the bake good, so a pyramid beside an absent spool is an adjudicated one.</item>
    /// </list>
    /// </remarks>
    public async Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct)
    {
        if (!HasPyramid(context.Directories.Tiles))
        {
            return false;
        }

        var spool = workspace.SpoolFor(context.Build.Id);
        var resultPath = Path.Combine(spool, TerrainBakeHandover.ResultFileName);

        if (!File.Exists(resultPath))
        {
            return !File.Exists(Path.Combine(spool, TerrainBakeHandover.RequestFileName));
        }

        var result = TerrainBakeResult.Parse(await File.ReadAllTextAsync(resultPath, ct));
        await ReadTheToolsWordsAsync(context, spool, result, ct);
        return true;
    }

    public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
    {
        // Asked of the step that wrote them rather than by opening the files here. Whether a
        // prepared set is whole is a question with one home — it knows that a run killed partway
        // leaves some of the set converted, and that a run killed while writing leaves a file of
        // the right name and the wrong length — and a second answer to it here would drift from
        // that one silently, in the direction of meshing a set nothing considers finished.
        var prepared = preparer.DescribePrepared(
            TerrainPreparedSet.RequestFor(context, TerrainPreparedSet.Sources(context)));

        if (prepared is null || prepared.Count == 0)
        {
            // Asked about this build before asking about the installation, deliberately. A build
            // with nothing to mesh is broken whether or not there is anything to mesh it with, and
            // telling an administrator to go and start a service would send them after the wrong
            // thing entirely.
            throw new TerrainBuildException(
                TerrainBuildFailures.NoRasters,
                "There is nothing to mesh: this build has no prepared rasters. They are written by "
                + "the step before this one and kept, so this means they were removed since.");
        }

        var depth = context.Build.RequestedMaxDepth;
        if (depth is < TerrainBakeHandover.ShallowestDepth or > TerrainBakeHandover.DeepestDepth)
        {
            var asked = FormattableString.Invariant(
                $"{depth}, outside {TerrainBakeHandover.ShallowestDepth} to {TerrainBakeHandover.DeepestDepth}");

            throw new TerrainBuildException(
                TerrainBuildFailures.BakeRefused,
                $"This build asks for a deepest tile level of {asked}, which is the range this "
                + "application will ask a tile maker for.");
        }

        if (!settings.BakeEnabled)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.BakeUnavailable,
                "This installation has nothing that can turn rasters into tiles. Start the terrain "
                + "worker alongside the application — docker compose -f docker-compose.yml "
                + "-f docker-compose.terrain-worker.yml up -d — with "
                + "SILEXGIS__Terrain__BakeEnabled=true in the environment file, and run this build "
                + "again. Everything it has done so far is kept, so it will start from here.");
        }

        var request = new TerrainBakeRequest(
            context.Directories.Prepared,
            context.Directories.Tiles,
            depth,
            context.Build.HeightDatum);

        var spool = workspace.SpoolFor(context.Build.Id);
        var alreadyRunning = Handover(spool, request);

        await context.ReportAsync(
            5,
            alreadyRunning
                ? "Waiting for a bake already under way"
                : FormattableString.Invariant(
                    $"Meshing {prepared.Count} raster(s) to level {depth}"),
            ct);

        if (!alreadyRunning)
        {
            var what = FormattableString.Invariant(
                $"{prepared.Count} prepared raster(s) down to level {depth}");

            await context.LogAsync(
                $"Asked for a mesh of {what}, heights measured from "
                + $"{Datum(context.Build.HeightDatum)}.",
                ct);
        }

        TerrainBakeResult result;
        try
        {
            result = await WaitAsync(context, spool, ct);
        }
        catch (TerrainBuildException)
        {
            // A bake abandoned without an answer leaves whatever it had written where a later run
            // would find it, and finding a pyramid is how a later run decides the meshing is done.
            // Every route through the step that judges an answer already sweeps for this reason;
            // the routes that never get an answer — nothing ever took the request, or it was taken
            // and the time ran out — have to sweep for it too. Cancellation is deliberately not
            // caught: a host shutting down is not a verdict, and that run is meant to resume.
            Clear(context.Directories.Tiles);
            throw;
        }

        await ReadTheToolsWordsAsync(context, spool, result, ct);

        logger.LogInformation(
            "Terrain build {BuildId} meshed to level {Depth}: {Outcome}",
            context.Build.Id, depth, result.Outcome);

        await context.ReportAsync(100, null, ct);
    }

    /// <summary>
    /// Puts the request where the other side will find it, and says whether one was already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request left over from an earlier attempt is two different situations. If it has an answer
    /// beside it, that answer is about a bake that has already been dealt with — read as this
    /// attempt's it would say a build succeeded seconds after it started, having meshed nothing —
    /// so the whole directory goes and a fresh request takes its place. If it has no answer, a bake
    /// may be running right now against the very directory this one would write into, and starting
    /// a second is how two programs come to be writing one pyramid. That one is waited for instead.
    /// </para>
    /// <para>
    /// Written under another name and renamed into place. A rename is the only way the other side
    /// can be sure it is reading a whole file, and it reads whatever it finds the moment it finds
    /// it: half a request is a bake of the wrong thing, silently. The bytes are plain, with no
    /// byte-order mark and single-character line endings, because what reads them is a shell
    /// script — a mark on the front makes the first line unmatchable and a carriage return on the
    /// end makes a depth not a number.
    /// </para>
    /// </remarks>
    private static bool Handover(string spool, TerrainBakeRequest request)
    {
        var requestPath = Path.Combine(spool, TerrainBakeHandover.RequestFileName);
        var resultPath = Path.Combine(spool, TerrainBakeHandover.ResultFileName);

        if (File.Exists(requestPath) && !File.Exists(resultPath))
        {
            return true;
        }

        if (Directory.Exists(spool))
        {
            Directory.Delete(spool, recursive: true);
        }

        Directory.CreateDirectory(spool);

        var partial = requestPath + TerrainBakeHandover.PartialSuffix;
        File.WriteAllText(partial, request.ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(partial, requestPath);
        return false;
    }

    /// <summary>
    /// Waits for the answer, and decides what it means when none comes.
    /// </summary>
    /// <remarks>
    /// Two different silences, told apart by whether anything ever claimed the request. Nothing
    /// claiming it means nothing is there to claim it — a service stopped, crashed, or never given
    /// the directory both sides need — which is worth trying again because a service coming back up
    /// looks exactly like this for a minute. Something claiming it and then never finishing is a
    /// bake that has stopped making progress, and the queue this runs on has no time limit of its
    /// own, so without a limit here one build holds its worker for ever.
    /// </remarks>
    private async Task<TerrainBakeResult> WaitAsync(
        TerrainBuildContext context, string spool, CancellationToken ct)
    {
        var resultPath = Path.Combine(spool, TerrainBakeHandover.ResultFileName);
        var startedPath = Path.Combine(spool, TerrainBakeHandover.StartedFileName);
        var logPath = Path.Combine(spool, TerrainBakeHandover.LogFileName);

        var began = DateTimeOffset.UtcNow;
        var pickupBy = began + TimeSpan.FromSeconds(Math.Clamp(settings.BakePickupSeconds, 5, 3600));
        var giveUpAt = began + TimeSpan.FromSeconds(Math.Clamp(settings.BakeTimeoutSeconds, 60, 604_800));
        var lastSaid = began;
        var claimed = false;

        while (true)
        {
            if (File.Exists(resultPath))
            {
                return TerrainBakeResult.Parse(await File.ReadAllTextAsync(resultPath, ct));
            }

            claimed = claimed || File.Exists(startedPath);
            var now = DateTimeOffset.UtcNow;

            if (!claimed && now > pickupBy)
            {
                var waited = FormattableString.Invariant($"{(now - began).TotalSeconds:F0}");

                throw new TerrainBuildException(
                    TerrainBuildFailures.BakeWorkerSilent,
                    $"Nothing took this bake in {waited} seconds. This installation is configured "
                    + "to have something that makes tiles, so check that it is running and that it "
                    + "has the shared terrain directory.");
            }

            if (now > giveUpAt)
            {
                var hours = FormattableString.Invariant($"{(now - began).TotalHours:F1}");

                throw new TerrainBuildException(
                    TerrainBuildFailures.BakeTimedOut,
                    $"The bake has been running for {hours} hours and has not finished. It was "
                    + "abandoned rather than left holding the queue every later build waits in.",
                    LastLines(logPath, KeptLogLines));
            }

            if (now - lastSaid >= ProgressInterval)
            {
                lastSaid = now;

                // Held at one number on purpose while this runs. The tool says nothing about how
                // much of the work is left — it reports neither a total nor a count — so a number
                // that crept upwards here would be a made-up one, and a made-up number on a screen
                // is read as a measurement. What moves instead is the tool's own last word.
                await context.ReportAsync(
                    10,
                    LastLines(logPath, 1)
                        ?? FormattableString.Invariant(
                            $"Meshing, {(now - began).TotalMinutes:F0} minute(s) so far"),
                    ct);
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>
    /// Reads what the tool said and decides whether the pyramid may be believed.
    /// </summary>
    /// <remarks>
    /// The log is read whatever the answer was, and read to the end rather than sampled, because
    /// the line that matters most is written once in the middle of a run that then finishes
    /// cleanly. A bake that gave up on detail it was asked for is a failed bake: what it leaves
    /// behind is a whole pyramid of ground at the wrong resolution, which no later check can tell
    /// from the right one.
    /// </remarks>
    private async Task ReadTheToolsWordsAsync(
        TerrainBuildContext context, string spool, TerrainBakeResult result, CancellationToken ct)
    {
        var logPath = Path.Combine(spool, TerrainBakeHandover.LogFileName);
        var degraded = FirstDegradedLine(logPath);

        if (degraded is not null)
        {
            await context.LogAsync(degraded, ct);
            Clear(context.Directories.Tiles);
            throw new TerrainBuildException(
                TerrainBuildFailures.BakeDegraded,
                "The tile maker ran short of memory and quietly produced coarser tiles than were "
                + "asked for, so what it made was discarded. It takes its memory as a share of its "
                + "container's limit and has no other setting for it, so give that container more "
                + "and run this build again.",
                degraded);
        }

        if (result.Outcome != TerrainBakeOutcome.Succeeded)
        {
            var tail = LastLines(logPath, KeptLogLines);
            if (tail is not null)
            {
                await context.LogAsync(tail, ct);
            }

            Clear(context.Directories.Tiles);
            throw new TerrainBuildException(Code(result.Outcome), Sentence(result), tail);
        }

        if (!HasPyramid(context.Directories.Tiles))
        {
            Clear(context.Directories.Tiles);
            throw new TerrainBuildException(
                TerrainBuildFailures.BakeIncomplete,
                "The tile maker finished without complaining and left no pyramid behind: a build "
                + "with no manifest, or with a manifest and no tiles, draws nothing at all and says "
                + "nothing about it.",
                LastLines(logPath, KeptLogLines));
        }

        await context.LogAsync("Meshing finished; the pyramid is written.", ct);
        Forget(spool);
    }

    /// <summary>
    /// Removes the directory the two sides met in, and only once the bake left in it has been read
    /// and found good.
    /// </summary>
    /// <remarks>
    /// Two separate things rest on it happening here and nowhere else. Nothing else ever deletes a
    /// spool directory — it deliberately sits outside the build's own folder, so deleting a build
    /// does not reach it — and each one holds the whole of the tool's log, so keeping them all
    /// would pile up without limit on the same volume the pyramids compete for. And its absence is
    /// a statement: a finished pyramid with no spool beside it is one whose log some run of this
    /// step read. A bake that ended badly keeps everything, because that is when its account is
    /// wanted most, and the next attempt replaces the directory rather than reading it.
    /// </remarks>
    private static void Forget(string spool)
    {
        try
        {
            if (Directory.Exists(spool))
            {
                Directory.Delete(spool, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left where it is rather than failing a bake that was good. A spool that outlives its
            // build costs disk; it is read again on any later run and judged the same way.
        }
    }

    private static string Code(TerrainBakeOutcome outcome) => outcome switch
    {
        TerrainBakeOutcome.Failed => TerrainBuildFailures.BakeFailed,
        TerrainBakeOutcome.Interrupted => TerrainBuildFailures.BakeInterrupted,
        _ => TerrainBuildFailures.BakeRefused,
    };

    private static string Sentence(TerrainBakeResult result)
    {
        var said = result.Reason is null ? "" : $" It said: {result.Reason}";
        var code = result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

        return result.Outcome switch
        {
            TerrainBakeOutcome.Failed =>
                $"The tile maker ended badly (code {code}).{said}",
            TerrainBakeOutcome.Interrupted => "The bake was stopped while it was running, so what it "
                + $"had written was discarded. Nothing is wrong with this build's data.{said}",
            TerrainBakeOutcome.Refused => "The tile maker would not accept the request this "
                + $"application wrote, which is a fault in the application rather than in the data.{said}",
            _ => "The tile maker's answer could not be read, so what it did is unknown and what it "
                + "wrote was discarded.",
        };
    }

    /// <summary>Whether both halves of a pyramid are there.</summary>
    private static bool HasPyramid(string tiles) =>
        File.Exists(Path.Combine(tiles, TerrainPyramid.ManifestFileName))
        && Directory.Exists(tiles)
        && Directory.EnumerateFiles(tiles, "*" + TerrainPyramid.TileExtension, SearchOption.AllDirectories)
            .Any();

    /// <summary>
    /// Empties a pyramid that must not be trusted, keeping the directory itself.
    /// </summary>
    /// <remarks>
    /// Done before the failure is raised, and this is load-bearing rather than tidy. A build that
    /// stops is put back on the queue, and the step that runs then asks the disk whether the work
    /// is already done — so a half-written or degraded pyramid left lying here would be found,
    /// believed, and published. The tool's own account of what went wrong is not in here; it stays
    /// where the two sides met, which is exactly when it is wanted most.
    /// </remarks>
    private static void Clear(string tiles)
    {
        if (!Directory.Exists(tiles))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(tiles))
            {
                File.Delete(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(tiles))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Whatever could not be swept is reported by the failure that is already on its way.
        }
    }

    /// <summary>The first line of the tool's log that means it gave up on detail, if any.</summary>
    private static string? FirstDegradedLine(string logPath)
    {
        foreach (var line in Lines(logPath))
        {
            if (TerrainBakeHandover.MeansDegraded(line))
            {
                return line.Length > 400 ? line[..400] : line;
            }
        }

        return null;
    }

    /// <summary>The last few non-blank lines of the tool's log, as one block, or nothing.</summary>
    private static string? LastLines(string logPath, int count)
    {
        var kept = new Queue<string>(count);

        foreach (var line in Lines(logPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (kept.Count == count)
            {
                kept.Dequeue();
            }

            kept.Enqueue(line.Length > 400 ? line[..400] : line);
        }

        return kept.Count == 0 ? null : string.Join('\n', kept);
    }

    /// <summary>
    /// The tool's log, a line at a time, or nothing at all if it is unreadable.
    /// </summary>
    /// <remarks>
    /// Opened sharing every access, because the program writing it may still be writing it — this
    /// is read while a bake is running to show what it is doing. Failing to read it is never worth
    /// failing a build over: it is diagnostic text, and the thing that went wrong is already known.
    /// </remarks>
    private static IEnumerable<string> Lines(string logPath)
    {
        StreamReader reader;
        try
        {
            reader = new StreamReader(new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        using (reader)
        {
            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }
    }

    private static string Datum(TerrainHeightDatum datum) =>
        datum == TerrainHeightDatum.Ellipsoidal ? "the ellipsoid" : "sea level";
}
