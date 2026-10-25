// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Formats.Tar;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>A tar stream between the application and the compiler service that cannot be read as asked.</summary>
public sealed class SurveyProjectTarException(string message, bool tooLarge = false) : Exception(message)
{
    /// <summary>Whether the stream was refused for its size rather than for its shape.</summary>
    public bool TooLarge { get; } = tooLarge;
}

/// <summary>What was read out of a tar stream.</summary>
/// <param name="Files">The files written, relative to the destination, with forward slashes, in stream order.</param>
/// <param name="Manifest">The bytes of the manifest entry, or null when the stream had none.</param>
public sealed record SurveyProjectTarReading(IReadOnlyList<string> Files, byte[]? Manifest);

/// <summary>
/// Packs a compile project into a tar stream and unpacks one, the way a project travels to the
/// compiler service and its outputs travel back.
/// </summary>
/// <remarks>
/// Both directions read a stream the other side wrote, and either side may be the one that went
/// wrong: the service unpacks what a field device uploaded, and the application unpacks what a
/// program that ran over that upload answered. So unpacking takes ordinary files only — no links,
/// devices or anything else a tar can describe — each at a relative path that stays inside the
/// destination, never over a file already there, within a byte and an entry budget. Packing takes
/// ordinary files only, too, so a link in a project directory is never followed to what it names.
/// </remarks>
public static class SurveyProjectTar
{
    /// <summary>The media type both directions are sent as.</summary>
    public const string MediaType = "application/x-tar";

    /// <summary>The most a manifest entry may be; it is a short JSON document.</summary>
    private const int MaxManifestBytes = 1024 * 1024;

    /// <summary>
    /// The ordinary files under <paramref name="root"/>, relative to it with forward slashes, leaving
    /// out any directory named <paramref name="skipDirectory"/> and anything that is a link.
    /// </summary>
    public static IReadOnlyList<string> FilesUnder(string root, string? skipDirectory = null)
    {
        var full = Path.GetFullPath(root);
        var found = new List<string>();
        Walk(new DirectoryInfo(full));
        found.Sort(StringComparer.Ordinal);
        return found;

        void Walk(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (skipDirectory is null || child.Name != skipDirectory || directory.FullName != full)
                    {
                        Walk(child);
                    }
                }
                else if (entry is FileInfo)
                {
                    found.Add(Path.GetRelativePath(full, entry.FullName).Replace('\\', '/'));
                }
            }
        }
    }

    /// <summary>
    /// Writes a tar of <paramref name="files"/> (relative to <paramref name="root"/>) to
    /// <paramref name="destination"/>, with <paramref name="manifest"/> first under
    /// <paramref name="manifestName"/> when one is given. A listed directory is written as the
    /// ordinary files under it; a listed path that is missing or a link is left out.
    /// </summary>
    public static async Task WriteAsync(
        Stream destination, string root, IEnumerable<string> files, CancellationToken ct,
        string? manifestName = null, byte[]? manifest = null)
    {
        var full = Path.GetFullPath(root);
        await using var writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);

        if (manifestName is not null && manifest is not null)
        {
            using var body = new MemoryStream(manifest, writable: false);
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, manifestName) { DataStream = body }, ct);
        }

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var name = SafeName(file) ?? throw new SurveyProjectTarException($"'{file}' is not a path inside the project.");
            var path = Path.Combine(full, name);
            var entries = Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null
                ? FilesUnder(path).Select(f => name + "/" + f)
                : [name];

            foreach (var entry in entries)
            {
                var source = new FileInfo(Path.Combine(full, entry));
                if (!source.Exists || source.LinkTarget is not null || !written.Add(entry))
                {
                    continue;
                }

                await using var body = source.OpenRead();
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, entry) { DataStream = body }, ct);
            }
        }
    }

    /// <summary>
    /// Unpacks the ordinary files of a tar stream into <paramref name="destination"/>, refusing the
    /// whole stream at the first entry that is not an ordinary file or directory, names a path
    /// outside the destination or one already there, or passes either budget. The entry named
    /// <paramref name="manifestName"/>, when given, is handed back instead of written.
    /// </summary>
    public static async Task<SurveyProjectTarReading> ExtractAsync(
        Stream source, string destination, long maxBytes, int maxEntries, CancellationToken ct,
        string? manifestName = null)
    {
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);

        var files = new List<string>();
        byte[]? manifest = null;
        long total = 0;
        var count = 0;

        await using var reader = new TarReader(source, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, ct) is { } entry)
        {
            if (++count > maxEntries)
            {
                throw new SurveyProjectTarException($"More than {maxEntries} entries.", tooLarge: true);
            }

            if (entry.EntryType is TarEntryType.Directory)
            {
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                throw new SurveyProjectTarException($"'{entry.Name}' is not an ordinary file.");
            }

            var name = SafeName(entry.Name) ?? throw new SurveyProjectTarException($"'{entry.Name}' is not a path inside the project.");

            total += entry.Length;
            if (total > maxBytes)
            {
                throw new SurveyProjectTarException($"More than {maxBytes} bytes.", tooLarge: true);
            }

            if (manifestName is not null && name == manifestName && manifest is null)
            {
                if (entry.Length > MaxManifestBytes)
                {
                    throw new SurveyProjectTarException("The manifest is too large.", tooLarge: true);
                }

                using var buffer = new MemoryStream();
                if (entry.DataStream is { } data)
                {
                    await data.CopyToAsync(buffer, ct);
                }

                manifest = buffer.ToArray();
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new SurveyProjectTarException($"'{entry.Name}' is not a path inside the project.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                if (entry.DataStream is { } data)
                {
                    await data.CopyToAsync(target, ct);
                }
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                throw new SurveyProjectTarException($"'{entry.Name}' is already there.");
            }

            files.Add(name);
        }

        return new SurveyProjectTarReading(files, manifest);
    }

    /// <summary>
    /// The path as a relative one with forward slashes, or null when it is rooted, empty, or steps
    /// outside with a <c>..</c> part.
    /// </summary>
    public static string? SafeName(string name)
    {
        var path = name.Replace('\\', '/');
        if (path.Length == 0 || path.StartsWith('/') || Path.IsPathRooted(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToList();
        if (parts.Count == 0 || parts.Any(p => p == ".." || p.Contains(':', StringComparison.Ordinal)))
        {
            return null;
        }

        return string.Join('/', parts);
    }
}
