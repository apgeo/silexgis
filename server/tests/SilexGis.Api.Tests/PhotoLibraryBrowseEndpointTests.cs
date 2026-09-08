// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.PhotoLibraries;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// The routes that let somebody look through a neighbouring photo library as a list of pictures,
/// exercised against a stub standing in for the library.
///
/// <para>
/// The far side is a whole separate product and is not run here. What is proved instead is
/// everything on this side of the socket: that an installation given no library asks nothing of
/// anybody, that the audience is decided on the server rather than by not drawing a page, that
/// words are refused rather than dropped by a library which cannot match them, that a page which
/// was capped says so — and the one that matters most, that <b>nothing on either of these
/// responses is a position</b>. That last is asserted against the serialised body rather than
/// against the record's fields, because a field added later would be caught by the first and not
/// by the second.
/// </para>
/// <para>
/// Every coordinate, identifier, hash and title below is invented. Nothing here comes from a real
/// library and no photograph named here exists.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhotoLibraryBrowseEndpointTests : IAsyncLifetime, IDisposable
{
    private const string ListUrl = "/api/v1/photo-libraries/photoprism/photographs";
    private const string OtherListUrl = "/api/v1/photo-libraries/immich/photographs";
    private const string DetailUrl = $"{ListUrl}/psinvented5";

    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeApiKey = "not-a-real-key-0000";
    private const string LibraryAddress = "http://photo-library.invalid:2342";
    private const string OtherLibraryAddress = "http://other-photo-library.invalid:2283";

    /// <summary>An invented identifier and an invented hash, of the shapes this product mints.</summary>
    private const string Uid = "psinvented5";
    private const string Hash = "bb22cc33dd44";

    private readonly string connectionString;
    private readonly LibraryStub library = new();
    private readonly SilexGisApiFactory factory;

    private string tag = string.Empty;
    private HttpClient admin = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;

    public PhotoLibraryBrowseEndpointTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        connectionString = postgres.ConnectionString;
        factory = Configured(library);
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"pb-ad-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"pb-ad-{tag}@t.local");

        // A viewer, and deliberately not an editor: the seeded editors group reads past visibility
        // by design, so an editor proves less than it looks about an account with no grant.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pb-vw-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pb-vw-{tag}@t.local");
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

    // ------------------------------------------------------------------------------- who may look

    /// <summary>
    /// Both routes are behind the audience setting, and the refusal is made on the server rather
    /// than by a rail that does not offer the page. Nothing is asked of the library on behalf of
    /// somebody who may not see it.
    /// </summary>
    [Fact]
    public async Task The_listing_is_administrators_only_unless_an_operator_says_otherwise()
    {
        library.AnswersListing(OnePhotograph);

        (await anonymous.GetAsync(ListUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(DetailUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await viewer.GetAsync(ListUrl);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe("photo_library.forbidden");

        (await viewer.GetAsync(DetailUrl)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        library.Calls.ShouldBeEmpty();

        // And the account that may see it does, through the very same route.
        (await JsonAsync(admin, ListUrl)).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    /// <summary>A product this installation does not run is not found, on both new routes.</summary>
    [Fact]
    public async Task A_library_this_installation_does_not_run_is_not_found()
    {
        (await admin.GetAsync("/api/v1/photo-libraries/nosuchproduct/photographs"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/v1/photo-libraries/nosuchproduct/photographs/whatever"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A product this build knows and this installation has been given no address for is the
        // same answer, and no socket is opened reaching it.
        (await admin.GetAsync(OtherListUrl)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------- positionless by construction

    /// <summary>
    /// Neither response carries a position, and neither request takes a rectangle.
    /// </summary>
    /// <remarks>
    /// Every name in the serialised answer is walked rather than the fields somebody thought to
    /// read, because that is the difference this case exists for: a field added to one of these
    /// records later — from a library answer that happens to carry a coordinate — shows up in the
    /// walk and shows up in no test that only asks about the fields it already knows. Names rather
    /// than the whole text, so nothing here can pass or fail on the characters of a credential.
    /// </remarks>
    [Fact]
    public async Task Nothing_on_the_listing_or_the_detail_is_a_position()
    {
        library.AnswersListing(OnePhotograph);
        library.AnswersDetail(OnePhotographInFull);

        var listing = await JsonAsync(admin, ListUrl);
        var detail = await JsonAsync(admin, DetailUrl);

        foreach (var answer in (JsonElement[])[listing, detail])
        {
            var names = NamesIn(answer);

            names.ShouldNotBeEmpty();

            foreach (var forbidden in (string[])
                ["latitude", "longitude", "lat", "lon", "lng", "bbox", "coordinates", "geometry",
                 "city", "state", "country", "place", "locality"])
            {
                names.ShouldNotContain(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }

        // A rectangle sent to a route that has no such parameter narrows nothing: there is no such
        // parameter to bind it to, and nothing about the answer changes.
        var withRectangle = await JsonAsync(admin, $"{ListUrl}?bbox=21.5,45.125,24.25,46.75");
        withRectangle.GetProperty("items").GetArrayLength()
            .ShouldBe(listing.GetProperty("items").GetArrayLength());

        // And nothing about a rectangle reached the library either.
        foreach (var call in library.Calls)
        {
            call.Url.ShouldNotContain("latlng");
            call.Url.ShouldNotContain("bbox");
        }
    }

    /// <summary>Every property name in an answer, however deeply nested.</summary>
    private static List<string> NamesIn(JsonElement element)
    {
        var names = new List<string>();

        void Walk(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        names.Add(property.Name);
                        Walk(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;

                default:
                    break;
            }
        }

        Walk(element);
        return names;
    }

    /// <summary>
    /// The library's own address never reaches a browser, on either route.
    /// </summary>
    /// <remarks>
    /// The pictures are proxied through this application, which is the reason a page may name no
    /// foreign host: that host is reached over the deployment's internal network and is often not
    /// resolvable from a browser at all, and its picture addresses are usable by anyone who holds
    /// one.
    /// </remarks>
    [Fact]
    public async Task No_answer_names_the_library_itself()
    {
        library.AnswersListing(OnePhotograph);
        library.AnswersDetail(OnePhotographInFull);

        (await RawAsync(admin, ListUrl)).ShouldNotContain("photo-library.invalid");
        (await RawAsync(admin, DetailUrl)).ShouldNotContain("photo-library.invalid");
    }

    // --------------------------------------------------------------------------- one page of it

    /// <summary>
    /// A page of the library, with what the library said about it and what this installation did
    /// to the question.
    /// </summary>
    [Fact]
    public async Task A_page_carries_what_the_library_said_and_how_it_was_asked()
    {
        library.AnswersListing(OnePhotograph);

        var page = await JsonAsync(admin, $"{ListUrl}?page=2&pageSize=10");

        page.GetProperty("source").GetString().ShouldBe("photoprism");
        page.GetProperty("libraryName").GetString().ShouldBe("PhotoPrism");
        page.GetProperty("page").GetInt32().ShouldBe(2);
        page.GetProperty("pageSize").GetInt32().ShouldBe(10);
        page.GetProperty("pageSizeCapped").GetBoolean().ShouldBeFalse();
        page.GetProperty("textSearchSupported").GetBoolean().ShouldBeTrue();
        page.GetProperty("picturesAvailable").GetBoolean().ShouldBeTrue();
        page.GetProperty("pictureUrlTemplate").GetString().ShouldNotBeNullOrEmpty();

        // This product publishes no way to ask how many it holds, so the total is unknown rather
        // than a number nobody can tell from a fact.
        page.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);

        var item = page.GetProperty("items")[0];
        item.GetProperty("photographId").GetString().ShouldBe(Uid);
        item.GetProperty("reference").GetString().ShouldBe(Hash);
        item.GetProperty("title").GetString().ShouldBe("A photograph of nowhere");

        // The page was asked of the library's own paging, as a page rather than as everything up
        // to it.
        library.Only.Url.ShouldContain("count=10");
        library.Only.Url.ShouldContain("offset=10");
    }

    /// <summary>
    /// A page larger than this installation will ask a library for is cut down, and the answer says
    /// it was.
    /// </summary>
    /// <remarks>
    /// A shortened list that does not say it was shortened is a wrong answer: a reader paging
    /// through what they believe is five hundred at a time silently skips four fifths of the
    /// library between one page and the next.
    /// </remarks>
    [Fact]
    public async Task A_page_bigger_than_this_installation_will_ask_for_says_it_was_capped()
    {
        library.AnswersListing(OnePhotograph);

        var page = await JsonAsync(admin, $"{ListUrl}?pageSize=5000");

        page.GetProperty("pageSize").GetInt32().ShouldBe(PhotoLibraryBrowseEndpoints.MaxPageSize);
        page.GetProperty("pageSizeCapped").GetBoolean().ShouldBeTrue();

        // Clamped before asking rather than after answering: a limit that lives only at the far end
        // stops existing the day the far end changes.
        library.Only.Url.ShouldContain($"count={PhotoLibraryBrowseEndpoints.MaxPageSize}");
    }

    /// <summary>
    /// Words are passed to a library that matches them, and refused for one that does not — rather
    /// than dropped.
    /// </summary>
    /// <remarks>
    /// A parameter neither honoured nor refused comes back as a full unfiltered page with the
    /// reader's words still in the box, which is the one failure this route can produce that looks
    /// exactly like a working search.
    /// </remarks>
    [Fact]
    public async Task Words_are_passed_to_a_library_that_matches_them_and_refused_by_one_that_does_not()
    {
        library.AnswersListing(OnePhotograph);

        (await JsonAsync(admin, $"{ListUrl}?q=rope")).GetProperty("items").GetArrayLength().ShouldBe(1);
        library.Only.Url.ShouldContain("q=rope");

        var other = new LibraryStub();
        using var both = Configured(library, other);
        var email = $"pb-two-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(both, GlobalRoles.Admin, email);
        using var caller = await AuthHelper.BearerClientAsync(both, email);

        var refused = await caller.GetAsync($"{OtherListUrl}?q=rope");
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(PhotoLibraryBrowseEndpoints.TextSearchUnsupportedCode);

        // The refusal is made before anything leaves this machine, and the same library answers a
        // listing perfectly well when it is not asked to match words.
        other.Calls.ShouldBeEmpty();

        other.AnswersListing("""{"assets":{"count":0,"items":[],"total":0,"nextPage":null}}""");
        (await JsonAsync(caller, OtherListUrl)).GetProperty("textSearchSupported").GetBoolean()
            .ShouldBeFalse();
    }

    /// <summary>
    /// A library that did not answer fails the request rather than arriving as an empty page.
    /// </summary>
    /// <remarks>
    /// A grid that quietly draws nothing cannot be told apart from a library nobody has put
    /// anything in, and that is the mistake this feature is most likely to make while looking
    /// correct.
    /// </remarks>
    [Fact]
    public async Task A_library_that_did_not_answer_fails_the_request()
    {
        library.Answers(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("no", Encoding.UTF8, "text/plain"),
        });

        var answer = await admin.GetAsync(ListUrl);
        var body = await answer.Content.ReadAsStringAsync();

        answer.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    // ------------------------------------------------------------------------- one photograph

    /// <summary>
    /// One photograph, with what the library said about it and nothing where it said nothing.
    /// </summary>
    [Fact]
    public async Task A_photograph_carries_what_the_library_said_and_leaves_the_rest_absent()
    {
        library.AnswersDetail(OnePhotographInFull);

        var detail = await JsonAsync(admin, DetailUrl);

        detail.GetProperty("photographId").GetString().ShouldBe(Uid);
        detail.GetProperty("reference").GetString().ShouldBe(Hash);
        detail.GetProperty("title").GetString().ShouldBe("A photograph of nowhere");
        detail.GetProperty("cameraModel").GetString().ShouldBe("Invented 1");
        detail.GetProperty("iso").GetInt32().ShouldBe(400);

        // What the library did not say is absent rather than invented, and a zero it wrote for a
        // picture whose own metadata carried nothing is read as unsaid.
        detail.GetProperty("lens").ValueKind.ShouldBe(JsonValueKind.Null);
        detail.GetProperty("aperture").ValueKind.ShouldBe(JsonValueKind.Null);

        // The picture is fetched through this application, so a panel showing the larger rendering
        // needs the same template the grid uses.
        detail.GetProperty("pictureUrlTemplate").GetString().ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// A photograph the library no longer reports is its own answer, kept apart from a library
    /// that is not working.
    /// </summary>
    /// <remarks>
    /// The two send a reader somewhere completely different: one means the picture is gone from
    /// over there and the list it was opened from is describing a library as it was a moment ago,
    /// and the other means somebody has to go and look at a container.
    /// </remarks>
    [Fact]
    public async Task A_photograph_the_library_no_longer_reports_is_not_found_rather_than_a_failure()
    {
        library.Answers(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("no", Encoding.UTF8, "text/plain"),
        });

        var answer = await admin.GetAsync(DetailUrl);
        var body = await answer.Content.ReadAsStringAsync();

        answer.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
        CodeOf(body).ShouldBe(PhotoLibraryBrowseEndpoints.PhotographNotFoundCode);
    }

    /// <summary>
    /// An identifier this application will not put in a request path never reaches the library.
    /// </summary>
    [Fact]
    public async Task An_identifier_that_is_not_one_is_refused_before_anything_leaves()
    {
        var answer = await admin.GetAsync($"{ListUrl}/not%20an%20identifier");

        answer.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------ and the pictures

    /// <summary>
    /// The address a grid loads its pictures from is the one this application already serves, and
    /// it works.
    /// </summary>
    /// <remarks>
    /// The whole template rather than a hand-built address, because what is being checked is that
    /// the browsing surface reaches pictures through the existing proxy — the credential, the path
    /// and the byte gate included — rather than through a second arrangement of its own.
    /// </remarks>
    [Fact]
    public async Task A_grids_pictures_go_through_the_delivery_route_this_application_already_has()
    {
        library.AnswersListing(OnePhotograph);
        library.AnswersPicture(InventedJpeg);

        var page = await JsonAsync(admin, ListUrl);
        var template = page.GetProperty("pictureUrlTemplate").GetString()!;
        var reference = page.GetProperty("items")[0].GetProperty("reference").GetString()!;

        var address = template.Replace("{reference}", reference, StringComparison.Ordinal)
            .Replace("{size}", "small", StringComparison.Ordinal);

        // Anonymous on purpose: a browser loads an image ambiently and cannot attach a bearer
        // token, which is what the short-lived credential in the address is for.
        var picture = await anonymous.GetAsync(address);

        picture.StatusCode.ShouldBe(HttpStatusCode.OK);
        picture.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");
        (await picture.Content.ReadAsByteArrayAsync()).ShouldBe(InventedJpeg);
    }

    /// <summary>
    /// A library whose pictures are stopped publishes no template, so a grid asks for none.
    /// </summary>
    /// <remarks>
    /// Not a preference: against this product a picture request it cannot resolve on disk marks the
    /// file missing and can delete the photograph from its own index, so the gate reaching the
    /// browser as an absent address is what stops a grid of a hundred tiles becoming a hundred
    /// deletions.
    /// </remarks>
    [Fact]
    public async Task A_library_whose_pictures_are_stopped_offers_a_listing_and_no_pictures()
    {
        // What a library with its originals out of reach answers a picture request with: a drawing,
        // carrying success — which is why the status code is not the signal.
        library.Answers(call => call.Url.Contains("/api/v1/t/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<svg/>", Encoding.UTF8, "image/svg+xml"),
            }
            : WithPreviewToken(Listing(OnePhotograph)));

        var page = await JsonAsync(admin, ListUrl);
        var template = page.GetProperty("pictureUrlTemplate").GetString()!;
        var address = template.Replace("{reference}", Hash, StringComparison.Ordinal)
            .Replace("{size}", "small", StringComparison.Ordinal);

        (await anonymous.GetAsync(address)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        // Closed for the whole process by that one answer, and published as an absent address on
        // every later listing — so a grid drawn after it asks for no pictures at all.
        var after = await JsonAsync(admin, ListUrl);
        after.GetProperty("picturesAvailable").GetBoolean().ShouldBeFalse();
        after.GetProperty("pictureUrlTemplate").ValueKind.ShouldBe(JsonValueKind.Null);

        // And the listing itself still works: a library that cannot serve pictures can list
        // perfectly well, and refusing the whole page would be this application inventing an
        // outage.
        after.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    // ------------------------------------------------------------------------------------- support

    private static readonly byte[] InventedJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    /// <summary>One invented photograph as this product lists one.</summary>
    private const string OnePhotograph = $$"""
    [{"UID":"{{Uid}}","Hash":"{{Hash}}","Title":"A photograph of nowhere",
      "TakenAt":"2026-02-03T04:05:06Z"}]
    """;

    /// <summary>The same invented photograph as this product describes one in full.</summary>
    private const string OnePhotographInFull = $$"""
    {"UID":"{{Uid}}","Hash":"{{Hash}}","Title":"A photograph of nowhere",
     "TakenAt":"2026-02-03T04:05:06Z","CameraMake":"Invented","CameraModel":"Invented 1",
     "Iso":400,"FNumber":0,"Exposure":"1/125"}
    """;

    private SilexGisApiFactory Configured(LibraryStub stub, LibraryStub? other = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PhotoLibraries:PhotoPrism:Enabled"] = "true",
            ["PhotoLibraries:PhotoPrism:BaseUrl"] = LibraryAddress,
            ["PhotoLibraries:PhotoPrism:AccessToken"] = FakeToken,
        };

        if (other is not null)
        {
            settings["PhotoLibraries:Immich:Enabled"] = "true";
            settings["PhotoLibraries:Immich:BaseUrl"] = OtherLibraryAddress;
            settings["PhotoLibraries:Immich:ApiKey"] = FakeApiKey;
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

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The answer as it was written, because one case here is about what is <em>not</em> in it and
    /// a parsed body can only be asked about fields somebody already thought of.
    /// </summary>
    private static async Task<string> RawAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    private static HttpResponseMessage Listing(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// The credential this product serves its pictures under, which it puts on every answer — so a
    /// listing carries it exactly as a map pan does.
    /// </summary>
    private static HttpResponseMessage WithPreviewToken(HttpResponseMessage response)
    {
        response.Headers.TryAddWithoutValidation("X-Preview-Token", "previewtokeninvented");
        return response;
    }

    private sealed record LibraryCall(string Method, string Url, string? Authorization);

    /// <summary>
    /// Stands in for the library, recording everything that would have left this machine and
    /// answering whatever a test scripts. The listing and the detail are told apart by their
    /// addresses, because this product answers both under the same path.
    /// </summary>
    private sealed class LibraryStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<LibraryCall> calls = [];

        private Func<LibraryCall, HttpResponseMessage> answer = _ => Listing("[]");

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

        /// <summary>Answers a page of the library, carrying the picture credential as the real one does.</summary>
        public void AnswersListing(string json) => Answers(_ => WithPreviewToken(Listing(json)));

        /// <summary>Answers about one photograph, and about nothing else.</summary>
        public void AnswersDetail(string json)
        {
            var previous = answer;
            Answers(call => call.Url.Contains($"/photos/{Uid}", StringComparison.Ordinal)
                ? Listing(json)
                : previous(call));
        }

        public void AnswersPicture(byte[] bytes)
        {
            var previous = answer;
            Answers(call => call.Url.Contains("/api/v1/t/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") },
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
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString());

            lock (gate) { calls.Add(call); }
            return Task.FromResult(answer(call));
        }
    }
}
