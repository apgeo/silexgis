// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

    /// <summary>
    /// The route that describes the configured key: its name, and the list of what it may do.
    /// </summary>
    /// <remarks>
    /// It needs no permission of its own, which is what makes it the right route to ask about a
    /// key that can do nothing else — a key with every permission removed still answers here, and
    /// says so.
    /// </remarks>
    private const string KeyRoute = "api/api-keys/me";

    /// <summary>
    /// The route a page of the library is asked for. Part of this library's published contract and
    /// what its own interface uses, which is why it is this one and not the timeline route: that
    /// one is keyed by calendar month and is marked by its own authors as promising nothing to
    /// anybody outside the product.
    /// </summary>
    private const string SearchRoute = "api/search/metadata";

    /// <summary>
    /// The most photographs asked for in one page however this installation is configured. A page
    /// is what somebody is looking at; anything above this is a listing nobody reads and a request
    /// somebody else's container has to answer.
    /// </summary>
    public const int ListPageCeiling = 500;

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
    /// What this integration cannot work without, named exactly as the library's own settings
    /// screen names them so an operator can find them there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two, and only two: the positions come from the map route and the pictures from the asset
    /// route, and nothing else here asks this library for anything. Reported as a set difference
    /// rather than as a yes or no, because "your key is missing asset.view" is a fix and "it does
    /// not work" is a support thread.
    /// </para>
    /// <para>
    /// A key may also carry a single entry standing for everything, which satisfies both and is
    /// what a key minted without narrowing looks like — read as sufficient rather than as an
    /// unknown name, or every operator who took the default would be told to add two rights they
    /// already have.
    /// </para>
    /// </remarks>
    private static readonly string[] RequiredPermissions = ["map.read", "asset.view"];

    /// <summary>The entry a key carries when it was minted with no narrowing at all.</summary>
    private const string EveryPermission = "all";

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

    /// <summary>
    /// The last thing the library said about itself, held for a short window. Process-wide for the
    /// same reason the positions are: a status line refreshed in two browsers must not become two
    /// rounds of requests against a container next door.
    /// </summary>
    private readonly LibraryHealthCache health = new();

    /// <summary>One at a time, so a stale reading being served does not start a re-read per viewport.</summary>
    private int refreshing;

    private long lastFailureTicks;

    private ImmichOptions Options => options.Value;

    public PhotoLibrarySource Source => PhotoLibrarySource.Immich;

    public bool IsConfigured => Options.IsConfigured;

    public bool PicturesAvailable => IsConfigured && !picturesStopped;

    /// <summary>
    /// What state the library is in, asked of the two routes that describe it and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions rather than one, because they fail separately and send an operator to
    /// different places: whether anything is listening at that address, and what the configured key
    /// is actually allowed to do. The second is the one worth having. A key here is not scoped by
    /// this application and cannot be — it carries whatever its own account holds — so the single
    /// most likely way this integration is misconfigured is a key minted with the wrong rights,
    /// which fails by drawing an empty map rather than by saying anything.
    /// </para>
    /// <para>
    /// The route that describes the key needs no permission of its own, so this works even for a
    /// key that can do nothing else — which is exactly the key an operator most needs told about.
    /// </para>
    /// </remarks>
    public Task<LibraryHealth> ProbeAsync(CancellationToken ct) =>
        IsConfigured ? health.GetAsync(ProbeOnceAsync, ct) : Task.FromResult(LibraryHealth.NotAsked);

    private async Task<LibraryHealth> ProbeOnceAsync(CancellationToken ct)
    {
        string? version;
        try
        {
            version = await ReadVersionAsync(ct);
        }
        catch (PhotoLibraryException e)
        {
            return LibraryHealth.DidNotAnswer(e.Code);
        }

        try
        {
            return LibraryHealth.Answered(version, await ReadMissingPermissionsAsync(ct));
        }
        catch (PhotoLibraryException e)
        {
            // It is answering; it would not answer this. Reported as reachable with the reason
            // beside it, because "the container is down" and "the key is no longer accepted" are
            // two different afternoons for whoever has to fix it.
            return LibraryHealth.Answered(version, failureCode: e.Code);
        }
    }

    /// <summary>
    /// What the library says it is, written the way its own release notes write it. Null when it
    /// names none, never a guess.
    /// </summary>
    private async Task<string?> ReadVersionAsync(CancellationToken ct)
    {
        using var response = await SendJsonAsync(
            new Uri(BaseAddress(Options.BaseUrl), "api/server/version"), ct);

        using var document = await ReadDocumentAsync(response, ct);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("major", out var major)
            || !document.RootElement.TryGetProperty("minor", out var minor)
            || !document.RootElement.TryGetProperty("patch", out var patch)
            || major.ValueKind != JsonValueKind.Number
            || minor.ValueKind != JsonValueKind.Number
            || patch.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var release = string.Create(
            CultureInfo.InvariantCulture,
            $"{major.GetInt32()}.{minor.GetInt32()}.{patch.GetInt32()}");

        // A build before a release names itself with a suffix beside the three numbers, and
        // dropping it would report an operator's release candidate as the release it precedes —
        // which is a version line that lies rather than one that says less.
        return document.RootElement.TryGetProperty("prerelease", out var prerelease)
            && prerelease.ValueKind == JsonValueKind.String
            && prerelease.GetString() is { Length: > 0 } suffix
            ? $"{release}-{suffix}"
            : release;
    }

    /// <summary>
    /// The rights this integration needs that the configured key does not carry, in the order they
    /// are declared so that two readings of the same key read the same.
    /// </summary>
    /// <remarks>
    /// An answer this application cannot read is not evidence either way, so it produces no
    /// missing names: telling an operator to add a right their key already carries sends them to
    /// change a working key, and is a worse answer than saying nothing. That the check could not be
    /// made is written to the log instead, where an unexplained empty map is investigated from.
    /// </remarks>
    private async Task<IReadOnlyList<string>> ReadMissingPermissionsAsync(CancellationToken ct)
    {
        using var response = await SendJsonAsync(new Uri(BaseAddress(Options.BaseUrl), KeyRoute), ct);
        using var document = await ReadDocumentAsync(response, ct);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("permissions", out var permissions)
            || permissions.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning(
                "The {Source} photo library did not say what its key may do, so no claim is made "
                + "about the key's rights.", Source);
            return [];
        }

        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissions.EnumerateArray())
        {
            if (permission.ValueKind == JsonValueKind.String && permission.GetString() is { } name)
            {
                granted.Add(name);
            }
        }

        if (granted.Contains(EveryPermission))
        {
            return [];
        }

        return [.. RequiredPermissions.Where(required => !granted.Contains(required))];
    }

    /// <summary>
    /// Parses an answer already known to be JSON. A body that stopped halfway parses as nothing,
    /// and a health line built from half an answer would describe a library nobody is running.
    /// </summary>
    private async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException e)
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                $"The {Source} photo library's answer was not readable as JSON.", e);
        }
    }

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
    /// This library has no text matching worth offering as one.
    /// </summary>
    /// <remarks>
    /// Its structured search filters over columns — dates, cameras, people, tags — rather than over
    /// anything somebody would type as a sentence, and its one text-shaped question is a similarity
    /// search over what a picture <em>looks like</em>, which is a different feature answering a
    /// different question and belongs on a surface that says so. Rather than pass words to a filter
    /// that would match almost nothing while looking like a search, this library is asked none and
    /// the surface tells the reader why there is no box.
    /// </remarks>
    public bool SupportsTextSearch => false;

    /// <summary>
    /// One page of the library, newest first, asked for without a rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not answered from the reading this process holds, and that is the point of it. That reading
    /// is of the <em>located</em> library — the only thing the route behind it reports — so a list
    /// built from it would silently omit every photograph with no position, which in a caving club
    /// is most of them: everything taken underground, every scan, every camera with no receiver. A
    /// listing is therefore a live question, and holds nothing between calls.
    /// </para>
    /// <para>
    /// The search route rather than the timeline: the timeline is keyed by calendar month and is
    /// marked by this library's own authors as carrying no promise to anybody outside the product,
    /// while this one is part of the contract, pages, and is what its own interface uses.
    /// </para>
    /// </remarks>
    public async Task<LibraryPhotoListPage> ListAsync(LibraryPhotoQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            // Refused rather than dropped. A caller that sends words to a library which cannot
            // match them gets a full unfiltered page back with nothing in it saying the words were
            // ignored, which is a search box that looks like it worked.
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "This photo library does not match text, so it is not asked to.");
        }

        var size = Math.Clamp(query.PageSize, 1, ListPageCeiling);
        var page = Math.Max(1, query.Page);

        // Written out rather than serialised from an object, so what leaves this machine is exactly
        // these three fields and cannot silently grow a fourth: every field here is a question
        // asked about somebody else's photographs. The order is asked for rather than assumed — a
        // listing that quietly came back oldest-first would look like a working feature showing the
        // wrong decade.
        var body = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"page":{{page}},"size":{{size}},"order":"desc"}""");

        var url = new Uri(BaseAddress(Options.BaseUrl), SearchRoute);
        using var response = await PostJsonAsync(url, body, ct);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException e)
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "The photo library's answer was not readable as JSON.", e);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Object
                || !assets.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                throw new PhotoLibraryException(
                    PhotoLibraryException.RejectedCode,
                    "The photo library did not answer a listing with a list of photographs.");
            }

            var photos = new List<LibraryListedPhoto>(items.GetArrayLength());

            foreach (var item in items.EnumerateArray())
            {
                if (TryReadListed(item, out var photo))
                {
                    photos.Add(photo);
                }
            }

            // What this library says about a further page is a page number rather than a flag, and
            // an absent one is the end of the listing.
            var hasMore = assets.TryGetProperty("nextPage", out var next)
                && next.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(next.GetString());

            return new LibraryPhotoListPage(
                photos, TotalOf(assets, page, size, photos.Count, hasMore), hasMore,
                DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// How many photographs <b>the library</b> holds for this request, or null when what it sent
    /// cannot be that number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library states a total beside every page, and it is published as read: it is a fact
    /// about the library rather than about whoever asked, because every caller reaches this library
    /// through one credential belonging to the whole installation and so there is one answer to
    /// give. The day that stops being true, this is one of the places that has to change.
    /// </para>
    /// <para>
    /// It is checked against the paging first, and that check is the honest part. A number smaller
    /// than the photographs already handed out cannot be a total of them, and a number equal to
    /// them while a further page is promised cannot be either — both are what a field counting
    /// something narrower than the whole collection looks like. Neither is corrected: a total that
    /// contradicts the paging is reported as unknown, because a wrong number a reader cannot tell
    /// from a right one is worse than no number at all.
    /// </para>
    /// </remarks>
    private static int? TotalOf(JsonElement assets, int page, int size, int shown, bool hasMore)
    {
        if (!assets.TryGetProperty("total", out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var total)
            || total < 0)
        {
            return null;
        }

        var seen = ((page - 1) * (long)size) + shown;

        return total > seen || (total == seen && !hasMore) ? total : null;
    }

    /// <summary>
    /// One row of a listing. A row this application cannot name a photograph from is left out
    /// rather than drawn as a gap: every address built from it later assumes the identifier is the
    /// shape this build expects.
    /// </summary>
    /// <remarks>
    /// This library names a photograph and the picture of it with the same string, so one value
    /// answers both. It is written from the parsed identifier rather than from what arrived, so
    /// what goes into a request path is a shape this application produced.
    /// </remarks>
    private static bool TryReadListed(JsonElement element, out LibraryListedPhoto photo)
    {
        photo = default;

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("id", out var identifier)
            || identifier.ValueKind != JsonValueKind.String
            || !Guid.TryParse(identifier.GetString(), out var id))
        {
            return false;
        }

        var name = id.ToString("D");

        photo = new LibraryListedPhoto(
            PhotographId: name,
            Reference: name,
            // This library keeps no title of its own for a photograph. Left null rather than filled
            // in from the file's name, which is a fact about a disk rather than something anybody
            // wrote about the picture — and a screen showing it under a heading reading "title"
            // would be saying somebody named it.
            Title: null,
            // The date the listing is ordered by, so what a reader sees under each picture is the
            // date the order in front of them was made with.
            TakenAt: Moment(element, "fileCreatedAt"),
            Kind: KindOf(element));

        return true;
    }

    /// <summary>
    /// Everything this library will say about one photograph, or null when it reports none under
    /// that identifier.
    /// </summary>
    /// <remarks>
    /// The camera facts come from what the library read out of the picture itself, where it sends
    /// them. Nothing is inferred: a field the answer does not carry stays absent all the way to the
    /// screen, because a camera model this application guessed would be a claim about somebody
    /// else's photograph that nobody made.
    /// </remarks>
    public async Task<LibraryPhotoDetail?> DetailAsync(string photographId, CancellationToken ct)
    {
        EnsureConfigured();

        // Parsed rather than merely checked, and written back out from the parsed value: this
        // library names photographs in one shape, and anything else is a question it would answer
        // with a refusal this application would then have to explain.
        if (!Guid.TryParse(photographId, out var id))
        {
            return null;
        }

        var name = id.ToString("D");
        var url = new Uri(BaseAddress(Options.BaseUrl), $"api/assets/{name}");

        // A photograph the library does not report is an answer rather than a failure: it may have
        // been deleted over there since the page listing it was drawn, and telling a reader the
        // library is broken would send them looking for a fault nobody has.
        using var response = await SendJsonAsync(url, missingIsAnswer: true, ct);
        if (response is null)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException e)
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "The photo library's answer was not readable as JSON.", e);
        }

        using (document)
        {
            var element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // The picture's own facts live under their own object here, and its absence is
            // ordinary: a picture whose metadata carried none, or a library that keeps them behind
            // a right this installation's credential does not carry.
            var exif = element.TryGetProperty("exifInfo", out var read)
                && read.ValueKind == JsonValueKind.Object
                    ? read
                    : default;

            return new LibraryPhotoDetail(
                PhotographId: name,
                Reference: name,
                // No title of its own — see the listing above.
                Title: null,
                Description: Text(exif, "description"),
                // The moment written into the picture where there is one, and the moment the
                // library filed it under otherwise. In that order, because the first is when
                // somebody stood there and the second is when a file arrived on a disk.
                TakenAt: Moment(exif, "dateTimeOriginal") ?? Moment(element, "fileCreatedAt"),
                Kind: KindOf(element),
                CameraMake: Text(exif, "make"),
                CameraModel: Text(exif, "model"),
                Lens: Text(exif, "lensModel"),
                Aperture: Number(exif, "fNumber"),
                ShutterSpeed: Text(exif, "exposureTime"),
                Iso: Whole(exif, "iso"),
                FocalLengthMm: Number(exif, "focalLength"));
        }
    }

    /// <summary>
    /// What the library says a photograph is, from the word it files it under. Anything that is
    /// neither of the two words this application knows is left unsaid rather than read as an image.
    /// </summary>
    private static LibraryPhotoKind? KindOf(JsonElement element) => Text(element, "type") switch
    {
        { } type when string.Equals(type, "VIDEO", StringComparison.OrdinalIgnoreCase) =>
            LibraryPhotoKind.Video,
        { } type when string.Equals(type, "IMAGE", StringComparison.OrdinalIgnoreCase) =>
            LibraryPhotoKind.Image,
        _ => null,
    };

    /// <summary>A string the library wrote, or null where it wrote nothing worth showing.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static DateTimeOffset? Moment(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out var when)
            ? when
            : null;

    /// <summary>
    /// A measurement the library states, or null. Zero is read as unsaid rather than as a value: a
    /// panel reporting an aperture of zero would state something nobody measured.
    /// </summary>
    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var read)
        && read > 0
            ? read
            : null;

    /// <summary>A whole measurement, on the same reading of zero.</summary>
    private static int? Whole(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var read)
        && read > 0
            ? read
            : null;

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

        try
        {
            // Nothing in the answer is read. That it answered at all, and accepted the key, is the check.
            using (await SendJsonAsync(new Uri(BaseAddress(Options.BaseUrl), KeyRoute), ct))
            {
            }

            // Reopened only on an answer, and never in the block below: with the originals out of
            // reach a picture request is a deletion, so the byte path opens on evidence that the
            // library is answering and on nothing else.
            picturesStopped = false;
        }
        finally
        {
            // The held reading describes the library as it was before whatever the operator has
            // just finished doing, and it is forgotten whether or not the call above succeeded.
            // Forgetting only on success would make the button do least when it is pressed most: a
            // recheck against a library that is still down would leave the health line describing
            // the state before the fix attempt for the rest of the window, which is exactly the
            // button that visibly does nothing this is here to remove.
            health.Clear();
        }

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
    private async Task<HttpResponseMessage> SendJsonAsync(Uri url, CancellationToken ct) =>
        // Never null: only a caller that says a missing thing is an answer can be given one.
        (await SendJsonAsync(url, missingIsAnswer: false, ct))!;

    /// <summary>
    /// One question asked about a named thing that may not be there any more.
    /// </summary>
    /// <remarks>
    /// <paramref name="missingIsAnswer"/> turns the far side's "no such thing" into a null rather
    /// than a refusal, and only for a caller that asked for it. It is not leniency: a photograph
    /// deleted over there is an ordinary answer somebody has to be shown, while the same status
    /// from a route naming no particular thing means this application asked a question the library
    /// does not understand — which is a defect on this side and must stay loud.
    /// </remarks>
    private Task<HttpResponseMessage?> SendJsonAsync(
        Uri url, bool missingIsAnswer, CancellationToken ct) =>
        SendJsonAsync(HttpMethod.Get, url, body: null, missingIsAnswer, ct);

    /// <summary>
    /// One question whose parameters do not fit in an address, asked with a body this application
    /// writes out itself. Everything else about it — the credential, the retries, the refusal
    /// codes, the insistence on an answer in JSON — is the same as for any other question.
    /// </summary>
    private async Task<HttpResponseMessage> PostJsonAsync(Uri url, string body, CancellationToken ct) =>
        (await SendJsonAsync(HttpMethod.Post, url, body, missingIsAnswer: false, ct))!;

    private async Task<HttpResponseMessage?> SendJsonAsync(
        HttpMethod method, Uri url, string? body, bool missingIsAnswer, CancellationToken ct)
    {
        var attempts = Math.Max(0, Options.MaxRetries) + 1;
        var client = CreateClient();

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(method, url);
                Authorise(request);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (body is not null)
                {
                    // Built inside the loop, because a content already sent cannot be sent again —
                    // a retry that re-used it would fail on the stream rather than on whatever made
                    // the first attempt worth repeating.
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

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

            if (missingIsAnswer && response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
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
