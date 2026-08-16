// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Terrain;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// One cell of elevation, obtained and put on disk.
/// </summary>
/// <remarks>
/// <para>
/// A port of what the command-line tool already does, behaviour for behaviour, because this is the
/// one part of the chain that has been run against the real bucket over real ground and every one
/// of its rules was learned there rather than reasoned out.
/// </para>
/// <para>
/// The transfer lands under a temporary name and is moved onto the final one only after it has
/// been checked against the length the server declared; anything that goes wrong takes the
/// temporary file with it. That is the whole difference between "obtaining a country's worth of
/// elevation again costs nothing" and "obtaining it again silently accepts whatever was on disk":
/// bytes sitting at the name a finished file would have are indistinguishable from a finished
/// file, so a fragment must never be able to occupy that name. A truncated raster is not a visible
/// failure either — it bakes into terrain with a hole in it, and a hole is drawn as smooth ground.
/// </para>
/// </remarks>
public sealed class CopernicusFetcher(
    IHttpClientFactory httpClientFactory,
    IOptions<TerrainBuildOptions> options)
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "terrain-elevation";

    /// <summary>What a transfer still in flight is called.</summary>
    public const string PartialSuffix = TerrainRasterFiles.PartialSuffix;

    /// <summary>
    /// Obtains one named cell into <paramref name="target"/>, answering the number of bytes
    /// written — or <c>null</c> when the cell is not published at all.
    /// </summary>
    /// <remarks>
    /// <b>Not published is an ordinary answer, not a failure.</b> Cells that are entirely ocean are
    /// never published, so any rectangle drawn around a coast asks for some that are not there. A
    /// fetch that treated a missing cell as an error would refuse every coastal area on Earth, and
    /// it is the single easiest rule here to forget because it only ever shows up at the sea.
    /// </remarks>
    public async Task<long?> DownloadAsync(string cell, string target, CancellationToken ct)
    {
        var url = CopernicusCoverage.Url(cell);
        var partial = target + PartialSuffix;

        var client = httpClientFactory.CreateClient(HttpClientName);

        // Bounded so a stalled connection cannot hold a build open for ever, and generous because
        // what is being read is tens of megabytes over whatever link the server happens to have.
        // The ceiling covers reading the body as well as answering the headers.
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.CellTimeoutSeconds, 30, 7200));

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.FetchFailed,
                $"Obtaining the elevation cell {cell} failed: the server answered {(int)response.StatusCode}.");
        }

        var declared = response.Content.Headers.ContentLength;
        try
        {
            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(partial))
            {
                await body.CopyToAsync(file, ct);
            }

            var written = new FileInfo(partial).Length;

            // A body that ends early usually throws above, but not always: a proxy can close a
            // connection cleanly on a byte boundary and nothing about that reads as an error. The
            // length the server declared is right there in the headers, so it is checked.
            if (declared is > 0 && written != declared)
            {
                throw new TerrainBuildException(
                    TerrainBuildFailures.FetchFailed,
                    $"Obtaining the elevation cell {cell} stopped short: got {written} bytes of {declared}.");
            }

            File.Move(partial, target, overwrite: true);
            return written;
        }
        catch
        {
            // Every way out that is not the move above — a dropped link, a full disk, a shutdown,
            // a length that did not match — takes the fragment with it, so the next attempt starts
            // this cell again rather than trusting what is lying there.
            Discard(partial);
            throw;
        }
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Removing the fragment is a courtesy on a path that is already failing, and the
            // failure being reported matters more than the leftover: the name it occupies is not
            // one anything will ever mistake for a finished cell.
        }
    }
}
