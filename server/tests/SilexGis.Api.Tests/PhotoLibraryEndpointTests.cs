// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// The routes that put a neighbouring photo library's photographs on the map, exercised against a
/// stub standing in for the library.
///
/// <para>
/// The far side is a whole separate product and is not run here; what is proved instead is
/// everything on this side of the socket — that an installation given no library asks nothing of
/// anybody, that the audience is decided on the server and not in the layer panel, what exactly
/// leaves this machine in an outbound request, and that a picture answer which was not a picture
/// stops every later picture request until somebody rechecks. That last one is not a preference: on
/// this product a picture request it cannot resolve on disk deletes the photograph from its own
/// index, so a retry loop is a deletion loop.
/// </para>
/// <para>
/// Every coordinate, identifier, hash and title below is invented. Nothing here comes from a real
/// library and no coordinate names a real place.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhotoLibraryEndpointTests : IAsyncLifetime, IDisposable
{
    private const string StatusUrl = "/api/v1/photo-libraries/status";
    private const string MapUrl = "/api/v1/photo-libraries/photoprism/map";
    private const string RecheckUrl = "/api/v1/photo-libraries/photoprism/recheck";
    private const string ThumbnailUrl = "/api/v1/photo-libraries/photoprism/thumbnails/";

    /// <summary>An invented rectangle whose four numbers all differ, so a transposition shows.</summary>
    private const string Bbox = "21.5,45.125,24.25,46.75";

    private const string FakeToken = "not-a-real-token-0000";
    private const string LibraryAddress = "http://photo-library.invalid:2342";
    private const int ConfiguredCount = 5;

    private readonly string connectionString;
    private readonly LibraryStub library = new();
    private readonly SilexGisApiFactory factory;

    private string tag = string.Empty;
    private HttpClient admin = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;

    public PhotoLibraryEndpointTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        connectionString = postgres.ConnectionString;
        factory = Configured(library);
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"pl-ad-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"pl-ad-{tag}@t.local");

        // A viewer, and deliberately not an editor: the seeded editors group reads past visibility
        // by design, so an editor proves less than it looks about an account with no grant.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pl-vw-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pl-vw-{tag}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        library.Dispose();
    }

    // ---------------------------------------------------------------- absent rather than broken

    /// <summary>
    /// An installation that has been given no library is a supported installation. The proof that
    /// matters is the last line: not one socket was opened on any of the three paths.
    /// </summary>
    [Fact]
    public async Task With_no_library_configured_the_routes_answer_and_nothing_is_asked_of_anybody()
    {
        var quiet = new LibraryStub();
        using var bare = new SilexGisApiFactory(
            connectionString,
            settings: null,
            configureServices: services => services
                .AddHttpClient(PhotoPrismClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => quiet));

        var email = $"pl-none-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(bare, GlobalRoles.Admin, email);
        using var caller = await AuthHelper.BearerClientAsync(bare, email);

        var status = await JsonAsync(caller, StatusUrl);
        status.GetProperty("mayRead").GetBoolean().ShouldBeTrue();
        status.GetProperty("providers").GetArrayLength().ShouldBe(0);

        var collection = await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}");
        collection.GetProperty("features").GetArrayLength().ShouldBe(0);
        collection.GetProperty("picturesAvailable").GetBoolean().ShouldBeFalse();
        collection.GetProperty("pictureUrlTemplate").ValueKind.ShouldBe(JsonValueKind.Null);

        var picture = await caller.GetAsync($"{ThumbnailUrl}abcdef01?token=anything");
        picture.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        quiet.Calls.ShouldBeEmpty();
    }

    /// <summary>A product this installation does not run is not found, on every route that names one.</summary>
    [Fact]
    public async Task A_library_this_installation_does_not_run_is_not_found()
    {
        (await admin.GetAsync("/api/v1/photo-libraries/nosuchproduct/map?bbox=" + Bbox))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/v1/photo-libraries/nosuchproduct/thumbnails/abcdef01?token=x"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.PostAsync("/api/v1/photo-libraries/nosuchproduct/recheck", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------ the audience

    /// <summary>
    /// The setting ships closed, and the refusal is made on the server rather than by not drawing a
    /// row in the layer panel. Asserted against a host given no setting at all — injecting the
    /// shipped value and then asserting a refusal would pass just as happily if the product shipped
    /// it open — and the same test asserts the account that may see it does.
    /// </summary>
    [Fact]
    public async Task The_libraries_are_administrators_only_unless_an_operator_says_otherwise()
    {
        factory.Services.GetRequiredService<IOptions<PhotoLibraryOptions>>()
            .Value.Audience.ShouldBe(PhotoLibraryAudience.Administrators);

        library.AnswersGeo(OnePhotograph);

        var refused = await viewer.GetAsync($"{MapUrl}?bbox={Bbox}");
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe("photo_library.forbidden");

        // Told no without also being told which products this installation runs.
        var status = await JsonAsync(viewer, StatusUrl);
        status.GetProperty("mayRead").GetBoolean().ShouldBeFalse();
        status.GetProperty("providers").GetArrayLength().ShouldBe(0);

        (await viewer.PostAsync(RecheckUrl, content: null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Nothing was asked of the library on behalf of an account that may not see it.
        library.Calls.ShouldBeEmpty();

        // And the account that may see it does, through the very same routes.
        var allowed = await JsonAsync(admin, $"{MapUrl}?bbox={Bbox}");
        allowed.GetProperty("features").GetArrayLength().ShouldBe(1);
        (await JsonAsync(admin, StatusUrl)).GetProperty("providers").GetArrayLength().ShouldBe(1);
    }

    /// <summary>The other value of the setting, which is the whole of the authorisation model.</summary>
    [Fact]
    public async Task An_operator_may_open_the_libraries_to_every_signed_in_account()
    {
        var stub = new LibraryStub();
        stub.AnswersGeo(OnePhotograph);
        using var open = Configured(stub, ("PhotoLibraries:Audience", nameof(PhotoLibraryAudience.SignedIn)));

        var email = $"pl-open-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(open, GlobalRoles.Viewer, email);
        using var caller = await AuthHelper.BearerClientAsync(open, email);

        var collection = await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}");
        collection.GetProperty("features").GetArrayLength().ShouldBe(1);
        (await JsonAsync(caller, StatusUrl)).GetProperty("mayRead").GetBoolean().ShouldBeTrue();
    }

    /// <summary>Every route needs a session, including the one that reaches nothing.</summary>
    [Fact]
    public async Task A_caller_with_no_session_is_refused_before_anything_else()
    {
        (await anonymous.GetAsync(StatusUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{MapUrl}?bbox={Bbox}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(RecheckUrl, content: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------- what leaves this machine

    /// <summary>
    /// The rectangle on the wire, and the ceiling applied before the library is asked rather than to
    /// what it answered. A transposed rectangle draws an empty map rather than raising anything,
    /// which is indistinguishable from the feature being switched off, so what left the machine is
    /// asserted directly.
    /// </summary>
    [Fact]
    public async Task The_rectangle_and_the_ceiling_are_settled_before_the_library_is_asked()
    {
        library.AnswersGeo(OnePhotograph);

        await JsonAsync(admin, $"{MapUrl}?bbox={Bbox}");

        var asked = Uri.UnescapeDataString(library.Only.Url);
        asked.ShouldContain("latlng=46.75,24.25,45.125,21.5");
        asked.ShouldContain($"count={ConfiguredCount}");
        library.Only.Authorization.ShouldBe($"Bearer {FakeToken}");
    }

    [Fact]
    public async Task A_rectangle_that_is_not_a_rectangle_is_refused_without_asking_anybody()
    {
        var refused = await admin.GetAsync($"{MapUrl}?bbox=21.5,45.125,24.25");
        var body = await refused.Content.ReadAsStringAsync();

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe("map.invalid_bbox");
        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------ what comes back out

    /// <summary>
    /// The answer a map draws: a point per photograph, the library's own names for it, and one
    /// picture address for the whole collection rather than one per photograph.
    /// </summary>
    [Fact]
    public async Task Photographs_are_answered_as_points_carrying_what_the_library_said()
    {
        library.AnswersGeo(OnePhotograph);

        var collection = await JsonAsync(admin, $"{MapUrl}?bbox={Bbox}");

        collection.GetProperty("type").GetString().ShouldBe("FeatureCollection");
        collection.GetProperty("source").GetString().ShouldBe("photoprism");
        collection.GetProperty("libraryName").GetString().ShouldBe("PhotoPrism");
        collection.GetProperty("picturesAvailable").GetBoolean().ShouldBeTrue();
        collection.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        collection.GetProperty("omittedCount").GetInt32().ShouldBe(0);
        collection.GetProperty("readAt").GetDateTimeOffset()
            .ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        var feature = collection.GetProperty("features")[0];
        feature.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().ShouldBe(22.5);
        feature.GetProperty("geometry").GetProperty("coordinates")[1].GetDouble().ShouldBe(45.5);

        var properties = feature.GetProperty("properties");
        properties.GetProperty("source").GetString().ShouldBe("photoprism");
        properties.GetProperty("reference").GetString().ShouldBe("aa11bb22cc33");
        properties.GetProperty("title").GetString().ShouldBe("An invented photograph");

        // A place name is most of what a coordinate says, so the ones the library also sends are
        // deliberately not carried through this route.
        properties.TryGetProperty("locality", out _).ShouldBeFalse();
        properties.TryGetProperty("country", out _).ShouldBeFalse();

        // One address for the collection, with the two placeholders a client substitutes and the
        // credential carried once instead of once per photograph.
        var template = collection.GetProperty("pictureUrlTemplate").GetString()!;
        template.ShouldStartWith("/api/v1/photo-libraries/photoprism/thumbnails/{reference}");
        template.ShouldContain("size={size}");
        template.ShouldContain("token=");

        // Nothing about the far side reaches the browser: not its address, not its credential, and
        // not the credential its own pictures are served under.
        var raw = collection.GetRawText();
        raw.ShouldNotContain(LibraryAddress);
        raw.ShouldNotContain(FakeToken);
        raw.ShouldNotContain("preview-token-invented");
    }

    /// <summary>
    /// The far side answering badly does not become this installation answering badly. Every one of
    /// these is dropped rather than drawn, and the photograph that was well formed is still there —
    /// a collection that came back empty would prove nothing about which half failed.
    /// </summary>
    [Fact]
    public async Task A_photograph_the_library_describes_unusably_is_dropped_and_the_rest_are_drawn()
    {
        library.AnswersGeo("""
        {"type":"FeatureCollection","features":[
          {"id":"7","type":"Feature","geometry":{"type":"Point","coordinates":[22.5]},
           "properties":{"Hash":"aa11bb22cc33","UID":"psinvented1"}},
          {"id":"8","type":"Feature","geometry":{"type":"Point","coordinates":[22.6,45.6]},
           "properties":{"Hash":"aa11/bb22","UID":"psinvented2"}},
          {"id":"9","type":"Feature","geometry":{"type":"Point","coordinates":[22.7,45.7]}},
          {"id":"10","type":"Feature","geometry":{"type":"Point","coordinates":[22.8,45.8]},
           "properties":{"Hash":"dd44ee55ff66","UID":"psinvented4","Title":"The readable one"}}
        ]}
        """);

        var collection = await JsonAsync(admin, $"{MapUrl}?bbox={Bbox}");

        collection.GetProperty("features").GetArrayLength().ShouldBe(1);
        collection.GetProperty("features")[0].GetProperty("properties")
            .GetProperty("reference").GetString().ShouldBe("dd44ee55ff66");
    }

    /// <summary>
    /// A library that did not answer fails the request rather than arriving as an empty collection.
    /// An overlay that quietly draws nothing cannot be told apart from a place nobody has
    /// photographed, and that is this feature's most likely way of being wrong while looking right.
    /// </summary>
    [Fact]
    public async Task A_library_that_did_not_answer_says_so_rather_than_drawing_nothing()
    {
        library.Answers(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>the library is having trouble</html>", Encoding.UTF8, "text/html"),
        });

        var response = await admin.GetAsync($"{MapUrl}?bbox={Bbox}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    // --------------------------------------------------------------------------- the picture path

    /// <summary>
    /// A picture reaches the browser from this application's own origin, and the token in the
    /// address is the whole of the credential — a browser loading an image cannot attach a header.
    /// </summary>
    [Fact]
    public async Task A_picture_is_streamed_through_this_application_and_never_linked_to()
    {
        library.AnswersGeo(OnePhotograph);
        var template = (await JsonAsync(admin, $"{MapUrl}?bbox={Bbox}"))
            .GetProperty("pictureUrlTemplate").GetString()!;

        library.AnswersPicture(InventedJpeg);
        var address = template.Replace("{reference}", "aa11bb22cc33").Replace("{size}", "large");

        // Deliberately the client that carries no session: this is how a browser loads it.
        var picture = await anonymous.GetAsync(address);
        var bytes = await picture.Content.ReadAsByteArrayAsync();

        picture.StatusCode.ShouldBe(HttpStatusCode.OK);
        picture.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");
        picture.Headers.CacheControl?.Private.ShouldBeTrue();
        bytes.ShouldBe(InventedJpeg);

        // The credential the far side serves its pictures under is used to build the outbound
        // request and appears nowhere in the answer.
        library.Picture.Url.ShouldContain("preview-token-invented");
    }

    [Fact]
    public async Task A_picture_asked_for_without_a_token_this_installation_minted_is_not_found()
    {
        var before = library.Calls.Count;

        (await anonymous.GetAsync($"{ThumbnailUrl}aa11bb22cc33?size=large"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"{ThumbnailUrl}aa11bb22cc33?size=large&token=forged"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        library.Calls.Count.ShouldBe(before);
    }

    /// <summary>
    /// The guard that is not about privacy. On this product a picture request it cannot resolve on
    /// disk marks the file missing and deletes the photograph from its own index, so an answer that
    /// was not a picture closes the byte path for good and a second request must not reopen it. The
    /// count of calls is the assertion: one, then still one, and only a deliberate recheck adds
    /// another.
    /// </summary>
    [Fact]
    public async Task An_answer_that_was_not_a_picture_stops_every_later_one_until_a_recheck()
    {
        // The stop is process-wide state on a long-lived client, so this test is written against
        // its own host's client and asserts the count of calls rather than any one answer.
        var stub = library;
        var caller = admin;
        using var browser = factory.CreateClient();
        stub.AnswersGeo(OnePhotograph);

        var template = (await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}"))
            .GetProperty("pictureUrlTemplate").GetString()!;
        var address = template.Replace("{reference}", "aa11bb22cc33").Replace("{size}", "large");

        // What a library that has lost the disk its originals live on answers: a drawing, not an
        // error, and with a perfectly successful status code.
        stub.AnswersPicture(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "image/svg+xml");

        var refused = await browser.GetAsync(address);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(PhotoLibraryException.OriginalsUnavailableCode);
        body.ShouldNotContain("<svg");
        stub.PictureCalls.Count.ShouldBe(1);

        // The second request is the deletion this guard exists to prevent. It must not happen.
        (await browser.GetAsync(address)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        stub.PictureCalls.Count.ShouldBe(1);

        // The map still draws: the library's positions were never what was missing, and only the
        // picture stops — which is what the null template tells the browser.
        var afterwards = await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}");
        afterwards.GetProperty("features").GetArrayLength().ShouldBe(1);
        afterwards.GetProperty("picturesAvailable").GetBoolean().ShouldBeFalse();
        afterwards.GetProperty("pictureUrlTemplate").ValueKind.ShouldBe(JsonValueKind.Null);

        // Nothing reopens it on a timer. A person does, and then exactly one more picture is asked
        // for — the recheck itself asks about the configuration and never about a picture.
        stub.AnswersPicture(InventedJpeg);
        (await caller.PostAsync(RecheckUrl, content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        stub.PictureCalls.Count.ShouldBe(1);

        var reopened = (await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}"))
            .GetProperty("pictureUrlTemplate").GetString()!;
        (await browser.GetAsync(reopened.Replace("{reference}", "aa11bb22cc33").Replace("{size}", "large")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        stub.PictureCalls.Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------------------------ support

    private SilexGisApiFactory Configured(LibraryStub stub, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PhotoLibraries:PhotoPrism:Enabled"] = "true",
            ["PhotoLibraries:PhotoPrism:BaseUrl"] = LibraryAddress,
            ["PhotoLibraries:PhotoPrism:AccessToken"] = FakeToken,
            ["PhotoLibraries:PhotoPrism:MaxViewportCount"] = ConfiguredCount.ToString(),
        };
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return new SilexGisApiFactory(
            connectionString,
            settings,
            services => services
                .AddHttpClient(PhotoPrismClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => stub));
    }

    private static readonly byte[] InventedJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    /// <summary>One invented photograph, in the shape this product answers a rectangle with.</summary>
    private const string OnePhotograph = """
    {"type":"FeatureCollection","features":[
      {"id":"1","type":"Feature","geometry":{"type":"Point","coordinates":[22.5,45.5]},
       "properties":{"Hash":"aa11bb22cc33","UID":"psinvented1","Title":"An invented photograph",
                     "TakenAt":"2026-02-03T04:05:06Z"}}
    ]}
    """;

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    private sealed record LibraryCall(string Method, string Url, string? Authorization);

    /// <summary>
    /// Stands in for the library, recording everything that would have left this machine and
    /// answering whatever a test scripts. Picture calls are counted apart from the rest because the
    /// count of them is itself an assertion.
    /// </summary>
    private sealed class LibraryStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<LibraryCall> calls = [];

        private Func<LibraryCall, HttpResponseMessage> answer =
            _ => Json("""{"type":"FeatureCollection","features":[]}""");

        public IReadOnlyList<LibraryCall> Calls
        {
            get { lock (gate) { return [.. calls]; } }
        }

        public IReadOnlyList<LibraryCall> PictureCalls =>
            [.. Calls.Where(c => c.Url.Contains("/api/v1/t/", StringComparison.Ordinal))];

        public LibraryCall Only
        {
            get
            {
                var recorded = Calls;
                recorded.Count.ShouldBe(1, $"expected exactly one call to the library, saw {recorded.Count}");
                return recorded[0];
            }
        }

        public LibraryCall Picture => PictureCalls.Single();

        public void Answers(Func<LibraryCall, HttpResponseMessage> responder) => answer = responder;

        /// <summary>Answers the rectangle question, and carries the picture credential the way the real one does.</summary>
        public void AnswersGeo(string json) => Answers(_ =>
        {
            var response = Json(json);
            response.Headers.TryAddWithoutValidation("X-Preview-Token", "preview-token-invented");
            return response;
        });

        public void AnswersPicture(byte[] bytes, string mediaType = "image/jpeg")
        {
            var previous = answer;
            Answers(call => call.Url.Contains("/api/v1/t/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue(mediaType) },
                    },
                }
                : previous(call));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var call = new LibraryCall(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString());

            lock (gate) { calls.Add(call); }
            return Task.FromResult(answer(call));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
    }
}
