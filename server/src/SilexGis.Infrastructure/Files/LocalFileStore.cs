// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Files;

public sealed class FilesOptions
{
    public const string SectionName = "Files";

    /// <summary>
    /// File store root. Relative values resolve against the application base
    /// directory; deployments mount a volume and point this at it.
    /// </summary>
    public string Root { get; set; } = Path.Combine("data", "files");

    /// <summary>
    /// Largest upload accepted, in bytes. The default is sized for the material a club
    /// archive actually holds — long-form scans and field video — rather than for photos
    /// alone.
    /// </summary>
    /// <remarks>
    /// Three independent limits govern one upload and raising this alone changes nothing:
    /// the request body limit and the multipart limit the web server applies before any
    /// application code runs, and the body-size cap of the reverse proxy in front of it.
    /// The first two are derived from this value at startup; the proxy's is part of the
    /// deployment configuration and has to allow at least as much, or an oversized upload
    /// fails at the proxy with an error the application never sees and cannot explain.
    /// </remarks>
    public long MaxUploadBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// Request-body ceiling for the upload routes: the file limit plus room for the
    /// multipart envelope around it.
    /// </summary>
    /// <remarks>
    /// The transport limit has to sit above the file limit, not on it. A multipart request
    /// carries boundaries, part headers and a file name in addition to the bytes, so a file
    /// of exactly <see cref="MaxUploadBytes"/> makes a request slightly larger than that —
    /// and a transport limit set to the same number would cut it off with a bare 413 that
    /// says nothing, instead of letting the upload reach the check that can name the limit
    /// it exceeded. The allowance is generous because it costs nothing: the file size is
    /// still checked exactly.
    /// </remarks>
    public long MaxRequestBodyBytes => MaxUploadBytes + MultipartEnvelopeBytes;

    /// <summary>Room a multipart request needs around the file itself.</summary>
    /// <remarks>
    /// Public because the multipart reader's ceiling is process-wide rather than per-route,
    /// so whoever sets it has to add the same allowance to whichever upload limit is
    /// largest, not only to this one.
    /// </remarks>
    public const long MultipartEnvelopeBytes = 1024 * 1024;

    /// <summary>
    /// How much stored content one person may own by default, in bytes. Zero — the default —
    /// means no personal limit, which is what a club installation wants: the people using it
    /// are known to each other, and a quota that has to be raised by hand the first time
    /// somebody scans a survey is a support request rather than a protection.
    /// </summary>
    /// <remarks>
    /// An account may be given its own figure, which overrides this. Neither is an allocation:
    /// nothing is reserved, and the sum of everybody's quotas may exceed the disk. What
    /// actually protects the disk is <see cref="MaxTotalStoreBytes"/>.
    /// </remarks>
    public long DefaultUserQuotaBytes { get; set; }

    /// <summary>
    /// How much the whole installation may hold, in bytes. Zero — the default — means no
    /// limit, and an operator who mounts a volume of a known size is the one who knows what
    /// to put here.
    /// </summary>
    public long MaxTotalStoreBytes { get; set; }

    /// <summary>
    /// Extensions this installation accepts, empty for all of them. Empty is the default and
    /// the right one for an archive whose purpose is holding whatever a caving club has
    /// accumulated — including formats nobody thought to list.
    /// </summary>
    public IList<string> AcceptedExtensions { get; set; } = [];

    /// <summary>
    /// Extensions this installation refuses whatever <see cref="AcceptedExtensions"/> says.
    /// Empty by default: nothing is refused unless an operator says so.
    /// </summary>
    public IList<string> RefusedExtensions { get; set; } = [];

    /// <summary>
    /// Directories on the server's own disk that an administrator may import from. Empty —
    /// the default — switches the feature off entirely.
    /// </summary>
    /// <remarks>
    /// This is the one setting here that is a security boundary rather than a capacity one.
    /// Importing from the filesystem reads whatever the service account can open, so the
    /// directories it may reach are named by whoever deploys the installation rather than
    /// chosen by whoever is signed in — an administrator account is not the same thing as the
    /// operator who owns the machine, and a compromised one must not become a way to read the
    /// database's data directory.
    /// </remarks>
    public IList<string> ImportRoots { get; set; } = [];

    /// <summary>How many entries an uploaded archive may hold before it is refused.</summary>
    public int MaxArchiveEntries { get; set; } = 5000;

    /// <summary>How large an uploaded archive may expand to, in bytes.</summary>
    public long MaxArchiveExpandedBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    /// <summary>How much larger than its compressed form one archive entry may expand.</summary>
    public int MaxArchiveCompressionRatio { get; set; } = 200;
}

/// <summary>
/// Local-disk file store. Blobs live under root/yyyy/MM/{uuid}{ext} — the name is
/// server-generated, so storage paths never contain user input. Storage paths use
/// forward slashes for cross-platform stability of persisted values.
/// </summary>
public sealed class LocalFileStore(IOptions<FilesOptions> options) : IFileStore
{
    private readonly string root = Path.GetFullPath(options.Value.Root, AppContext.BaseDirectory);

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var safeExtension = SanitizeExtension(extension);
        var relative = $"{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}{safeExtension}";

        var absolute = GetAbsolutePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await using var target = File.Create(absolute);
        await content.CopyToAsync(target, ct);
        return relative;
    }

    public async Task<long> AppendAsync(string storagePath, Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var absolute = GetAbsolutePath(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        // Opened for append with no sharing: two pieces of one upload arriving at once would
        // otherwise interleave into a file neither of them describes. The second request
        // fails to open, is answered as a conflict, and the client resends from the length
        // the first one actually reached.
        await using (var target = new FileStream(
            absolute, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(target, ct);

            // Flushed through to the device before the length is read and recorded. The
            // recorded length is what a resuming client is told to continue from, so it has
            // to describe bytes that survive the process dying, not bytes still in a buffer.
            await target.FlushAsync(ct);
        }

        return new FileInfo(absolute).Length;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(GetAbsolutePath(storagePath)));

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var absolute = GetAbsolutePath(storagePath);
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }

        return Task.CompletedTask;
    }

    public string GetAbsolutePath(string storagePath)
    {
        var combined = Path.GetFullPath(Path.Combine(root, storagePath));
        // Defense in depth: paths are server-generated, but never allow escaping the root.
        return combined.StartsWith(root, StringComparison.Ordinal)
            ? combined
            : throw new InvalidOperationException("Storage path escapes the file store root.");
    }

    /// <summary>Keeps only a plain ".ext" suffix; anything suspicious is dropped.</summary>
    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.StartsWith('.') ? extension : $".{extension}";
        return trimmed.Length <= 10 && trimmed.Skip(1).All(char.IsLetterOrDigit)
            ? trimmed.ToLowerInvariant()
            : string.Empty;
    }
}
