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
