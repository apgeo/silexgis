// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>One raster a build reads where the operator keeps it.</summary>
/// <param name="Name">
/// The name it is prepared under — its own file name unless another of the build's rasters already
/// has that one, and then numbered. Fixed the first time the build reads the directory.
/// </param>
/// <param name="Path">Where it is, links resolved, as the build first read it.</param>
/// <param name="Length">Its length in bytes then.</param>
/// <param name="WrittenTicks">When it was last written then, in UTC ticks.</param>
public sealed record TerrainInPlaceRaster(string Name, string Path, long Length, long WrittenTicks);

/// <summary>
/// What a build reads from directories on the server's own disk, read where it lies rather than
/// copied into the build.
/// </summary>
/// <remarks>
/// <para>
/// A directory an operator names is typically a national survey that is already on the machine —
/// tens of gigabytes — and copying it into the build would hold it twice on the disk that runs out
/// first. So those rasters are read in place: the raster library only ever opens them read-only,
/// with its habit of writing a sidecar file beside what it reads switched off, and nothing of this
/// application writes, renames or deletes anything in the operator's directory. Nothing needs to:
/// what everything after the preparing step reads is the build's own prepared rasters.
/// </para>
/// <para>
/// What the build keeps instead is a list, beside its own folders: which directories it was given,
/// and every raster it found there with its length and the time it was last written, as they were
/// the first time it looked. That list is the record of what the build was made from, which a copy
/// used to be — and it is how a later run of the same build (run again, or handed back after a
/// restart) knows the directory is still the one it read. Each run looks again and compares; any
/// raster added, removed, or changed in length or time stops the build with
/// <see cref="TerrainBuildFailures.SourceChanged"/>, because carrying on would mesh ground nobody
/// chose or keep prepared rasters that no longer describe their source. Putting the directory back
/// lets it carry on; otherwise a new build reads the directory as it now is.
/// </para>
/// <para>
/// Every look resolves the directory again through the operator's list, the same way the request was
/// judged when it arrived, so a directory taken off the list, or replaced by a link to somewhere
/// else, is refused at the moment it would be read rather than trusted from hours before. Links
/// inside it are stepped over, and so are this application's own directories should one lie inside
/// it, because those are the places it does delete in.
/// </para>
/// </remarks>
public sealed class TerrainInPlaceSources(ServerDirectorySource directories, TerrainWorkspace workspace)
{
    /// <summary>The list's file name, inside the build's own folder.</summary>
    public const string RecordFileName = "in-place.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// What a build recorded the first time it read its directories, or null when it never has.
    /// </summary>
    public TerrainInPlaceRecord? Read(TerrainBuildDirectories build)
    {
        var path = Path.Combine(build.Root, RecordFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TerrainInPlaceRecord>(File.ReadAllText(path), Json);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable is not "never read": the build was made from something this cannot now say,
            // and guessing would be building something else under the same name.
            throw new TerrainBuildException(
                TerrainBuildFailures.SourceChanged,
                "This build's record of the server directories it reads cannot be read, so whether "
                + "they still hold what it was made from cannot be told. Start a new build.");
        }
    }

    /// <summary>
    /// Every raster the named directories hold right now, judged against the operator's list at
    /// this moment.
    /// </summary>
    /// <param name="references">The directories, as the build recorded them when it was asked for.</param>
    public IReadOnlyList<TerrainInPlaceRaster> Look(IReadOnlyList<string> references, CancellationToken ct)
    {
        var own = workspace.OwnDirectories();
        var seen = new Dictionary<string, TerrainInPlaceRaster>(PathComparer);

        foreach (var reference in references)
        {
            var (resolved, code) = directories.Resolve(reference);
            if (resolved is null)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.SourceUnreadable,
                    $"The directory this build was given cannot be read now ({code}).");
            }

            if (workspace.IsOwnDirectory(resolved))
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.SourceUnreadable,
                    "The directory this build was given lies inside the directories this application "
                    + "keeps its own terrain work in, which it deletes from; it is not read as a source.");
            }

            foreach (var raster in Rasters(resolved, own, ct))
            {
                seen.TryAdd(raster.Path, raster);
            }
        }

        return [.. seen.Values.OrderBy(r => r.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Writes down what a build is reading for the first time, giving each raster the name it will
    /// be prepared under.
    /// </summary>
    /// <param name="taken">
    /// The names the build's copied rasters are already prepared under, which these must not take.
    /// </param>
    public TerrainInPlaceRecord Record(
        TerrainBuildDirectories build,
        IReadOnlyList<string> references,
        IReadOnlyList<TerrainInPlaceRaster> seen,
        IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken.Select(TerrainRasterPreparation.PreparedNameFor), StringComparer.OrdinalIgnoreCase);
        var named = new List<TerrainInPlaceRaster>(seen.Count);

        foreach (var raster in seen)
        {
            named.Add(raster with { Name = Unused(Path.GetFileName(raster.Path), used) });
        }

        var record = new TerrainInPlaceRecord([.. references], named);
        var path = Path.Combine(build.Root, RecordFileName);
        var partial = path + TerrainRasterFiles.PartialSuffix;

        // Whole or absent: a list cut short would read as a directory that lost rasters.
        File.WriteAllText(partial, JsonSerializer.Serialize(record, Json));
        File.Move(partial, path, overwrite: true);
        return record;
    }

    /// <summary>
    /// What changed between the list a build recorded and what its directories hold now, in words,
    /// or null when nothing did.
    /// </summary>
    public static string? Difference(TerrainInPlaceRecord recorded, IReadOnlyList<string> references, IReadOnlyList<TerrainInPlaceRaster> seen)
    {
        if (!recorded.Directories.Order(StringComparer.Ordinal)
                .SequenceEqual(references.Order(StringComparer.Ordinal), PathComparer))
        {
            return "the directories it names are not the ones it recorded";
        }

        var before = recorded.Rasters.ToDictionary(r => r.Path, PathComparer);
        var now = seen.ToDictionary(r => r.Path, PathComparer);

        var added = now.Keys.Count(p => !before.ContainsKey(p));
        var removed = before.Keys.Count(p => !now.ContainsKey(p));
        var changed = now.Values.Count(r =>
            before.TryGetValue(r.Path, out var was) && (was.Length != r.Length || was.WrittenTicks != r.WrittenTicks));

        if (added == 0 && removed == 0 && changed == 0)
        {
            return null;
        }

        var parts = new List<string>(3);
        if (added > 0)
        {
            parts.Add($"{added} raster(s) added");
        }

        if (removed > 0)
        {
            parts.Add($"{removed} removed");
        }

        if (changed > 0)
        {
            parts.Add($"{changed} changed in size or time");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The rasters a build reads in place, once its directories have been looked at again and found
    /// to hold exactly what it recorded; none when it reads no directory.
    /// </summary>
    /// <remarks>
    /// Asked by the step that reads the rasters and by the step that asks whether they were all
    /// prepared, at the moment each asks — so the check against the operator's list and against the
    /// record is always the one nearest the read.
    /// </remarks>
    public IReadOnlyList<TerrainInPlaceRaster> Verified(TerrainBuildDirectories build, CancellationToken ct)
    {
        if (Read(build) is not { } recorded)
        {
            return [];
        }

        var seen = Look(recorded.Directories, ct);
        if (Difference(recorded, recorded.Directories, seen) is { } what)
        {
            throw Changed(what);
        }

        return recorded.Rasters;
    }

    /// <summary>The failure a changed directory stops a build with.</summary>
    public static TerrainBuildException Changed(string what) => new(
        TerrainBuildFailures.SourceChanged,
        $"A directory on the server this build reads in place no longer holds what it held when the "
        + $"build first read it ({what}). The build is not carried on over a different set of "
        + "rasters: put the directory back as it was and run it again, or start a new build.");

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>A name for a raster whose prepared form no other raster of the build has.</summary>
    private static string Unused(string name, HashSet<string> used)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var candidate = attempt == 0 ? name : $"{stem}-{attempt}{extension}";
            if (used.Add(TerrainRasterPreparation.PreparedNameFor(candidate)))
            {
                return candidate;
            }
        }

        throw new TerrainBuildException(
            TerrainBuildFailures.SourceUnreadable,
            $"Too many rasters named {name} were given to one build.");
    }

    /// <summary>
    /// Every raster under a resolved directory, with its length and time.
    /// </summary>
    /// <remarks>
    /// Links are stepped over, files and directories alike. Following one would re-open the
    /// question the operator's list has just answered, several levels down where nobody is looking,
    /// and a loop of them turns a bounded walk into an endless one. A folder that cannot be read is
    /// stepped over too: a permissions problem on one corner of an archive is a fact about that
    /// corner, not a reason to abandon everything else somebody asked for. This application's own
    /// directories are stepped over because what is in them is its own work, not the operator's.
    /// </remarks>
    private static IEnumerable<TerrainInPlaceRaster> Rasters(
        string resolvedRoot, IReadOnlyList<string> own, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(resolvedRoot);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(current).GetFileSystemInfos();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.LinkTarget is not null)
                {
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    if (!own.Any(o => TerrainWorkspace.Within(o, directory.FullName)))
                    {
                        pending.Push(directory.FullName);
                    }
                }
                else if (entry is FileInfo file && TerrainRasterFiles.IsRaster(file.Name))
                {
                    yield return new TerrainInPlaceRaster(
                        file.Name, file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
                }
            }
        }
    }
}

/// <summary>What a build recorded the first time it read its server directories.</summary>
/// <param name="Directories">The directories, as the build names them.</param>
/// <param name="Rasters">Every raster found in them, in a fixed order, with its prepared name.</param>
public sealed record TerrainInPlaceRecord(
    IReadOnlyList<string> Directories,
    IReadOnlyList<TerrainInPlaceRaster> Rasters);
