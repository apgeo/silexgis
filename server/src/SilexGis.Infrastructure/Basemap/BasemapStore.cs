// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Map;

namespace SilexGis.Infrastructure.Basemap;

/// <summary>What was cut into one extract, kept beside it.</summary>
public sealed record BasemapExtractRecord(
    string Build, DateTimeOffset ExtractedAt, BasemapRegion Region, int MaxZoom, long SizeBytes, Guid? RequestedBy);

/// <summary>The extract being served: its file, the address it is served under, and what was cut into it.</summary>
/// <param name="Record">What was cut into it, when it was cut by a download here; null for a file put there by hand.</param>
public sealed record BasemapFile(
    string FileName, string SourceId, string Build, DateTimeOffset ExtractedAt, long SizeBytes, BasemapExtractRecord? Record);

/// <summary>What a basemap download asks for: a box, the last level, and a build — null for the latest.</summary>
public sealed record BasemapExtractPayload(double West, double South, double East, double North, int MaxZoom, string? Build)
{
    public BasemapRegion Region => new(West, South, East, North);
}

/// <summary>
/// The directory the installation's own basemap is kept in, read for what is served now and written
/// by a download when it finishes.
/// </summary>
/// <remarks>
/// The tile server reads the same directory and serves every finished archive in it under its name,
/// picking a new one up as it appears and dropping one that goes. So a download is written under a
/// name the tile server does not read, takes its final name only when it is complete, and the
/// extracts before it are removed afterwards — the one being served is never rewritten.
/// </remarks>
public sealed class BasemapStore(IOptions<BasemapOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Whether this installation serves a basemap of its own at all.</summary>
    public bool SelfHosted => options.Value.SelfHosted;

    /// <summary>The directory, made absolute.</summary>
    public string Directory
    {
        get
        {
            var configured = options.Value.Directory;
            return Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured);
        }
    }

    /// <summary>The extract being served now, or null when there is none — whether or not self-hosting is on.</summary>
    public BasemapFile? Current()
    {
        var directory = Directory;
        if (!System.IO.Directory.Exists(directory))
        {
            return null;
        }

        var name = SelfHostedBasemap.Current(
            System.IO.Directory.EnumerateFiles(directory, SelfHostedBasemap.FilePrefix + "*" + SelfHostedBasemap.FileExtension)
                .Select(Path.GetFileName)
                .OfType<string>());
        if (name is null || SelfHostedBasemap.ParseFileName(name) is not { } parsed)
        {
            return null;
        }

        var info = new FileInfo(Path.Combine(directory, name));
        return info.Exists
            ? new BasemapFile(name, SelfHostedBasemap.SourceId(name), parsed.Build, parsed.ExtractedAt, info.Length, ReadRecord(directory, name))
            : null;
    }

    /// <summary>
    /// The name the current extract's tiles are served under, or null while there is nothing to
    /// serve — self-hosting switched off, or no extract yet. What publishing an entry asks.
    /// </summary>
    public string? CurrentSourceId() => SelfHosted ? Current()?.SourceId : null;

    /// <summary>Where a download of the given final name is written while it is being made.</summary>
    public string PartialPath(string fileName)
    {
        System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, SelfHostedBasemap.SourceId(fileName) + SelfHostedBasemap.PartialExtension);
    }

    /// <summary>The free space on the disk holding the directory, in bytes, or null when it cannot be told.</summary>
    public long? FreeBytes()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            return new DriveInfo(Directory).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes a finished download the extract being served: its record written beside it, its final
    /// name given — the moment the tile server picks it up — and every earlier extract removed.
    /// </summary>
    public void Publish(string partialPath, string fileName, BasemapExtractRecord record)
    {
        var directory = Directory;
        var finalPath = Path.Combine(directory, fileName);
        File.WriteAllText(RecordPath(directory, fileName), JsonSerializer.Serialize(record, Json));
        File.Move(partialPath, finalPath, overwrite: false);

        foreach (var path in System.IO.Directory.EnumerateFiles(directory, SelfHostedBasemap.FilePrefix + "*"))
        {
            var name = Path.GetFileName(path);
            if (name == fileName || name == Path.GetFileName(RecordPath(directory, fileName)))
            {
                continue;
            }

            TryDelete(path);
        }
    }

    /// <summary>Removes what a download that did not finish left behind.</summary>
    public static void Discard(string? partialPath)
    {
        if (partialPath is not null)
        {
            TryDelete(partialPath);
        }
    }

    private static string RecordPath(string directory, string fileName) =>
        Path.Combine(directory, SelfHostedBasemap.SourceId(fileName) + ".json");

    private static BasemapExtractRecord? ReadRecord(string directory, string fileName)
    {
        try
        {
            var path = RecordPath(directory, fileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<BasemapExtractRecord>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the next download to remove; a stray file is served by nobody.
        }
    }
}

/// <summary>
/// The basemap downloads running in this process, by job, so that one can be called off. The
/// downloads run in this process's own job worker, one at a time, so this is all the coordination
/// calling one off needs.
/// </summary>
public sealed class BasemapCancellation
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> running = new();

    /// <summary>The token a running download is called off by, held until <see cref="Forget"/>.</summary>
    public CancellationToken Register(long jobId) => running.GetOrAdd(jobId, _ => new CancellationTokenSource()).Token;

    /// <summary>Calls a running download off; false when no download of that job is running here.</summary>
    public bool Cancel(long jobId)
    {
        if (!running.TryGetValue(jobId, out var source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    /// <summary>Whether a download was called off.</summary>
    public bool IsCancelled(long jobId) => running.TryGetValue(jobId, out var source) && source.IsCancellationRequested;

    /// <summary>Drops a download that has ended.</summary>
    public void Forget(long jobId)
    {
        if (running.TryRemove(jobId, out var source))
        {
            source.Dispose();
        }
    }
}
