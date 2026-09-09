// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// The library that cannot be asked about a rectangle, and the reading of its whole located
/// contents that stands in for one.
///
/// <para>
/// No database and no host: the client is built directly over a recording stub, because what is
/// under test is when a socket is opened and what leaves the machine when one is. That is the whole
/// of the difficulty here. This library publishes no way to ask what is inside a rectangle — the
/// one route it has that takes one is marked by its own authors as not part of the contract — so
/// every located photograph's position is read in one answer, held, and scanned in process. The
/// consequences are a freshness interval, a reading served while it is being replaced, a refusal
/// when the library outgrows what this installation will hold, and a pause after a failure. Each of
/// those has a way of being wrong that looks exactly like the feature working, which is why the
/// count of calls is asserted as often as the answer is.
/// </para>
/// <para>
/// Every identifier, coordinate and rectangle below is invented. Nothing here comes from any real
/// library, and no coordinate names a real place.
/// </para>
/// </summary>
public sealed class ImmichPhotoLibraryTests
{
    private const string FakeKey = "not-a-real-key-0000";
    private const string LibraryAddress = "http://photo-library.invalid:2283";

    /// <summary>Invented identifiers of the shape this library mints. Distinct in their first characters, so an ordering is visible.</summary>
    private const string First = "11111111-1111-4111-8111-111111111111";
    private const string Second = "22222222-2222-4222-8222-222222222222";
    private const string Third = "33333333-3333-4333-8333-333333333333";

    /// <summary>An invented rectangle whose four numbers all differ, so a transposition shows.</summary>
    private static Envelope Rectangle => new(x1: 21.5, x2: 24.25, y1: 45.125, y2: 46.75);

    // ------------------------------------------------------------------ absent rather than broken

    /// <summary>
    /// An installation that has been given no library is a supported installation. The proof that
    /// matters is the last line: not one socket was opened, on either path.
    /// </summary>
    [Fact]
    public async Task With_no_library_configured_nothing_is_asked_of_anybody()
    {
        var stub = new LibraryStub();
        var library = Client(stub, options => options.Enabled = false);

        library.IsConfigured.ShouldBeFalse();
        library.PicturesAvailable.ShouldBeFalse();

        (await Refused(() => library.PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);
        (await Refused(() => library.ThumbnailAsync(First, LibraryThumbnailSize.Large, null, default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        stub.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------- what leaves this machine

    /// <summary>
    /// One request for the whole located library, and the filtering done here.
    /// </summary>
    /// <remarks>
    /// The absence of a rectangle in what leaves the machine is asserted, not implied: this library
    /// does have a route that takes one, and it is the one thing this client must never be quietly
    /// rewritten to call, because its own authors mark it as carrying no promise to anybody outside
    /// the product.
    /// </remarks>
    [Fact]
    public async Task The_whole_located_library_is_read_in_one_request_and_the_rectangle_is_applied_here()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers($$"""
        [{"id":"{{First}}","lat":45.5,"lon":22.5,"city":null,"state":null,"country":null},
         {"id":"{{Second}}","lat":10.25,"lon":10.75,"city":null,"state":null,"country":null}]
        """);

        var page = await Client(stub).PhotosInAsync(Rectangle, 100, default);

        // One inside the rectangle, one far outside it, and the far one is not answered — so the
        // filtering really happened rather than the whole library being handed on.
        page.Photos.Count.ShouldBe(1);
        page.Photos[0].Reference.ShouldBe(First);
        page.Photos[0].ForeignId.ShouldBe(First);
        page.Photos[0].Longitude.ShouldBe(22.5);
        page.Photos[0].Latitude.ShouldBe(45.5);
        page.Truncated.ShouldBeFalse();

        // This library's position feed carries no date, no title and no type. Null here means the
        // library did not say, and must never be read as "a photograph with no title".
        page.Photos[0].TakenAt.ShouldBeNull();
        page.Photos[0].Title.ShouldBeNull();
        page.Photos[0].Kind.ShouldBeNull();

        var asked = stub.Only.Url;
        asked.ShouldContain("/api/map/markers");
        asked.ShouldContain("isArchived=false");
        asked.ShouldContain("withPartners=true");
        asked.ShouldContain("withSharedAlbums=true");
        asked.ShouldNotContain("bbox");

        // The credential travels as a header and never as a query parameter: this library accepts
        // one there too, and a whole-library credential in an address is a credential in browser
        // history, in a referer and in every log between here and there.
        stub.Only.ApiKey.ShouldBe(FakeKey);
        asked.ShouldNotContain(FakeKey);
    }

    // ------------------------------------------------------------------------ the reading in hand

    /// <summary>
    /// The point of holding the positions: a second viewport costs no network at all.
    /// </summary>
    [Fact]
    public async Task A_reading_younger_than_the_interval_answers_a_viewport_with_no_network()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(OnePhotograph);
        var library = Client(stub, options => options.PositionCacheSeconds = 300);

        var first = await library.PhotosInAsync(Rectangle, 100, default);
        var second = await library.PhotosInAsync(Rectangle, 100, default);

        second.Photos.Count.ShouldBe(1);
        stub.Calls.Count.ShouldBe(1);

        // The same reading, and it says so: the moment travels with the answer, because a stale
        // coordinate that looks live is what this field exists to prevent.
        second.ReadAt.ShouldBe(first.ReadAt);
    }

    /// <summary>
    /// An interval of zero re-reads on every viewport and waits for it. It is a real setting for a
    /// very small library — not a switch that only means something in a test — which is why the
    /// tests below can use it without reaching inside anything.
    /// </summary>
    [Fact]
    public async Task An_interval_of_zero_re_reads_on_every_viewport()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(OnePhotograph);
        var library = Client(stub, options => options.PositionCacheSeconds = 0);

        await library.PhotosInAsync(Rectangle, 100, default);
        await library.PhotosInAsync(Rectangle, 100, default);

        stub.Calls.Count.ShouldBe(2);
    }

    /// <summary>
    /// A reading past its interval is served <em>at once</em> and replaced behind the answer, so
    /// panning never waits on a neighbour.
    /// </summary>
    /// <remarks>
    /// The assertion that carries this is that the answer arrives while the library is still being
    /// read: the stub holds its second answer open, and a viewport asked during that is answered
    /// from the reading already in hand rather than blocking on the one in flight. Without the hold
    /// this test would pass just as happily against a client that waited, because a fast stub
    /// answers before anybody could tell.
    /// </remarks>
    [Fact]
    public async Task A_reading_past_its_interval_is_served_at_once_and_replaced_behind_the_answer()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(OnePhotograph);
        var library = Client(stub, options => options.PositionCacheSeconds = 1);

        var first = await library.PhotosInAsync(Rectangle, 100, default);
        first.Photos.Count.ShouldBe(1);

        var release = stub.HoldsTheNextAnswer();
        stub.AnswersMarkers(TwoPhotographs);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        // Past the interval, with the next answer held open: this must not wait for it. Bounded so
        // that a client which did wait fails here saying so, rather than hanging on an answer this
        // test is holding and taking the whole run with it.
        var stale = await library.PhotosInAsync(Rectangle, 100, default)
            .WaitAsync(TimeSpan.FromSeconds(5));
        stale.Photos.Count.ShouldBe(1);
        stale.ReadAt.ShouldBe(first.ReadAt);

        await WaitUntil(() => stub.Calls.Count == 2);

        // And a second viewport arriving while the same read is still in flight is answered from
        // the same held reading rather than queueing behind it or starting another one.
        (await library.PhotosInAsync(Rectangle, 100, default)).ReadAt.ShouldBe(first.ReadAt);
        stub.Calls.Count.ShouldBe(2);

        release.SetResult();

        await WaitUntil(async () => (await library.PhotosInAsync(Rectangle, 100, default)).Photos.Count == 2);
    }

    // ------------------------------------------------------------------------------- the ceiling

    /// <summary>
    /// The far end applies no limit to this answer — no page size, no offset, no rectangle — so the
    /// configured one is the only limit there is. Past it the answer is refused with a code of its
    /// own and nothing is drawn.
    /// </summary>
    /// <remarks>
    /// Truncating instead would drop whichever photographs happened to arrive last and produce a
    /// map silently missing whole regions — a wrong answer that looks exactly like a right one,
    /// which is the failure this project has catalogued and the reason this is a refusal.
    /// </remarks>
    [Fact]
    public async Task A_library_larger_than_this_installation_will_hold_is_refused_rather_than_truncated()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(TwoPhotographs);
        var library = Client(stub, options =>
        {
            options.MaxPositions = 1;
            options.PositionCacheSeconds = 0;
        });

        (await Refused(() => library.PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.TooLargeCode);

        // Nothing partial was kept: the viewport after it does not answer with the one photograph
        // that fitted. A truncated reading held quietly is the whole failure this refusal avoids.
        await Should.ThrowAsync<PhotoLibraryException>(
            () => library.PhotosInAsync(Rectangle, 100, default));
    }

    /// <summary>A library exactly at the ceiling is read, so the refusal is the limit and not one short of it.</summary>
    [Fact]
    public async Task A_library_exactly_at_the_ceiling_is_read()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(TwoPhotographs);
        var library = Client(stub, options => options.MaxPositions = 2);

        (await library.PhotosInAsync(Rectangle, 100, default)).Photos.Count.ShouldBe(2);
    }

    /// <summary>
    /// This installation's own point limit stops the answer short of the reading, and says so — and
    /// picks the same photographs every time, because the reading is ordered once when it is taken.
    /// </summary>
    /// <remarks>
    /// An unordered cap would hand a different arbitrary subset back on every pan, so features
    /// would flicker in and out of a map on which nothing had changed.
    /// </remarks>
    [Fact]
    public async Task A_point_limit_stops_the_answer_short_and_picks_the_same_photographs_every_time()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(ThreePhotographs);
        var library = Client(stub, options => options.PositionCacheSeconds = 300);

        var first = await library.PhotosInAsync(Rectangle, 2, default);
        var again = await library.PhotosInAsync(Rectangle, 2, default);

        first.Photos.Count.ShouldBe(2);
        first.Truncated.ShouldBeTrue();
        again.Photos.Select(p => p.Reference).ShouldBe(first.Photos.Select(p => p.Reference));
    }

    // ------------------------------------------------------------ what the far end does to itself

    /// <summary>
    /// A library that stopped answering while a reading is in hand keeps the reading. The map draws
    /// older positions and says how old they are, which is a different and truer answer than a map
    /// that goes blank — an empty overlay cannot be told apart from a valley nobody has
    /// photographed.
    /// </summary>
    [Fact]
    public async Task A_re_reading_that_failed_keeps_the_reading_it_had()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers(OnePhotograph);
        var library = Client(stub, options =>
        {
            options.PositionCacheSeconds = 0;
            options.MaxRetries = 0;
        });

        var first = await library.PhotosInAsync(Rectangle, 100, default);
        stub.AnswersStatus(HttpStatusCode.InternalServerError);

        var kept = await library.PhotosInAsync(Rectangle, 100, default);
        kept.Photos.Count.ShouldBe(1);
        kept.ReadAt.ShouldBe(first.ReadAt);
        stub.Calls.Count.ShouldBe(2);

        // And the viewport after that opens no socket at all: shortly after a failure the reading
        // in hand is served as it stands, so one stopped container does not cost every pan by every
        // viewer a fresh timeout.
        (await library.PhotosInAsync(Rectangle, 100, default)).ReadAt.ShouldBe(first.ReadAt);
        stub.Calls.Count.ShouldBe(2);
    }

    /// <summary>
    /// A library that has never answered fails the viewport, and then stops being asked for a
    /// while. The count of calls is the assertion: one, and still one.
    /// </summary>
    [Fact]
    public async Task A_library_that_never_answered_says_so_and_is_then_left_alone_for_a_while()
    {
        var stub = new LibraryStub();
        stub.AnswersStatus(HttpStatusCode.InternalServerError);
        var library = Client(stub, options => options.MaxRetries = 0);

        (await Refused(() => library.PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.UnavailableCode);
        stub.Calls.Count.ShouldBe(1);

        (await Refused(() => library.PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.UnavailableCode);
        stub.Calls.Count.ShouldBe(1);
    }

    /// <summary>A refused credential is told apart from a library in trouble, and is never tried again.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_credential_is_not_retried_and_names_itself(HttpStatusCode status)
    {
        var stub = new LibraryStub();
        stub.AnswersStatus(status);
        var library = Client(stub, options => options.MaxRetries = 3);

        (await Refused(() => library.PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.UnauthorizedCode);
        stub.Calls.Count.ShouldBe(1);
    }

    /// <summary>
    /// A position this build cannot name a photograph from is dropped and the rest are drawn. It is
    /// not a failure of the request: every address built from an identifier assumes the shape this
    /// build expects, so an identifier of another shape is unusable rather than alarming — and the
    /// photograph that was well formed is still there, because a reading that came back empty would
    /// prove nothing about which half failed.
    /// </summary>
    [Fact]
    public async Task A_position_this_build_cannot_name_a_photograph_from_is_dropped_and_the_rest_are_drawn()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers($$"""
        [{"id":"not-an-identifier","lat":45.5,"lon":22.5},
         {"id":"{{Second}}","lat":45.6,"lon":22.6}]
        """);

        var page = await Client(stub).PhotosInAsync(Rectangle, 100, default);

        page.Photos.Count.ShouldBe(1);
        page.Photos[0].Reference.ShouldBe(Second);
    }

    /// <summary>
    /// A body that stopped halfway is a body that parses as nothing. Half a library's positions is
    /// worse than none, because nothing on the screen would say it is half.
    /// </summary>
    [Fact]
    public async Task An_answer_that_stopped_halfway_is_refused_rather_than_half_drawn()
    {
        var stub = new LibraryStub();
        stub.AnswersMarkers($$"""[{"id":"{{First}}","lat":45.5,"lon":22.5},{"id":"{{Second}}","la""");

        (await Refused(() => Client(stub).PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);
    }

    /// <summary>A library answering a question with something other than the document it was asked for is a change on the far side, not a viewport to draw.</summary>
    [Fact]
    public async Task An_answer_that_is_not_the_document_that_was_asked_for_is_refused()
    {
        var markup = new LibraryStub();
        markup.Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>the library is having trouble</html>", Encoding.UTF8, "text/html"),
        });

        (await Refused(() => Client(markup).PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        var notAList = new LibraryStub();
        notAList.AnswersMarkers("""{"markers":[]}""");

        (await Refused(() => Client(notAList).PhotosInAsync(Rectangle, 100, default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);
    }

    // --------------------------------------------------------------------------- the picture path

    /// <summary>
    /// A picture is asked for by the name this library gives it, at a rendering named rather than
    /// measured: the sizes behind these names are the far side's to configure, so nothing here
    /// assumes a dimension.
    /// </summary>
    [Theory]
    [InlineData(LibraryThumbnailSize.Large, "size=preview")]
    [InlineData(LibraryThumbnailSize.Small, "size=thumbnail")]
    public async Task A_picture_is_asked_for_by_the_name_the_library_gives_it(
        LibraryThumbnailSize size, string expected)
    {
        var stub = new LibraryStub();
        stub.AnswersPicture(InventedJpeg);

        await using var picture = await Client(stub).ThumbnailAsync(First, size, "\"invented-etag\"", default);

        picture.ContentType.ShouldBe("image/jpeg");
        stub.Only.Url.ShouldContain($"/api/assets/{First}/thumbnail");
        stub.Only.Url.ShouldContain(expected);
        stub.Only.ApiKey.ShouldBe(FakeKey);

        // The browser's own validator goes to the library unchanged, so a balloon opened twice
        // costs a conditional request and no image bytes.
        stub.Only.IfNoneMatch.ShouldBe("\"invented-etag\"");
    }

    /// <summary>
    /// The guard that is not about privacy. A library whose originals have gone out from under it
    /// answers a picture request by writing off what it cannot find, so an answer that was not a
    /// picture closes the byte path for the whole process and a second request must not reopen it.
    /// The count of calls is the assertion: one, then still one, and only a deliberate recheck adds
    /// another.
    /// </summary>
    [Fact]
    public async Task An_answer_that_was_not_a_picture_stops_every_later_one_until_a_recheck()
    {
        var stub = new LibraryStub();
        var library = Client(stub);

        // What a library that has lost the disk its originals live on answers: a drawing, not an
        // error, and with a perfectly successful status code.
        stub.AnswersPicture(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "image/svg+xml");

        (await Refused(() => library.ThumbnailAsync(First, LibraryThumbnailSize.Large, null, default)))
            .ShouldBe(PhotoLibraryException.OriginalsUnavailableCode);
        stub.Calls.Count.ShouldBe(1);
        library.PicturesAvailable.ShouldBeFalse();

        // The second request is what this guard exists to prevent. It must not happen.
        (await Refused(() => library.ThumbnailAsync(First, LibraryThumbnailSize.Large, null, default)))
            .ShouldBe(PhotoLibraryException.OriginalsUnavailableCode);
        stub.Calls.Count.ShouldBe(1);

        // Nothing reopens it on a timer. A person does — and the recheck itself asks the library
        // about the credential rather than about a picture, so it proves the address and the key
        // without asking it to resolve a single file on disk.
        stub.AnswersJson("""{"name":"an invented key","permissions":["map.read","asset.view","asset.read"]}""");
        await library.RecheckOriginalsAsync(default);

        library.PicturesAvailable.ShouldBeTrue();
        stub.Calls.Count.ShouldBe(2);
        stub.Calls[1].Url.ShouldNotContain("/thumbnail");

        stub.AnswersPicture(InventedJpeg);
        await using var reopened = await library.ThumbnailAsync(First, LibraryThumbnailSize.Large, null, default);
        reopened.ContentType.ShouldBe("image/jpeg");
        stub.Calls.Count.ShouldBe(3);
    }

    /// <summary>
    /// A reference that could ask a different question never reaches the library. Checked again
    /// here even though the route checked it: these strings are interpolated into an address, and
    /// two guards on one value is the right number when the value is somebody else's.
    /// </summary>
    [Fact]
    public async Task A_reference_that_could_ask_a_different_question_is_refused_before_any_call()
    {
        var stub = new LibraryStub();

        (await Refused(() => Client(stub).ThumbnailAsync(
            "../../etc/passwd", LibraryThumbnailSize.Large, null, default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        stub.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------------ support

    private static ImmichClient Client(LibraryStub stub, Action<ImmichOptions>? configure = null)
    {
        var options = new ImmichOptions
        {
            Enabled = true,
            BaseUrl = LibraryAddress,
            ApiKey = FakeKey,
        };
        configure?.Invoke(options);

        return new ImmichClient(
            new OneClient(stub),
            Options.Create(options),
            new NothingStopped(),
            new NeverStopping(),
            NullLogger<ImmichClient>.Instance);
    }

    /// <summary>The code a call was refused with, so a test names the sentence an operator is sent.</summary>
    private static async Task<string> Refused(Func<Task> call) =>
        (await Should.ThrowAsync<PhotoLibraryException>(call)).Code;

    private static Task WaitUntil(Func<bool> settled) => WaitUntil(() => Task.FromResult(settled()));

    /// <summary>
    /// Waits for something a detached read will make true. Polled rather than signalled: what is
    /// being waited for is a task this client started on its own, deliberately not tied to the
    /// request that noticed the reading was old.
    /// </summary>
    private static async Task WaitUntil(Func<Task<bool>> settled)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await settled())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The reading behind the answer never arrived.");
    }

    private static readonly byte[] InventedJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private const string OnePhotograph = $$"""[{"id":"{{First}}","lat":45.5,"lon":22.5}]""";

    private const string TwoPhotographs = $$"""
    [{"id":"{{First}}","lat":45.5,"lon":22.5},{"id":"{{Second}}","lat":45.6,"lon":22.6}]
    """;

    private const string ThreePhotographs = $$"""
    [{"id":"{{First}}","lat":45.5,"lon":22.5},{"id":"{{Second}}","lat":45.6,"lon":22.6},
     {"id":"{{Third}}","lat":45.7,"lon":22.7}]
    """;

    private sealed record LibraryCall(string Method, string Url, string? ApiKey, string? IfNoneMatch);

    /// <summary>A factory that hands out one client, over the stub a test supplied.</summary>
    /// <summary>
    /// A brake nobody has pulled, so these cases exercise a library this installation is using.
    /// The client asks it before every outgoing call, which is what makes a stopped library cost no
    /// network on any path.
    /// </summary>
    private sealed class NothingStopped : IPhotoLibraryBrake
    {
        public ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// An application that never stops, so a detached reading is not cancelled out from under a
    /// test by a lifetime nobody started.
    /// </summary>
    private sealed class NeverStopping : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    /// <summary>
    /// Stands in for the library, recording everything that would have left this machine and
    /// answering whatever a test scripts — including, when a test asks, by not answering yet.
    /// </summary>
    private sealed class LibraryStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<LibraryCall> calls = [];

        private Func<LibraryCall, HttpResponseMessage> answer = _ => Json("[]");
        private TaskCompletionSource? held;

        public IReadOnlyList<LibraryCall> Calls
        {
            get { lock (gate) { return [.. calls]; } }
        }

        public LibraryCall Only
        {
            get
            {
                var recorded = Calls;
                recorded.Count.ShouldBe(1, $"expected exactly one call to the library, saw {recorded.Count}");
                return recorded[0];
            }
        }

        public void Answers(Func<LibraryCall, HttpResponseMessage> responder) => answer = responder;

        public void AnswersMarkers(string json) => Answers(_ => Json(json));

        public void AnswersJson(string json) => Answers(_ => Json(json));

        public void AnswersStatus(HttpStatusCode status) =>
            Answers(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent("the library is having trouble", Encoding.UTF8, "text/plain"),
            });

        public void AnswersPicture(byte[] bytes, string mediaType = "image/jpeg") =>
            Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(mediaType) },
                },
            });

        /// <summary>
        /// Records the next call but does not answer it until the returned source is completed, so
        /// a test can assert what happens while a reading is still in flight. Exactly one call is
        /// held; everything after it is answered as usual.
        /// </summary>
        public TaskCompletionSource HoldsTheNextAnswer()
        {
            var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            held = pause;
            return pause;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var call = new LibraryCall(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("x-api-key", out var keys) ? keys.FirstOrDefault() : null,
                request.Headers.TryGetValues("If-None-Match", out var etags) ? etags.FirstOrDefault() : null);

            lock (gate) { calls.Add(call); }

            var pause = Interlocked.Exchange(ref held, null);
            if (pause is not null)
            {
                await pause.Task;
            }

            return answer(call);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
    }
}
