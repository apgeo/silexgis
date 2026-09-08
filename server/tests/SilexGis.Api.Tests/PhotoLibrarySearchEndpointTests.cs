// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
/// The route that puts words to a neighbouring photo library, exercised against stubs standing in
/// for both products.
///
/// <para>
/// Both are configured here, and that is the point of the file: the two answer a completely
/// different question — one matches the words against text somebody wrote down, the other orders
/// everything it holds by how close each picture is to what the words describe — and almost every
/// case below asserts that the answer says which of the two happened rather than presenting them
/// as one kind of result.
/// </para>
/// <para>
/// The failures being guarded are the ones that look like the feature working: a search that comes
/// back as the whole library because the words went nowhere, a library that is stopped coming back
/// as "nothing matches", and a count rendered over a ranking as though something had been counted.
/// </para>
/// <para>
/// Every identifier, hash, title and phrase below is invented. Nothing here comes from a real
/// library and no photograph named here exists.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhotoLibrarySearchEndpointTests : IAsyncLifetime, IDisposable
{
    private const string TextUrl = "/api/v1/photo-libraries/photoprism/search";
    private const string MeaningUrl = "/api/v1/photo-libraries/immich/search";
    private const string StatusUrl = "/api/v1/photo-libraries/status";

    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeApiKey = "not-a-real-key-0000";
    private const string TextLibraryAddress = "http://photo-library.invalid:2342";
    private const string MeaningLibraryAddress = "http://other-photo-library.invalid:2283";

    /// <summary>Invented identifiers of the shapes the two products mint.</summary>
    private const string Uid = "psinvented5";
    private const string Hash = "bb22cc33dd44";
    private const string AssetId = "11111111-1111-4111-8111-111111111111";

    private readonly string connectionString;
    private readonly LibraryStub text = new();
    private readonly LibraryStub meaning = new();
    private readonly SilexGisApiFactory factory;

    private string tag = string.Empty;
    private HttpClient admin = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;

    public PhotoLibrarySearchEndpointTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        connectionString = postgres.ConnectionString;
        factory = Configured();
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ps-ad-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"ps-ad-{tag}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ps-vw-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ps-vw-{tag}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        admin?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        text.Dispose();
        meaning.Dispose();
    }

    // ------------------------------------------------------------------------------- who may look

    /// <summary>
    /// The route is behind the same audience setting the rest of the feature is, and nothing is
    /// asked of any library on behalf of somebody who may not see it.
    /// </summary>
    [Fact]
    public async Task A_search_is_administrators_only_unless_an_operator_says_otherwise()
    {
        text.AnswersRows(OnePhotograph);

        (await anonymous.GetAsync($"{TextUrl}?q=rope")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var refused = await viewer.GetAsync($"{TextUrl}?q=rope");
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe("photo_library.forbidden");

        text.Calls.ShouldBeEmpty();

        (await JsonAsync(admin, $"{TextUrl}?q=rope")).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    /// <summary>A product this installation does not run is not found.</summary>
    [Fact]
    public async Task A_library_this_installation_does_not_run_is_not_found()
    {
        (await admin.GetAsync("/api/v1/photo-libraries/nosuchproduct/search?q=rope"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        text.Calls.ShouldBeEmpty();
        meaning.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------- what may be asked for

    /// <summary>
    /// A search with nothing to search for is refused, rather than answered with the library.
    /// </summary>
    /// <remarks>
    /// This is the failure the whole surface is arranged to avoid: a full page of a library drawn
    /// under a heading saying it matched what somebody typed. The listing route exists for the
    /// question "what does this library hold", and it says so in its own answer.
    /// </remarks>
    [Fact]
    public async Task A_search_with_nothing_to_search_for_is_refused()
    {
        foreach (var address in (string[])[TextUrl, $"{TextUrl}?q=", $"{TextUrl}?q=%20%20"])
        {
            var response = await admin.GetAsync(address);
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
            CodeOf(body).ShouldBe(PhotoLibrarySearchEndpoints.SearchEmptyCode);
        }

        // Refused before anything leaves this machine.
        text.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// Words longer than this installation will put in a request to a neighbour are refused here
    /// rather than sent and left to the far side to think about.
    /// </summary>
    [Fact]
    public async Task A_search_longer_than_this_installation_will_send_is_refused()
    {
        var tooLong = new string('a', PhotoLibrarySearchEndpoints.MaxSearchLength + 1);

        var response = await admin.GetAsync($"{TextUrl}?q={tooLong}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(PhotoLibrarySearchEndpoints.SearchTooLongCode);

        text.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// A page larger than this installation will ask a library for is cut down, and the answer says
    /// it was.
    /// </summary>
    [Fact]
    public async Task A_page_bigger_than_this_installation_will_ask_for_says_it_was_capped()
    {
        text.AnswersRows(OnePhotograph);

        var page = await JsonAsync(admin, $"{TextUrl}?q=rope&pageSize=5000");

        page.GetProperty("pageSize").GetInt32().ShouldBe(PhotoLibraryBrowseEndpoints.MaxPageSize);
        page.GetProperty("pageSizeCapped").GetBoolean().ShouldBeTrue();

        text.Only.Url.ShouldContain($"count={PhotoLibraryBrowseEndpoints.MaxPageSize}");
    }

    // ------------------------------------------------ which question the library actually answered

    /// <summary>
    /// Each answer says which of the two questions the library answered, and the status route says
    /// which it will answer before anybody has typed anything.
    /// </summary>
    /// <remarks>
    /// Both halves are needed and they are not the same fact. The status route lets a screen word
    /// its search box before the first request — a box inviting somebody to describe a picture over
    /// a library that can only look up words is a promise the far side cannot keep. The answer says
    /// what actually happened, which is the only thing this application is in a position to know:
    /// whether picture recognition is even switched on at the far end is readable by an
    /// administrator of that product, and a credential belonging to this installation is not one.
    /// </remarks>
    [Fact]
    public async Task An_answer_says_which_question_the_library_answered()
    {
        text.AnswersRows(OnePhotograph);
        meaning.AnswersAssets(OneAsset);

        (await JsonAsync(admin, $"{TextUrl}?q=rope")).GetProperty("matching").GetString()
            .ShouldBe("text");
        (await JsonAsync(admin, $"{MeaningUrl}?q=rope")).GetProperty("matching").GetString()
            .ShouldBe("meaning");

        var status = await JsonAsync(admin, StatusUrl);
        var published = status.GetProperty("providers").EnumerateArray()
            .ToDictionary(
                provider => provider.GetProperty("source").GetString()!,
                provider => provider.GetProperty("search").GetString());

        published["photoprism"].ShouldBe("text");
        published["immich"].ShouldBe("meaning");
    }

    /// <summary>
    /// The words reach each library in the form that library reads, and reach the ranking whole.
    /// </summary>
    /// <remarks>
    /// The difference is deliberate rather than an inconsistency. One product parses its search
    /// parameter into the same form its own filters bind to — geographic ones included — so a
    /// <c>name:value</c> pair typed into a box would set a field rather than match a word, and a
    /// search narrowed to a circle around a point is a way of reading a coordinate off a surface
    /// built to carry none. The other reads the words as a description with no grammar to abuse, so
    /// taking punctuation out of them would only change what somebody asked for.
    /// </remarks>
    [Fact]
    public async Task The_words_reach_each_library_in_the_form_that_library_reads()
    {
        text.AnswersRows(OnePhotograph);
        meaning.AnswersAssets(OneAsset);

        await JsonAsync(admin, $"{TextUrl}?q=lat%3A45.18%20rope");
        var sent = text.Only.Url;
        sent[(sent.IndexOf("&q=", StringComparison.Ordinal) + 3)..].ShouldNotContain("%3A");
        sent.ShouldContain("order=newest");

        await JsonAsync(admin, $"{MeaningUrl}?q=a%20muddy%20crawl");
        meaning.Only.Body.ShouldContain("\"query\":\"a muddy crawl\"");
    }

    // -------------------------------------------------------- what the answer does and does not say

    /// <summary>
    /// Nothing on a search answer is a count of matches, on either product.
    /// </summary>
    /// <remarks>
    /// Asserted against the serialised body rather than the record's fields, because what is being
    /// checked is an absence: a field added later — from a library answer that happens to carry a
    /// number — shows up in the body and shows up in no assertion that only asks about fields
    /// somebody already thought of. One product ranks its whole library and so has no set of
    /// matches to count; the other counts only the page it has just sent, which is a number the
    /// page already is. Either way a total here would be invented, and nothing on a screen
    /// distinguishes an invented number from a counted one.
    /// </remarks>
    [Fact]
    public async Task Nothing_on_a_search_answer_is_a_count_of_matches()
    {
        text.AnswersRows(OnePhotograph);

        // The ranking product states a number beside every page it sends. It is not carried.
        meaning.AnswersAssets(OneAsset, total: 4321);

        foreach (var address in (string[])[$"{TextUrl}?q=rope", $"{MeaningUrl}?q=rope"])
        {
            var names = NamesIn(await JsonAsync(admin, address));

            names.ShouldNotBeEmpty();

            foreach (var forbidden in (string[])["total", "count", "matches", "ranked", "results"])
            {
                names.ShouldNotContain(
                    name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>
    /// Nothing on a search answer is a position, and no request takes a rectangle.
    /// </summary>
    /// <remarks>
    /// Every name in the serialised answer is walked rather than the fields somebody thought to
    /// read: a field added to this record later, from a library answer that happens to carry a
    /// coordinate, shows up in the walk and in no assertion that only asks about what it knows.
    /// </remarks>
    [Fact]
    public async Task Nothing_on_a_search_answer_is_a_position()
    {
        text.AnswersRows(OnePhotograph);
        meaning.AnswersAssets(OneAsset);

        foreach (var address in (string[])[$"{TextUrl}?q=rope", $"{MeaningUrl}?q=rope"])
        {
            var names = NamesIn(await JsonAsync(admin, address));

            foreach (var forbidden in (string[])
                ["latitude", "longitude", "lat", "lon", "lng", "bbox", "coordinates", "geometry",
                 "city", "state", "country", "place", "locality"])
            {
                names.ShouldNotContain(
                    name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }

        // A rectangle in the address binds to nothing, and nothing about one reaches a library.
        await JsonAsync(admin, $"{TextUrl}?q=rope&bbox=21.5,45.125,24.25,46.75");

        foreach (var call in text.Calls.Concat(meaning.Calls))
        {
            call.Url.ShouldNotContain("latlng");
            call.Url.ShouldNotContain("bbox");
            call.Body.ShouldNotContain("bbox");
        }
    }

    /// <summary>
    /// A search answer carries the address a browser fetches its pictures from, which is this
    /// application's own and never the library's.
    /// </summary>
    [Fact]
    public async Task A_searchs_pictures_go_through_the_delivery_route_this_application_already_has()
    {
        text.AnswersRows(OnePhotograph);

        var page = await JsonAsync(admin, $"{TextUrl}?q=rope");
        var template = page.GetProperty("pictureUrlTemplate").GetString();

        template.ShouldNotBeNullOrEmpty();
        template.ShouldStartWith("/api/v1/photo-libraries/photoprism/thumbnails/");
        template.ShouldContain("{reference}");

        (await RawAsync(admin, $"{TextUrl}?q=rope")).ShouldNotContain(TextLibraryAddress);
    }

    // ------------------------------------------------------------------ silence is not emptiness

    /// <summary>
    /// A library that did not answer fails the request, and a library that answered with nothing
    /// succeeds with nothing. They are different states and they are told apart.
    /// </summary>
    /// <remarks>
    /// This is the pair a reader most needs told apart on this surface. Nothing found sends
    /// somebody to try other words; a library that did not answer sends somebody to look at a
    /// container. Rendered as one, the second is an afternoon spent looking for better words for a
    /// library that is stopped.
    /// </remarks>
    [Fact]
    public async Task A_library_that_did_not_answer_is_not_a_search_that_found_nothing()
    {
        text.Answers(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var silent = await admin.GetAsync($"{TextUrl}?q=rope");
        var body = await silent.Content.ReadAsStringAsync();

        silent.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(PhotoLibraryException.UnavailableCode);

        text.AnswersRows("[]");

        var nothing = await JsonAsync(admin, $"{TextUrl}?q=rope");
        nothing.GetProperty("items").GetArrayLength().ShouldBe(0);
        nothing.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        nothing.GetProperty("matching").GetString().ShouldBe("text");
    }

    // ------------------------------------------------------------------------------------ fixtures

    /// <summary>One invented photograph as one product lists one.</summary>
    private const string OnePhotograph = $$"""
    [{"UID":"{{Uid}}","Hash":"{{Hash}}","Title":"A photograph of nowhere",
      "TakenAt":"2026-02-03T04:05:06Z"}]
    """;

    /// <summary>One invented photograph as the other product lists one, inside its own wrapper.</summary>
    private const string OneAsset = $$"""
    {"id":"{{AssetId}}","type":"IMAGE","fileCreatedAt":"2026-02-03T04:05:06Z"}
    """;

    private SilexGisApiFactory Configured() =>
        new(
            connectionString,
            new Dictionary<string, string?>
            {
                ["PhotoLibraries:PhotoPrism:Enabled"] = "true",
                ["PhotoLibraries:PhotoPrism:BaseUrl"] = TextLibraryAddress,
                ["PhotoLibraries:PhotoPrism:AccessToken"] = FakeToken,
                ["PhotoLibraries:Immich:Enabled"] = "true",
                ["PhotoLibraries:Immich:BaseUrl"] = MeaningLibraryAddress,
                ["PhotoLibraries:Immich:ApiKey"] = FakeApiKey,
            },
            services =>
            {
                services.AddHttpClient(PhotoPrismClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => text);
                services.AddHttpClient(ImmichClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => meaning);
            });

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The answer as it was written, because two cases here are about what is <em>not</em> in it and
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

    private static HttpResponseMessage Answer(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// The credential one of the products serves its pictures under, which it puts on every answer.
    /// </summary>
    private static HttpResponseMessage WithPreviewToken(HttpResponseMessage response)
    {
        response.Headers.TryAddWithoutValidation("X-Preview-Token", "previewtokeninvented");
        return response;
    }

    private sealed record LibraryCall(string Method, string Url, string Body);

    /// <summary>
    /// Stands in for a library, recording everything that would have left this machine — the body
    /// included, because one of these products is asked its questions in one.
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

        /// <summary>
        /// Answers with rows as one product writes them: a bare list, with the picture credential
        /// on the response the way that product puts it there.
        /// </summary>
        public void AnswersRows(string json) => Answers(_ => WithPreviewToken(Answer(json)));

        /// <summary>
        /// Answers with rows as the other product writes them, inside the envelope it wraps a page
        /// in. Written here rather than in each case so no case accidentally asserts against a
        /// shape this product does not produce.
        /// </summary>
        public void AnswersAssets(string items, int? total = null, string? nextPage = null)
        {
            var page = """{"assets":{"count":1,"items":["""
                + items
                + """],"total":"""
                + (total?.ToString(CultureInfo.InvariantCulture) ?? "null")
                + ""","nextPage":"""
                + (nextPage is null ? "null" : $"\"{nextPage}\"")
                + "}}";

            Answers(_ => Answer(page));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var call = new LibraryCall(request.Method.Method, request.RequestUri!.AbsoluteUri, body);

            lock (gate) { calls.Add(call); }

            return answer(call);
        }
    }
}
