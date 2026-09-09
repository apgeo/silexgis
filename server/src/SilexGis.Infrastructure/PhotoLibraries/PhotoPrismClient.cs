// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// This installation's only way of talking to a neighbouring PhotoPrism.
///
/// <para>
/// <b>It is a singleton, and not for the reason the cave catalogue's client next door is one.</b>
/// That one exists to be the throttle: it holds one gate with a minimum interval, because the
/// catalogue is a volunteer-run service whose documentation asks not to be hammered. Every premise
/// of that is false here. This library is a container in this installation's own deployment, on
/// this operator's own host and electricity; it asks for no restraint and applies none of its own to
/// successful calls; and the round trip is a loopback or a bridge, so a one-second floor between
/// calls would make panning a map feel broken for a politeness nobody is owed. <b>There is
/// deliberately no minimum interval and no serialising gate here, and its absence is a decision
/// rather than an omission.</b>
/// </para>
/// <para>
/// What it is a singleton for instead is two process-wide facts. The picture credential it harvests
/// is one, and is refreshed by every answer to a position query. The other is the sticky flag that
/// closes the picture path: against a library whose originals are out of reach, a picture request
/// makes that library mark the file missing and drop the photograph from its own index, so the
/// first such answer stops every later one until an operator rechecks. A per-request client would
/// forget both between one map pan and the next.
/// </para>
/// <para>
/// What does carry over from the catalogue, stripped of the politeness: the limit is applied before
/// asking rather than trusted to the far end, because a limit that lives only at the far end stops
/// existing the day the far end changes; the timeout is this application's rather than inherited;
/// and the error codes stay apart, because they send an administrator to different places.
/// </para>
/// </summary>
public sealed class PhotoPrismClient(
    IHttpClientFactory httpClientFactory,
    IOptions<PhotoPrismOptions> options,
    IPhotoLibraryBrake brake,
    ILogger<PhotoPrismClient> logger) : IPhotoLibrary
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "photo-library-photoprism";

    /// <summary>
    /// The most photographs this application will ask for in one viewport whatever an operator
    /// configures. The library's own ceiling is a hundred thousand and it would serve them; nothing
    /// draws that many, and a browser handed them stops responding.
    /// </summary>
    public const int ViewportCountCeiling = 10_000;

    /// <summary>
    /// The two renderings asked for, hardcoded rather than configured, and the reason is a trap
    /// worth writing down: this product's advertised size list is not the set of sizes it actually
    /// serves, and asking for a size that was never written during indexing makes it decode a
    /// full-resolution original in software — on a machine with no graphics hardware, for every pin
    /// in a viewport. These two are among the few that both its source and its published table
    /// agree are always written. A size above the instance's configured limit is silently clamped
    /// rather than refused, so a wrong choice here fails invisibly rather than loudly.
    /// </summary>
    private const string SmallRendering = "tile_500";
    private const string LargeRendering = "fit_720";

    /// <summary>
    /// The header every answer carries the picture credential on, so a map pan refreshes it for
    /// free. Its format and its length are the far side's business and are read, never assumed —
    /// the vendor's own instruction, and a measured build has already changed it once.
    /// </summary>
    private const string PreviewTokenHeader = "X-Preview-Token";

    /// <summary>
    /// The rectangle the token check asks about: two degrees square, off the west coast of Africa,
    /// where no cave is.
    /// </summary>
    /// <remarks>
    /// An ordinary rectangle rather than a degenerate one, because it is the exact shape the map
    /// asks about on every pan and so is certainly a form the far side accepts. Nothing here rests
    /// on a zero-area rectangle being accepted, and a rectangle refused as malformed would be read
    /// as a bad token — a false alarm on every poll against a library that works perfectly.
    /// Measured against a running instance: this rectangle answers 200 with a valid credential and
    /// 401 without one, which is precisely the discrimination this call is here to make.
    /// </remarks>
    private static readonly Envelope TokenCheckRectangle = new(x1: -1, x2: 1, y1: -1, y2: 1);

    /// <summary>
    /// De-duplicates a concurrent fetch of the picture credential so a burst of balloon openings
    /// with no credential in hand costs one call rather than one each. <b>Not a throttle</b>: it is
    /// never held while a picture or a position query is in flight, and nothing else in this class
    /// waits on it.
    /// </summary>
    private readonly SemaphoreSlim previewTokenGate = new(1, 1);

    private volatile string? previewToken;

    /// <summary>
    /// Set once and never cleared except by an operator's recheck. See <see cref="ThumbnailAsync"/>.
    /// </summary>
    private volatile bool picturesStopped;

    /// <summary>
    /// The last thing the library said about itself, held for a short window. Process-wide for the
    /// same reason the picture credential is: a status line refreshed in two browsers must not
    /// become two rounds of requests against a container next door.
    /// </summary>
    private readonly LibraryHealthCache health = new();

    private PhotoPrismOptions Options => options.Value;

    public PhotoLibrarySource Source => PhotoLibrarySource.PhotoPrism;

    public bool IsConfigured => Options.IsConfigured;

    public bool PicturesAvailable => IsConfigured && !picturesStopped;

    /// <summary>
    /// What state the library is in, asked of the routes that describe it and of the one it answers
    /// positions from — and of nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three questions rather than one, because they fail separately and send an operator to
    /// different places: whether anything is listening at that address at all, what it says it is,
    /// and whether it accepts this installation's token. A container that is still starting and a
    /// token that expired look identical from one call and completely different from three.
    /// </para>
    /// <para>
    /// The second call is one the byte path already needs — it is where the picture credential
    /// comes from — so it refreshes that credential rather than adding a call of its own, and the
    /// first balloon opened after a probe costs nothing extra. The third exists because neither of
    /// the first two proves anything about the token; see the note where it is made.
    /// </para>
    /// <para>
    /// Nothing here names a permission. This product publishes no route saying what its access
    /// token may do, so no claim is made about it: an empty list of missing rights means nothing
    /// is known to be missing, never that everything is present.
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
        try
        {
            // Liveness, and deliberately the cheapest route this product has: it answers without
            // building the configuration document, so a container that is not up yet is told apart
            // from one that is up and refusing the token. Nothing in the answer is read — that it
            // answered as this product at all is the whole of the check.
            using (await SendJsonAsync(new Uri(BaseAddress(Options.BaseUrl), "api/v1/status"), ct))
            {
            }
        }
        catch (PhotoLibraryException e)
        {
            return LibraryHealth.DidNotAnswer(e.Code);
        }

        string? version;
        try
        {
            version = await ReadConfigurationAsync(ct);
        }
        catch (PhotoLibraryException e)
        {
            // It is answering; it would not answer this. Reported as reachable with the reason
            // beside it, because "the container is down" and "the answer was unreadable" are two
            // different afternoons for whoever has to fix it.
            return LibraryHealth.Answered(version: null, failureCode: e.Code);
        }

        try
        {
            // The only question here that the token has to be right to answer. Measured against a
            // running instance of this product: the liveness route and the configuration route both
            // answer 200 with no credential at all, and 200 with a wrong one — they even hand out a
            // picture credential to an anonymous caller — so neither of them can tell a working
            // token from an expired one. The route this feature actually reads positions from
            // refuses. Without this call a health line would go green for an installation whose
            // token expired, which is the exact failure it exists to catch: this product's tokens
            // last a year by default, so that is what a working installation turns into twelve
            // months after somebody set it up.
            //
            // A small ordinary rectangle and one photograph asked for: the smallest well-formed
            // form of the same question, and it resolves no file on disk, which is the one thing a
            // probe must never make this product do.
            using (await SendJsonAsync(GeoUrl(TokenCheckRectangle, count: 1), ct))
            {
            }
        }
        catch (PhotoLibraryException e)
        {
            return LibraryHealth.Answered(version, failureCode: e.Code);
        }

        return LibraryHealth.Answered(version);
    }

    /// <summary>
    /// Photographs inside a rectangle, asked of the library directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is cached. This library answers a rectangle natively, in one authenticated request,
    /// returning GeoJSON — so a viewport costs one call and always reflects what the library holds
    /// right now, and the moment the positions were read is always now.
    /// </para>
    /// <para>
    /// Both <c>count</c> and <c>quality</c> are sent on every call. The operation declares both
    /// required, and what the far end does when <c>quality</c> is absent has not been established,
    /// so it is stated rather than left to a default that may not exist.
    /// </para>
    /// </remarks>
    public async Task<LibraryPhotoPage> PhotosInAsync(Envelope bounds, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        await EnsureUsableAsync(ct);

        // Clamped here rather than trusted to the far end, which caps this at a hundred thousand
        // and would happily serve tens of thousands into a browser that cannot draw them.
        var count = Math.Clamp(Math.Min(limit, Options.MaxViewportCount), 1, ViewportCountCeiling);

        using var response = await SendJsonAsync(GeoUrl(bounds, count), ct);

        // The credential the picture routes need arrives on the answer to this call and refreshes
        // itself with every answer, so it is taken here rather than fetched separately. It never
        // leaves this process: the pictures are proxied, so no page ever carries it.
        CapturePreviewToken(response);

        var payload = await response.Content.ReadAsStringAsync(ct);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException e)
        {
            // A body that stopped halfway is a body that parses as nothing. Half a page of pins is
            // worse than none, because nothing on the screen says it is half.
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "The photo library's answer was not readable as JSON.", e);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                throw new PhotoLibraryException(
                    PhotoLibraryException.RejectedCode,
                    "The photo library did not answer with a feature collection.");
            }

            var photos = new List<LibraryPhoto>(features.GetArrayLength());

            foreach (var feature in features.EnumerateArray())
            {
                // The feature's own id is deliberately never read. This library serialises it as a
                // JSON string rather than as a number, so a reader expecting a number fails on the
                // first answer — and nothing here needs it: the photograph is named by its own
                // identifier property and the picture of it by the content hash beside it.
                if (TryReadFeature(feature, out var photo))
                {
                    photos.Add(photo);
                }
            }

            // Truncation is decided by what came back rather than by a header, because the header's
            // exact meaning is not settled and this reading holds under either: a full page is a
            // page that may have had more behind it. An answer longer than was asked for is also
            // truncated by this test, which is the safe way round.
            return new LibraryPhotoPage(photos, photos.Count >= count, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// The address of a position query. One place for it, because the viewport and the check that
    /// the token still works have to ask the library the same question — a check aimed at a
    /// different operation proves the credential opens a door nothing in this feature walks
    /// through.
    /// </summary>
    private Uri GeoUrl(Envelope bounds, int count) =>
        new(
            BaseAddress(Options.BaseUrl),
            "api/v1/geo?latlng=" + Uri.EscapeDataString(LatLng(bounds))
            + "&count=" + count.ToString(CultureInfo.InvariantCulture)
            + "&quality=" + Math.Clamp(Options.MinQuality, 0, 7).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The rectangle, in the order this library states one: north, east, south, west.
    /// </summary>
    /// <remarks>
    /// That is neither the order this application uses (west, south, east, north) nor the order
    /// GeoJSON positions use. Written once, here, and pinned by a test, because transposing two of
    /// these four numbers produces an empty map rather than an error — and an empty map is exactly
    /// what this feature looks like when it is merely switched off, so the defect would be invisible
    /// in the one place anybody would look for it. Public for that test alone; nothing else calls it.
    /// </remarks>
    public static string LatLng(Envelope bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{bounds.MaxY},{bounds.MaxX},{bounds.MinY},{bounds.MinX}");
    }

    /// <summary>
    /// This product matches text over what a photograph says about itself — its title, its caption,
    /// its keywords, and the labels its own classifier wrote — through the same search grammar its
    /// own interface uses, so what somebody types here means what it means over there.
    /// </summary>
    /// <remarks>
    /// Text and not meaning, and the difference is worth being plain about because the product does
    /// run a classifier of its own: what that classifier produces is a fixed vocabulary of words
    /// written onto a photograph, which is then matched as words like any other. Nothing here
    /// compares a sentence to a picture, so a search for something nobody wrote down finds nothing
    /// however well it describes what is in the library.
    /// </remarks>
    public LibrarySearchMatching SearchMatching => LibrarySearchMatching.Text;

    /// <summary>
    /// One page of the library, newest first, asked for without a rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A different route from the one the map uses, and deliberately. The route that takes a
    /// rectangle can only answer photographs that have a position, which is the smaller half of a
    /// club's library — everything taken underground, every scan, every camera with no receiver is
    /// missing from it. A list of the library has to show those, so it asks the route that knows
    /// about all of them, and that route takes no rectangle at all.
    /// </para>
    /// <para>
    /// Nothing is held between calls: one page is asked for, and it is gone when the response is
    /// written.
    /// </para>
    /// </remarks>
    public async Task<LibraryPhotoListPage> ListAsync(LibraryPhotoQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var answered = await PageAsync(query.Page, query.PageSize, words: null, ct);

        // The total is left unknown on purpose. This product does send a count beside a page, but
        // it counts what that page holds — a number the page already is — and nothing in the answer
        // says how many the library holds altogether. An unknown total said plainly is a better
        // answer than a number a reader cannot tell from a fact.
        return new LibraryPhotoListPage(
            answered.Photos, Total: null, answered.HasMore, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One page of what this library makes of a set of words.
    /// </summary>
    /// <remarks>
    /// The same route the listing uses, asked the same way with the words added, because on this
    /// product that <em>is</em> the search: there is no second question to put to it. What differs
    /// is what the answer is allowed to claim — a page of a listing is a page of the library, and a
    /// page of a search is a page of what one sentence matched — and that is carried in the two
    /// different records they come back in.
    /// </remarks>
    public async Task<LibraryPhotoSearchPage> SearchAsync(
        LibraryPhotoSearchQuery search, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(search);
        await EnsureUsableAsync(ct);

        var words = WordsOnly(search.Text);
        if (words is null)
        {
            // Somebody typed something this library could only have read as a filter, and taking
            // the filter out left no words at all. Nothing is asked, because the question that
            // would have gone out is "give me the library" — which would come back as a full page
            // under a heading saying it matched what they typed. An empty answer is the honest one:
            // there are no words here to match anything with.
            //
            // No time is stamped on it. Nothing was read, no socket was opened, and a page saying
            // when it was read from the library would be a false statement of fact on the one line
            // a reader would use to decide whether the library was reached at all.
            return new LibraryPhotoSearchPage(
                [], LibrarySearchMatching.Text, Searched: string.Empty, HasMore: false, ReadAt: null);
        }

        var answered = await PageAsync(search.Page, search.PageSize, words, ct);

        // The words as they were put, which is not always the words as they were typed: the
        // reduction below takes out the separator this product reads as naming one of its own
        // fields. Published so a surface can say what was asked when it differs from what is in the
        // box, rather than leaving a reader to conclude this application disagrees with the
        // product's own search box for no reason it can see.
        return new LibraryPhotoSearchPage(
            answered.Photos, LibrarySearchMatching.Text, words, answered.HasMore, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// One page of the library, newest first, with or without words to match.
    /// </summary>
    /// <remarks>
    /// One place for the question so the listing and the search cannot drift apart in what they ask
    /// for beyond the words: the order and the quality floor are decisions of this installation, and
    /// two copies of them would eventually disagree about which photographs a library is considered
    /// to hold depending on whether somebody had typed anything.
    /// </remarks>
    private async Task<(IReadOnlyList<LibraryListedPhoto> Photos, bool HasMore)> PageAsync(
        int page, int pageSize, string? words, CancellationToken ct)
    {
        await EnsureUsableAsync(ct);

        var count = Math.Clamp(pageSize, 1, ViewportCountCeiling);

        // Widened before it is multiplied, so a page number far past the end of any library
        // produces a large offset rather than a negative one: the far side answers an offset past
        // the end with an empty page, and would answer a negative one with nobody knows what.
        var offset = (int)Math.Clamp((Math.Max(1, page) - 1L) * count, 0, int.MaxValue);

        var url = new Uri(
            BaseAddress(Options.BaseUrl),
            "api/v1/photos?count=" + count.ToString(CultureInfo.InvariantCulture)
            + "&offset=" + offset.ToString(CultureInfo.InvariantCulture)
            // Newest first, asked for rather than assumed. This route takes an order and the one
            // the map uses does not, so without saying so the two surfaces would each be ordered by
            // whatever their own route happened to default to.
            + "&order=newest"
            // The same floor the map applies, so the two surfaces do not disagree about which
            // photographs this installation considers worth showing at all.
            + "&quality=" + Math.Clamp(Options.MinQuality, 0, 7).ToString(CultureInfo.InvariantCulture)
            // The reader's words, reduced to words first — see below for why that reduction is the
            // difference between a search box and a way of asking where a photograph was taken.
            + (words is null ? string.Empty : "&q=" + Uri.EscapeDataString(words)));

        using var response = await SendJsonAsync(url, ct);

        // Every answer from this product carries the picture credential, so listing the library
        // refreshes it exactly as a map pan does — which is what lets a grid of thumbnails load for
        // somebody who never opened the map.
        CapturePreviewToken(response);

        var payload = await response.Content.ReadAsStringAsync(ct);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
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
                throw new PhotoLibraryException(
                    PhotoLibraryException.RejectedCode,
                    "The photo library did not answer a listing with a list of photographs.");
            }

            // What the library handed over, before anything is dropped. It is a different number
            // from the one below it whenever a row cannot be read, and the two are not
            // interchangeable: this one is the library's answer to "was this page full", and the
            // other is how much of that answer this application could use.
            var handedOver = document.RootElement.GetArrayLength();

            var photos = new List<LibraryListedPhoto>(handedOver);

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (TryReadListed(element, out var photo))
                {
                    photos.Add(photo);
                }
            }

            // Rows this build cannot name both a photograph and a picture from are dropped, and a
            // page that lost rows shows fewer than the library sent. Said out loud rather than left
            // to be inferred from a short page: a page of sixty that arrives as five looks exactly
            // like a small library, and if a version change over there renames a field this reads,
            // whole pages empty while nothing anywhere says an assumption stopped holding.
            if (photos.Count < handedOver)
            {
                logger.LogWarning(
                    "The {Source} photo library answered {Question} with {HandedOver} rows, of which "
                    + "{Unreadable} named no photograph and picture this build could use; they are "
                    + "not shown.",
                    Source,
                    words is null ? "a listing" : "a search",
                    handedOver,
                    handedOver - photos.Count);
            }

            // A full page is a page that may have had more behind it, which is the safe way round:
            // offering a next page that turns out empty costs one request, while withholding one
            // hides the rest of the library behind a control that is not there.
            //
            // Counted against what the library handed over rather than against what survived the
            // reading, and that distinction is the whole of it: one unreadable row on an otherwise
            // full page would otherwise make this false, disable the next control, and put
            // everything past that offset out of reach with nothing on the screen saying the
            // listing stopped.
            return (photos, handedOver >= count);
        }
    }

    /// <summary>
    /// The reader's words, reduced to words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The parameter these words go into is not a text field.</b> This product parses it into
    /// the same form its request parameters bind to, so a <c>name:value</c> pair typed into a
    /// search box sets a field of that form rather than matching anything — and every family of
    /// field is reachable that way. That includes the two this call sets deliberately a few lines
    /// above, so a pair could undo the quality floor this installation applies or the order its
    /// paging depends on; it includes the fields naming what the library considers not for general
    /// viewing; and, worst of all here, it includes every field naming a place.
    /// </para>
    /// <para>
    /// That last one is why this exists rather than being left to a length check. A search that
    /// passed the text through would let anybody narrow it to a circle around a point and read a
    /// photograph's coordinate off the result to whatever precision they had patience for — on a
    /// surface whose whole premise is that it carries no position at all. A premise that a search
    /// box can undo is not a premise.
    /// </para>
    /// <para>
    /// So the separator that makes a pair is taken out and what is left is words. Nothing is
    /// refused: somebody who typed a colon was searching for something, and on a surface that has
    /// no filters the honest reading of their text is the words in it. What is left is carried back
    /// on the answer, though, because a search that was quietly rewritten on the way out answers a
    /// different question from the one still showing in the box — and a person who knows this
    /// product's own grammar would otherwise have no way to learn why it behaves differently here.
    /// Null when nothing is left, which the caller answers with nothing found rather than by asking
    /// for the whole library — words that were never sent must not come back as a page that looks
    /// like they matched everything.
    /// </para>
    /// </remarks>
    private static string? WordsOnly(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var words = string.Join(
            ' ', text.Replace(':', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return words.Length == 0 ? null : words;
    }

    /// <summary>
    /// Everything this library will say about one photograph, or null when it reports none under
    /// that identifier.
    /// </summary>
    /// <remarks>
    /// The identifier asked for is the photograph's own, which on this product is not the string
    /// its pictures are fetched with: that one is a hash of the picture's contents. It is read back
    /// out of this answer, so a detail panel shows the larger rendering without a second question.
    /// </remarks>
    public async Task<LibraryPhotoDetail?> DetailAsync(string photographId, CancellationToken ct)
    {
        await EnsureUsableAsync(ct);

        if (!PhotoLibraryHttp.IsSafeReference(photographId))
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "That is not an identifier this application will ask a photo library about.");
        }

        var url = new Uri(BaseAddress(Options.BaseUrl), $"api/v1/photos/{photographId}");

        // A photograph the library does not report is an answer rather than a failure: it may have
        // been deleted or re-identified over there since the page listing it was drawn, and telling
        // a reader the library is broken would send them looking for a fault nobody has.
        using var response = await SendJsonAsync(url, missingIsAnswer: true, ct);
        if (response is null)
        {
            return null;
        }

        CapturePreviewToken(response);

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
            return TryReadDetail(document.RootElement, photographId, out var detail) ? detail : null;
        }
    }

    /// <summary>
    /// One row of a listing. A row this application cannot name both a photograph and a picture
    /// from is left out rather than drawn as a gap: every address built from it later needs both.
    /// </summary>
    private static bool TryReadListed(JsonElement element, out LibraryListedPhoto photo)
    {
        photo = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var uid = Text(element, "UID");
        var hash = PictureHash(element);

        // Both end up in a request path, so both are checked against what this application is
        // willing to send rather than passed on as they were written.
        if (!PhotoLibraryHttp.IsSafeReference(uid) || !PhotoLibraryHttp.IsSafeReference(hash))
        {
            return false;
        }

        photo = new LibraryListedPhoto(
            PhotographId: uid!,
            Reference: hash!,
            Title: Text(element, "Title"),
            TakenAt: Moment(element, "TakenAt"),
            Kind: KindOf(element));

        return true;
    }

    private static bool TryReadDetail(
        JsonElement element, string photographId, out LibraryPhotoDetail? detail)
    {
        detail = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hash = PictureHash(element);
        if (!PhotoLibraryHttp.IsSafeReference(hash))
        {
            // The photograph is there and no picture of it can be asked for, which is not a detail
            // panel: every field below would be rendered around an empty frame.
            return false;
        }

        detail = new LibraryPhotoDetail(
            PhotographId: photographId,
            Reference: hash!,
            Title: Text(element, "Title"),
            // Two names, because this product keeps a short line and a longer one and either may be
            // the only thing anybody wrote. Neither is invented from the other.
            Description: Text(element, "Description") ?? Text(element, "Caption"),
            TakenAt: Moment(element, "TakenAt"),
            Kind: KindOf(element),
            CameraMake: Text(element, "CameraMake"),
            CameraModel: Text(element, "CameraModel"),
            Lens: Text(element, "LensModel"),
            Aperture: Number(element, "FNumber"),
            ShutterSpeed: Text(element, "Exposure"),
            Iso: Whole(element, "Iso"),
            FocalLengthMm: Number(element, "FocalLength"));

        return true;
    }

    /// <summary>
    /// The string this product's pictures are fetched with: a hash of the picture's own contents,
    /// which it states beside the photograph and, on a fuller answer, on each file under it.
    /// </summary>
    /// <remarks>
    /// The primary file is the one wanted where there are several — a photograph here can carry a
    /// raw original, a sidecar and a rendering, and only one of them is what its own interface
    /// shows. Where nothing is marked primary the first file naming a hash is taken.
    /// </remarks>
    private static string? PictureHash(JsonElement element)
    {
        if (Text(element, "Hash") is { } stated)
        {
            return stated;
        }

        if (!element.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? first = null;

        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object || Text(file, "Hash") is not { } hash)
            {
                continue;
            }

            if (file.TryGetProperty("Primary", out var primary)
                && primary.ValueKind == JsonValueKind.True)
            {
                return hash;
            }

            first ??= hash;
        }

        return first;
    }

    /// <summary>
    /// What the library says a photograph is. A kind is stated only when it is not a plain image,
    /// so its absence is not a statement that it is one — which is why this stays three-valued all
    /// the way to the screen.
    /// </summary>
    private static LibraryPhotoKind? KindOf(JsonElement element) =>
        Text(element, "Type") is { } type
        && string.Equals(type, "video", StringComparison.OrdinalIgnoreCase)
            ? LibraryPhotoKind.Video
            : null;

    /// <summary>A string the library wrote, or null where it wrote nothing worth showing.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static DateTimeOffset? Moment(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out var when)
            ? when
            : null;

    /// <summary>
    /// A measurement the library states, or null. Zero is read as unsaid rather than as a value:
    /// this product writes a zero into these fields for a picture whose own metadata carried
    /// nothing, and a panel reporting an aperture of zero would state something nobody measured.
    /// </summary>
    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var read)
        && read > 0
            ? read
            : null;

    /// <summary>A whole measurement, on the same reading of zero.</summary>
    private static int? Whole(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var read)
        && read > 0
            ? read
            : null;

    /// <summary>
    /// One rendering's bytes. Exactly one attempt, no retry and no backoff, whatever
    /// <c>MaxRetries</c> says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the path that can destroy the operator's library. When this product cannot resolve a
    /// file on disk while serving a picture, it marks the file missing, and when a photograph has no
    /// surviving files it deletes the photograph from its index — on a GET, with no indexing run
    /// involved. With the originals mount absent, a read-only map viewport is enough to purge
    /// photographs. So: one attempt, and the first answer that is not a picture closes this path for
    /// the whole process until an operator rechecks. Nothing reopens it on a timer, because an
    /// automatic re-probe against a detached drive is the deletion loop with a schedule attached.
    /// </para>
    /// <para>
    /// The status code is not the signal. This product answers a failed picture request with a
    /// placeholder drawing carrying HTTP 200, so a proxy checking only the status would go on
    /// requesting while the library deleted itself.
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

        var token = await PreviewTokenAsync(ct);
        var rendering = size == LibraryThumbnailSize.Large ? LargeRendering : SmallRendering;

        var url = new Uri(
            BaseAddress(Options.BaseUrl),
            $"api/v1/t/{reference}/{token}/{rendering}");

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
    /// One request, and deliberately to the configuration route rather than to a picture: that route
    /// proves the address and the credential without asking the library to resolve a single file on
    /// disk, which is the act that deletes photographs when the disk is not there. It also refreshes
    /// the picture credential, so the first balloon after a recheck costs nothing extra. Whether the
    /// originals are back is then answered by the next real picture request, which is one request
    /// against one photograph rather than a sweep.
    /// </remarks>
    public async Task RecheckOriginalsAsync(CancellationToken ct)
    {
        await EnsureUsableAsync(ct);

        try
        {
            await FetchPreviewTokenAsync(ct);

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

    private static bool TryReadFeature(JsonElement feature, out LibraryPhoto photo)
    {
        photo = default;

        if (!feature.TryGetProperty("geometry", out var geometry)
            || !geometry.TryGetProperty("coordinates", out var coordinates)
            || coordinates.ValueKind != JsonValueKind.Array
            || coordinates.GetArrayLength() < 2
            || coordinates[0].ValueKind != JsonValueKind.Number
            || coordinates[1].ValueKind != JsonValueKind.Number
            || !feature.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var uid = properties.TryGetProperty("UID", out var u) ? u.GetString() : null;
        var hash = properties.TryGetProperty("Hash", out var h) ? h.GetString() : null;

        // Both end up in a request path, so both are checked against what this application is
        // willing to send rather than passed on as written.
        if (!PhotoLibraryHttp.IsSafeReference(uid) || !PhotoLibraryHttp.IsSafeReference(hash))
        {
            return false;
        }

        // A type is present only when the picture is not a plain image, so its absence is not a
        // statement that it is one — which is why the kind stays nullable all the way to the balloon.
        LibraryPhotoKind? kind = properties.TryGetProperty("Type", out var t)
            ? string.Equals(t.GetString(), "video", StringComparison.OrdinalIgnoreCase)
                ? LibraryPhotoKind.Video
                : null
            : null;

        photo = new LibraryPhoto(
            ForeignId: uid!,
            Reference: hash!,
            Longitude: coordinates[0].GetDouble(),
            Latitude: coordinates[1].GetDouble(),
            TakenAt: properties.TryGetProperty("TakenAt", out var taken)
                && taken.ValueKind == JsonValueKind.String
                && taken.TryGetDateTimeOffset(out var when) ? when : null,
            Title: properties.TryGetProperty("Title", out var title)
                && title.ValueKind == JsonValueKind.String ? title.GetString() : null,
            Kind: kind);

        return true;
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
    /// The credential the pictures are served under goes with the rest. It was minted for this
    /// installation's use of the library, it is what makes a preview address work for anyone who
    /// holds it, and a library this installation has stopped using has no business leaving one
    /// resident. Stopping and forgetting are deliberately the same act here. It changes nothing on
    /// the far side: the library still holds everything it held, and still serves anyone who signs
    /// into it directly.
    /// </remarks>
    public void Forget()
    {
        previewToken = null;
        health.Clear();
    }

    /// <summary>The address, with the trailing slash a relative path needs to be appended rather than to replace.</summary>
    private static Uri BaseAddress(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        // Ours, not theirs, and clamped so a nonsensical configured value cannot disable it.
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(Options.TimeoutSeconds, 5, 120));
        return client;
    }

    private void Authorise(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.AccessToken);

    /// <summary>
    /// Sends one question asked in JSON, retrying only what is worth retrying.
    /// </summary>
    /// <remarks>
    /// Retries live here and nowhere else. The byte path never reaches this method, and that
    /// separation is the guard rather than a tidiness: a retry against a library that cannot reach
    /// its originals is a second deletion.
    /// </remarks>
    private async Task<HttpResponseMessage> SendJsonAsync(Uri url, CancellationToken ct) =>
        // Never null: only a caller that says a missing thing is an answer can be given one.
        (await SendJsonAsync(url, missingIsAnswer: false, ct))!;

    /// <summary>
    /// The same question, for a caller asking about one named thing that may not be there.
    /// </summary>
    /// <remarks>
    /// <paramref name="missingIsAnswer"/> turns the far side's "no such thing" into a null rather
    /// than a refusal, and only for a caller that asked for it. It is not leniency: a photograph
    /// deleted or re-identified over there is an ordinary answer somebody has to be shown, while
    /// the same status from a route that names no particular thing means this application asked a
    /// question the library does not understand — which is a defect here and must stay loud.
    /// </remarks>
    private async Task<HttpResponseMessage?> SendJsonAsync(
        Uri url, bool missingIsAnswer, CancellationToken ct)
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

    private void CapturePreviewToken(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(PreviewTokenHeader, out var values))
        {
            var value = values.FirstOrDefault();

            // Whatever shape it is this month, it still has to survive being put in a path.
            if (PhotoLibraryHttp.IsSafeReference(value))
            {
                previewToken = value;
            }
        }
    }

    /// <summary>
    /// The credential the picture routes need, fetched only when none is in hand.
    /// </summary>
    /// <remarks>
    /// A picture request can arrive with no position query behind it — a reload opens a balloon
    /// before anything panned — so this fetches one rather than failing. Its shape and its length
    /// are never assumed: a measured build answers with a shorter value than the vendor's own
    /// documentation implies, which is why the vendor's instruction is to read the current value
    /// rather than to derive it.
    /// </remarks>
    private async Task<string> PreviewTokenAsync(CancellationToken ct)
    {
        if (previewToken is { } held)
        {
            return held;
        }

        await previewTokenGate.WaitAsync(ct);
        try
        {
            return previewToken ?? await FetchPreviewTokenAsync(ct);
        }
        finally
        {
            previewTokenGate.Release();
        }
    }

    private async Task<string> FetchPreviewTokenAsync(CancellationToken ct)
    {
        await ReadConfigurationAsync(ct);

        return previewToken ?? throw new PhotoLibraryException(
            PhotoLibraryException.RejectedCode,
            "The photo library did not say what credential its pictures are served under.");
    }

    /// <summary>
    /// Reads the configuration answer, returning the version it names and taking the picture
    /// credential from it. Null version where it names none.
    /// </summary>
    /// <remarks>
    /// One place for this call, shared by the byte path and the health probe, because it is the
    /// same call: the credential the pictures need and the version an operator quotes in a bug
    /// report arrive in the same answer, and asking twice for the halves would double what this
    /// installation costs its neighbour for no gain.
    /// </remarks>
    private async Task<string?> ReadConfigurationAsync(CancellationToken ct)
    {
        var url = new Uri(BaseAddress(Options.BaseUrl), "api/v1/config");
        using var response = await SendJsonAsync(url, ct);

        // The answer carries the credential in the same header as every other answer; the body is
        // read for the version, and for the credential when the header did not carry one.
        CapturePreviewToken(response);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException e)
        {
            throw new PhotoLibraryException(
                PhotoLibraryException.RejectedCode,
                "The photo library's configuration was not readable as JSON.", e);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (previewToken is null
                && document.RootElement.TryGetProperty("previewToken", out var value)
                && value.ValueKind == JsonValueKind.String
                && PhotoLibraryHttp.IsSafeReference(value.GetString()))
            {
                previewToken = value.GetString();
            }

            // Read as whatever string the product writes and never parsed into parts: this one
            // states a build rather than a number, and a reader that split it on dots would turn a
            // line an operator quotes into a line that lies.
            return document.RootElement.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
    }
}
