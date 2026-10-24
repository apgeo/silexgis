// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Map;

namespace SilexGis.Infrastructure.Basemap;

/// <summary>One planet build a source publishes: its date, its size, and the schema its tiles follow.</summary>
public sealed record BasemapBuild(string Build, long SizeBytes, DateTimeOffset? Uploaded, string? Version);

/// <summary>The planet builds the configured source publishes.</summary>
public interface IBasemapBuilds
{
    /// <summary>The builds, newest first. Throws a <see cref="BasemapException"/> when the list cannot be read.</summary>
    Task<IReadOnlyList<BasemapBuild>> ListAsync(CancellationToken ct);
}

/// <summary>
/// Reads the list of daily planet builds Protomaps publishes beside them: one entry per build,
/// named by its file (<c>20261024.pmtiles</c>), with its size and the version of the schema.
/// </summary>
public sealed class ProtomapsBuildList(IHttpClientFactory http, IOptions<BasemapOptions> options) : IBasemapBuilds
{
    public const string HttpClientName = "basemap-builds";

    private sealed record Entry(string? Key, long? Size, DateTimeOffset? Uploaded, string? Version);

    public async Task<IReadOnlyList<BasemapBuild>> ListAsync(CancellationToken ct)
    {
        List<Entry>? entries;
        try
        {
            using var client = http.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(20);
            entries = await client.GetFromJsonAsync<List<Entry>>(
                options.Value.BuildsUrl, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException
            && !ct.IsCancellationRequested)
        {
            throw new BasemapException(SelfHostedBasemap.Codes.SourceUnreachable, $"the list of builds could not be read: {e.Message}");
        }

        return [.. (entries ?? [])
            .Select(e => (Entry: e, Build: e.Key?.EndsWith(SelfHostedBasemap.FileExtension, StringComparison.Ordinal) == true
                ? e.Key[..^SelfHostedBasemap.FileExtension.Length]
                : null))
            .Where(e => SelfHostedBasemap.IsBuild(e.Build))
            .Select(e => new BasemapBuild(e.Build!, e.Entry.Size ?? 0, e.Entry.Uploaded, e.Entry.Version))
            .OrderByDescending(b => b.Build, StringComparer.Ordinal)];
    }
}
