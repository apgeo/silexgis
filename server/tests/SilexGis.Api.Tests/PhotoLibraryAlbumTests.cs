// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading a neighbouring library's albums, and narrowing a listing to one of them.
///
/// <para>
/// No database and no host: the clients are built directly over a recording stub, because what is
/// under test is which question leaves this machine and what is made of the answer. Both halves
/// have a way of being wrong that looks exactly like the feature working. A narrowing that never
/// reached the far side comes back as a full page of the library under an album's name, which on a
/// screen is indistinguishable from an album that happens to hold everything; and a count read out
/// of a field that counts something else is a number nobody can tell from a fact. So the addresses
/// and the bodies are asserted as often as the answers.
/// </para>
/// <para>
/// Every identifier, title and count below is invented. Nothing here comes from any real library,
/// and no album named here exists.
/// </para>
/// </summary>
public sealed class PhotoLibraryAlbumTests
{
    private const string PrismAddress = "http://photo-library.invalid:2342";
    private const string ImmichAddress = "http://other-photo-library.invalid:2283";
    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeKey = "not-a-real-key-0000";

    /// <summary>Invented album identifiers of the shape each product mints.</summary>
    private const string ImmichAlbum = "33333333-3333-4333-8333-333333333333";
    private const string PrismAlbum = "asinvented000000one";

    // ------------------------------------------------------- the question that leaves this machine

    /// <summary>
    /// The albums are asked of each product's own album route, and nothing about the photographs in
    /// one is asked at all.
    /// </summary>
    /// <remarks>
    /// The second half matters as much as the first. A chooser needs a name and a number; asking
    /// each album for its contents in order to count them would be one request per album against a
    /// neighbouring container, for a control somebody has not used yet.
    /// </remarks>
    [Fact]
    public async Task Albums_are_asked_of_the_album_route_and_nothing_else()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).AlbumsAsync(default);

        prism.Only.Url.ShouldContain("/api/v1/albums?");
        prism.Only.Url.ShouldNotContain("/api/v1/photos");
        prism.Only.Method.ShouldBe("GET");

        var immich = new LibraryStub();
        immich.Answers(_ => Json("[]"));
        await Immich(immich).AlbumsAsync(default);

        immich.Only.Url.ShouldEndWith("/api/albums");
        immich.Only.Url.ShouldNotContain("/api/search");
        immich.Only.Method.ShouldBe("GET");
    }

    /// <summary>
    /// One product is asked for the collections somebody made and not for the ones it works out for
    /// itself.
    /// </summary>
    /// <remarks>
    /// It files months, folders and places under the same route as albums, and one of those is
    /// derived from where the pictures were taken. A chooser offering them would be offering a
    /// place filter under the heading "album", on a surface whose whole premise is that it carries
    /// no position.
    /// </remarks>
    [Fact]
    public async Task Only_the_albums_somebody_made_are_asked_for()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).AlbumsAsync(default);

        prism.Only.Url.ShouldContain("type=album");

        // Ordered at the far side rather than here, because the list may be cut at this
        // installation's ceiling and a set sorted after cutting names a different set.
        prism.Only.Url.ShouldContain("order=name");
    }

    /// <summary>Nothing geographic is asked for and nothing geographic comes back.</summary>
    [Fact]
    public async Task Reading_albums_sends_no_rectangle_and_no_coordinate()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json($"[{PrismAlbumRow(PrismAlbum, "An invented album", 12)}]"));
        await Prism(prism).AlbumsAsync(default);

        foreach (var word in (string[])["latlng", "lat=", "lng=", "bbox", "dist=", "s2=", "olc="])
        {
            prism.Only.Url.ShouldNotContain(word);
        }

        var immich = new LibraryStub();
        immich.Answers(_ => Json($"[{ImmichAlbumRow(ImmichAlbum, "An invented album", 12)}]"));
        await Immich(immich).AlbumsAsync(default);

        foreach (var word in (string[])["bbox", "lat", "lon", "city", "state", "country"])
        {
            immich.Only.Url.ShouldNotContain(word);
        }
    }

    // ------------------------------------------------------------------- what is made of an answer

    /// <summary>
    /// A count is the library's own number, and it is absent where the library did not state one.
    /// </summary>
    /// <remarks>
    /// The absence is the point. Nothing here counts an album by asking for its photographs, so an
    /// album whose product publishes no count is offered without one rather than with a number this
    /// application worked out — and a reader cannot tell a worked-out number from a stated one.
    /// </remarks>
    [Fact]
    public async Task A_count_is_the_librarys_own_number_or_absent()
    {
        var stated = new LibraryStub();
        stated.Answers(_ => Json($"[{ImmichAlbumRow(ImmichAlbum, "An invented album", 412)}]"));

        var counted = await Immich(stated).AlbumsAsync(default);
        counted.Albums.Single().PhotographCount.ShouldBe(412);

        var silent = new LibraryStub();
        silent.Answers(_ => Json(
            $$"""[{"id":"{{ImmichAlbum}}","albumName":"An invented album"}]"""));

        var unsaid = await Immich(silent).AlbumsAsync(default);
        unsaid.Albums.Single().PhotographCount.ShouldBeNull();
    }

    /// <summary>
    /// Zero is a number the library stated, not a field it left out.
    /// </summary>
    /// <remarks>
    /// Worth its own case because the reader used for a measurement written into a picture treats a
    /// zero as unsaid — that product writes zeroes into an aperture it read nothing for — and using
    /// the same reader here would turn "somebody has just emptied this album" into "this product
    /// does not publish counts".
    /// </remarks>
    [Fact]
    public async Task An_empty_album_is_reported_as_holding_none_rather_than_as_unsaid()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json($"[{ImmichAlbumRow(ImmichAlbum, "An emptied album", 0)}]"));

        (await Immich(immich).AlbumsAsync(default)).Albums.Single().PhotographCount.ShouldBe(0);

        var prism = new LibraryStub();
        prism.Answers(_ => Json($"[{PrismAlbumRow(PrismAlbum, "An emptied album", 0)}]"));

        (await Prism(prism).AlbumsAsync(default)).Albums.Single().PhotographCount.ShouldBe(0);
    }

    /// <summary>
    /// What only one of the two products says is carried from that one and left absent on the
    /// other, never derived from something adjacent.
    /// </summary>
    /// <remarks>
    /// One product states the stretch of time an album covers. The other states the year, month and
    /// day it files an album under, which is a different claim — a picture added later belongs to
    /// the album without moving the date it is filed under — so no span is invented from it.
    /// </remarks>
    [Fact]
    public async Task A_span_is_carried_where_the_product_states_one_and_absent_where_it_does_not()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json(
            $$"""
            [{"id":"{{ImmichAlbum}}","albumName":"An invented album","assetCount":3,
              "startDate":"2024-05-06T07:08:09Z","endDate":"2024-05-09T10:11:12Z"}]
            """));

        var withSpan = (await Immich(immich).AlbumsAsync(default)).Albums.Single();
        withSpan.From.ShouldBe(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero));
        withSpan.To.ShouldBe(new DateTimeOffset(2024, 5, 9, 10, 11, 12, TimeSpan.Zero));

        var prism = new LibraryStub();
        prism.Answers(_ => Json(
            $$"""
            [{"UID":"{{PrismAlbum}}","Title":"An invented album","PhotoCount":3,
              "Year":2024,"Month":5,"Day":6,"CreatedAt":"2024-05-06T07:08:09Z"}]
            """));

        var withoutSpan = (await Prism(prism).AlbumsAsync(default)).Albums.Single();
        withoutSpan.From.ShouldBeNull();
        withoutSpan.To.ShouldBeNull();
    }

    /// <summary>
    /// A title the library did not write stays absent rather than being filled in.
    /// </summary>
    /// <remarks>
    /// Absent is not "untitled": one is the library declining to say and the other is a name. What
    /// a screen writes in the gap is the screen's decision, made once, where it can be read.
    /// </remarks>
    [Fact]
    public async Task An_album_the_library_named_nothing_is_carried_without_a_name()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json($$"""[{"id":"{{ImmichAlbum}}","assetCount":1}]"""));

        (await Immich(immich).AlbumsAsync(default)).Albums.Single().Title.ShouldBeNull();
    }

    /// <summary>
    /// A row naming no album this application could ask about later is left out rather than offered
    /// as an entry that narrows nothing.
    /// </summary>
    [Fact]
    public async Task A_row_naming_no_usable_album_is_left_out()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json(
            $$"""
            [{"albumName":"No identifier at all","assetCount":2},
             {"id":"has a space in it","albumName":"Not a shape this application will send"},
             {"id":"{{ImmichAlbum}}","albumName":"An invented album","assetCount":2}]
            """));

        var albums = (await Immich(immich).AlbumsAsync(default)).Albums;
        albums.Count.ShouldBe(1);
        albums.Single().AlbumId.ShouldBe(ImmichAlbum);
    }

    /// <summary>
    /// A library keeping more albums than this installation offers is cut, and says it was cut.
    /// </summary>
    /// <remarks>
    /// The saying is the point. A chooser that quietly ends leaves a reader whose album is missing
    /// looking for what the library lost. What is offered is also the front of what was sent rather
    /// than a re-ordering of it: a set sorted after cutting names a different five hundred from the
    /// one that was read.
    /// </remarks>
    [Fact]
    public async Task More_albums_than_this_installation_offers_are_cut_and_said_to_be_cut()
    {
        var rows = string.Join(
            ',',
            Enumerable.Range(0, ImmichClient.AlbumCeiling + 5)
                .Select(index => ImmichAlbumRow(InventedGuid(index), $"Album {index}", index)));

        var immich = new LibraryStub();
        immich.Answers(_ => Json($"[{rows}]"));

        var answer = await Immich(immich).AlbumsAsync(default);

        answer.Albums.Count.ShouldBe(ImmichClient.AlbumCeiling);
        answer.Truncated.ShouldBeTrue();
        answer.Albums[0].AlbumId.ShouldBe(InventedGuid(0));
        answer.Albums[^1].AlbumId.ShouldBe(InventedGuid(ImmichClient.AlbumCeiling - 1));
    }

    /// <summary>A list that fits is not reported as cut.</summary>
    [Fact]
    public async Task A_list_that_fits_is_not_reported_as_cut()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json($"[{ImmichAlbumRow(ImmichAlbum, "An invented album", 1)}]"));

        (await Immich(immich).AlbumsAsync(default)).Truncated.ShouldBeFalse();
    }

    /// <summary>
    /// An answer that is not a list of albums is a refusal rather than a library with no albums.
    /// </summary>
    /// <remarks>
    /// An address in front of the wrong container answers markup with HTTP 200, and a chooser
    /// rendering that as "this library keeps no albums" is the failure this feature is most likely
    /// to make while looking correct — it sends a reader to make an album they already have.
    /// </remarks>
    [Fact]
    public async Task An_answer_that_is_not_a_list_of_albums_is_refused_rather_than_read_as_none()
    {
        var markup = new LibraryStub();
        markup.Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not this product</html>", Encoding.UTF8, "text/html"),
        });

        (await Refused(() => Prism(markup).AlbumsAsync(default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        var wrongShape = new LibraryStub();
        wrongShape.Answers(_ => Json("""{"albums":[]}"""));

        (await Refused(() => Immich(wrongShape).AlbumsAsync(default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);
    }

    /// <summary>
    /// A library this installation is not using is asked nothing about its albums, and no socket is
    /// opened.
    /// </summary>
    /// <remarks>
    /// Asserted on the client rather than only on the route above it, because that is what makes
    /// "no socket is opened" a property rather than a promise: a route added later that forgets to
    /// ask is refused here.
    /// </remarks>
    [Fact]
    public async Task A_library_this_installation_is_not_using_is_asked_nothing_about_albums()
    {
        var prism = new LibraryStub();

        (await Refused(() => Prism(prism, options => options.Enabled = false).AlbumsAsync(default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        var immich = new LibraryStub();

        (await Refused(() => Immich(immich, options => options.Enabled = false).AlbumsAsync(default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        prism.Calls.ShouldBeEmpty();
        immich.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// A library this installation has stopped using lists no albums and opens no socket.
    /// </summary>
    /// <remarks>
    /// Asked of the client rather than only of the route above it, because that is what makes "no
    /// socket is opened" a property rather than a promise: the refusal is in the one method every
    /// outgoing call already goes through, so a route added later that forgets to ask the gate is
    /// refused here instead of quietly succeeding against a library somebody stopped. The refusal
    /// is the same one a library nobody configured gives, because that is what a stopped library is
    /// to every surface above: absent rather than broken.
    /// </remarks>
    [Fact]
    public async Task A_stopped_library_lists_no_albums_and_opens_no_socket()
    {
        var prism = new LibraryStub();
        var prismLibrary = new PhotoPrismClient(
            new OneClient(prism),
            Options.Create(new PhotoPrismOptions
            {
                Enabled = true,
                BaseUrl = PrismAddress,
                AccessToken = FakeToken,
                MaxRetries = 0,
            }),
            new Stopped(),
            NullLogger<PhotoPrismClient>.Instance);

        (await Refused(() => prismLibrary.AlbumsAsync(default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        var immich = new LibraryStub();
        var immichLibrary = new ImmichClient(
            new OneClient(immich),
            Options.Create(new ImmichOptions
            {
                Enabled = true,
                BaseUrl = ImmichAddress,
                ApiKey = FakeKey,
                MaxRetries = 0,
            }),
            new Stopped(),
            new NeverStopping(),
            NullLogger<ImmichClient>.Instance);

        (await Refused(() => immichLibrary.AlbumsAsync(default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        prism.Calls.ShouldBeEmpty();
        immich.Calls.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------- narrowing to an album

    /// <summary>
    /// The narrowing is done by the far side, in each product's own way, and nothing is dropped
    /// from the answer here.
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole narrowing rests on and the defect it prevents is invisible
    /// on a screen: an album that never reached the library comes back as a full page of everything
    /// under the album's name, and a reader cannot tell that from an album holding everything.
    /// </remarks>
    [Fact]
    public async Task A_listing_narrowed_to_an_album_asks_the_library_for_the_album()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, Album: PrismAlbum), default);

        // Inside this product's own search grammar, and escaped, because that is the parameter it
        // parses its filters out of.
        prism.Only.Url.ShouldContain("q=album%3A" + PrismAlbum);

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, Album: ImmichAlbum), default);

        immich.Only.Body.ShouldContain($"\"albumIds\":[\"{ImmichAlbum}\"]");
    }

    /// <summary>
    /// The album goes to the second product under the name that product's listing route actually
    /// reads, and in the shape it reads it: a set of album identifiers, named in the plural, with
    /// the one album asked for sent as a set of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a spelling test, and it is the most valuable case in the file.</b> That route
    /// reads its request through a schema that discards a field name it does not recognise instead
    /// of refusing the request, so a singular name — the obvious one, and the one the product's own
    /// timeline routes really do take — leaves this machine, is dropped on arrival, and comes back
    /// as the whole library under one album's heading. Nothing on this side can tell that answer
    /// from an album that happens to hold everything: the page is full, the count agrees with the
    /// page, no status code is out of the ordinary and no log line is written.
    /// </para>
    /// <para>
    /// Which is why it is asserted here as an exact string rather than left to the other cases. The
    /// case above would pass just as happily against the wrong name; only naming the field
    /// character for character puts the product's own contract in the suite, where a rename over
    /// there fails a test instead of quietly widening every narrowed listing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_album_is_named_to_the_second_product_as_a_set_of_identifiers()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, Album: ImmichAlbum), default);

        // Plural, and an array. Both halves, because either alone is silently ignored over there.
        immich.Only.Body.ShouldContain($"\"albumIds\":[\"{ImmichAlbum}\"]");

        // And not the singular the other routes take, which is the shape this defect wears.
        immich.Only.Body.ShouldNotContain("\"albumId\":");
    }

    /// <summary>
    /// A listing that named no album asks for no album, on either product.
    /// </summary>
    /// <remarks>
    /// The other half of the same defect, and the more dangerous half if it ever broke: a term left
    /// over from a previous question would narrow a listing of the whole library to one album while
    /// the screen went on calling it the library.
    /// </remarks>
    [Fact]
    public async Task A_listing_that_named_no_album_asks_for_none()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60), default);

        prism.Only.Url.ShouldNotContain("album");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60), default);

        immich.Only.Body.ShouldNotContain("album");
    }

    /// <summary>
    /// A window and an album asked for together are both put to the library, in that order, with
    /// nothing else between them.
    /// </summary>
    /// <remarks>
    /// The order is asserted on the product whose grammar is positional: its trailing free text is
    /// what it matches words against, so a term after the words would be read as part of them.
    /// </remarks>
    [Fact]
    public async Task A_window_and_an_album_are_both_put_to_the_library()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));

        var window = new LibraryPhotoWindow(
            new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 5, 9, 0, 0, 0, TimeSpan.Zero));

        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, window, PrismAlbum), default);

        prism.Only.Url.ShouldContain(
            "q=after%3A2024-05-06%20before%3A2024-05-09%20album%3A" + PrismAlbum);

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, window, ImmichAlbum), default);

        immich.Only.Body.ShouldContain("\"takenAfter\":\"2024-05-06T00:00:00.000Z\"");
        immich.Only.Body.ShouldContain($"\"albumIds\":[\"{ImmichAlbum}\"]");
    }

    /// <summary>
    /// A search carries no album, on either product.
    /// </summary>
    /// <remarks>
    /// A search is put to the library whole — neither product will take a set of albums to search
    /// within — so an album attached to one here would narrow somebody's typed question by
    /// something that was never on the screen, and the count beside the answer would be counting
    /// that instead.
    /// </remarks>
    [Fact]
    public async Task A_search_carries_no_album()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).SearchAsync(new LibraryPhotoSearchQuery("muddy crawl", 1, 60), default);

        prism.Only.Url.ShouldNotContain("album");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).SearchAsync(new LibraryPhotoSearchQuery("muddy crawl", 1, 60), default);

        immich.Only.Body.ShouldNotContain("album");
    }

    /// <summary>
    /// The listing narrowed to an album is still the listing route, not the map's.
    /// </summary>
    /// <remarks>
    /// The map's route on either product reports only photographs that carry a position, which in a
    /// caving club is the smaller half of an album: everything taken underground, every scan and
    /// every camera with no receiver would silently be missing, and the album would look small
    /// rather than wrong.
    /// </remarks>
    [Fact]
    public async Task An_album_listing_still_asks_the_route_that_knows_about_positionless_pictures()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, Album: PrismAlbum), default);

        prism.Only.Url.ShouldContain("/api/v1/photos?");
        prism.Only.Url.ShouldNotContain("/api/v1/geo");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, Album: ImmichAlbum), default);

        immich.Only.Url.ShouldContain("/api/search/metadata");
        immich.Only.Url.ShouldNotContain("/api/map/markers");
    }

    // ------------------------------------------------------------------------------------- support

    /// <summary>One invented album, in the shape one product writes one.</summary>
    private static string PrismAlbumRow(string uid, string title, int count) =>
        $$"""{"UID":"{{uid}}","Title":"{{title}}","Type":"album","PhotoCount":{{count.ToString(CultureInfo.InvariantCulture)}}}""";

    /// <summary>One invented album, in the shape the other product writes one.</summary>
    private static string ImmichAlbumRow(string id, string name, int count) =>
        $$"""{"id":"{{id}}","albumName":"{{name}}","assetCount":{{count.ToString(CultureInfo.InvariantCulture)}}}""";

    /// <summary>An invented identifier of the shape one product mints, distinct per index.</summary>
    private static string InventedGuid(int index) =>
        $"00000000-0000-4000-8000-{index.ToString("D12", CultureInfo.InvariantCulture)}";

    /// <summary>One invented page of photographs, in the shape one product wraps one.</summary>
    private static string ImmichPage(string items, int total, string? nextPage) =>
        """{"assets":{"count":0,"items":"""
        + items
        + ""","total":"""
        + total.ToString(CultureInfo.InvariantCulture)
        + ""","nextPage":"""
        + (nextPage is null ? "null" : $"\"{nextPage}\"")
        + "}}";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static PhotoPrismClient Prism(LibraryStub stub, Action<PhotoPrismOptions>? configure = null)
    {
        var options = new PhotoPrismOptions
        {
            Enabled = true,
            BaseUrl = PrismAddress,
            AccessToken = FakeToken,

            // Nothing here is about what a call worth retrying costs, and a retry would make every
            // count of calls below depend on it.
            MaxRetries = 0,
        };
        configure?.Invoke(options);

        return new PhotoPrismClient(
            new OneClient(stub),
            Options.Create(options),
            new NothingStopped(),
            NullLogger<PhotoPrismClient>.Instance);
    }

    private static ImmichClient Immich(LibraryStub stub, Action<ImmichOptions>? configure = null)
    {
        var options = new ImmichOptions
        {
            Enabled = true,
            BaseUrl = ImmichAddress,
            ApiKey = FakeKey,
            MaxRetries = 0,
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

    private sealed record LibraryCall(string Method, string Url, string Body);

    /// <summary>
    /// A brake nobody has pulled, so these cases exercise a library this installation is using.
    /// </summary>
    private sealed class NothingStopped : IPhotoLibraryBrake
    {
        public ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct) =>
            ValueTask.FromResult(false);
    }

    /// <summary>A brake somebody has pulled, so a stopped library can be put under test.</summary>
    private sealed class Stopped : IPhotoLibraryBrake
    {
        public ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct) =>
            ValueTask.FromResult(true);
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>An application that never stops, so nothing detached is cancelled out from under a test.</summary>
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
    /// Stands in for the library, recording everything that would have left this machine — the body
    /// included, because one of these products is asked its questions in one.
    /// </summary>
    private sealed class LibraryStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<LibraryCall> calls = [];

        private Func<LibraryCall, HttpResponseMessage> answer =
            _ => throw new HttpRequestException("the stub was not told how to answer");

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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // The escaped form, which is what goes on the wire. The display form a URI writes by
            // default puts back the punctuation that was escaped, so a test reading it would be
            // blind to exactly the defect the escaping exists to prevent.
            var call = new LibraryCall(request.Method.Method, request.RequestUri!.AbsoluteUri, body);

            lock (gate) { calls.Add(call); }

            return answer(call);
        }
    }
}
