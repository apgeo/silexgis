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
    /// <para>
    /// Containment is judged before existence, and a path outside the roots gets the same answer
    /// whether or not anything is there. Asking the disk first would turn this route into an
    /// oracle over the whole server's filesystem — "not found" for one path, "not allowed" for
    /// another — for anybody able to call it. Only inside an allowed root, whose contents the
    /// caller may see anyway, does "it is not there" become an answer of its own.
    /// </para>
    /// <para>
    /// Every link along the path is resolved, not only the last one, and containment is judged
    /// again on the real location: without that, a symbolic link placed anywhere inside an
    /// allowed directory — <c>root/link/sub</c> as much as <c>root/link</c> — is a way to read
    /// whatever it points at, which is precisely what the allow-list exists to stop. A link
    /// that leads anywhere but into a root, or towards one, is refused as outside before its
    /// target is looked at.
    /// </para>
    /// <para>
    /// The first judgement is made on the path as written, against the roots both as the
    /// operator configured them and as they really are, so a root that is itself a link still
    /// admits paths spelled through it. A path spelled through a link that lies OUTSIDE every
    /// root is refused even when it happens to lead into one: admitting it would mean reading
    /// the disk outside the roots to find out.
    /// </para>
    /// </remarks>
    public (string? Resolved, string? Code) Resolve(string? requestedPath)
    {
        var roots = Roots(out var configuredRoots);
        if (roots.Count == 0)
        {
            return (null, ServerImportPaths.NoRootsCode);
        }

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return (null, ServerImportPaths.NotFoundCode);
        }

        string written;
        try
        {
            written = Path.GetFullPath(requestedPath);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a path on any filesystem, so refusing it says nothing about this one.
            return (null, ServerImportPaths.OutsideRootsCode);
        }

        if (!ServerImportPaths.IsWithinRoots(roots, written)
            && !ServerImportPaths.IsWithinRoots(configuredRoots, written))
        {
            return (null, ServerImportPaths.OutsideRootsCode);
        }

        string? resolved;
        try
        {
            // A link is followed only when it leads into a root or towards one — a root's own
            // parent directory reached through a link the operator laid. One that leads anywhere
            // else is refused before anything past it is looked at.
            resolved = RealPath(
                written,
                target => ServerImportPaths.IsWithinRoots(roots, target)
                    || ServerImportPaths.IsWithinRoots(configuredRoots, target)
                    || roots.Concat(configuredRoots).Any(r => ServerImportPaths.IsWithinRoots([target], r)),
                out var escaped);
            if (escaped)
            {
                return (null, ServerImportPaths.OutsideRootsCode);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Inside a root, a part of the path the process cannot look at is "not there".
            return (null, ServerImportPaths.NotFoundCode);
        }

        if (resolved is null)
        {
            // A cycle of links, or a chain longer than any honest layout needs.
            return (null, ServerImportPaths.NotFoundCode);
        }

        if (!ServerImportPaths.IsWithinRoots(roots, resolved))
        {
            return (null, ServerImportPaths.OutsideRootsCode);
        }

        return Directory.Exists(resolved)
            ? (resolved, null)
            : (null, ServerImportPaths.NotFoundCode);
    }

    /// <summary>Whether this installation has the feature switched on at all.</summary>
    public bool IsConfigured => ResolvedRoots().Count > 0;

    /// <summary>The configured roots as absolute, link-resolved paths.</summary>
    public IReadOnlyList<string> Roots() => ResolvedRoots();

    /// <summary>
    /// The real location of an absolute path: every component that is a symbolic link replaced
    /// by what it points at, until no component is one. <c>null</c> when the links go round in a
    /// circle or run longer than <see cref="MaxLinkHops"/>.
    /// </summary>
    /// <remarks>
    /// The platform's own resolution only follows the LAST component, which leaves a link in
    /// the middle of a path standing — and a containment check run on a path with a link still
    /// in it is a check of the spelling, not of the location. Components that do not exist are
    /// kept as written; there is nothing left in them to follow.
    /// </remarks>
    internal static string? RealPath(string absolutePath) => RealPath(absolutePath, null, out _);

    /// <summary>
    /// <see cref="RealPath(string)"/>, stopping at the first link whose target
    /// <paramref name="mayFollow"/> refuses; <paramref name="escaped"/> then says so and the
    /// result is <c>null</c>.
    /// </summary>
    internal static string? RealPath(string absolutePath, Func<string, bool>? mayFollow, out bool escaped)
    {
        escaped = false;
        var pending = new List<string>(Components(absolutePath, out var current));
        var hops = 0;

        for (var i = 0; i < pending.Count; i++)
        {
            var next = Path.Combine(current, pending[i]);
            var target = new FileInfo(next).LinkTarget;
            if (target is null)
            {
                current = next;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                return null;
            }

            // A relative target is relative to the directory holding the link, which is already
            // real; the target's own components are resolved in their turn.
            var targetPath = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
            if (mayFollow is not null && !mayFollow(targetPath))
            {
                escaped = true;
                return null;
            }

            var rest = pending.Skip(i + 1).ToList();
            pending = [.. Components(targetPath, out current), .. rest];
            i = -1;
        }

        return current;
    }

    /// <summary>The longest chain of links <see cref="RealPath"/> follows, as the kernel does.</summary>
    internal const int MaxLinkHops = 40;

    private static string[] Components(string absolutePath, out string root)
    {
        root = Path.GetPathRoot(absolutePath) ?? string.Empty;
        return absolutePath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private List<string> Roots(out List<string> configured)
    {
        configured = [];
        foreach (var root in options.Value.ImportRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            try
            {
                configured.Add(Path.GetFullPath(root));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Not a path; it admits nothing.
            }
        }

        // Only roots that really exist admit anything, here as in the real-location check.
        var resolved = ResolvedRoots();
        configured = configured.Where(c => Directory.Exists(c)).ToList();
        return resolved;
    }

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
                // make a prefix comparison against a path that was never a directory. Resolved
                // the same way a requested path is, every component, or a root reached through
                // a linked parent directory would never contain anything asked for.
                if (info.Exists && RealPath(info.FullName) is { } real)
                {
                    resolved.Add(real);
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
