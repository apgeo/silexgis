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
