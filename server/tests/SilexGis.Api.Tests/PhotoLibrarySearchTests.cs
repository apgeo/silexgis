// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Features.PhotoLibraries;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// Putting words to a neighbouring photo library, and what may honestly be said about what comes
/// back.
///
/// <para>
/// No database and no host: the clients are built directly over a recording stub, because what is
/// under test is which question leaves this machine and what is made of the answer. Both products
/// are asked, and they are asked <em>differently on purpose</em> — one matches the words against
/// text somebody wrote down, the other orders its whole library by how close each picture is to
/// what the words describe — so most of what follows asserts the difference rather than smoothing
/// it away.
/// </para>
/// <para>
/// The failure this file exists to prevent is the quiet one: a search that comes back as a full
/// page of the library with the reader's words still in the box, or as an empty page from a library
/// that was never asked. Both look exactly like the feature working.
/// </para>
/// <para>
/// Every identifier, hash, title and phrase below is invented. Nothing here comes from any real
/// library, and no photograph named here exists.
/// </para>
/// </summary>
public sealed class PhotoLibrarySearchTests
{
    private const string PrismAddress = "http://photo-library.invalid:2342";
    private const string ImmichAddress = "http://other-photo-library.invalid:2283";
    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeKey = "not-a-real-key-0000";

    /// <summary>Invented identifiers of the shape each product mints.</summary>
    private const string First = "11111111-1111-4111-8111-111111111111";
    private const string PrismUid = "psinvented0000000one";
    private const string PrismHash = "aa11bb22cc33dd44ee55";

    // ------------------------------------------------- which question is put to which library

    /// <summary>
    /// Each library is asked the one question it can answer, and says which one it answered.
    /// </summary>
    /// <remarks>
    /// The two are not the same question with different quality. One looks words up in what
    /// somebody wrote down, so a word nobody wrote finds nothing however well it describes the
    /// picture; the other compares a description to the pictures themselves and excludes nothing at
    /// all. A surface that offered both under one sentence would invite somebody to describe a
    /// photograph to a library that can only look up words, and the empty answer would read as an
    /// empty library.
    /// </remarks>
    [Fact]
    public async Task Each_library_is_asked_the_question_it_can_answer()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));
        var prismLibrary = Prism(prism);

        prismLibrary.SearchMatching.ShouldBe(LibrarySearchMatching.Text);

        var matched = await prismLibrary.SearchAsync(
            new LibraryPhotoSearchQuery("rope traverse", 1, 60), default);

        matched.Matching.ShouldBe(LibrarySearchMatching.Text);
        matched.Photos.Count.ShouldBe(1);

        // The product's own route for what it holds, with the words on it — and escaped, so what
        // somebody typed stays one value of one parameter whatever punctuation they used.
        prism.Only.Method.ShouldBe("GET");
        prism.Only.Url.ShouldContain("api/v1/photos");
        prism.Only.Url.ShouldContain("q=rope%20traverse");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)}]", total: 1, nextPage: null)));
        var immichLibrary = Immich(immich);

        immichLibrary.SearchMatching.ShouldBe(LibrarySearchMatching.Meaning);

        var ranked = await immichLibrary.SearchAsync(
            new LibraryPhotoSearchQuery("rope traverse", 1, 60), default);

        ranked.Matching.ShouldBe(LibrarySearchMatching.Meaning);
        ranked.Photos.Count.ShouldBe(1);

        // Its own route for a sentence, which is not the route its listing uses: that one takes
        // filters over columns and would have answered the whole library while looking filtered.
        immich.Only.Method.ShouldBe("POST");
        immich.Only.Url.ShouldEndWith("api/search/smart");
        immich.Only.Body.ShouldContain("\"query\":\"rope traverse\"");
    }

    /// <summary>
    /// The library that reads words as a description is given them whole, and they stay one value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole, because there is nothing there to reduce them to: this product's search takes a
    /// sentence and has no pair, prefix or operator that binds to a field, so taking punctuation
    /// out would only change what somebody asked for. That is the opposite of what the other
    /// product needs, and the difference is deliberate rather than an inconsistency.
    /// </para>
    /// <para>
    /// One value, because the words travel inside a request this application writes out by hand.
    /// A quotation mark or a backslash in somebody's sentence would end the string early and turn
    /// the rest of what they typed into fields of the request — so the words are written by the
    /// serialiser, and this asserts that the result still parses and still says three things.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_words_reach_the_library_that_reads_them_as_a_description_whole()
    {
        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));

        const string typed = "a \"muddy\" crawl: back\\slash and 45.18";
        await Immich(immich).SearchAsync(new LibraryPhotoSearchQuery(typed, 1, 60), default);

        using var sent = JsonDocument.Parse(immich.Only.Body);

        sent.RootElement.GetProperty("query").GetString().ShouldBe(typed);
        sent.RootElement.GetProperty("page").GetInt32().ShouldBe(1);
        sent.RootElement.GetProperty("size").GetInt32().ShouldBe(60);

        // Three fields and no fourth. Everything on this request is a question about somebody
        // else's photographs, and one that grew a filter nobody meant to send would be a question
        // this application never decided to ask.
        sent.RootElement.EnumerateObject().Count().ShouldBe(3);
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
    /// this surface. A search narrowed to a circle around a point is a way of reading a
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

        await Prism(prism).SearchAsync(
            new LibraryPhotoSearchQuery(
                "lat:45.18 lng:23.21 dist:1 quality:0 order:oldest private:true archived:true",
                1,
                60),
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
    /// A search that was nothing but filter syntax asks the library for nothing at all.
    /// </summary>
    /// <remarks>
    /// The listing behaves the other way round and should: a filter left empty is a listing. A
    /// search is not. Taking the pairs out of "lat:45.18" leaves words, but taking them out of
    /// " : " leaves nothing, and the question that would go out is "give me the library" — whose
    /// answer would be drawn under a heading saying it matched what somebody typed. Nothing found
    /// is the honest answer, and it is reached without opening a socket.
    /// </remarks>
    [Fact]
    public async Task A_search_left_with_no_words_asks_the_library_for_nothing()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));

        var answer = await Prism(prism).SearchAsync(new LibraryPhotoSearchQuery(" : ", 1, 60), default);

        answer.Photos.ShouldBeEmpty();
        answer.HasMore.ShouldBeFalse();
        answer.Matching.ShouldBe(LibrarySearchMatching.Text);
        prism.Calls.ShouldBeEmpty();

        // Nothing was searched for, and no time is stamped for a reading that never happened. The
        // screen prints that time as "read from the library", which is the one line a reader would
        // use to decide whether the far side was reached at all.
        answer.Searched.ShouldBeEmpty();
        answer.ReadAt.ShouldBeNull();
    }

    /// <summary>
    /// The words the library was actually given come back with its answer, because they are not
    /// always the words that were typed.
    /// </summary>
    /// <remarks>
    /// The reduction that keeps a search box from becoming a way of asking where a photograph was
    /// taken also changes what somebody asked for, and a screen that shows the typed words above an
    /// answer to different ones is telling a reader this application disagrees with the product's
    /// own search box for no reason they can see. The other product is given the sentence whole and
    /// says so by publishing it unchanged.
    /// </remarks>
    [Fact]
    public async Task What_the_library_was_actually_given_comes_back_with_the_answer()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));

        var reduced = await Prism(prism).SearchAsync(
            new LibraryPhotoSearchQuery("label:cave muddy", 1, 60), default);

        reduced.Searched.ShouldBe("label cave muddy");
        reduced.ReadAt.ShouldNotBeNull();

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));

        // Nothing to reduce on the library that reads a sentence: it binds no pair to a field, so
        // taking anything out would only change what somebody asked for.
        (await Immich(immich).SearchAsync(new LibraryPhotoSearchQuery("label:cave muddy", 1, 60), default))
            .Searched.ShouldBe("label:cave muddy");
    }

    /// <summary>
    /// A page that lost rows says so where an operator can see it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row this build cannot name a photograph from is left out, which is right — every address
    /// built from it later assumes the shape this build expects. What is wrong is losing it in
    /// silence: the count above the grid then describes five photographs on a page the library
    /// filled with sixty, and if a version change over there renames one of these fields, whole
    /// pages arrive empty and read as a library that holds nothing.
    /// </para>
    /// <para>
    /// Said in the log rather than on the screen, as the map path already says it about positions
    /// it could not read: it is a fact about this build's assumptions rather than about the
    /// library, and a reader has nothing to do with it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_page_that_lost_rows_says_so_where_an_operator_can_see_it()
    {
        var prismLog = new Recorder();
        var prism = new LibraryStub();

        // One row naming both a photograph and a picture, and one whose picture nothing names.
        prism.Answers(_ => Json(
            $$"""[{{PrismRow(PrismUid, PrismHash)}},{"UID":"psinvented0000000two"}]"""));

        var matched = await Prism(prism, logger: prismLog)
            .SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default);

        matched.Photos.Count.ShouldBe(1);
        prismLog.Warnings.ShouldContain(line => line.Contains('2') && line.Contains("not shown"));

        var immichLog = new Recorder();
        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage(
            $$"""[{{ImmichRow(First)}},{"id":"not-an-identifier-of-that-shape"}]""",
            total: 2,
            nextPage: null)));

        var ranked = await Immich(immich, logger: immichLog)
            .SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default);

        ranked.Photos.Count.ShouldBe(1);
        immichLog.Warnings.ShouldContain(line => line.Contains('2') && line.Contains("not shown"));

        // The control: a page every row of which could be read says nothing, so the warning means
        // what it says rather than appearing under every page.
        var quiet = new Recorder();
        var whole = new LibraryStub();
        whole.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));

        await Prism(whole, logger: quiet).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default);
        quiet.Warnings.ShouldBeEmpty();
    }

    // ------------------------------------------------------------- what may be said about a page

    /// <summary>
    /// A page of a search is asked of the library's own paging, not cut out of a larger read.
    /// </summary>
    [Fact]
    public async Task A_page_of_a_search_is_asked_of_the_librarys_own_paging()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));
        await Prism(prism).SearchAsync(new LibraryPhotoSearchQuery("rope", 3, 20), default);

        // The third page of twenty begins after forty, and the count asked for is the page rather
        // than everything up to it.
        prism.Only.Url.ShouldContain("count=20");
        prism.Only.Url.ShouldContain("offset=40");

        var immich = new LibraryStub();
        immich.Answers(_ => Json(ImmichPage("[]", total: 0, nextPage: null)));
        await Immich(immich).SearchAsync(new LibraryPhotoSearchQuery("rope", 3, 20), default);

        immich.Only.Body.ShouldContain("\"page\":3");
        immich.Only.Body.ShouldContain("\"size\":20");
    }

    /// <summary>
    /// A page beginning further into a library than this installation will ask for is refused here,
    /// not sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both products document a limit on how far in a request may begin, and a request past it
    /// comes back refused. That refusal reaches a reader as "this library did not answer", which
    /// sends whoever reads it to look at a container that is running perfectly — for a request this
    /// application knew was out of range before it built it.
    /// </para>
    /// <para>
    /// Measured in photographs rather than in pages, because how far a page number reaches depends
    /// on how large the pages are: the same page number is two different distances into the same
    /// library at sixty and at two hundred a page.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_page_further_in_than_this_installation_will_ask_for_is_refused()
    {
        const int ceiling = PhotoLibraryBrowseEndpoints.MaxOffset;

        // The last page that still begins inside what this installation will ask for.
        PhotoLibraryBrowseEndpoints.TooDeep((ceiling / 200) + 1, 200).ShouldBeNull();
        PhotoLibraryBrowseEndpoints.TooDeep(1, 60).ShouldBeNull();

        // And the first one past it, refused with a code of its own rather than as a library that
        // did not answer.
        var refused = PhotoLibraryBrowseEndpoints.TooDeep((ceiling / 200) + 2, 200);
        refused.ShouldNotBeNull();
        refused.ProblemDetails.Extensions["code"]
            .ShouldBe(PhotoLibraryBrowseEndpoints.PageTooDeepCode);

        // A page number large enough to overflow a smaller arithmetic than the one this uses is
        // still just a refusal.
        PhotoLibraryBrowseEndpoints.TooDeep(int.MaxValue, 200).ShouldNotBeNull();
    }

    /// <summary>
    /// An ordering says only whether it continues, and a number stated beside it is not a count of
    /// anything this surface may publish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library ranks everything it holds by closeness to the words: nothing is matched and
    /// nothing is excluded, so there is no set of matches to count. A number arriving beside such a
    /// page counts the slice the library chose to send, and rendered as "of 4 321 matches" it would
    /// be a claim nobody made about a set that does not exist.
    /// </para>
    /// <para>
    /// The last assertion is structural, because the defect it guards cannot be seen in an answer:
    /// a number that is never published leaves nothing behind to assert on. What is pinned is that
    /// this record has nowhere to put one, so a later edit that decides to pass the total through
    /// has to change the shape and read the reasons written on it first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_ordering_says_only_whether_it_continues()
    {
        var more = new LibraryStub();
        more.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)}]", total: 4321, nextPage: "2")));

        var page = await Immich(more).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default);

        page.Photos.Count.ShouldBe(1);
        page.HasMore.ShouldBeTrue();

        var last = new LibraryStub();
        last.Answers(_ => Json(ImmichPage($"[{ImmichRow(First)}]", total: 4321, nextPage: null)));

        (await Immich(last).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default))
            .HasMore.ShouldBeFalse();

        typeof(LibraryPhotoSearchPage).GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain("Total");
    }

    /// <summary>
    /// A page that came back full may have had more behind it, and one that did not is the end.
    /// </summary>
    /// <remarks>
    /// The product that matches text says nothing about a further page, so a full page is the only
    /// evidence there is. Offering a next page that turns out empty costs one request; withholding
    /// one hides the rest of an answer behind a control that is not there.
    /// </remarks>
    [Fact]
    public async Task A_full_page_of_matches_offers_the_next_one()
    {
        var full = new LibraryStub();
        full.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)},{PrismRow("psinvented000000two", "bb22cc33dd44ee55ff66")}]"));

        (await Prism(full).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 2), default))
            .HasMore.ShouldBeTrue();

        var ended = new LibraryStub();
        ended.Answers(_ => Json($"[{PrismRow(PrismUid, PrismHash)}]"));

        (await Prism(ended).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 2), default))
            .HasMore.ShouldBeFalse();
    }

    /// <summary>
    /// A row this application cannot name a photograph from is left out of a search as it is left
    /// out of a listing, and does not shorten what the page says about itself.
    /// </summary>
    [Fact]
    public async Task A_row_naming_no_photograph_is_left_out_of_a_search_too()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json(
            $"[{PrismRow(PrismUid, PrismHash)},{{\"UID\":\"\",\"Hash\":\"\"}}]"));

        var page = await Prism(prism).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 2), default);

        page.Photos.Count.ShouldBe(1);

        // Counted against what the library handed over rather than against what survived the
        // reading: one unreadable row on an otherwise full page would otherwise put everything
        // behind it out of reach with nothing on the screen saying the answer stopped.
        page.HasMore.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ silence is not emptiness

    /// <summary>
    /// A library that answered a search with something that is not a page of photographs is refused
    /// rather than read as nothing found.
    /// </summary>
    /// <remarks>
    /// These are the two states hardest to tell apart on this surface and the two a reader most
    /// needs told apart. Nothing found sends somebody to try other words; a library that did not
    /// answer sends somebody to look at a container. Rendered as one, the second is spent looking
    /// for better words for a library that is stopped.
    /// </remarks>
    [Fact]
    public async Task An_answer_that_is_not_a_page_of_photographs_is_refused()
    {
        var markup = new LibraryStub();
        markup.Answers(_ => Json("<html>a sign-in page</html>"));

        (await Refused(() => Prism(markup).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);

        var wrongShape = new LibraryStub();
        wrongShape.Answers(_ => Json("""{"assets":{"items":"none"}}"""));

        (await Refused(() => Immich(wrongShape).SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default)))
            .ShouldBe(PhotoLibraryException.RejectedCode);
    }

    /// <summary>
    /// With no library configured, a search asks nothing of anybody.
    /// </summary>
    [Fact]
    public async Task With_no_library_configured_a_search_asks_nothing_of_anybody()
    {
        var prism = new LibraryStub();
        var prismLibrary = Prism(prism, options => options.BaseUrl = string.Empty);

        prismLibrary.IsConfigured.ShouldBeFalse();
        (await Refused(() => prismLibrary.SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        var immich = new LibraryStub();
        var immichLibrary = Immich(immich, options => options.ApiKey = string.Empty);

        immichLibrary.IsConfigured.ShouldBeFalse();
        (await Refused(() => immichLibrary.SearchAsync(new LibraryPhotoSearchQuery("rope", 1, 60), default)))
            .ShouldBe(PhotoLibraryException.NotConfiguredCode);

        prism.Calls.ShouldBeEmpty();
        immich.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// Listing a library carries no words, whatever anybody typed anywhere else.
    /// </summary>
    /// <remarks>
    /// The two questions are two calls precisely so that this is structural rather than remembered:
    /// there is nowhere on a listing for words to be put, so a listing cannot come back narrowed by
    /// something a reader typed and cannot come back reordered by a ranking they did not ask for.
    /// </remarks>
    [Fact]
    public async Task Listing_a_library_carries_no_words()
    {
        var prism = new LibraryStub();
        prism.Answers(_ => Json("[]"));

        await Prism(prism).ListAsync(new LibraryPhotoQuery(1, 60), default);

        prism.Only.Url.ShouldNotContain("&q=");
        prism.Only.Url.ShouldContain("order=newest");
    }

    // ------------------------------------------------------------------------------------ fixtures

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

    private static PhotoPrismClient Prism(
        LibraryStub stub,
        Action<PhotoPrismOptions>? configure = null,
        ILogger<PhotoPrismClient>? logger = null)
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
            logger ?? NullLogger<PhotoPrismClient>.Instance);
    }

    private static ImmichClient Immich(
        LibraryStub stub,
        Action<ImmichOptions>? configure = null,
        ILogger<ImmichClient>? logger = null)
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
            logger ?? NullLogger<ImmichClient>.Instance);
    }

    /// <summary>
    /// A logger that keeps what was warned about, so a test can assert on something that is
    /// deliberately not on any screen.
    /// </summary>
    private sealed class Recorder : ILogger<PhotoPrismClient>, ILogger<ImmichClient>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    /// <summary>The code a call was refused with, so a test names the sentence an operator is sent.</summary>
    private static async Task<string> Refused(Func<Task> call) =>
        (await Should.ThrowAsync<PhotoLibraryException>(call)).Code;

    private sealed record LibraryCall(string Method, string Url, string Body);

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
