// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// Opens and creates files in the hand-off directory without trusting what the worker left there.
/// </summary>
/// <remarks>
/// <para>
/// The worker runs tools over files somebody uploaded, and it can write anything on the volume it
/// shares with the application. A symbolic link it leaves in a job directory names a path that the
/// application resolves in its own container: a product linked to the application's key store
/// would be stored as a scan's file and served to whoever may see the scan, and a link where the
/// application is about to write would have it write somewhere of the worker's choosing. So every
/// file the application reads from a job is opened only when neither it nor the directories above
/// it up to the jobs folder is a link, and every file it writes is created new, failing on anything
/// already there instead of following it.
/// </para>
/// <para>
/// The checks are made on the path and, where the platform tells where an open handle really
/// points, again on the handle once it is open, so a link swapped in between the look and the open
/// is caught too. Reads are bounded: a result or a log the worker made enormous costs the
/// application no more than the bound.
/// </para>
/// </remarks>
internal static class ScanHandoffFiles
{
    /// <summary>
    /// Throws unless <paramref name="directory"/> and its parent (the jobs folder) are real
    /// directories, not links.
    /// </summary>
    public static void RequireRealDirectory(string directory)
    {
        foreach (var path in new[] { Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory)), directory })
        {
            if (path is null)
            {
                continue;
            }

            var info = new DirectoryInfo(path);
            if (info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new ScanHandoffTamperedException($"The hand-off directory {path} is a link, not a directory.");
            }
        }
    }

    /// <summary>
    /// Creates a file in a job directory for the application to write, refusing when anything at
    /// all is already at that name — a link included, which is not followed.
    /// </summary>
    public static FileStream CreateNew(string directory, string fileName)
    {
        RequireRealDirectory(directory);
        var path = Path.Combine(directory, fileName);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException e) when (File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            throw new ScanHandoffTamperedException($"Something is already at {path} in the hand-off directory.", e);
        }

        try
        {
            RequireOpenedInside(stream, directory);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a file the worker wrote, for reading: only a plain file that is not a link, in a job
    /// directory that is not one either. Null when there is no such file; throws when there is
    /// something else at the name.
    /// </summary>
    /// <param name="requireContent">Refuse an empty file too: every product holds something, and a pipe or a device reads as empty.</param>
    public static FileStream? OpenPlain(string directory, string fileName, bool requireContent = false)
    {
        RequireRealDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.Exists)
        {
            throw new ScanHandoffTamperedException($"{fileName} in the hand-off directory is a link, not a file.");
        }

        if (!info.Exists)
        {
            return Directory.Exists(path)
                ? throw new ScanHandoffTamperedException($"{fileName} in the hand-off directory is a directory, not a file.")
                : null;
        }

        if (requireContent && info.Length == 0)
        {
            throw new ScanHandoffTamperedException($"{fileName} in the hand-off directory is empty or not a plain file.");
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            RequireOpenedInside(stream, directory);
            if (!stream.CanSeek)
            {
                throw new ScanHandoffTamperedException($"{fileName} in the hand-off directory is not a plain file.");
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads a file the worker wrote, at most <paramref name="maxBytes"/> of it; null when there is
    /// no file, it is not a plain file, or it is larger than the bound. Never throws for what the
    /// worker left: these are files read while polling, and an unreadable one is the same as none.
    /// </summary>
    public static byte[]? ReadBounded(string directory, string fileName, int maxBytes)
    {
        try
        {
            using var stream = OpenPlain(directory, fileName);
            if (stream is null || stream.Length > maxBytes)
            {
                return null;
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ScanHandoffTamperedException)
        {
            return null;
        }
    }

    /// <summary>The last lines of a text file the worker wrote, read from at most its last <paramref name="maxBytes"/>.</summary>
    public static IReadOnlyList<string> TailLines(string directory, string fileName, int maxBytes, int lines)
    {
        try
        {
            using var stream = OpenPlain(directory, fileName);
            if (stream is null)
            {
                return [];
            }

            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Position = start;
            var bytes = new byte[stream.Length - start];
            stream.ReadExactly(bytes);
            var text = Encoding.UTF8.GetString(bytes);
            var all = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (start > 0 && all.Count > 0)
            {
                // The first line read was cut somewhere inside.
                all.RemoveAt(0);
            }

            if (all.Count > 0 && all[^1].Length == 0)
            {
                all.RemoveAt(all.Count - 1);
            }

            return all.TakeLast(lines).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ScanHandoffTamperedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Where the platform says where an open handle really is (Linux, through <c>/proc/self/fd</c>),
    /// throws unless it is directly inside <paramref name="directory"/> as that resolves now.
    /// </summary>
    private static void RequireOpenedInside(FileStream stream, string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var descriptor = stream.SafeFileHandle.DangerousGetHandle().ToInt64();
        var opened = new FileInfo($"/proc/self/fd/{descriptor}").LinkTarget;
        if (opened is null)
        {
            return;
        }

        if (!string.Equals(Path.GetDirectoryName(opened), RealPath(directory), StringComparison.Ordinal))
        {
            throw new ScanHandoffTamperedException($"A file opened in the hand-off directory {directory} turned out to be somewhere else.");
        }
    }

    /// <summary>The directory's path with every link above it resolved, as the kernel reports open handles.</summary>
    private static string RealPath(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var parts = full.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = Path.DirectorySeparatorChar.ToString();
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }

        return current;
    }
}

/// <summary>Something in the hand-off directory is not what the application left or the worker should have.</summary>
public sealed class ScanHandoffTamperedException : IOException
{
    public ScanHandoffTamperedException(string message)
        : base(message)
    {
    }

    public ScanHandoffTamperedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
