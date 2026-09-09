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
    IPhotoLibraryBrake brake,
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
    /// The route a set of words is put to. Part of this library's published contract, and the only
    /// question it has that takes a sentence: it turns the words into a description of an image and
    /// orders what it holds by closeness to that description.
    /// </summary>
    /// <remarks>
    /// It takes the same filters the listing route takes, and none of them is geographic — there is
    /// no coordinate, no rectangle and no radius on any of this library's search requests. So a
    /// search cannot be asked about a place, and this application does not pretend it can by
    /// narrowing the answer afterwards and calling the result a search of that place.
    /// </remarks>
    private const string SmartSearchRoute = "api/search/smart";

    /// <summary>
    /// The route this library's albums are read from. Part of its published contract, and the only
    /// question this integration asks it about albums at all.
    /// </summary>
    /// <remarks>
    /// What comes back describes each album and no longer carries the photographs in it: this
    /// product removed the member listing an album's assets, along with the two naming its owner,
    /// when it broke its contract at its third major version. So an album is a name, a number and a
    /// span here, and the photographs in one are asked for through the listing, narrowed.
    /// </remarks>
    private const string AlbumsRoute = "api/albums";

    /// <summary>
    /// The most albums this build will carry into a chooser, however many the library keeps. A
    /// list somebody picks one entry out of; a library with more albums than this has outgrown a
    /// chooser, and what is offered says it was cut rather than quietly ending.
    /// </summary>
    public const int AlbumCeiling = 500;

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
    // Every right this integration actually uses, so that a key missing one is named before a
    // screen fails on it rather than after. `map.read` reads the positions, `asset.view` fetches
    // the pictures, and `asset.read` is what the listing and the search go through — a key
    // carrying only the first two passes the health check and then refuses every browse and
    // every search, which is the health check reporting on something adjacent to the question.
    private static readonly string[] RequiredPermissions = ["map.read", "asset.view", "asset.read"];

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
    public async Task<LibraryHealth> ProbeAsync(CancellationToken ct)
    {
        // A library this installation has stopped using is asked nothing, including this — and
        // "nobody asked" is the honest answer for it. Saying it did not answer would invent a
        // failure and send an operator to restart a container that is working perfectly.
        if (!IsConfigured || await brake.IsSuspendedAsync(Source, ct))
        {
            return LibraryHealth.NotAsked;
        }

        return await health.GetAsync(ProbeOnceAsync, ct);
    }

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
        await EnsureUsableAsync(ct);

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
    /// This library answers words by meaning rather than by matching them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It has no text matching worth offering as one: its structured filters are over columns —
    /// dates, cameras, people, tags — rather than over anything somebody would type as a sentence.
    /// What it does have is a question of a different kind, which turns the words into a
    /// description of an image and orders what it holds by how close each picture is to that
    /// description. So nothing is matched and nothing is excluded, and the front of that ordering
    /// is what a page of a search is.
    /// </para>
    /// <para>
    /// Two consequences the surfaces above have to carry rather than smooth over. There is no count
    /// of matches, because there is no set of matches. And a search that finds nothing useful is
    /// not the same as a library holding nothing: the ordering always has a front, so what comes
    /// back is whatever was least unlike the words.
    /// </para>
    /// </remarks>
    public LibrarySearchMatching SearchMatching => LibrarySearchMatching.Meaning;

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
    /// <para>
    /// That route also takes the two ends of a stretch of time, which is the whole of what a window
    /// costs here: the narrowing is the library's own, it pages as any other listing does, and no
    /// geography is involved in asking for it. Nothing is filtered out of the answer on this side —
    /// the far end has already decided what this page is, and dropping rows from it afterwards would
    /// leave a page short of what it says it is and a next page that skips what was dropped.
    /// </para>
    /// </remarks>
    public async Task<LibraryPhotoListPage> ListAsync(LibraryPhotoQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await EnsureUsableAsync(ct);

        var size = Math.Clamp(query.PageSize, 1, ListPageCeiling);
        var page = Math.Max(1, query.Page);

        // Written out rather than serialised from an object, so what leaves this machine is exactly
        // these fields and cannot silently grow one more: every field here is a question asked about
        // somebody else's photographs. The order is asked for rather than assumed — a listing that
        // quietly came back oldest-first would look like a working feature showing the wrong decade.
        var body = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"page":{{page}},"size":{{size}},"order":"desc"{{Taken(query.Window)}}{{InAlbum(query.Album)}}}""");

        var answered = await AskAsync(
            new Uri(BaseAddress(Options.BaseUrl), SearchRoute), body, "a listing", ct);

        // Checked against what the library handed over rather than against what survived the
        // reading: the check below asks whether the stated total can be a total of the photographs
        // already paged past, and the library paged past every row it sent — not the subset of them
        // this application could name.
        return new LibraryPhotoListPage(
            answered.Photos,
            TotalOf(answered.StatedTotal, page, size, answered.HandedOver, answered.HasMore),
            answered.HasMore,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One page of what this library makes of a set of words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A different route from the listing, and a different kind of answer. This one turns the words
    /// into a description of an image and orders the library by how close each picture is to it, so
    /// what comes back is the front of an ordering rather than the photographs that matched: there
    /// is no matching, nothing is excluded, and no number anywhere counts a set that does not
    /// exist. That is why the answer comes back in a record with no total in it.
    /// </para>
    /// <para>
    /// The words go over as they were typed. This route reads them as a description and not as a
    /// query language — there is no pair, prefix or operator it binds to a field — so unlike the
    /// other product's grammar there is nothing here to reduce them to, and reducing them would
    /// change what somebody asked for. What they must not do is escape the string they travel in,
    /// which is why they are written by the serialiser rather than put between quotation marks.
    /// </para>
    /// </remarks>
    public async Task<LibraryPhotoSearchPage> SearchAsync(
        LibraryPhotoSearchQuery search, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(search);
        await EnsureUsableAsync(ct);

        var size = Math.Clamp(search.PageSize, 1, ListPageCeiling);
        var page = Math.Max(1, search.Page);

        // Written out rather than serialised from an object, for the same reason the listing is:
        // what leaves this machine is exactly these three fields and cannot silently grow a fourth.
        // The words themselves go through the serialiser, because they are somebody's sentence and
        // a quotation mark or a backslash in them would otherwise end the string early and turn the
        // rest of what they typed into a different request.
        var body = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"query":{{JsonSerializer.Serialize(search.Text, Json)}},"page":{{page}},"size":{{size}}}""");

        var answered = await AskAsync(
            new Uri(BaseAddress(Options.BaseUrl), SmartSearchRoute), body, "a search", ct);

        // Whatever number came back beside this page is left where it was found. A total is a count
        // of what matched, and nothing matched: this route ranks the whole library and hands back a
        // slice of the ranking, so the only honest arithmetic is how many came back and whether the
        // ordering continues.
        // The words are published back exactly as they were put, because on this library they are
        // put as they were typed: it reads them as a description and binds no pair, prefix or
        // operator to a field, so there is nothing here to reduce them to.
        return new LibraryPhotoSearchPage(
            answered.Photos,
            LibrarySearchMatching.Meaning,
            search.Text,
            answered.HasMore,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The albums this library keeps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One question, answered whole: this route pages nothing and this library keeps far fewer
    /// albums than photographs, so what comes back is the set and the only bound is this
    /// application's own — a chooser somebody picks one entry out of, cut at a ceiling and saying
    /// so when it was cut.
    /// </para>
    /// <para>
    /// What this answer no longer carries is the photographs in each album, along with the two
    /// fields naming an album's owner: this product removed all three when it broke its contract at
    /// its third major version. So nothing here reaches for them, and the photographs in an album
    /// are asked for through the listing rather than read out of an album.
    /// </para>
    /// </remarks>
    public async Task<LibraryAlbumPage> AlbumsAsync(CancellationToken ct)
    {
        await EnsureUsableAsync(ct);

        using var response = await SendJsonAsync(new Uri(BaseAddress(Options.BaseUrl), AlbumsRoute), ct);

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
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                // Refused rather than read as a library with no albums. An address in front of the
                // wrong container answers markup with HTTP 200, and a chooser that rendered that as
                // "this library has no albums" is the mistake this feature is most likely to make
                // while looking correct.
                throw new PhotoLibraryException(
                    PhotoLibraryException.RejectedCode,
                    "The photo library did not answer with a list of albums.");
            }

            var handedOver = document.RootElement.GetArrayLength();
            var albums = new List<LibraryAlbum>(Math.Min(handedOver, AlbumCeiling));

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (albums.Count >= AlbumCeiling)
                {
                    break;
                }

                if (TryReadAlbum(element, out var album))
                {
                    albums.Add(album);
                }
            }

            // Cut, or short because rows could not be read — two different facts, and only the
            // first of them is something a reader can act on. The second is said in the log rather
            // than on the screen, because a renamed field over there empties a chooser while
            // nothing anywhere says an assumption stopped holding.
            var truncated = handedOver > AlbumCeiling;
            if (albums.Count < Math.Min(handedOver, AlbumCeiling))
            {
                logger.LogWarning(
                    "The {Source} photo library answered with {HandedOver} albums, of which "
                    + "{Unreadable} named no album this build could use; they are not offered.",
                    Source,
                    handedOver,
                    Math.Min(handedOver, AlbumCeiling) - albums.Count);
            }

            return new LibraryAlbumPage(albums, truncated, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// One album as this library describes one. A row naming no album this application could ask
    /// about later is left out rather than offered as an entry that narrows nothing.
    /// </summary>
    /// <remarks>
    /// The count is the library's own number and is absent where it did not send one. Zero is a
    /// number here and not an absence — an album somebody has just emptied holds none, and that is
    /// a fact the library stated rather than a field it left out.
    /// </remarks>
    private static bool TryReadAlbum(JsonElement element, out LibraryAlbum album)
    {
        album = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Checked against what this application is willing to put in a request rather than passed
        // on as it arrived: this value goes back to the library as the narrowing on a listing.
        var id = Text(element, "id");
        if (!PhotoLibraryHttp.IsSafeReference(id))
        {
            return false;
        }

        album = new LibraryAlbum(
            AlbumId: id!,
            Title: Text(element, "albumName"),
            PhotographCount: Counted(element, "assetCount"),
            From: Moment(element, "startDate"),
            To: Moment(element, "endDate"));

        return true;
    }

    /// <summary>
    /// A count the library states, or null where it stated none. Zero is a count and not an
    /// absence, which is what makes this a different reader from the one used for a measurement
    /// written into a picture — there a zero is what this product writes when it read nothing.
    /// </summary>
    private static int? Counted(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var read)
        && read >= 0
            ? read
            : null;

    /// <summary>
    /// Puts one question to this library and reads the page of photographs out of its answer.
    /// </summary>
    /// <remarks>
    /// One place for it because the two routes that page through photographs — the listing's and
    /// the search's — answer in the same shape, and reading that shape twice is how one of them
    /// ends up quietly disagreeing with the other about what an unreadable row or a missing next
    /// page means.
    /// </remarks>
    /// <param name="question">
    /// What was asked, for the sentence an operator reads when the answer was not of the expected
    /// shape. It says which of the two questions came back wrong, because they are different
    /// routes and a library can serve one and not the other.
    /// </param>
    private async Task<(IReadOnlyList<LibraryListedPhoto> Photos, int HandedOver, bool HasMore, int? StatedTotal)>
        AskAsync(Uri url, string body, string question, CancellationToken ct)
    {
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
                    $"The photo library did not answer {question} with a list of photographs.");
            }

            var handedOver = items.GetArrayLength();
            var photos = new List<LibraryListedPhoto>(handedOver);

            foreach (var item in items.EnumerateArray())
            {
                if (TryReadListed(item, out var photo))
                {
                    photos.Add(photo);
                }
            }

            // A row this build cannot name a photograph from is dropped, and a page that lost rows
            // is a page whose count is smaller than what the library sent. Said out loud here for
            // the same reason the positions are: a page of sixty that arrives as five looks exactly
            // like a small library, and if a version change over there renames the field this reads,
            // every page empties while nothing anywhere says an assumption stopped holding.
            if (photos.Count < handedOver)
            {
                logger.LogWarning(
                    "The {Source} photo library answered {Question} with {HandedOver} rows, of which "
                    + "{Unreadable} named no photograph this build could use; they are not shown.",
                    Source, question, handedOver, handedOver - photos.Count);
            }

            // What this library says about a further page is a page number rather than a flag, and
            // an absent one is the end of the answer.
            var hasMore = assets.TryGetProperty("nextPage", out var next)
                && next.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(next.GetString());

            var stated = assets.TryGetProperty("total", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var total)
                && total >= 0
                    ? total
                    : (int?)null;

            return (photos, handedOver, hasMore, stated);
        }
    }

    /// <summary>
    /// The one field that narrows a listing to an album, ready to be dropped into the body beside
    /// the paging — or nothing at all when the whole library was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written straight into the body rather than serialised, on the same argument the window's two
    /// instants are: the value has already been checked against the shape this application is
    /// willing to put in a request to a neighbour — letters, digits, hyphens and underscores and
    /// nothing else — so it cannot carry a quotation mark, a backslash or a second field into the
    /// request.
    /// </para>
    /// <para>
    /// Nothing is filtered out of the answer afterwards, for the reason the window is not: the far
    /// side has already decided what this page is, so dropping rows from it here would leave a page
    /// short of the number beside it and a next page that skips whatever was dropped.
    /// </para>
    /// <para>
    /// <b>Whether the identifier names an album this library keeps is the library's to decide, not
    /// this application's.</b> A value naming nothing over there comes back as a listing with
    /// nothing in it, which is the same answer an album somebody has just emptied gives, and both
    /// are ordinary. A value this library will not even read as an identifier comes back as a
    /// refusal, and that is reported as a refusal rather than as an empty album — the one thing
    /// that must never happen here is a narrowed question answered with the whole library.
    /// </para>
    /// </remarks>
    private static string InAlbum(string? album) =>
        album is null ? string.Empty : $",\"albumId\":\"{album}\"";

    /// <summary>
    /// The two fields that narrow a listing to a stretch of time, ready to be dropped into the body
    /// beside the paging — or nothing at all when the whole library was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built here out of two instants rather than assembled anywhere a caller's value could reach.
    /// The values that go out are produced entirely by <see cref="Instant"/> from a
    /// <see cref="DateTimeOffset"/>, so they are a fixed shape of digits and separators and cannot
    /// carry a quotation mark, a backslash or a second field into the request — which is what lets
    /// them be written straight into the body beside the fields above rather than being serialised.
    /// </para>
    /// <para>
    /// This library narrows by the moment it holds for a photograph, which it keeps in UTC, so the
    /// instants are sent as UTC and are not re-expressed in anybody's local zone on the way. The
    /// window handed in is already wide enough to cover the difference between a calendar day here
    /// and a calendar day wherever a camera was.
    /// </para>
    /// </remarks>
    private static string Taken(LibraryPhotoWindow? window)
    {
        if (window is not { } asked)
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $",\"takenAfter\":\"{Instant(asked.From)}\",\"takenBefore\":\"{Instant(asked.To)}\"");
    }

    /// <summary>One instant, in the shape this library reads a moment in.</summary>
    /// <remarks>
    /// Invariant and explicitly UTC. A moment written in the running machine's own format is a
    /// request that means one thing on a developer's box and another on a server, and the symptom is
    /// a window silently off by hours rather than an error anybody would see.
    /// </remarks>
    private static string Instant(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

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
    /// <para>
    /// <paramref name="handedOver"/> is how many rows the library put on this page, not how many of
    /// them could be read. The two differ whenever a row is unreadable, and the check only holds
    /// with the first: counting what survived would shrink the number of photographs believed to
    /// have been paged past and let exactly the totals this guard exists to suppress through.
    /// </para>
    /// </remarks>
    private static int? TotalOf(int? stated, int page, int size, int handedOver, bool hasMore)
    {
        if (stated is not { } total)
        {
            return null;
        }

        var seen = ((page - 1) * (long)size) + handedOver;

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
        await EnsureUsableAsync(ct);

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
        await EnsureUsableAsync(ct);

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
        await EnsureUsableAsync(ct);

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

    /// <summary>
    /// The one question asked before anything leaves this machine: does this installation have this
    /// library at all, and is it still using it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both answers arrive as the same refusal on purpose. A library the deployment never supplied
    /// and one an administrator has stopped are the same thing to every surface above — absent,
    /// rather than broken — and the whole point of the brake is that the application looks like an
    /// installation that never had the library rather than like one whose library is failing.
    /// </para>
    /// <para>
    /// It is here, in the method every outgoing call already goes through, rather than only in the
    /// routes that call this client, because a check a caller must remember to make is one a caller
    /// will eventually forget, and forgetting is silent: the request succeeds, the library answers,
    /// and the only symptom is traffic at a neighbour's container that nobody is watching. Callers
    /// still ask their own question first — a route that knows a library is stopped can answer
    /// without constructing anything — but this is what makes "no socket is opened" true rather
    /// than promised.
    /// </para>
    /// </remarks>
    private async ValueTask EnsureUsableAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new PhotoLibraryException(PhotoLibraryException.NotConfiguredCode);
        }

        if (await brake.IsSuspendedAsync(Source, ct))
        {
            Forget();
            throw new PhotoLibraryException(PhotoLibraryException.NotConfiguredCode);
        }
    }

    /// <summary>
    /// Drops everything this client is holding about the library, without asking it anything.
    /// </summary>
    /// <remarks>
    /// What the whole located library's coordinates are held for is a map that is being drawn; a
    /// library this installation has stopped using is not being drawn, so keeping them resident
    /// would leave the positions of every located photograph in memory for as long as the process
    /// lives, and re-serve them the instant the brake came off however much later. Stopping and
    /// forgetting are deliberately the same act here. It changes nothing on the far side: the
    /// library still holds everything it held.
    /// </remarks>
    public void Forget()
    {
        snapshot = null;
        health.Clear();
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
