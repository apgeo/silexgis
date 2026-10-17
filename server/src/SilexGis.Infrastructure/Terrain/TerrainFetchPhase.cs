// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The first step: establishing which rasters a build is made from, and copying in the ones that
/// are not already on this machine to stay.
/// </summary>
/// <remarks>
/// <para>
/// Three ways in. Coverage for the rectangle is obtained from the open dataset this application
/// knows, and a raster sent through the browser is taken from where the upload left it — both
/// copied into the build's input directory. A directory on the server the operator has listed as
/// readable is <b>not</b> copied: it is typically a survey of tens of gigabytes that is already on
/// this disk, so its rasters are read where they are and the build records a list of them instead
/// (<see cref="TerrainInPlaceSources"/>). Nothing here or later writes, renames or deletes in that
/// directory; the step after this one reads both places, and the tool that meshes is handed the
/// build's own prepared rasters, never the operator's.
/// </para>
/// <para>
/// Every one of the three is safe to run again. A cell already on disk is not obtained twice, a
/// raster already copied is not copied twice, a directory already listed is compared with its list
/// rather than listed afresh, and nothing here writes a row — the sources were recorded when the
/// build was asked for, so being handed the same build after a restart leaves the record exactly as
/// it was rather than doubling it.
/// </para>
/// </remarks>
public sealed class TerrainFetchPhase(
    SilexGisDbContext db,
    CopernicusFetcher fetcher,
    TerrainInPlaceSources inPlace,
    TerrainUploads uploads,
    ILogger<TerrainFetchPhase> logger) : ITerrainPhase
{
    public TerrainBuildPhase Phase => TerrainBuildPhase.Fetch;

    /// <summary>
    /// Always answers no, and does the skipping one raster at a time instead.
    /// </summary>
    /// <remarks>
    /// The question this is asked is whether the step's output is already there. For this step the
    /// only honest answer is per-raster: a directory holding some of what was asked for looks
    /// exactly like a directory holding all of it, and accepting the first as finished is how a
    /// build gets meshed with part of its ground missing — which draws as smooth terrain and not as
    /// a hole. Every path below therefore checks the one file it is about to write, which costs a
    /// look at the disk each and skips just as much work as a directory-shaped answer would.
    /// </remarks>
    public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
        Task.FromResult(false);

    public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
    {
        var build = context.Build;

        // A source inherited from the build this one extends is passed over: its tiles are already
        // in the pyramid this build starts from, and obtaining and meshing it again is the full
        // re-bake an extension exists to avoid. It is still one of this build's sources — the
        // credit it owes is composed from the whole list — just not one this step has work for.
        var sources = await db.TerrainBuildSources.AsNoTracking()
            .Where(s => s.TerrainBuildId == build.Id && !s.Inherited)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        if (sources.Count == 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.NoRasters, "This build names nothing to build from.");
        }

        var input = context.Directories.Input;
        Directory.CreateDirectory(input);
        var adopted = AdoptedRasters.For(context.Directories);

        // The directories read in place come last, together, so that every copied raster already
        // has its name when the in-place ones are given theirs: two rasters prepared under one
        // name would refuse the whole build, and a name is fixed the first time it is given.
        var copiedSources = sources.Where(s => s.Kind != TerrainBuildSourceKind.ServerDirectory).ToList();
        var references = sources
            .Where(s => s.Kind == TerrainBuildSourceKind.ServerDirectory)
            .Select(s => s.Reference)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var shares = copiedSources.Count + (references.Count > 0 ? 1 : 0);

        for (var i = 0; i < copiedSources.Count; i++)
        {
            var source = copiedSources[i];
            var from = Share(i, shares);
            var to = Share(i + 1, shares);

            switch (source.Kind)
            {
                case TerrainBuildSourceKind.Fetched:
                    await CoverageAsync(context, input, from, to, ct);
                    break;
                case TerrainBuildSourceKind.Uploaded:
                    await UploadAsync(context, source, input, to, adopted, ct);
                    break;
                default:
                    throw new TerrainBuildException(
                        TerrainBuildFailures.SourceUnreadable,
                        $"This build names a kind of source this installation does not know ({source.Kind}).");
            }
        }

        var readInPlace = references.Count == 0
            ? 0
            : await DirectoriesAsync(context, references, input, ct);

        if (readInPlace == 0 && !Directory.EnumerateFiles(input).Any(f => TerrainRasterFiles.IsRaster(f)))
        {
            // Well-formed and empty: a rectangle drawn over open water asks only for cells nobody
            // publishes, and a directory somebody emptied while this waited names nothing. Neither
            // is a broken request, and neither can be built from, so it stops here and says which.
            throw new TerrainBuildException(
                TerrainBuildFailures.NoRasters,
                "Nothing this build names holds elevation data. A rectangle entirely over the sea "
                + "has no published coverage, and a directory may have been emptied since.");
        }
    }

    /// <summary>Obtains the open dataset's cells covering the rectangle.</summary>
    private async Task CoverageAsync(
        TerrainBuildContext context, string input, int from, int to, CancellationToken ct)
    {
        var box = context.Build.Extent.EnvelopeInternal;
        var cells = CopernicusCoverage.Cells(box.MinX, box.MinY, box.MaxX, box.MaxY);

        var obtained = 0;
        var present = 0;
        var unpublished = 0;

        for (var i = 0; i < cells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = cells[i];
            var target = Path.Combine(input, CopernicusCoverage.FileName(cell));

            await context.ReportAsync(
                from + ((to - from) * i / Math.Max(cells.Count, 1)),
                $"{CopernicusCoverage.Name}: {cell} ({i + 1} of {cells.Count})",
                ct);

            // Only a file at the final name counts. A transfer still in flight is written under
            // another name entirely, so there is nothing here that could be a fragment.
            if (File.Exists(target) && new FileInfo(target).Length > 0)
            {
                present++;
                continue;
            }

            var bytes = await fetcher.DownloadAsync(cell, target, ct);
            if (bytes is null)
            {
                // Cells that are entirely ocean are simply not published. An ordinary answer for
                // any rectangle drawn near a coast, and the one rule here that, forgotten, refuses
                // every coastal area on Earth while looking like a broken download.
                unpublished++;
            }
            else
            {
                obtained++;
            }
        }

        logger.LogInformation(
            "Terrain build {BuildId} coverage: {Obtained} obtained, {Present} already present, "
            + "{Unpublished} not published",
            context.Build.Id, obtained, present, unpublished);

        var summary = $"{CopernicusCoverage.Name}: {obtained + present} of {cells.Count} cells, "
            + $"{unpublished} not published (sea)";
        await context.ReportAsync(to, summary, ct);
        await context.LogAsync(summary + ".", ct);
    }

    /// <summary>Takes a raster sent through the browser from where the upload left it.</summary>
    private async Task UploadAsync(
        TerrainBuildContext context,
        TerrainBuildSource source,
        string input,
        int to,
        AdoptedRasters adopted,
        CancellationToken ct)
    {
        var path = uploads.PathOf(source.Reference)
            ?? throw new TerrainBuildException(
                TerrainBuildFailures.SourceUnreadable,
                "A raster this build was given is no longer where it was left.");

        Adopt(path, input, adopted);
        await context.ReportAsync(to, $"Uploaded raster {source.Reference}", ct);
        await context.LogAsync($"Took the uploaded raster {source.Reference}.", ct);
    }

    /// <summary>
    /// Lists the rasters in the directories on the server's own disk that this build names, which
    /// it reads where they are — or, on any later run, checks that they are still what it listed.
    /// </summary>
    /// <returns>How many rasters the build reads in place.</returns>
    /// <remarks>
    /// The path is resolved and judged against the operator's list <b>here</b>, not merely when the
    /// request was accepted, and the rights of whoever asked are rebuilt at the same moment. Both
    /// halves matter and for the same reason: this runs minutes or hours after the request, and in
    /// between the disk can change — a directory replaced by a link to somewhere else — and rights
    /// can be taken away. Trusting either answer from the moment the request arrived is a way to
    /// read whatever the service account can open, on a delay. The step that reads the rasters
    /// judges the directory against the list again, immediately before it reads.
    ///
    /// <para>
    /// The rights asked for are the same pair the request was held to: the terrain right, and the
    /// separate question of whether this account may name a location for the server to read at all.
    /// A build queued by the installation itself names nobody and is refused here, because there is
    /// nobody whose permission to read the disk could be checked.
    /// </para>
    /// <para>
    /// The first run writes the list; every later one compares the directory with it and stops on
    /// any difference with its own reason, rather than resuming over a set of rasters that is not
    /// the one this build was made from.
    /// </para>
    /// </remarks>
    private async Task<int> DirectoriesAsync(
        TerrainBuildContext context,
        IReadOnlyList<string> references,
        string input,
        CancellationToken ct)
    {
        if (context.Requester is not { } requester
            || !AccessEvaluator.Decide(requester, AccessDomain.Terrain, AccessAction.Execute, null).Allowed
            || !ServerImportPaths.MayReadServerDisk(requester))
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.SourceUnreadable,
                "Whoever asked for this build no longer holds the right to read rasters from the "
                + "server's own disk.");
        }

        var seen = inPlace.Look(references, ct);
        var recorded = inPlace.Read(context.Directories);
        if (recorded is null)
        {
            var copied = Directory.EnumerateFiles(input)
                .Where(TerrainRasterFiles.IsRaster)
                .Select(Path.GetFileName)
                .OfType<string>();
            recorded = inPlace.Record(context.Directories, references, seen, copied);
        }
        else if (TerrainInPlaceSources.Difference(recorded, references, seen) is { } what)
        {
            throw TerrainInPlaceSources.Changed(what);
        }

        var count = recorded.Rasters.Count;
        var where = string.Join(", ", references);

        logger.LogInformation(
            "Terrain build {BuildId} reads {Count} rasters in place from {Directories}",
            context.Build.Id, count, where);
        await context.ReportAsync(100, $"{where}: {count} rasters, read where they are", ct);
        await context.LogAsync(
            $"Reads {count} rasters from {where} where they are; nothing there is copied or changed.",
            ct);

        return count;
    }

    /// <summary>
    /// Copies one raster into the build's input directory, under a name nothing else there has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copied because these rasters have no other lasting home on this machine: a downloaded cell
    /// would otherwise be fetched again on every run, and an upload is swept once no build names
    /// it. A directory the operator keeps on the server is the one source that is not copied.
    /// </para>
    /// <para>
    /// A raster this build has already taken from this same place, still there and still the same
    /// length, is left alone: that is what makes the step cheap to run again after a restart.
    /// Sameness is decided by which file it came from and not by its name and size, because several
    /// of the formats accepted here are fixed-size grids — every tile of a given resolution is
    /// exactly the same number of bytes — so two different tiles named alike would otherwise look
    /// identical, and the second would be dropped. Two rasters from different places therefore get
    /// a number: losing one of them silently would leave a hole in the ground with nothing to say
    /// so, and a hole in the ground bakes as smooth terrain.
    /// </para>
    /// <para>
    /// The copy goes to a partial name and is renamed onto the final one, which is the discipline
    /// every writer into this directory keeps. An interrupted copy — a crash, a disk that filled, a
    /// share that dropped — must not leave a truncated raster at a name a later step reads as a
    /// whole one.
    /// </para>
    /// </remarks>
    private static void Adopt(string file, string input, AdoptedRasters adopted)
    {
        var length = new FileInfo(file).Length;
        var name = Path.GetFileName(file);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var candidate = attempt == 0 ? name : $"{stem}-{attempt}{extension}";
            var destination = Path.Combine(input, candidate);

            if (!File.Exists(destination))
            {
                CopyWhole(file, destination);
                adopted.Record(candidate, file);
                return;
            }

            if (adopted.CameFrom(candidate, file) && new FileInfo(destination).Length == length)
            {
                return;
            }
        }

        throw new TerrainBuildException(
            TerrainBuildFailures.SourceUnreadable,
            $"Too many rasters named {name} were given to one build.");
    }

    /// <summary>
    /// Copies one file so that its final name never exists until every byte of it does.
    /// </summary>
    private static void CopyWhole(string file, string destination)
    {
        var partial = destination + CopernicusFetcher.PartialSuffix;
        try
        {
            // Overwriting: a fragment left by an attempt that died is not worth keeping, and it is
            // under a name nothing will ever read as a raster.
            File.Copy(file, partial, overwrite: true);
            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(partial);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The failure being reported matters more than the leftover fragment.
            }

            throw;
        }
    }

    /// <summary>
    /// Which raster in a build's input directory came from which file, kept beside the build so it
    /// survives a restart.
    /// </summary>
    /// <remarks>
    /// It exists to answer one question — "have I already taken this exact file?" — which the disk
    /// cannot answer on its own once two sources contribute files of the same name and size. It is
    /// only ever an optimisation: a record that has been lost costs a second copy under a numbered
    /// name, never a raster silently dropped.
    /// </remarks>
    private sealed class AdoptedRasters
    {
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private readonly string path;
        private readonly Dictionary<string, string> sourceByName;

        private AdoptedRasters(string path, Dictionary<string, string> sourceByName)
        {
            this.path = path;
            this.sourceByName = sourceByName;
        }

        /// <summary>Reads what earlier runs of this build recorded, if anything.</summary>
        public static AdoptedRasters For(TerrainBuildDirectories directories)
        {
            var path = Path.Combine(directories.Root, "adopted.tsv");
            var known = new Dictionary<string, string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        var tab = line.IndexOf('\t');
                        if (tab > 0 && tab < line.Length - 1)
                        {
                            known[line[..tab]] = line[(tab + 1)..];
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Unreadable is the same as absent: nothing is skipped that should have been.
            }

            return new AdoptedRasters(path, known);
        }

        /// <summary>Whether the file already at <paramref name="name"/> came from this source.</summary>
        public bool CameFrom(string name, string source) =>
            sourceByName.TryGetValue(name, out var recorded)
            && string.Equals(recorded, Full(source), PathComparison);

        /// <summary>Remembers that this name in the input directory holds that file.</summary>
        public void Record(string name, string source)
        {
            var full = Full(source);
            sourceByName[name] = full;

            try
            {
                File.AppendAllText(path, $"{name}\t{full}\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The raster is copied either way; only the cheapness of resuming is lost.
            }
        }

        private static string Full(string source)
        {
            try
            {
                return Path.GetFullPath(source);
            }
            catch (Exception e) when (e is ArgumentException or IOException)
            {
                return source;
            }
        }
    }

    /// <summary>Where one source of several sits on this step's own nought-to-a-hundred.</summary>
    private static int Share(int index, int count) => count <= 0 ? 100 : 100 * index / count;
}
