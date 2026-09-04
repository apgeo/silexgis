// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// This installation's only way of talking to a neighbouring Immich.
///
/// <para>
/// <b>It is a singleton because it holds the positions, not because it rations requests.</b> This
/// library publishes no way to ask what is inside a rectangle. The one route it has that takes one
/// is marked by its own authors as internal — not part of the contract offered to anybody outside
/// the product — and is keyed by calendar month, so a viewport spanning ten years of caving is
/// about a hundred and twenty requests per pan against an interface that carries no promise it will
/// still be there. The route that <em>is</em> part of the contract answers with the whole located
/// library and applies no limit of any kind. So the whole library is read once, kept in memory as
/// three fields per photograph, and every viewport after that is answered from memory with no
/// network at all. A second instance of this object would be a second copy of that memory and a
/// second whole-library read, which is why there is exactly one.
/// </para>
/// <para>
/// <b>There is deliberately no minimum interval between calls and no serialising gate.</b> The
/// service on the other end is a container in this installation's own deployment, run by the same
/// operator, sharing this machine's memory and processors. Spacing requests to it would slow this
/// application's map down in order to be polite to a neighbour that has no capacity of its own to
/// protect. The one permit held here exists so that ten viewports arriving together cause one
/// whole-library read rather than ten — de-duplication, not rationing.
/// </para>
/// <para>
/// <b>It clamps what it will accept.</b> The far end enforces no ceiling on the whole-library
/// answer, so the configured one is the only ceiling there is; a library larger than it is refused
/// with a code that says so rather than truncated into a map that is quietly missing places.
/// </para>
/// <para>
/// The credential is a key issued by the library's own settings screen, and that key <em>is</em>
/// the account it was minted for: it carries that account's whole reach, cannot be narrowed, does
/// not expire, and cannot be replaced in place through any operation this version publishes. What
/// is shared to that account is the whole of what this application can see.
/// </para>
/// </summary>
public sealed class ImmichClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ImmichOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<ImmichClient> logger) : IPhotoLibrary
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "photo-library-immich";

    /// <summary>The header the key travels on. Never a query parameter: this library accepts one there too, and a whole-library credential in an address is a credential in browser history, in a referer and in every log between here and there.</summary>
    private const string ApiKeyHeader = "x-api-key";

    /// <summary>
    /// The renderings asked for. Both are shipped defaults of the product rather than facts about
    /// every installation of it — the sizes behind these names are administrator-configurable at
    /// the far end — so they are names, not pixel counts, and nothing here assumes a dimension.
    /// </summary>
    private const string SmallRendering = "thumbnail";
    private const string LargeRendering = "preview";

    /// <summary>
    /// The most located photographs this build will hold however it is configured. Thirty-two bytes
    /// each, so this is thirty-two megabytes of long-lived array; a deployment needing more than
    /// this has outgrown an index that lives in memory and wants a durable one instead.
    /// </summary>
    public const int PositionCeiling = 1_000_000;

    /// <summary>
    /// How long a failed reading is remembered when there is nothing to draw. A library that is
    /// down would otherwise be given a fresh timeout on every pan by every viewer, which turns one
    /// stopped container into a slow application.
    /// </summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// One permit, held only for the duration of a whole-library reading. Concurrent viewports that
    /// find the reading stale queue behind it and then all use the one answer. <b>Not a
    /// throttle</b>: nothing is spaced, nothing waits on it once a reading is in hand, and the
    /// picture path never touches it.
    /// </summary>
    private readonly SemaphoreSlim reading = new(1, 1);

    /// <summary>
    /// The reading in force. Replaced wholesale and never mutated, so a reader holds whichever
    /// array it started with, sees no half-built reading, and needs no lock of its own.
    /// </summary>
    private volatile MarkerSnapshot? snapshot;

    /// <summary>
    /// Set once and never cleared except by an operator's recheck. See <see cref="ThumbnailAsync"/>.
    /// </summary>
    private volatile bool picturesStopped;

    /// <summary>One at a time, so a stale reading being served does not start a re-read per viewport.</summary>
    private int refreshing;

    private long lastFailureTicks;

    private ImmichOptions Options => options.Value;

    public PhotoLibrarySource Source => PhotoLibrarySource.Immich;

    public bool IsConfigured => Options.IsConfigured;

    public bool PicturesAvailable => IsConfigured && !picturesStopped;

    /// <summary>
    /// Photographs inside a rectangle, answered from the reading this process holds.
    /// </summary>
    /// <remarks>
    /// A linear pass over one contiguous array of value types. At the shipped ceiling that is eight
    /// megabytes read end to end, which costs well under a millisecond and no network at all — and
    /// being able to do this is the entire reason the positions are held here rather than asked for
    /// per viewport.
    /// </remarks>
    public async Task<LibraryPhotoPage> PhotosInAsync(Envelope bounds, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        EnsureConfigured();

        var current = await CurrentAsync(ct);

        var found = new List<LibraryPhoto>();
        var truncated = false;

        foreach (var marker in current.Markers)
        {
            if (marker.Longitude < bounds.MinX || marker.Longitude > bounds.MaxX
                || marker.Latitude < bounds.MinY || marker.Latitude > bounds.MaxY)
            {
                continue;
            }

            if (found.Count >= Math.Max(1, limit))
            {
                truncated = true;
                break;
            }

            // Neither the identifier nor the derivative name differs here: this library names a
            // photograph and the picture of it with the same string. Written from the parsed value
            // rather than from what arrived, so what goes into a request path is a shape this
            // application produced rather than one it was handed.
            var id = marker.Id.ToString("D");
            found.Add(new LibraryPhoto(
                ForeignId: id,
                Reference: id,
                Longitude: marker.Longitude,
                Latitude: marker.Latitude,
                TakenAt: null,   // the position feed carries no date
                Title: null,     // nor a title
                Kind: null));    // nor a type: null here means "not said", never "photograph"
        }

        return new LibraryPhotoPage(found, truncated, current.ReadAt);
    }

    /// <summary>
    /// The reading in force, re-read when it is old enough and re-readable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five situations, deliberately different from each other:
    /// </para>
    /// <list type="bullet">
    /// <item>A reading younger than the configured interval is used as it stands, and no socket is
    /// opened.</item>
    /// <item>An older one is used <em>immediately</em> anyway and re-read behind the answer, so
    /// panning never waits on a neighbour. How old it is travels with the answer, because a stale
    /// coordinate that looks live is the failure this whole arrangement has to avoid.</item>
    /// <item>An interval of zero means every viewport re-reads and waits for it — a real setting
    /// for a very small library, not a switch that only means something in a test.</item>
    /// <item>No reading at all — the first viewport after a restart — waits, because the
    /// alternative is an empty map, and an empty map cannot be told apart from an unphotographed
    /// valley.</item>
    /// <item>Shortly after a failure nothing opens a socket on the request path at all: a held
    /// reading is served as it stands, and with nothing held the library is reported as not
    /// answering. One stopped container must not cost every pan by every viewer a fresh
    /// timeout.</item>
    /// </list>
    /// </remarks>
    private async Task<MarkerSnapshot> CurrentAsync(CancellationToken ct)
    {
        var ttl = TimeSpan.FromSeconds(Math.Clamp(Options.PositionCacheSeconds, 0, 3600));
        var held = snapshot;

        if (held is not null && ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - held.ReadAt < ttl)
        {
            return held;
        }

        var backingOff =
            TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastFailureTicks))
            < FailureBackoff;

        if (held is not null && backingOff)
        {
            // The positions on the map become the last ones this library gave, and the answer says
            // when that was. A map drawing older positions and admitting it is a better answer than
            // one that waits twenty seconds to draw the same thing.
            return held;
        }

        if (held is not null && ttl > TimeSpan.Zero)
        {
            BeginBackgroundRead();
            return held;
        }

        if (held is null && backingOff)
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.UnavailableCode, "The photo library did not answer.");
        }

        try
        {
            return await ReadAsync(ct);
        }
        catch (PhotoLibraryException) when (snapshot is { } kept)
        {
            // A re-read that failed while a reading was in hand keeps the reading. Only the first
            // one, with nothing held, can fail a viewport.
            logger.LogWarning(
                "Re-reading the {Source} photo library's positions failed; the previous reading stands.",
                Source);
            return kept;
        }
    }

    private async Task<MarkerSnapshot> ReadAsync(CancellationToken ct)
    {
        await reading.WaitAsync(ct);
        try
        {
            // Somebody may have read while this call waited for the permit. Checked again rather
            // than assumed, because the permit exists precisely so that concurrent viewports share
            // one reading; taking it and then reading anyway would defeat it.
            var ttl = TimeSpan.FromSeconds(Math.Clamp(Options.PositionCacheSeconds, 0, 3600));
            var held = snapshot;
            if (held is not null && ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - held.ReadAt < ttl)
            {
                return held;
            }

            try
            {
                var read = await ReadMarkersAsync(ct);
                snapshot = read;
                return read;
            }
            catch
            {
                Interlocked.Exchange(ref lastFailureTicks, DateTime.UtcNow.Ticks);
                throw;
            }
        }
        finally
        {
            reading.Release();
        }
    }

    private void BeginBackgroundRead()
    {
        if (Interlocked.CompareExchange(ref refreshing, 1, 0) != 0)
        {
            return;
        }

        // Detached on purpose, and explicitly not on the request's cancellation token: the request
        // that noticed the reading was stale is about to complete, and cancelling with it would mean
        // the positions are only ever re-read by a request willing to wait for them — which is the
        // one case this branch exists to avoid. It follows the application's own shutdown instead,
        // so a stopping process does not sit on a whole-library read.
        _ = Task.Run(async () =>
        {
            try
            {
                await ReadAsync(lifetime.ApplicationStopping);
            }
            catch (Exception e)
            {
                // The previous reading stands. A neighbour that is down produces a map drawing
                // older positions and saying how old they are, rather than a map that goes blank.
                logger.LogWarning(
                    e, "Re-reading the {Source} photo library's positions failed; the previous reading stands.",
                    Source);
            }
            finally
            {
                Interlocked.Exchange(ref refreshing, 0);
            }
        });
    }

    /// <summary>
    /// Reads every located photograph's position in one request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Streamed rather than buffered. At club scale the answer is tens of megabytes of JSON, and
    /// parsing it into a document before reading it would cost several times what the positions
    /// themselves occupy; elements arrive one at a time and only three fields of each are ever
    /// built. The transport decompresses, because this payload is extremely compressible.
    /// </para>
    /// <para>
    /// The place-name fields the far end also sends — locality, region, country — are deliberately
    /// not declared on the wire record. Nothing this application shows needs them, and a field that
    /// is never read cannot be emitted by accident.
    /// </para>
    /// </remarks>
    private async Task<MarkerSnapshot> ReadMarkersAsync(CancellationToken ct)
    {
        // Archived pictures are left out; a partner's and a shared album's are taken in. That is
        // this installation's only lever over which photographs it can see at all, and it is the
        // operator's to pull: what is shared to the account whose key is configured here is what
        // appears, and what is not shared to it never leaves the far side.
        var url = new Uri(
            BaseAddress(Options.BaseUrl),
            "api/map/markers?isArchived=false&withPartners=true&withSharedAlbums=true");

        using var response = await SendJsonAsync(url, ct);

        var ceiling = Math.Clamp(Options.MaxPositions, 1, PositionCeiling);
        var markers = new List<ImmichMarker>(4096);
        var unreadable = 0;

        await using var body = await response.Content.ReadAsStreamAsync(ct);

        try
        {
            await foreach (var wire in JsonSerializer
                .DeserializeAsyncEnumerable<ImmichMarkerDto>(body, Json, ct))
            {
                if (wire is null)
                {
                    continue;
                }

                if (!Guid.TryParse(wire.Id, out var id))
                {
                    // Not a failure of the request: the position is simply unusable, because every
                    // address this application later builds from it assumes the identifier is the
                    // shape this build expects. Counted rather than thrown, so an assumption that
                    // stopped holding becomes a number somebody can see instead of an outage nobody
                    // can explain.
                    unreadable++;
                    continue;
                }

                markers.Add(new ImmichMarker(id, wire.Lon, wire.Lat));

                if (markers.Count > ceiling)
                {
                    // The far end applies no limit at all to this answer — no page size, no offset,
                    // no rectangle — so this is the only one there is. Refused rather than
                    // truncated: truncating would drop whichever photographs happened to come last
                    // and produce a map that is silently missing places, which is a worse answer
                    // than an honest refusal because nothing on the screen would say it happened.
                    throw new PhotoLibraryException(
                        PhotoLibraryException.TooLargeCode,
                        $"The photo library holds more than {ceiling} located photographs, "
                        + "which is more than this installation will hold in memory.");
                }
            }
        }
        catch (JsonException e)
        {
            // A body that stopped halfway is a body that parses as nothing. Half a library's
            // positions is worse than none, because nothing on the screen says it is half.
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "The photo library's positions were not readable as JSON.", e);
        }

        if (unreadable > 0)
        {
            logger.LogWarning(
                "The {Source} photo library reported {Unreadable} positions this application could not "
                + "name a photograph from; they are not drawn.", Source, unreadable);
        }

        var array = markers.ToArray();

        // Ordered once, here, so that a point limit picks the same photographs every time rather
        // than a different arbitrary subset on each pan back — the same reason this application's
        // own map queries order before they cap. Sorting a quarter of a million of these costs tens
        // of milliseconds, once per reading, and it is the difference between a stable map and one
        // whose features flicker in and out while nothing has changed.
        Array.Sort(array, static (a, b) => a.Id.CompareTo(b.Id));

        return new MarkerSnapshot(array, unreadable, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One rendering's bytes. Exactly one attempt, no retry and no backoff, whatever
    /// <c>MaxRetries</c> says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the path that can destroy the operator's library. These products resolve a file on
    /// disk while serving a picture, and one whose originals have gone out from under it — an
    /// unmounted drive, a bind mount that came up empty — reacts to being asked by writing off what
    /// it cannot find. So: one attempt, and the first answer that is not a picture closes this path
    /// for the whole process until an operator rechecks. Nothing reopens it on a timer, because an
    /// automatic re-probe against a detached drive is the same loop with a schedule attached.
    /// </para>
    /// <para>
    /// The status code is not the whole signal. A library having trouble answers a picture request
    /// with a drawing or a page of markup as readily as with an error, and a proxy checking only
    /// the status would go on asking.
    /// </para>
    /// </remarks>
    public async Task<LibraryThumbnail> ThumbnailAsync(
        string reference, LibraryThumbnailSize size, string? ifNoneMatch, CancellationToken ct)
    {
        EnsureConfigured();

        if (!PhotoLibraryHttp.IsSafeReference(reference))
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "That is not a reference this application will ask a photo library for.");
        }

        if (picturesStopped)
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.OriginalsUnavailableCode,
                "Picture requests to this photo library are stopped until an operator rechecks it.");
        }

        var rendering = size == LibraryThumbnailSize.Large ? LargeRendering : SmallRendering;
        var url = new Uri(
            BaseAddress(Options.BaseUrl),
            $"api/assets/{reference}/thumbnail?size={rendering}");

        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorise(request);

        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Not retried, and this is the reason the retry count does not reach this method.
            throw new PhotoLibraryException(
                PhotoLibraryException.UnavailableCode,
                "The photo library could not be reached for a picture.", e);
        }

        switch (PhotoLibraryHttp.ClassifyPicture(response))
        {
            case PictureVerdict.Picture:
            case PictureVerdict.NotModified:
                return new LibraryThumbnail(response);

            case PictureVerdict.Unauthorized:
                response.Dispose();
                throw new PhotoLibraryException(
                    PhotoLibraryException.UnauthorizedCode,
                    "The photo library did not accept this installation's credential for a picture.");

            case PictureVerdict.WrongRendering:
                response.Dispose();
                throw new PhotoLibraryException(
                    PhotoLibraryException.RejectedCode,
                    "The photo library does not serve the rendering this installation asked for.");

            default:
                var status = (int)response.StatusCode;
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                response.Dispose();
                picturesStopped = true;
                logger.LogError(
                    "The {Source} photo library answered a picture request with {Status} {MediaType}; "
                    + "picture requests to it are stopped until an operator rechecks it.",
                    Source, status, mediaType ?? "no content type");

                throw new PhotoLibraryException(
                    PhotoLibraryException.OriginalsUnavailableCode,
                    "The photo library answered without a picture, so it may be unable to reach its "
                    + "originals. Picture requests to it are stopped.");
        }
    }

    /// <summary>
    /// Reopens the picture path, if the library still answers.
    /// </summary>
    /// <remarks>
    /// One request, and deliberately to the route that describes the key rather than to a picture:
    /// it proves the address and the credential without asking the library to resolve a single file
    /// on disk, which is the act that costs photographs when the disk is not there. Whether the
    /// originals are back is then answered by the next real picture request, which is one request
    /// against one photograph rather than a sweep.
    /// </remarks>
    public async Task RecheckOriginalsAsync(CancellationToken ct)
    {
        EnsureConfigured();

        // Nothing in the answer is read. That it answered at all, and accepted the key, is the check.
        using (await SendJsonAsync(new Uri(BaseAddress(Options.BaseUrl), "api/api-keys/me"), ct))
        {
        }

        picturesStopped = false;

        logger.LogInformation(
            "Picture requests to the {Source} photo library were reopened by an operator recheck.", Source);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new PhotoLibraryException(PhotoLibraryException.NotConfiguredCode);
        }
    }

    /// <summary>The address, with the trailing slash a relative path needs to be appended rather than to replace.</summary>
    private static Uri BaseAddress(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        // Ours, not theirs, and clamped so a nonsensical configured value cannot disable it. The
        // far end's own request timeout is a day, which nothing here may inherit.
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(Options.TimeoutSeconds, 5, 300));
        return client;
    }

    private void Authorise(HttpRequestMessage request) =>
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, Options.ApiKey);

    /// <summary>
    /// Sends one question asked in JSON, retrying only what is worth retrying.
    /// </summary>
    /// <remarks>
    /// Retries live here and nowhere else. The byte path never reaches this method, and that
    /// separation is the guard rather than a tidiness: a retry against a library that cannot reach
    /// its originals is a second chance to lose a photograph.
    /// </remarks>
    private async Task<HttpResponseMessage> SendJsonAsync(Uri url, CancellationToken ct)
    {
        var attempts = Math.Max(0, Options.MaxRetries) + 1;
        var client = CreateClient();

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                Authorise(request);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt >= attempts)
                {
                    throw new PhotoLibraryException(
                        PhotoLibraryException.UnavailableCode,
                        "The photo library could not be reached.", e);
                }

                logger.LogWarning(
                    e, "The {Source} photo library did not answer (attempt {Attempt} of {Attempts}).",
                    Source, attempt, attempts);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw new PhotoLibraryException(
                    PhotoLibraryException.UnauthorizedCode,
                    "The photo library did not accept this installation's credential.");
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
            {
                var status = (int)response.StatusCode;
                response.Dispose();

                if (attempt >= attempts)
                {
                    throw new PhotoLibraryException(
                        PhotoLibraryException.UnavailableCode,
                        $"The photo library answered {status} and did not recover.");
                }

                logger.LogWarning(
                    "The {Source} photo library answered {Status}; trying once more.", Source, status);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new PhotoLibraryException(
                    PhotoLibraryException.RejectedCode, $"The photo library answered {status}.");
            }

            PhotoLibraryHttp.EnsureJson(response, Source);
            return response;
        }
    }

    /// <summary>One position exactly as the far end writes it, and nothing else it also writes.</summary>
    private sealed record ImmichMarkerDto(string Id, double Lat, double Lon);

    /// <summary>
    /// One position as it is kept. Thirty-two bytes: an identifier validated on the way in rather
    /// than carried as text, and two full-precision coordinates. Held as a value type in one array
    /// so that a whole reading is one allocation and a viewport is one sequential scan.
    /// </summary>
    private readonly struct ImmichMarker(Guid id, double longitude, double latitude)
    {
        public readonly Guid Id = id;
        public readonly double Longitude = longitude;
        public readonly double Latitude = latitude;
    }

    /// <summary>
    /// One whole reading of the located library, and when it was taken.
    /// </summary>
    /// <param name="Unreadable">
    /// How many positions were dropped because nothing in them named a photograph in the shape this
    /// build expects. A number rather than a failure, so an assumption that stopped holding is
    /// visible while the map still draws.
    /// </param>
    private sealed record MarkerSnapshot(
        ImmichMarker[] Markers, int Unreadable, DateTimeOffset ReadAt);
}
