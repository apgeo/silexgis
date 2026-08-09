// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using SilexGis.Domain.Documents;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// Reads a directory on the server's own disk as a bulk source, having first established that
/// it is one the operator allowed this installation to read.
///
/// <para>
/// The point of the feature is bulk that never crosses the network: ten gigabytes of scans
/// already sitting on the machine, or on a share mounted into it, filed without anybody
/// uploading them twice. The point of the guard is that this is the one request in the
/// application that names a location the server reads directly, so a mistake here hands over
/// whatever the service account can open.
/// </para>
/// <para>
/// Resolution happens twice and must: once when the request is made, so the caller gets a
/// straight refusal rather than a queued job that fails silently, and once inside the walk,
/// because the disk can change between the two and the check that matters is the one nearest
/// the read.
/// </para>
/// </summary>
public sealed class ServerDirectorySource(IOptions<FilesOptions> options)
{
    /// <summary>
    /// The real location of <paramref name="requestedPath"/>, or the code saying why it may
    /// not be read.
    /// </summary>
    /// <remarks>
    /// Links are resolved before containment is judged, and that is the whole of the check
    /// being worth anything: without it, a symbolic link placed inside an allowed directory is
    /// a way to read whatever it points at, which is precisely what the allow-list exists to
    /// stop.
    /// </remarks>
    public (string? Resolved, string? Code) Resolve(string? requestedPath)
    {
        var roots = ResolvedRoots();
        if (roots.Count == 0)
        {
            return (null, ServerImportPaths.NoRootsCode);
        }

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return (null, ServerImportPaths.NotFoundCode);
        }

        string resolved;
        try
        {
            var info = new DirectoryInfo(Path.GetFullPath(requestedPath));
            if (!info.Exists)
            {
                return (null, ServerImportPaths.NotFoundCode);
            }

            // LinkTarget is null for an ordinary directory and the real path for a link.
            resolved = Path.GetFullPath(
                info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A path the process cannot even look at is answered as "not there", the same as
            // one that is not: the difference between the two is itself information about the
            // server's filesystem.
            return (null, ServerImportPaths.NotFoundCode);
        }

        return ServerImportPaths.IsWithinRoots(roots, resolved)
            ? (resolved, null)
            : (null, ServerImportPaths.OutsideRootsCode);
    }

    /// <summary>Whether this installation has the feature switched on at all.</summary>
    public bool IsConfigured => ResolvedRoots().Count > 0;

    /// <summary>The configured roots as absolute, link-resolved paths.</summary>
    public IReadOnlyList<string> Roots() => ResolvedRoots();

    /// <summary>
    /// Every file under <paramref name="resolvedRoot"/>, named by its path relative to that
    /// root so the folder structure can be mirrored as cabinets.
    /// </summary>
    /// <remarks>
    /// Links are skipped rather than followed — both files and directories. Following them
    /// would re-open the question the allow-list just answered, one entry at a time and
    /// several levels down where nobody is looking; and a link loop turns a bounded walk into
    /// an endless one. An installation that genuinely wants a linked directory imported can
    /// name it as a root, which is a decision the operator makes rather than one the
    /// filesystem makes for them.
    /// </remarks>
    public async IAsyncEnumerable<BulkSourceFile> ReadAsync(
        string resolvedRoot,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(resolvedRoot);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            // A directory that has become unreadable mid-walk is stepped over rather than
            // ending the import: a permissions problem on one folder of an archive is a fact
            // about that folder.
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(directory).GetFileSystemInfos();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.LinkTarget is not null)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    pending.Push(child.FullName);
                    continue;
                }

                if (entry is not FileInfo file)
                {
                    continue;
                }

                // Relative to the root that was allowed, which is what makes the mirrored
                // cabinet tree describe the archive rather than the server's disk layout.
                var relative = Path.GetRelativePath(resolvedRoot, file.FullName);
                var captured = file.FullName;
                yield return new BulkSourceFile(
                    relative,
                    file.Length,
                    _ => Task.FromResult<Stream>(File.OpenRead(captured)));
            }

            // Yields nothing but keeps the method genuinely asynchronous, so a very large
            // directory does not hold the thread while it is enumerated.
            await Task.Yield();
        }
    }

    private List<string> ResolvedRoots()
    {
        var resolved = new List<string>();
        foreach (var configured in options.Value.ImportRoots)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                continue;
            }

            try
            {
                var info = new DirectoryInfo(Path.GetFullPath(configured));

                // A root that does not exist is dropped rather than kept as a string: an
                // allow-list entry that names nothing admits nothing, and keeping it would
                // make a prefix comparison against a path that was never a directory.
                if (info.Exists)
                {
                    resolved.Add(Path.GetFullPath(
                        info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // An unreadable root is no root.
            }
        }

        return resolved;
    }
}
