// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// anybody, that the audience is decided on the server rather than by not drawing a page, that a
/// listing carries no words however an address is written, that a page which was capped says so —
/// and the one that matters most, that <b>nothing on either of these responses is a position</b>. That last is asserted against the serialised body rather than
/// against the record's fields, because a field added later would be caught by the first and not
/// by the second.
/// </para>
/// <para>
/// Every coordinate, identifier, hash and title below is invented. Nothing here comes from a real
/// library and no photograph named here exists.
/// </para>
/// </summary>
public sealed class PhotoLibraryBrowseEndpointTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string ListUrl = "/api/v1/photo-libraries/photoprism/photographs";
    private const string OtherListUrl = "/api/v1/photo-libraries/immich/photographs";
    private const string DetailUrl = $"{ListUrl}/psinvented5";

    private const string FakeToken = "not-a-real-token-0000";
    private const string LibraryAddress = "http://photo-library.invalid:2342";

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
    /// A listing takes no words at all, whatever is put in the address.
    /// </summary>
    /// <remarks>
    /// The two questions are two routes on purpose — one asks a library what it holds, the other
    /// asks what it makes of a sentence, and the second comes back from one of the two products as
    /// an ordering of the whole library rather than as a narrowing of anything. So a listing has
    /// nowhere to put words, and a parameter left over in a bookmarked address cannot quietly
    /// narrow, reorder or empty it.
    /// </remarks>
    [Fact]
    public async Task A_listing_takes_no_words_whatever_is_in_the_address()
    {
        library.AnswersListing(OnePhotograph);

        (await JsonAsync(admin, $"{ListUrl}?q=rope")).GetProperty("items").GetArrayLength().ShouldBe(1);

        library.Only.Url.ShouldNotContain("q=rope");
        library.Only.Url.ShouldContain("order=newest");
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

    // --------------------------------------------------------- the days one trip was out

    /// <summary>
    /// Naming a trip narrows the listing to the days that trip was out, in the library's own
    /// grammar, with a day of margin at each end.
    /// </summary>
    /// <remarks>
    /// The margin is asserted rather than left implicit because it is the whole of the answer to a
    /// question this application cannot answer exactly: a trip's dates carry no zone and a camera's
    /// stamps are instants, so the two frames can be displaced by hours. A window without it draws a
    /// panel that is quietly short at both edges, with a number under it and nothing saying the
    /// number is small.
    /// </remarks>
    [Fact]
    public async Task A_trip_narrows_the_listing_to_the_days_it_was_out()
    {
        library.AnswersListing(OnePhotograph);
        var trip = await TripAsync("2026-03-14", "2026-03-15");

        var page = await JsonAsync(admin, $"{ListUrl}?tripId={trip}");
        page.GetProperty("items").GetArrayLength().ShouldBe(1);

        // Escaped, because that is what goes on the wire: both terms travel in one parameter.
        library.Only.Url.ShouldContain("q=after%3A2026-03-13%20before%3A2026-03-17");
    }

    /// <summary>
    /// A trip that named no end is asked about for its own day, and not for everything since.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is the one that looks most like the feature working: an open
    /// window pages, counts and fills a grid exactly as a real answer does, and would present a
    /// club's whole library as the photographs of one weekend.
    /// </remarks>
    [Fact]
    public async Task A_trip_that_named_no_end_is_not_asked_about_open_endedly()
    {
        library.AnswersListing(OnePhotograph);
        var trip = await TripAsync("2026-03-14", tripDateEnd: null);

        await JsonAsync(admin, $"{ListUrl}?tripId={trip}");

        library.Only.Url.ShouldContain("q=after%3A2026-03-13%20before%3A2026-03-16");
    }

    /// <summary>
    /// A trip that is not there is not found, and the library is asked nothing on its behalf.
    /// </summary>
    /// <remarks>
    /// The second half is what keeps this route from being a way of asking whether a trip exists:
    /// a request that went out to a neighbouring container and came back would take a visibly
    /// different length of time from one that never left.
    /// </remarks>
    [Fact]
    public async Task A_trip_that_is_not_there_is_not_found_and_nothing_is_asked()
    {
        library.AnswersListing(OnePhotograph);

        var refused = await admin.GetAsync($"{ListUrl}?tripId={Guid.NewGuid()}");
        var body = await refused.Content.ReadAsStringAsync();

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
        CodeOf(body).ShouldBe(PhotoLibraryBrowseEndpoints.TripNotFoundCode);
        library.Calls.ShouldBeEmpty();

        // And a trip that is there answers through the very same route, so the case above is a
        // refusal rather than a parameter that never works.
        var trip = await TripAsync("2026-03-14", "2026-03-15");
        (await JsonAsync(admin, $"{ListUrl}?tripId={trip}")).GetProperty("items")
            .GetArrayLength().ShouldBe(1);
    }

    /// <summary>
    /// A value that names no trip at all is answered the same way, with this application's own
    /// code, rather than being refused before the route is reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one parameter on the route a person types or pastes, so a mistyped one is
    /// ordinary. Bound as an identifier it would never arrive: the request would be refused before
    /// the handler, as a bad request carrying none of the codes this slice publishes — and a screen
    /// reading no code from a failure says the library did not answer, which is the one sentence
    /// the whole surface is careful never to show for a question no library was asked. Somebody
    /// would go and restart a container that is working perfectly.
    /// </para>
    /// <para>
    /// An empty value is refused with the rest and is the case worth having separately: read as
    /// "no trip named" it would answer a request that did name one with the whole library, under
    /// the trip's own heading.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("notaguid")]
    [InlineData("")]
    public async Task A_value_that_names_no_trip_is_not_found_and_nothing_is_asked(string named)
    {
        library.AnswersListing(OnePhotograph);

        var refused = await admin.GetAsync($"{ListUrl}?tripId={named}");
        var body = await refused.Content.ReadAsStringAsync();

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
        CodeOf(body).ShouldBe(PhotoLibraryBrowseEndpoints.TripNotFoundCode);
        library.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// A trip this account may not read is not found either, and again nothing is asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same answer as a trip that does not exist, on purpose and as everywhere else a row is
    /// read by identifier: telling somebody that a trip exists but is not theirs is itself a fact
    /// about the trip, and this route would be a way of asking it about every identifier in turn.
    /// </para>
    /// <para>
    /// The audience is opened for this one, because the point is an account refused by the
    /// <em>trip</em>. With the shipped setting the only accounts that reach this route at all are
    /// administrators, who can read every trip — so the case would pass while proving nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_trip_this_account_may_not_read_is_not_found_and_nothing_is_asked()
    {
        library.AnswersListing(OnePhotograph);

        // Written by the administrator and kept private, so nothing else reaches it.
        var trip = await TripAsync("2026-03-14", "2026-03-15");

        using var open = Configured(library, ("PhotoLibraries:Audience", "SignedIn"));
        var email = $"pb-tr-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(open, GlobalRoles.Viewer, email);
        using var outsider = await AuthHelper.BearerClientAsync(open, email);

        var refused = await outsider.GetAsync($"{ListUrl}?tripId={trip}");
        var body = await refused.Content.ReadAsStringAsync();

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
        CodeOf(body).ShouldBe(PhotoLibraryBrowseEndpoints.TripNotFoundCode);
        library.Calls.ShouldBeEmpty();

        // And the same account may still look through the library itself: what was refused is this
        // trip, not the feature, which is the difference the two codes carry.
        (await JsonAsync(outsider, ListUrl)).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    /// <summary>
    /// A trip whose dates cannot make a window is refused, and the library is asked nothing.
    /// </summary>
    /// <remarks>
    /// The record this is really about is one whose end date carries a mistyped year. Nothing
    /// refuses it, it looks ordinary in a list, and the window it would produce asks a club's
    /// library for a decade — which pages and counts perfectly and would be shown as the
    /// photographs of one weekend.
    /// </remarks>
    [Fact]
    public async Task A_trip_whose_dates_cannot_make_a_window_is_refused()
    {
        library.AnswersListing(OnePhotograph);
        var trip = await TripAsync("2026-03-14", "2036-03-15");

        var refused = await admin.GetAsync($"{ListUrl}?tripId={trip}");
        var body = await refused.Content.ReadAsStringAsync();

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(PhotoLibraryBrowseEndpoints.TripWindowUnusableCode);
        library.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// A listing asked for without a trip is still the whole library.
    /// </summary>
    /// <remarks>
    /// Asserted beside the cases above because the narrowing is a parameter on the route the
    /// browsing page already uses, and a window that leaked into the unnamed case would narrow that
    /// page to a day while every count above it went on saying "the library holds".
    /// </remarks>
    [Fact]
    public async Task A_listing_with_no_trip_named_asks_about_no_stretch_of_time()
    {
        library.AnswersListing(OnePhotograph);

        await JsonAsync(admin, ListUrl);

        library.Only.Url.ShouldNotContain("after");
        library.Only.Url.ShouldNotContain("before");
    }

    /// <summary>
    /// Naming a trip files nothing: it is a question about the library, and the answer says nothing
    /// about the trip.
    /// </summary>
    /// <remarks>
    /// Binding a photograph to a trip is a later feature and a different decision, so this pins the
    /// absence rather than leaving it to be noticed: the answer carries no trip, and every call this
    /// makes to the library is a reading one. What a photograph is <em>of</em> is nobody's claim
    /// here.
    /// </remarks>
    [Fact]
    public async Task Naming_a_trip_writes_nothing_and_claims_nothing_about_it()
    {
        library.AnswersListing(OnePhotograph);
        var trip = await TripAsync("2026-03-14", "2026-03-15");

        var raw = await RawAsync(admin, $"{ListUrl}?tripId={trip}");

        raw.ShouldNotContain(trip.ToString());
        raw.ShouldNotContain("trip");

        foreach (var call in library.Calls)
        {
            call.Method.ShouldBe("GET");
        }
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

    /// <summary>
    /// One product configured and the other not, which is also the state the case above about a
    /// library this installation does not run depends on.
    /// </summary>
    private SilexGisApiFactory Configured(
        LibraryStub stub, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PhotoLibraries:PhotoPrism:Enabled"] = "true",
            ["PhotoLibraries:PhotoPrism:BaseUrl"] = LibraryAddress,
            ["PhotoLibraries:PhotoPrism:AccessToken"] = FakeToken,
        };

        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return new SilexGisApiFactory(
            connectionString,
            settings,
            services => services.AddHttpClient(PhotoPrismClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => stub));
    }

    /// <summary>
    /// One trip written by the administrator, private, naming no cave and nobody — the dates are the
    /// whole of what these cases are about, and anything else on the record would only be another
    /// way for one of them to fail.
    /// </summary>
    private async Task<Guid> TripAsync(string tripDate, string? tripDateEnd)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"An invented trip {Guid.NewGuid():N}",
            tripDate,
            tripDateEnd,
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility = "private",
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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
