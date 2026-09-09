// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
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
/// The route that reads a neighbouring library's albums, and the narrowing it exists to feed,
/// exercised against a stub standing in for the library.
///
/// <para>
/// The far side is a whole separate product and is not run here. What is proved instead is
/// everything on this side of the socket: that an installation given no library asks nothing of
/// anybody, that the audience is decided on the server rather than by not drawing a chooser, that
/// an album this application will not put in a request is refused before anything leaves the
/// machine — and the one that matters most, that <b>a request naming an album is never answered
/// with the whole library</b>. On a screen those two are the same page.
/// </para>
/// <para>
/// Every identifier, title and count below is invented. Nothing here comes from a real library and
/// no album named here exists.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhotoLibraryAlbumEndpointTests : IAsyncLifetime, IDisposable
{
    private const string AlbumsUrl = "/api/v1/photo-libraries/photoprism/albums";
    private const string OtherAlbumsUrl = "/api/v1/photo-libraries/immich/albums";
    private const string ListUrl = "/api/v1/photo-libraries/photoprism/photographs";

    private const string FakeToken = "not-a-real-token-0000";
    private const string LibraryAddress = "http://photo-library.invalid:2342";

    /// <summary>Invented identifiers of the shapes this product mints.</summary>
    private const string AlbumUid = "asinvented000000one";
    private const string Uid = "psinvented5";
    private const string Hash = "bb22cc33dd44";

    private readonly string connectionString;
    private readonly LibraryStub library = new();
    private readonly SilexGisApiFactory factory;

    private string tag = string.Empty;
    private HttpClient admin = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;

    public PhotoLibraryAlbumEndpointTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        connectionString = postgres.ConnectionString;
        factory = Configured(library);
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"pa-ad-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"pa-ad-{tag}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"pa-vw-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"pa-vw-{tag}@t.local");
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
    /// The route is behind the audience setting, decided on the server rather than by a rail that
    /// does not offer the page. Nothing is asked of the library on behalf of somebody who may not
    /// see it.
    /// </summary>
    [Fact]
    public async Task The_album_route_is_administrators_only_unless_an_operator_says_otherwise()
    {
        library.AnswersAlbums(OneAlbum);

        (await anonymous.GetAsync(AlbumsUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await viewer.GetAsync(AlbumsUrl);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe("photo_library.forbidden");

        library.Calls.ShouldBeEmpty();

        (await JsonAsync(admin, AlbumsUrl)).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    /// <summary>A product this installation does not run has no albums to offer, and is not found.</summary>
    [Fact]
    public async Task A_library_this_installation_does_not_run_is_not_found()
    {
        (await admin.GetAsync("/api/v1/photo-libraries/nosuchproduct/albums"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A product this build knows and this installation has been given no address for is the
        // same answer, and no socket is opened reaching it.
        (await admin.GetAsync(OtherAlbumsUrl)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        library.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------- positionless by construction

    /// <summary>
    /// Nothing on the album list is a position, and nothing about one is asked for.
    /// </summary>
    /// <remarks>
    /// Every name in the serialised answer is walked rather than the fields somebody thought to
    /// read, because a field added to this record later — from a library answer that happens to
    /// carry a coordinate, and both products will say where an album's photographs were taken —
    /// shows up in the walk and in no test that only asks about fields it already knows.
    /// </remarks>
    [Fact]
    public async Task Nothing_on_the_album_list_is_a_position()
    {
        library.AnswersAlbums(OneAlbum);

        var names = NamesIn(await JsonAsync(admin, AlbumsUrl));

        names.ShouldNotBeEmpty();

        foreach (var forbidden in (string[])
            ["latitude", "longitude", "lat", "lon", "lng", "bbox", "coordinates", "geometry",
             "city", "state", "country", "place", "locality"])
        {
            names.ShouldNotContain(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>The library's own address never reaches a browser.</summary>
    [Fact]
    public async Task No_answer_names_the_library_itself()
    {
        library.AnswersAlbums(OneAlbum);

        (await RawAsync(admin, AlbumsUrl)).ShouldNotContain("photo-library.invalid");
    }

    // ------------------------------------------------------------------ what the answer may claim

    /// <summary>
    /// The album's count is the library's own number, published as it was stated.
    /// </summary>
    /// <remarks>
    /// And it describes the library rather than the caller, which is not something the number
    /// itself could show: every account reaches this library through one credential belonging to
    /// the installation, so two different accounts see the same number for the same album.
    /// </remarks>
    [Fact]
    public async Task An_albums_count_is_the_librarys_own_number_and_is_the_same_for_everybody()
    {
        library.AnswersAlbums(OneAlbum);

        var mine = await JsonAsync(admin, AlbumsUrl);
        var album = mine.GetProperty("items")[0];

        album.GetProperty("albumId").GetString().ShouldBe(AlbumUid);
        album.GetProperty("photographCount").GetInt32().ShouldBe(12);

        // The same question from a second administrator, answered with the same number, because the
        // credential that reached the library was the installation's rather than either of theirs.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"pa-ad2-{tag}@t.local");
        using var second = await AuthHelper.BearerClientAsync(factory, $"pa-ad2-{tag}@t.local");

        var theirs = await JsonAsync(second, AlbumsUrl);
        theirs.GetProperty("items")[0].GetProperty("photographCount").GetInt32().ShouldBe(12);
    }

    /// <summary>
    /// A library that did not answer fails the request rather than arriving as a library with no
    /// albums.
    /// </summary>
    /// <remarks>
    /// The two draw the same empty chooser and send a reader to entirely different places — one to
    /// make an album they already have, the other to a container that is down — so only the server
    /// can keep them apart, and this is where it does.
    /// </remarks>
    [Fact]
    public async Task A_library_that_did_not_answer_fails_rather_than_reporting_no_albums()
    {
        library.Answers(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await admin.GetAsync(AlbumsUrl);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    /// <summary>A library that keeps no albums says so, with an answer it actually gave.</summary>
    [Fact]
    public async Task A_library_that_keeps_no_albums_answers_with_none()
    {
        library.AnswersAlbums("[]");

        var answer = await JsonAsync(admin, AlbumsUrl);

        answer.GetProperty("items").GetArrayLength().ShouldBe(0);
        answer.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        answer.GetProperty("readAt").GetDateTimeOffset().ShouldBeGreaterThan(
            DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    // ---------------------------------------------------------------------- narrowing to an album

    /// <summary>
    /// A listing narrowed to an album puts the album to the library, and the album alone.
    /// </summary>
    [Fact]
    public async Task A_listing_narrowed_to_an_album_asks_the_library_for_that_album()
    {
        library.AnswersListing(OnePhotograph);

        var answer = await JsonAsync(admin, $"{ListUrl}?albumId={AlbumUid}");
        answer.GetProperty("items").GetArrayLength().ShouldBe(1);

        library.Only.Url.ShouldContain("album%3A" + AlbumUid);
    }

    /// <summary>
    /// An album this application will not put in a request to a neighbour is refused before
    /// anything leaves the machine, and is not answered with the whole library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is the point twice over. One of these products parses a value like this into the
    /// same form its own filters bind to, geographic ones included, so a value carrying a space or
    /// a colon would let a listing be narrowed to a circle around a point on a surface whose whole
    /// premise is that it carries no position. And a request that named an album and came back with
    /// everything would be showing a full page of the library under the album's own heading.
    /// </para>
    /// <para>
    /// Unreachable from the chooser, which only offers albums the library itself listed, and
    /// reachable from an address somebody typed or was handed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_album_this_application_will_not_send_is_refused_rather_than_ignored()
    {
        library.AnswersListing(OnePhotograph);

        foreach (var written in (string[])
            ["", "  ", "album:one lat:45", "one two", "../../etc", "one%20two", "a:b"])
        {
            var response = await admin.GetAsync(
                $"{ListUrl}?albumId={Uri.EscapeDataString(written)}");
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
            CodeOf(body).ShouldBe(PhotoLibraryAlbumEndpoints.AlbumNotFoundCode);
        }

        // Nothing was asked of the library for any of them.
        library.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// A listing that named no album is not narrowed to one, and the address it came from cannot
    /// smuggle one in through another parameter.
    /// </summary>
    [Fact]
    public async Task A_listing_that_named_no_album_is_not_narrowed()
    {
        library.AnswersListing(OnePhotograph);

        await JsonAsync(admin, ListUrl);

        library.Only.Url.ShouldNotContain("album");
    }

    /// <summary>
    /// A search is not narrowed by an album, whatever the address says.
    /// </summary>
    /// <remarks>
    /// The words go to the library whole — neither product will take a set of albums to search
    /// within — so a search that quietly carried one would answer somebody's typed question with a
    /// subset that was never on the screen, under a count that then counted the subset.
    /// </remarks>
    [Fact]
    public async Task A_search_is_not_narrowed_by_an_album()
    {
        library.AnswersListing(OnePhotograph);

        await JsonAsync(
            admin,
            $"/api/v1/photo-libraries/photoprism/search?q=crawl&albumId={AlbumUid}");

        library.Only.Url.ShouldNotContain("album");
    }

    // ------------------------------------------------------------------------------------- support

    /// <summary>One invented album, as this product describes one.</summary>
    private const string OneAlbum = $$"""
    [{"UID":"{{AlbumUid}}","Title":"An invented expedition","Type":"album","PhotoCount":12}]
    """;

    /// <summary>One invented photograph, as this product describes one in a listing.</summary>
    private const string OnePhotograph = $$"""
    [{"UID":"{{Uid}}","Hash":"{{Hash}}","Title":"A photograph of nowhere",
      "TakenAt":"2026-02-03T04:05:06Z"}]
    """;

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

    /// <summary>One product configured and the other not.</summary>
    private SilexGisApiFactory Configured(LibraryStub stub) =>
        new(
            connectionString,
            new Dictionary<string, string?>
            {
                ["PhotoLibraries:PhotoPrism:Enabled"] = "true",
                ["PhotoLibraries:PhotoPrism:BaseUrl"] = LibraryAddress,
                ["PhotoLibraries:PhotoPrism:AccessToken"] = FakeToken,
            },
            services => services.AddHttpClient(PhotoPrismClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => stub));

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

    private static HttpResponseMessage Answer(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// The credential this product serves its pictures under, which it puts on every answer — so
    /// filling a chooser carries it exactly as a map pan does.
    /// </summary>
    private static HttpResponseMessage WithPreviewToken(HttpResponseMessage response)
    {
        response.Headers.TryAddWithoutValidation("X-Preview-Token", "previewtokeninvented");
        return response;
    }

    private sealed record LibraryCall(string Method, string Url, string? Authorization);

    /// <summary>
    /// Stands in for the library, recording everything that would have left this machine and
    /// answering whatever a test scripts.
    /// </summary>
    private sealed class LibraryStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<LibraryCall> calls = [];

        private Func<LibraryCall, HttpResponseMessage> answer = _ => Answer("[]");

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

        /// <summary>Answers the album route, and nothing else.</summary>
        public void AnswersAlbums(string json)
        {
            var previous = answer;
            Answers(call => call.Url.Contains("/api/v1/albums", StringComparison.Ordinal)
                ? WithPreviewToken(Answer(json))
                : previous(call));
        }

        /// <summary>Answers a page of the library, carrying the picture credential as the real one does.</summary>
        public void AnswersListing(string json)
        {
            var previous = answer;
            Answers(call => call.Url.Contains("/api/v1/photos", StringComparison.Ordinal)
                ? WithPreviewToken(Answer(json))
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
