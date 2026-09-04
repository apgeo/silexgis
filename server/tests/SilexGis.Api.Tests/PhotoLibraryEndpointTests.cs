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
    private const string OtherMapUrl = "/api/v1/photo-libraries/immich/map";
    private const string RecheckUrl = "/api/v1/photo-libraries/photoprism/recheck";
    private const string ThumbnailUrl = "/api/v1/photo-libraries/photoprism/thumbnails/";

    /// <summary>
    /// The route that writes, aimed at the one invented photograph the stub reports. The rectangle
    /// rides on the address exactly as it does for the map, because it is the same question: it says
    /// where to look for the photograph and never where to put what is made from it.
    /// </summary>
    private const string FeatureUrl =
        $"/api/v1/photo-libraries/photoprism/photographs/aa11bb22cc33/feature?bbox={Bbox}";

    /// <summary>
    /// A second invented photograph, far enough from the first that nothing created at one is near
    /// the other. Every test class in this suite shares one database, so a case asserting that
    /// nothing was already standing nearby has to stand somewhere nothing else builds — otherwise it
    /// passes or fails on which order the tests happened to run in.
    /// </summary>
    private const string LonePhotographReference = "bb22cc33dd44";

    private const string LoneFeatureUrl =
        $"/api/v1/photo-libraries/photoprism/photographs/{LonePhotographReference}/feature?bbox={Bbox}";

    /// <summary>An invented rectangle whose four numbers all differ, so a transposition shows.</summary>
    private const string Bbox = "21.5,45.125,24.25,46.75";

    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeApiKey = "not-a-real-key-0000";
    private const string LibraryAddress = "http://photo-library.invalid:2342";
    private const string OtherLibraryAddress = "http://other-photo-library.invalid:2283";
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

        // An empty layer panel cannot say why it is empty, so the answer names the products this
        // build can read and no address was supplied for. Nothing was asked of them: they report
        // that nothing was asked, which is a different answer from reporting that they did not
        // answer.
        var unconfigured = status.GetProperty("unconfigured");
        unconfigured.GetArrayLength().ShouldBe(2);
        unconfigured[0].GetProperty("configured").GetBoolean().ShouldBeFalse();
        unconfigured[0].GetProperty("health").GetProperty("reach").GetString().ShouldBe("unknown");
        unconfigured[0].GetProperty("health").GetProperty("probedAt").ValueKind.ShouldBe(JsonValueKind.Null);

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

        // Told no without also being told which products this installation runs — nor which it
        // could run and does not, which is the same disclosure said the other way round.
        var status = await JsonAsync(viewer, StatusUrl);
        status.GetProperty("mayRead").GetBoolean().ShouldBeFalse();
        status.GetProperty("providers").GetArrayLength().ShouldBe(0);
        status.GetProperty("unconfigured").GetArrayLength().ShouldBe(0);

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
        using var open = Configured(
            stub, other: null, ("PhotoLibraries:Audience", nameof(PhotoLibraryAudience.SignedIn)));

        var email = $"pl-open-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(open, GlobalRoles.Viewer, email);
        using var caller = await AuthHelper.BearerClientAsync(open, email);

        var collection = await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}");
        collection.GetProperty("features").GetArrayLength().ShouldBe(1);

        var status = await JsonAsync(caller, StatusUrl);
        status.GetProperty("mayRead").GetBoolean().ShouldBeTrue();
        status.GetProperty("providers").GetArrayLength().ShouldBe(1);

        // Opening the libraries to every account does not make every account an administrator.
        // Which products this installation could run and has not is an installation fact with no
        // errand behind it for somebody who cannot change it.
        status.GetProperty("unconfigured").GetArrayLength().ShouldBe(0);
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

    /// <summary>
    /// The one thing this route must never do. A library that is not answering is the answer an
    /// operator came to read, so producing it has to succeed: the probes run together and a single
    /// one of them escaping as an exception would fail the whole status answer, taking the healthy
    /// library's line and the list of unconnected products down with it — and the screen would then
    /// show nothing at all for a case whose entire purpose is to be shown.
    /// </summary>
    [Fact]
    public async Task The_status_route_answers_for_a_library_that_is_not_answering()
    {
        library.Answers(_ => throw new HttpRequestException("nothing is listening at that address"));

        var status = await JsonAsync(admin, StatusUrl);

        var health = status.GetProperty("providers")[0].GetProperty("health");
        health.GetProperty("reach").GetString().ShouldBe("unreachable");
        health.GetProperty("failureCode").GetString().ShouldBe(PhotoLibraryException.UnavailableCode);

        // Read, and said to have been read. A health line with no moment on it describes a minute
        // ago while looking like now.
        health.GetProperty("probedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
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

    // ------------------------------------------------------------------------- two of them at once

    /// <summary>
    /// Two libraries, read one at a time, on their own routes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One route each rather than one merged route, and the assertion that carries it is that
    /// asking one leaves the other's stub with nothing recorded. They are separate products with
    /// separate databases, separate storage and separate uptime, and one of them answers a
    /// rectangle live while the other is answered from a reading of its whole located library held
    /// here — so a joined answer would be as slow as the slower and as broken as the more broken,
    /// and comparing the two is what somebody running both is doing.
    /// </para>
    /// <para>
    /// The last assertion only becomes possible with two of them configured: a credential minted
    /// for one library's pictures must not open the other's. Adding a second product must not
    /// silently widen the first one's tokens.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Each_library_is_read_on_its_own_and_asking_one_asks_nothing_of_the_other()
    {
        using var rectangleLibrary = new LibraryStub();
        using var wholeLibrary = new LibraryStub();
        rectangleLibrary.AnswersGeo(OnePhotograph);
        wholeLibrary.AnswersMarkers(OnePosition);
        using var both = Configured(rectangleLibrary, wholeLibrary);

        var email = $"pl-two-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(both, GlobalRoles.Admin, email);
        using var caller = await AuthHelper.BearerClientAsync(both, email);
        using var browser = both.CreateClient();

        var fromWhole = await JsonAsync(caller, $"{OtherMapUrl}?bbox={Bbox}");
        fromWhole.GetProperty("source").GetString().ShouldBe("immich");
        fromWhole.GetProperty("libraryName").GetString().ShouldBe("Immich");
        fromWhole.GetProperty("features").GetArrayLength().ShouldBe(1);
        fromWhole.GetProperty("features")[0].GetProperty("properties")
            .GetProperty("reference").GetString().ShouldBe(InventedAsset);

        // The whole located library in one request, with the credential on a header and nowhere
        // near the address, and no rectangle anywhere in it — this library publishes no way to ask
        // for one, and the route it has that takes one is not part of the contract it offers.
        wholeLibrary.Only.Url.ShouldContain("/api/map/markers");
        wholeLibrary.Only.Url.ShouldNotContain("bbox");
        wholeLibrary.Only.ApiKey.ShouldBe(FakeApiKey);

        // And the other library was not asked anything at all.
        rectangleLibrary.Calls.ShouldBeEmpty();

        var fromRectangle = await JsonAsync(caller, $"{MapUrl}?bbox={Bbox}");
        fromRectangle.GetProperty("source").GetString().ShouldBe("photoprism");
        fromRectangle.GetProperty("features").GetArrayLength().ShouldBe(1);
        wholeLibrary.Calls.Count.ShouldBe(1);

        var address = fromWhole.GetProperty("pictureUrlTemplate").GetString()!;
        address.ShouldStartWith("/api/v1/photo-libraries/immich/thumbnails/{reference}");

        var crossed = address
            .Replace("{reference}", InventedAsset, StringComparison.Ordinal)
            .Replace("{size}", "large", StringComparison.Ordinal)
            .Replace("/immich/", "/photoprism/", StringComparison.Ordinal);

        (await browser.GetAsync(crossed)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Asked last on purpose. This route asks both libraries what state they are in, so calling
        // it earlier would put its requests among the ones counted above — and what is counted
        // above is that reading one library asks nothing of the other.
        var status = await JsonAsync(caller, StatusUrl);
        status.GetProperty("providers").GetArrayLength().ShouldBe(2);
        status.GetProperty("unconfigured").GetArrayLength().ShouldBe(0);

        foreach (var provider in status.GetProperty("providers").EnumerateArray())
        {
            // Both stubs answer everything, so both are reachable here. What this pins is that the
            // health of each library is carried per library rather than as one verdict for the
            // pair: they are separate installations with separate uptime, and one answer for both
            // would be as broken as the more broken of them.
            provider.GetProperty("health").GetProperty("reach").GetString().ShouldBe("reachable");
            provider.GetProperty("health").GetProperty("probedAt").ValueKind
                .ShouldNotBe(JsonValueKind.Null);
        }
    }

    // ------------------------------------------------ a photograph becomes an object in the registry

    /// <summary>
    /// The whole gesture, and the two things that make it worth having: the position is the one the
    /// library reported, and the object it created says where that came from.
    /// </summary>
    /// <remarks>
    /// The request deliberately carries a coordinate this route has no field for. Members a body
    /// carries and a request has no home for are ignored, which is exactly the property worth
    /// pinning: a body able to nudge the position would turn this into a way of putting an object
    /// anywhere at all while it looked as though a camera had measured it.
    /// </remarks>
    [Fact]
    public async Task A_photograph_becomes_a_cave_at_the_position_the_library_reported()
    {
        library.AnswersGeo(LonePhotograph);

        var created = await PostAsync(admin, LoneFeatureUrl, new
        {
            kind = "cave",
            name = "Invented cave from a picture",
            latitude = 0.0,
            longitude = 0.0,
        });

        created.Status.ShouldBe(HttpStatusCode.Created, created.Body);
        created.Json.GetProperty("kind").GetString().ShouldBe("cave");
        created.Json.GetProperty("name").GetString().ShouldBe("Invented cave from a picture");
        created.Json.GetProperty("nearby").GetArrayLength().ShouldBe(0);

        var id = created.Json.GetProperty("featureId").GetString()!;
        var envelope = await JsonAsync(admin, $"/api/v1/features/{id}");
        var coordinates = envelope.GetProperty("feature").GetProperty("geometry").GetProperty("coordinates");
        coordinates[0].GetDouble().ShouldBe(24.113457);
        coordinates[1].GetDouble().ShouldBe(46.612819);

        // A cave's own point is a cache of its main entrance's, so a cave built around a photograph
        // is a cave *and* the entrance that carries that position. Without it the cave would draw
        // nowhere, which is the opposite of what somebody creating one from a picture asked for.
        envelope.GetProperty("cave").GetProperty("entranceCount").GetInt32().ShouldBe(1);

        // The answer to "where did this cave's position come from?", years later.
        var properties = envelope.GetProperty("feature").GetProperty("properties");
        properties.GetProperty("photoLibrarySource").GetString().ShouldBe("photoprism");
        properties.GetProperty("photoLibraryReference").GetString().ShouldBe(LonePhotographReference);

        // And on the entrance as well, which is the row the coordinate actually lives on: a reader
        // who opens it to ask where its position came from is asking exactly what these keys answer.
        var entrances = await JsonAsync(admin, $"/api/v1/caves/{id}/entrances");
        var entranceId = entrances[0].GetProperty("id").GetString()!;
        (await JsonAsync(admin, $"/api/v1/features/{entranceId}"))
            .GetProperty("feature").GetProperty("properties")
            .GetProperty("photoLibraryReference").GetString().ShouldBe(LonePhotographReference);
    }

    /// <summary>
    /// A photograph the library does not report at that reference in that rectangle. The rectangle
    /// is a place to look and never a position: naming one the photograph is not in finds nothing,
    /// rather than putting an object where the rectangle is.
    /// </summary>
    [Fact]
    public async Task A_reference_the_library_does_not_report_there_creates_nothing()
    {
        library.AnswersGeo(OnePhotograph);
        var before = await FeatureCountAsync();

        var refused = await PostAsync(
            admin,
            $"/api/v1/photo-libraries/photoprism/photographs/nosuchreference/feature?bbox={Bbox}",
            new { kind = "cave", name = "Should not exist" });

        refused.Status.ShouldBe(HttpStatusCode.NotFound, refused.Body);
        CodeOf(refused.Body).ShouldBe("photo_library.photograph_not_found");
        (await FeatureCountAsync()).ShouldBe(before);
    }

    /// <summary>
    /// A library that did not answer creates nothing. There is no fallback position to reach for
    /// here and there must never be one: a coordinate no camera measured, filed as though one had,
    /// is worse than no object at all.
    /// </summary>
    [Fact]
    public async Task A_library_that_did_not_answer_creates_nothing()
    {
        library.Answers(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>the library is having trouble</html>", Encoding.UTF8, "text/html"),
        });
        var before = await FeatureCountAsync();

        var refused = await PostAsync(admin, FeatureUrl, new { kind = "cave", name = "Should not exist" });

        refused.Status.ShouldBe(HttpStatusCode.ServiceUnavailable, refused.Body);
        CodeOf(refused.Body).ShouldBe(PhotoLibraryException.UnavailableCode);
        (await FeatureCountAsync()).ShouldBe(before);
    }

    /// <summary>
    /// The audience rule reaches the route that writes, and reaches it before the library is asked
    /// anything at all: an account that may not see these photographs may not build objects out of
    /// one either, and asking the library on their behalf would already have been the disclosure.
    /// </summary>
    [Fact]
    public async Task Creating_from_a_photograph_is_refused_to_everybody_outside_the_audience()
    {
        library.AnswersGeo(OnePhotograph);

        var refused = await PostAsync(viewer, FeatureUrl, new { kind = "cave", name = "Should not exist" });
        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body);
        CodeOf(refused.Body).ShouldBe("photo_library.forbidden");

        var stranger = await PostAsync(anonymous, FeatureUrl, new { kind = "cave", name = "Should not exist" });
        stranger.Status.ShouldBe(HttpStatusCode.Unauthorized, stranger.Body);

        library.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// Forty photographs of one entrance must not quietly become forty caves — and must not be
    /// refused either, because the person who took them is the only one who can tell the
    /// forty-first picture of a known hole from a second hole four metres away.
    /// </summary>
    [Fact]
    public async Task A_second_photograph_of_the_same_place_is_warned_about_rather_than_refused()
    {
        library.AnswersGeo(OnePhotograph);

        var first = await PostAsync(admin, FeatureUrl, new { kind = "cave", name = "First reading" });
        first.Status.ShouldBe(HttpStatusCode.Created, first.Body);

        var second = await PostAsync(admin, FeatureUrl, new { kind = "cave", name = "Second reading" });
        second.Status.ShouldBe(HttpStatusCode.Created, second.Body);

        // Information, not a refusal: the object asked for was created, and the answer says what was
        // already standing there. Named exactly — every class in this suite shares one database, so
        // "something came back" would pass on a neighbour some other test left behind.
        var entrances = await JsonAsync(admin, $"/api/v1/caves/{first.Json.GetProperty("featureId").GetString()}/entrances");
        var firstEntranceId = entrances[0].GetProperty("id").GetString();

        // The entrance is what is reported rather than the cave, because a cave is not a position of
        // its own: its point is its main entrance's.
        second.Json.GetProperty("nearby").EnumerateArray().ShouldContain(hit =>
            hit.GetProperty("featureId").GetString() == firstEntranceId
            && hit.GetProperty("kind").GetString() == "caveEntrance"
            && hit.GetProperty("distanceMeters").GetDouble() <= 1);
    }

    /// <summary>
    /// An entrance belongs to a cave and cannot exist without one, so the cave is named — and naming
    /// it is a write on it, because adding the first entrance moves the cave's own point on the map.
    /// A cave that is not there and one the caller may not read answer the same way.
    /// </summary>
    [Fact]
    public async Task An_entrance_hangs_on_a_cave_the_caller_may_add_to()
    {
        library.AnswersGeo(OnePhotograph);

        var cave = await PostAsync(admin, FeatureUrl, new { kind = "cave", name = "A cave with one entrance" });
        cave.Status.ShouldBe(HttpStatusCode.Created, cave.Body);
        var caveId = cave.Json.GetProperty("featureId").GetString()!;

        var missing = await PostAsync(admin, FeatureUrl, new
        {
            kind = "caveEntrance",
            name = "Should not exist",
            caveFeatureId = Guid.NewGuid(),
        });
        missing.Status.ShouldBe(HttpStatusCode.NotFound, missing.Body);
        CodeOf(missing.Body).ShouldBe("photo_library.cave_not_found");

        var created = await PostAsync(admin, FeatureUrl, new
        {
            kind = "caveEntrance",
            name = "A second entrance",
            caveFeatureId = caveId,
        });
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body);

        var id = created.Json.GetProperty("featureId").GetString()!;
        var envelope = await JsonAsync(admin, $"/api/v1/features/{id}");
        envelope.GetProperty("entrance").GetProperty("caveFeatureId").GetString().ShouldBe(caveId);

        // A camera's own fix is a GPS reading, and saying so is what lets somebody later tell this
        // position from one that was dragged onto a map.
        envelope.GetProperty("entrance").GetProperty("positionQuality").GetString().ShouldBe("gps");
        envelope.GetProperty("feature").GetProperty("properties")
            .GetProperty("photoLibraryReference").GetString().ShouldBe("aa11bb22cc33");
    }

    /// <summary>
    /// The two shapes the body has to refuse. A centerline is a line and a photograph is one point;
    /// a feature of "another kind" is defined by the installation's own taxonomy, and one that names
    /// none is not a request anybody can answer.
    /// </summary>
    [Fact]
    public async Task A_photograph_cannot_become_a_line_or_a_feature_of_no_kind()
    {
        library.AnswersGeo(OnePhotograph);

        var line = await PostAsync(admin, FeatureUrl, new { kind = "centerline", name = "Not a line" });
        line.Status.ShouldBe(HttpStatusCode.BadRequest, line.Body);

        var untyped = await PostAsync(admin, FeatureUrl, new { kind = "generic", name = "Of no kind" });
        untyped.Status.ShouldBe(HttpStatusCode.BadRequest, untyped.Body);

        // Refused in front of the library rather than after it: a body nobody can act on is not a
        // reason to open a socket to a neighbouring container.
        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------------ support

    /// <summary>
    /// A host pointed at one library, or at two when a test supplies a stub for the second. Left at
    /// one by default on purpose: most of what is asserted here is about a single library, and a
    /// second one configured throughout would make every provider count read as two for reasons
    /// unrelated to what the case is about.
    /// </summary>
    private SilexGisApiFactory Configured(
        LibraryStub stub, LibraryStub? other = null, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PhotoLibraries:PhotoPrism:Enabled"] = "true",
            ["PhotoLibraries:PhotoPrism:BaseUrl"] = LibraryAddress,
            ["PhotoLibraries:PhotoPrism:AccessToken"] = FakeToken,
            ["PhotoLibraries:PhotoPrism:MaxViewportCount"] = ConfiguredCount.ToString(),
        };

        if (other is not null)
        {
            settings["PhotoLibraries:Immich:Enabled"] = "true";
            settings["PhotoLibraries:Immich:BaseUrl"] = OtherLibraryAddress;
            settings["PhotoLibraries:Immich:ApiKey"] = FakeApiKey;

            // Every viewport re-reads the whole located library, so no case here depends on how
            // long a reading another case took is held for. Zero is a real configured value with a
            // real meaning rather than a switch that only exists for tests.
            settings["PhotoLibraries:Immich:PositionCacheSeconds"] = "0";
        }

        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return new SilexGisApiFactory(
            connectionString,
            settings,
            services =>
            {
                services.AddHttpClient(PhotoPrismClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => stub);

                if (other is not null)
                {
                    services.AddHttpClient(ImmichClient.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => other);
                }
            });
    }

    private static readonly byte[] InventedJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    /// <summary>An invented identifier of the shape the other library names a photograph with.</summary>
    private const string InventedAsset = "11111111-1111-4111-8111-111111111111";

    /// <summary>
    /// One invented position, in the shape the library that cannot be asked about a rectangle
    /// answers with. Its place-name fields are sent by the real one and read by nothing here.
    /// </summary>
    private const string OnePosition =
        $$"""[{"id":"{{InventedAsset}}","lat":45.5,"lon":22.5,"city":null,"state":null,"country":null}]""";

    /// <summary>One invented photograph, in the shape this product answers a rectangle with.</summary>
    private const string OnePhotograph = """
    {"type":"FeatureCollection","features":[
      {"id":"1","type":"Feature","geometry":{"type":"Point","coordinates":[22.5,45.5]},
       "properties":{"Hash":"aa11bb22cc33","UID":"psinvented1","Title":"An invented photograph",
                     "TakenAt":"2026-02-03T04:05:06Z"}}
    ]}
    """;

    /// <summary>The photograph nothing else in this suite builds anything near.</summary>
    private const string LonePhotograph = """
    {"type":"FeatureCollection","features":[
      {"id":"2","type":"Feature","geometry":{"type":"Point","coordinates":[24.113457,46.612819]},
       "properties":{"Hash":"bb22cc33dd44","UID":"psinvented5","Title":"A photograph of nowhere",
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

    /// <summary>One answer, kept whole: the status, the raw body for a failed assertion to print,
    /// and the parsed body for the assertion itself.</summary>
    private sealed record Answer(HttpStatusCode Status, string Body, JsonElement Json);

    private static async Task<Answer> PostAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var raw = await response.Content.ReadAsStringAsync();

        JsonElement parsed = default;
        if (raw.Length > 0)
        {
            using var document = JsonDocument.Parse(raw);
            parsed = document.RootElement.Clone();
        }

        return new Answer(response.StatusCode, raw, parsed);
    }

    /// <summary>
    /// How many features this installation holds, as the registry itself reports it. Used either
    /// side of a refusal, because "nothing was created" is the assertion that matters on every path
    /// where the position could not be established — and a response code alone does not make it.
    /// </summary>
    private async Task<int> FeatureCountAsync() =>
        (await JsonAsync(admin, "/api/v1/features?pageSize=1")).GetProperty("totalItems").GetInt32();

    private sealed record LibraryCall(string Method, string Url, string? Authorization, string? ApiKey);

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

        /// <summary>Answers with a whole library's positions, the way the library that cannot be asked about a rectangle does.</summary>
        public void AnswersMarkers(string json) => Answers(_ => Json(json));

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
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("x-api-key", out var keys) ? keys.FirstOrDefault() : null);

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
