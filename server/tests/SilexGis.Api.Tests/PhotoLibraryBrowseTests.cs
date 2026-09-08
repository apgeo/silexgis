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
/// Looking through a neighbouring photo library as a list rather than as a map.
///
/// <para>
/// No database and no host: the clients are built directly over a recording stub, because what is
/// under test is which question leaves this machine and what is made of the answer. Both halves
/// have a way of being wrong that looks exactly like the feature working — a listing asked of the
/// map's own route answers only the photographs that carry a position, which in a caving club is
/// the smaller half of a library and produces a grid that is short rather than empty; and a total
/// read out of a field that counts something narrower produces a number nobody can tell from a
/// fact. So the addresses are asserted as often as the answers.
/// </para>
/// <para>
/// Every identifier, hash, title and camera below is invented. Nothing here comes from any real
/// library, and no photograph named here exists.
/// </para>
/// </summary>
public sealed class PhotoLibraryBrowseTests
{
    private const string PrismAddress = "http://photo-library.invalid:2342";
    private const string ImmichAddress = "http://other-photo-library.invalid:2283";
    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeKey = "not-a-real-key-0000";

    /// <summary>Invented identifiers of the shape each product mints.</summary>
    private const string First = "11111111-1111-4111-8111-111111111111";
    private const string Second = "22222222-2222-4222-8222-222222222222";
    private const string PrismUid = "psinvented0000000one";
    private const string PrismHash = "aa11bb22cc33dd44ee55";

    // ------------------------------------------------------- the question that leaves this machine

    /// <summary>
    /// The listing is asked of the route that knows about the whole library, and never of the one
    /// the map uses.
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole feature rests on, and the defect it prevents is invisible on
    /// a screen. The map's route on either product reports only photographs that carry a position;
    /// a listing built on it would silently omit every picture taken underground, every scan and
    /// every camera with no receiver, and would look like a working grid of a smaller library.
    /// </remarks>
    [Fact]
    public async Task A_listing_asks_the_route_that_knows_about_photographs_with_no_position()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        prism.Only.Url.ShouldContain("/api/v1/photos?");
        prism.Only.Url.ShouldNotContain("/api/v1/geo");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        immich.Only.Url.ShouldContain("/api/search/metadata");
        immich.Only.Url.ShouldNotContain("/api/map/markers");
        immich.Only.Method.ShouldBe("POST");
    }

    /// <summary>
    /// Nothing geographic is asked and nothing geographic comes back.
    /// </summary>
    /// <remarks>
    /// Asserted against what leaves the machine rather than against the record's fields, because
    /// the record having no coordinate is what a reader can see and this is what they cannot: a
    /// rectangle quietly added to either request would narrow the listing to a place while every
    /// screen above went on calling it a list of the library.
    /// </remarks>
    [Fact]
    public async Task A_listing_sends_no_rectangle_and_no_coordinate()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        foreach (var word in (string[])["latlng", "lat=", "lng=", "bbox", "dist=", "s2=", "olc="])
        {
            prism.Only.Url.ShouldNotContain(word);
        }

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)}]", total: 1, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        immich.Only.Url.ShouldNotContain("bbox");

        foreach (var word in (string[])["bbox", "lat", "lon", "city", "state", "country"])
        {
            immich.Only.Body.ShouldNotContain(word);
        }
    }

    /// <summary>
    /// Newest first, asked for in each product's own words.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed, and asserted, because a listing that came back oldest-first is a
    /// working feature showing the wrong decade: the grid fills, the paging works, and the only
    /// thing wrong is the answer.
    /// </remarks>
    [Fact]
    public async Task A_listing_asks_for_the_newest_first()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, null), default);
        prism.Only.Url.ShouldContain("order=newest");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, null), default);
        immich.Only.Body.ShouldContain("\"order\":\"desc\"");
    }

    /// <summary>
    /// A page is asked for as a page of the library's own paging, not cut out of a larger read.
    /// </summary>
    [Fact]
    public async Task A_later_page_is_asked_of_the_library_rather_than_taken_from_a_larger_read()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).ListAsync(new LibraryPhotoQuery(3, 20, null), default);

        // The third page of twenty begins after forty, and the count asked for is the page rather
        // than everything up to it.
        prism.Only.Url.ShouldContain("count=20");
        prism.Only.Url.ShouldContain("offset=40");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).ListAsync(new LibraryPhotoQuery(3, 20, null), default);

        immich.Only.Body.ShouldContain("\"page\":3");
        immich.Only.Body.ShouldContain("\"size\":20");
    }

    // ---------------------------------------------------------------------------- matching words

    /// <summary>
    /// One product matches words and is given them; the other does not and is asked nothing at all.
    /// </summary>
    /// <remarks>
    /// The last line is the one that matters. A library that cannot match text and is sent some
    /// answers with a full unfiltered page and no sign that the words were dropped, which is a
    /// search box that looks like it worked — so the refusal happens before a socket is opened.
    /// </remarks>
    [Fact]
    public async Task Words_go_only_to_the_library_that_matches_them()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        var prismLibrary = Prism(prism);

        prismLibrary.SupportsTextSearch.ShouldBeTrue();
        await prismLibrary.ListAsync(new LibraryPhotoQuery(1, 60, "rope traverse"), default);

        // Escaped on the way out, so what somebody typed stays one value of one parameter whatever
        // punctuation they used — an ampersand that arrived unescaped would be a second parameter
        // this application never meant to send.
        prism.Only.Url.ShouldContain("q=rope%20traverse");

        var immich = new LibraryStub();
        var immichLibrary = Immich(immich);

        immichLibrary.SupportsTextSearch.ShouldBeFalse();
        (await Refused(() => immichLibrary.ListAsync(new LibraryPhotoQuery(1, 60, "rope"), default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        immich.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// Words typed into a search box stay words, and cannot become a filter of any kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameter the words go into is not a text field on this product: it is parsed into the
    /// same form the request's own parameters bind to, so a <c>name:value</c> pair typed into a
    /// search box would set a field rather than match a word. This asserts that no such pair
    /// survives, and it asserts it about the fields it would matter most for.
    /// </para>
    /// <para>
    /// A place first, because that one is not a bug in a search box — it is the whole premise of
    /// this surface. A listing narrowed to a circle around a point is a way of reading a
    /// photograph's coordinate off which page it appears on, one halving at a time, on a surface
    /// built to carry no position at all. Then the order and the quality floor, which this call
    /// sets deliberately and which a pair would override, and the state fields, which reach
    /// photographs the library's own owner marked as not for general viewing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_search_cannot_become_a_filter_of_any_kind()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));

        await Prism(prism).ListAsync(
            new LibraryPhotoQuery(
                1, 60, "lat:45.18 lng:23.21 dist:1 quality:0 order:oldest private:true archived:true"),
            default);

        var url = prism.Only.Url;

        // The escaped form is what goes on the wire, so the separator is looked for in both
        // spellings: a test reading only the plain one would pass while every filter went through
        // percent-encoded, which is exactly how this would ship unnoticed.
        var sent = url[(url.IndexOf("&q=", StringComparison.Ordinal) + 3)..];
        sent.ShouldNotContain(":");
        sent.ShouldNotContain("%3A");
        sent.ShouldNotContain("%3a");

        // The words themselves survive — this reduces a search, it does not refuse one.
        sent.ShouldContain("lat");
        sent.ShouldContain("45.18");

        // And what this application decided remains what it decided.
        url.ShouldContain("order=newest");
        url.ShouldContain("quality=");
    }

    /// <summary>
    /// A search that was nothing but filter syntax asks for the whole listing rather than for a
    /// page of nothing.
    /// </summary>
    [Fact]
    public async Task A_search_left_with_no_words_is_no_search()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));

        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, " : "), default);

        prism.Only.Url.ShouldNotContain("&q=");
    }

    // -------------------------------------------------------------------- counts, and their honesty

    /// <summary>
    /// A total the library states and its own paging agree about is published as the library's.
    /// </summary>
    [Fact]
    public async Task A_total_that_agrees_with_the_paging_is_published_as_the_librarys_own()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)},{ImmichRow(Second)}]", total: 412, nextPage: "2")));

        var page = await Immich(stub).ListAsync(new LibraryPhotoQuery(1, 2, null), default);

        page.Total.ShouldBe(412);
        page.HasMore.ShouldBeTrue();
        page.Photos.Count.ShouldBe(2);
    }

    /// <summary>
    /// A number that contradicts the paging is reported as no number at all.
    /// </summary>
    /// <remarks>
    /// Two photographs in hand, a further page promised, and a total of two: whatever that field
    /// counts, it is not how many the library holds. Reported as unknown rather than corrected,
    /// because a wrong number a reader cannot tell from a right one is worse than none — and the
    /// surface has a sentence for "the library does not say" and none for "this number is a
    /// little off".
    /// </remarks>
    [Fact]
    public async Task A_total_that_contradicts_the_paging_is_reported_as_unknown()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)},{ImmichRow(Second)}]", total: 2, nextPage: "2")));

        var page = await Immich(stub).ListAsync(new LibraryPhotoQuery(1, 2, null), default);

        page.Total.ShouldBeNull();
        page.HasMore.ShouldBeTrue();

        // The control: the same number with no further page promised is consistent, and is kept.
        var last = new LibraryStub();
        last.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)},{ImmichRow(Second)}]", total: 2, nextPage: null)));

        (await Immich(last).ListAsync(new LibraryPhotoQuery(1, 2, null), default)).Total.ShouldBe(2);
    }

    /// <summary>
    /// The product that cannot say how many it holds says nothing, and still says whether there is
    /// more.
    /// </summary>
    /// <remarks>
    /// This product sends a count beside a page, and it counts what that page holds — so reading it
    /// as a total would put the page size on the screen as though it were the size of the library.
    /// The two facts are separate here for that reason: an unknown total beside a knowable "there
    /// is more" is exactly what this product can honestly answer.
    /// </remarks>
    [Fact]
    public async Task A_library_that_cannot_state_a_total_states_none_and_still_offers_the_next_page()
    {
        var full = new LibraryStub();
        full.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)},{PrismRow("psinvented0000000two", "bb22cc33dd44ee55ff66")}]"));

        var page = await Prism(full).ListAsync(new LibraryPhotoQuery(1, 2, null), default);

        page.Total.ShouldBeNull();
        page.HasMore.ShouldBeTrue();

        var short_ = new LibraryStub();
        short_.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));

        var end = await Prism(short_).ListAsync(new LibraryPhotoQuery(1, 2, null), default);

        end.Total.ShouldBeNull();
        end.HasMore.ShouldBeFalse();
    }

    // ------------------------------------------------------------------- what is made of an answer

    /// <summary>
    /// A row this application cannot name both a photograph and a picture from is left out rather
    /// than shown as a gap.
    /// </summary>
    /// <remarks>
    /// Every address built from a row later assumes both values are the shape this build expects,
    /// and a row that is drawn with neither is a tile that can never load and can never be opened.
    /// </remarks>
    [Fact]
    public async Task A_row_naming_no_photograph_or_no_picture_is_left_out()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(
            $$"""
            [{{PrismRow(PrismUid, PrismHash)}},
             {"UID":"not a uid","Hash":"{{PrismHash}}","Title":"invented"},
             {"UID":"psinvented0000000two"},
             {{PrismRow("psinvented000000three", "cc33dd44ee55ff66aa11")}}]
            """));

        var page = await Prism(stub).ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        page.Photos.Count.ShouldBe(2);
        page.Photos.Select(p => p.PhotographId)
            .ShouldBe(["psinvented0000000one", "psinvented000000three"]);
    }

    /// <summary>
    /// A row left out does not take the rest of the library with it.
    /// </summary>
    /// <remarks>
    /// Whether there is a page behind this one is the library's answer to "was this page full", and
    /// the library filled it. Counted after the unreadable rows were dropped it would be one short
    /// of full, the next control would be disabled, and everything past this offset would be out of
    /// reach through this surface — with nothing on the screen saying the listing stopped early.
    /// </remarks>
    [Fact]
    public async Task A_full_page_with_a_row_left_out_still_offers_the_next_page()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(
            $$"""
            [{{PrismRow(PrismUid, PrismHash)}},
             {"UID":"not a uid","Hash":"{{PrismHash}}"},
             {{PrismRow("psinvented000000three", "cc33dd44ee55ff66aa11")}}]
            """));

        var page = await Prism(stub).ListAsync(new LibraryPhotoQuery(1, 3, null), default);

        page.Photos.Count.ShouldBe(2);
        page.HasMore.ShouldBeTrue();
    }

    /// <summary>
    /// A total is weighed against what the library handed over, not against what could be read.
    /// </summary>
    /// <remarks>
    /// Two rows sent, one of them unreadable, a further page promised, and a total of two: whatever
    /// that field counts it is not how many the library holds, and it is suppressed. Weighed
    /// against the one row that survived instead, two would look larger than the photographs paged
    /// past and would be published — putting the size of a page on the screen under the words for
    /// the size of the library, for a library of any size at all.
    /// </remarks>
    [Fact]
    public async Task A_total_is_weighed_against_what_the_library_handed_over()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(ImmichPage(
            $$"""[{{ImmichRow(First)}},{"id":"not-an-identifier","type":"IMAGE"}]""",
            total: 2,
            nextPage: "2")));

        var page = await Immich(stub).ListAsync(new LibraryPhotoQuery(1, 2, null), default);

        page.Photos.Count.ShouldBe(1);
        page.Total.ShouldBeNull();
    }

    /// <summary>
    /// A kind the library did not state stays unstated, and is never read as a photograph.
    /// </summary>
    [Fact]
    public async Task A_kind_the_library_did_not_state_stays_unstated()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(
            $$"""[{{PrismRow(PrismUid, PrismHash)}},{"UID":"psinvented0000000two","Hash":"bb22cc33dd44ee55ff66","Type":"video"}]"""));

        var page = await Prism(stub).ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        page.Photos[0].Kind.ShouldBeNull();
        page.Photos[1].Kind.ShouldBe(LibraryPhotoKind.Video);
    }

    /// <summary>
    /// The library that names a photograph and its picture with one string is read as doing so, and
    /// the one that names them separately is read as doing that.
    /// </summary>
    /// <remarks>
    /// Confusing the two is not a crash: it is a grid whose every tile asks a neighbouring library
    /// for a picture under a name that names no picture, and against one of these products a
    /// picture request that cannot be resolved is what marks a file missing.
    /// </remarks>
    [Fact]
    public async Task Each_library_is_read_as_naming_its_photographs_and_its_pictures()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));

        var listed = (await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60, null), default)).Photos[0];

        listed.PhotographId.ShouldBe(PrismUid);
        listed.Reference.ShouldBe(PrismHash);
        listed.PhotographId.ShouldNotBe(listed.Reference);

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)}]", total: 1, nextPage: null)));

        var other = (await Immich(immich).ListAsync(new LibraryPhotoQuery(1, 60, null), default)).Photos[0];

        other.PhotographId.ShouldBe(First);
        other.Reference.ShouldBe(First);
    }

    /// <summary>
    /// Listing the library refreshes the credential its pictures are served under, so a grid of
    /// thumbnails loads for somebody who never opened the map.
    /// </summary>
    /// <remarks>
    /// The proof is the absence of a second call: with no credential in hand the picture path
    /// fetches one, so a picture asked for right after a listing that opened only one socket is a
    /// listing that harvested it.
    /// </remarks>
    [Fact]
    public async Task Listing_the_library_takes_the_credential_its_pictures_need()
    {
        var stub = new LibraryStub();
        stub.Answers(call => call.Url.Contains("/api/v1/t/", StringComparison.Ordinal)
            ? Picture(InventedJpeg)
            : WithPreviewToken(Json($"[{PrismRow(PrismUid, PrismHash)}]"), "invented0token"));

        var library = Prism(stub);
        await library.ListAsync(new LibraryPhotoQuery(1, 60, null), default);

        await using var picture = await library.ThumbnailAsync(
            PrismHash, LibraryThumbnailSize.Small, ifNoneMatch: null, default);

        stub.Calls.Count.ShouldBe(2, "a listing already carried the credential a picture needs");
        stub.Calls[1].Url.ShouldContain("invented0token");
    }

    // -------------------------------------------------------------------------------- one photograph

    /// <summary>
    /// A photograph the library no longer reports is an answer, not a failure.
    /// </summary>
    /// <remarks>
    /// It may have been deleted or re-identified over there since the page naming it was drawn.
    /// Telling a reader the library is broken would send them looking for a fault nobody has, and
    /// the two send whoever has to act on them to completely different places.
    /// </remarks>
    [Fact]
    public async Task A_photograph_the_library_no_longer_reports_is_an_answer_rather_than_a_failure()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("no", Encoding.UTF8, "text/plain"),
        });

        (await Prism(prism).DetailAsync(PrismUid, default)).ShouldBeNull();

        var immich = new LibraryStub();
        immich.Answers(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("no", Encoding.UTF8, "text/plain"),
        });

        (await Immich(immich).DetailAsync(First, default)).ShouldBeNull();

        // And a library that is having trouble is still a failure, on the same call.
        var broken = new LibraryStub();
        broken.Answers(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("no", Encoding.UTF8, "text/plain"),
        });

        (await Refused(() => Prism(broken).DetailAsync(PrismUid, default)))
            .ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    /// <summary>
    /// What the library did not say is absent, and is never filled in from anything else.
    /// </summary>
    /// <remarks>
    /// A zero is treated as unsaid rather than as a measurement: both products write one into these
    /// fields for a picture whose own metadata carried nothing, and a panel reporting an aperture
    /// of zero would state something nobody measured.
    /// </remarks>
    [Fact]
    public async Task What_the_library_did_not_say_is_absent_rather_than_invented()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(
            $$"""
            {"UID":"{{PrismUid}}","Hash":"{{PrismHash}}","Title":"An invented picture",
             "CameraMake":"","Iso":0,"FNumber":0,"CameraModel":"Invented 1"}
            """));

        var detail = await Prism(stub).DetailAsync(PrismUid, default);

        detail.ShouldNotBeNull();
        detail.Title.ShouldBe("An invented picture");
        detail.CameraModel.ShouldBe("Invented 1");
        detail.CameraMake.ShouldBeNull();
        detail.Iso.ShouldBeNull();
        detail.Aperture.ShouldBeNull();
        detail.Lens.ShouldBeNull();
        detail.TakenAt.ShouldBeNull();
    }

    /// <summary>
    /// One product's picture is named by the primary file under the photograph, where the
    /// photograph itself names none.
    /// </summary>
    /// <remarks>
    /// A photograph here can carry a raw original, a sidecar and a rendering, and only one of them
    /// is what its own interface shows — so taking the first would show a detail panel a picture of
    /// something that is not the picture.
    /// </remarks>
    [Fact]
    public async Task The_picture_of_a_photograph_is_the_primary_file_under_it()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(
            $$"""
            {"UID":"{{PrismUid}}","Files":[
              {"Hash":"ff66aa11bb22cc33dd44","Primary":false},
              {"Hash":"{{PrismHash}}","Primary":true}]}
            """));

        var detail = await Prism(stub).DetailAsync(PrismUid, default);

        detail.ShouldNotBeNull();
        detail.Reference.ShouldBe(PrismHash);
    }

    /// <summary>
    /// The other product's camera facts come out of what it read from the picture, and the moment
    /// written into the picture is preferred to the moment a file landed on a disk.
    /// </summary>
    [Fact]
    public async Task The_camera_facts_come_from_what_the_library_read_out_of_the_picture()
    {
        var stub = new LibraryStub();
        stub.Answers(_ => Json(
            $$$"""
            {"id":"{{{First}}}","type":"IMAGE","fileCreatedAt":"2024-03-04T10:00:00Z",
             "exifInfo":{"dateTimeOriginal":"2024-03-01T09:30:00Z","make":"Invented",
                         "model":"Camera 2","lensModel":"Invented 24mm","fNumber":2.8,
                         "exposureTime":"1/250","iso":400,"focalLength":24,
                         "description":"An invented line about an invented picture"}}
            """));

        var detail = await Immich(stub).DetailAsync(First, default);

        detail.ShouldNotBeNull();
        detail.TakenAt.ShouldBe(new DateTimeOffset(2024, 3, 1, 9, 30, 0, TimeSpan.Zero));
        detail.CameraMake.ShouldBe("Invented");
        detail.CameraModel.ShouldBe("Camera 2");
        detail.Lens.ShouldBe("Invented 24mm");
        detail.Aperture.ShouldBe(2.8);
        detail.ShutterSpeed.ShouldBe("1/250");
        detail.Iso.ShouldBe(400);
        detail.FocalLengthMm.ShouldBe(24);
        detail.Description.ShouldBe("An invented line about an invented picture");
        detail.Kind.ShouldBe(LibraryPhotoKind.Image);
    }

    /// <summary>
    /// An identifier this application would not put in a request path never leaves the machine.
    /// </summary>
    [Fact]
    public async Task An_identifier_that_is_not_one_is_never_sent()
    {
        var prism = new LibraryStub();
        (await Refused(() => Prism(prism).DetailAsync("../../etc/passwd", default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        var immich = new LibraryStub();
        (await Immich(immich).DetailAsync("not-a-photograph", default)).ShouldBeNull();

        prism.Calls.ShouldBeEmpty();
        immich.Calls.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------- absent rather than broken

    /// <summary>
    /// An installation that has been given no library is a supported installation: the two new
    /// paths refuse, and not one socket is opened on either.
    /// </summary>
    [Fact]
    public async Task With_no_library_configured_a_listing_asks_nothing_of_anybody()
    {
        var prism = new LibraryStub();
        var prismLibrary = Prism(prism, options => options.Enabled = false);

        (await Refused(() => prismLibrary.ListAsync(new LibraryPhotoQuery(1, 60, null), default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);
        (await Refused(() => prismLibrary.DetailAsync(PrismUid, default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        var immich = new LibraryStub();
        var immichLibrary = Immich(immich, options => options.Enabled = false);

        (await Refused(() => immichLibrary.ListAsync(new LibraryPhotoQuery(1, 60, null), default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);
        (await Refused(() => immichLibrary.DetailAsync(First, default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        prism.Calls.ShouldBeEmpty();
        immich.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// An answer that is not a list of photographs is a refusal rather than an empty page.
    /// </summary>
    /// <remarks>
    /// An address in front of the wrong container answers markup with HTTP 200, and a page that
    /// rendered that as "this library holds nothing" is the failure this whole feature is most
    /// likely to make while looking correct.
    /// </remarks>
    [Fact]
    public async Task An_answer_that_is_not_a_listing_is_refused_rather_than_read_as_empty()
    {
        var markup = new LibraryStub();
        markup.Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not this product</html>", Encoding.UTF8, "text/html"),
        });

        (await Refused(() => Prism(markup).ListAsync(new LibraryPhotoQuery(1, 60, null), default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        var wrongShape = new LibraryStub();
        wrongShape.Answers(_ => Json("""{"photos":[]}"""));

        (await Refused(() => Prism(wrongShape).ListAsync(new LibraryPhotoQuery(1, 60, null), default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        var noAssets = new LibraryStub();
        noAssets.Answers(_ => Json("""{"albums":{"items":[]}}"""));

        (await Refused(() => Immich(noAssets).ListAsync(new LibraryPhotoQuery(1, 60, null), default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);
    }

    // ------------------------------------------------------------------------------------- support

    private static readonly byte[] InventedJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    /// <summary>One invented row of a listing, in the shape one product writes one.</summary>
    private static string PrismRow(string uid, string hash) =>
        $$"""{"UID":"{{uid}}","Hash":"{{hash}}","Title":"An invented picture","TakenAt":"2024-05-06T07:08:09Z"}""";

    /// <summary>One invented row of a listing, in the shape the other product writes one.</summary>
    private static string ImmichRow(string id) =>
        $$"""{"id":"{{id}}","type":"IMAGE","fileCreatedAt":"2024-05-06T07:08:09Z"}""";

    /// <summary>One invented page, in the shape the other product wraps one.</summary>
    private static string ImmichPage(string items, int total, string? nextPage) =>
        """{"assets":{"count":1,"items":"""
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

    private static HttpResponseMessage WithPreviewToken(HttpResponseMessage response, string token)
    {
        response.Headers.TryAddWithoutValidation("X-Preview-Token", token);
        return response;
    }

    private static HttpResponseMessage Picture(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") },
            },
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
            new OneClient(stub), Options.Create(options), NullLogger<PhotoPrismClient>.Instance);
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
            new NeverStopping(),
            NullLogger<ImmichClient>.Instance);
    }

    /// <summary>The code a call was refused with, so a test names the sentence an operator is sent.</summary>
    private static async Task<string> Refused(Func<Task> call) =>
        (await Should.ThrowAsync<PhotoLibraryException>(call)).Code;

    private sealed record LibraryCall(string Method, string Url, string Body);

    /// <summary>A factory that hands out one client, over the stub a test supplied.</summary>
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
            // default puts back the spaces and the punctuation that were escaped, so a test reading
            // it would be blind to exactly the defect the escaping exists to prevent.
            var call = new LibraryCall(request.Method.Method, request.RequestUri!.AbsoluteUri, body);

            lock (gate) { calls.Add(call); }

            return answer(call);
        }
    }
}
