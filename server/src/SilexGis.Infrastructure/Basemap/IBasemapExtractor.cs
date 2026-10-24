// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Map;

namespace SilexGis.Infrastructure.Basemap;

/// <summary>Cuts a region out of a planet build into one archive of its own.</summary>
/// <remarks>
/// An interface because the real one is a program fetching from the internet, and nothing that
/// tests the download page may reach it: tests answer with a fake that writes a few bytes.
/// </remarks>
public interface IBasemapExtractor
{
    /// <summary>The size in bytes the archive for a region would have, worked out without downloading it.</summary>
    Task<long> EstimateAsync(string source, BasemapRegion region, int maxZoom, CancellationToken ct);

    /// <summary>
    /// Writes the archive for a region to <paramref name="outputPath"/>, telling
    /// <paramref name="progress"/> how far it has got in percent as it goes.
    /// </summary>
    Task ExtractAsync(
        string source, BasemapRegion region, int maxZoom, string outputPath, Func<int, Task> progress, CancellationToken ct);
}

/// <summary>A basemap download that could not be done, with its stable code.</summary>
public sealed class BasemapException(string code, string message) : Exception($"{code}: {message}")
{
    public string Code { get; } = code;
}
