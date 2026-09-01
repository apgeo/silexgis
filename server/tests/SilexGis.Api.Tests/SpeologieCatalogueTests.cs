// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Catalogue;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Catalogue;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Catalogue;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading somebody else's cave register and taking caves out of it.
///
/// <para>
/// Nothing here reaches the network. The catalogue answers from a stub wired in at the message
/// handler, which is also what makes the interesting half of this feature testable at all: what
/// this installation <em>asks</em> a volunteer-run service for is as much the subject as what it
/// does with the answer. The stub records every request, so the assertions about restraint — one
/// call for several spellings, never a page larger than the far end serves, never the field that
/// turns a listing from kilobytes into megabytes, and nothing at all sent for a request that was
/// going to be refused — are made against the bytes that would have left the machine.
/// </para>
/// <para>
/// The key is a fake one supplied through configuration, the way an operator supplies the real
/// one. No test here has, or needs, a working key.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpeologieCatalogueTests : IAsyncLifetime, IDisposable
{
    /// <summary>Stands in for the operator's own key. The real one is never in this repository.</summary>
    private const string FakeApiKey = "not-a-real-key-0000";

    private const string StatusUrl = "/api/v1/catalogue/speologie/status";
    private const string SearchUrl = "/api/v1/catalogue/speologie/caves";
    private const string ImportUrl = "/api/v1/catalogue/speologie/import";

    /// <summary>
    /// What this installation is configured to allow itself, set larger than the catalogue's own
    /// ceiling on purpose: the clamp that matters is the one applied before asking, and a
    /// configuration that asks for more than the far end serves is exactly the case that proves it.
    /// </summary>
    private const int ConfiguredMaxPageSize = 5000;

    /// <summary>Small, so "more than one confirmation takes" costs four integers rather than a hundred.</summary>
    private const int ConfiguredMaxSelection = 3;

    private readonly string connectionString;
    private readonly CatalogueStub catalogue = new();
    private readonly SilexGisApiFactory factory;

    private HttpClient editor = null!;    // may create caves — the right every route here is gated on
    private HttpClient viewer = null!;    // signed in, and may create nothing
    private HttpClient anonymous = null!; // nobody at all
    private Guid editorId;
    private string tag = null!;

    public SpeologieCatalogueTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                // Supplied the way an operator supplies it — SILEXGIS__Speologie__ApiKey.
                ["Speologie:ApiKey"] = FakeApiKey,
                // The politeness gap is a second in production and nothing here; what it protects
                // is somebody else's service, and the stub is not one.
                ["Speologie:MinRequestIntervalMs"] = "0",
                ["Speologie:MaxPageSize"] = ConfiguredMaxPageSize.ToString(),
                ["Speologie:MaxSelection"] = ConfiguredMaxSelection.ToString(),
            },
            services => services
                .AddHttpClient(SpeologieClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => catalogue));
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];

        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"spl-ed-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"spl-ed-{tag}@t.local");

        // The ruleset every account joins opens map layers, tags and the caver directory and says
        // nothing about creating features, so this account genuinely holds no create right.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"spl-vw-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"spl-vw-{tag}@t.local");

        anonymous = factory.CreateClient();
    }

    // ---------- who may ask ----------

    [Fact]
    public async Task Every_route_is_closed_to_somebody_who_has_not_signed_in()
    {
        (await anonymous.GetAsync(StatusUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{SearchUrl}?q=ursilor")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{SearchUrl}/1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(ImportUrl, ImportBody([1]))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        catalogue.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// Looking things up is gated on the right to create caves, which is not the obvious gate.
    /// </summary>
    /// <remarks>
    /// A search spends this installation's own key against somebody else's small service, so a
    /// read-only account that could call it would make this server a free proxy to that service.
    /// The refusal is also made before anything is asked, which is the half worth asserting: a
    /// request that was never going to be allowed must not cost the far end a call.
    /// </remarks>
    [Fact]
    public async Task Searching_and_importing_both_take_the_right_to_create_caves()
    {
        CatalogueHolds(Record(70100, $"Peștera Refuzată {tag}"));

        (await viewer.GetAsync($"{SearchUrl}?q=ursilor")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"{SearchUrl}/70100")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var refusedImport = await ImportAsync(ImportBody([70100]), viewer);
        var body = await BodyAsync(refusedImport);
        refusedImport.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        CodeOf(body).ShouldBe(CreateRules.ForbiddenCode);

        // Nothing left the machine on behalf of an account that was never going to be served.
        catalogue.Calls.ShouldBeEmpty();

        // The pass that makes the three refusals mean something.
        (await editor.GetAsync($"{SearchUrl}/70100")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- whether this installation is set up at all ----------

    [Fact]
    public async Task Status_reports_the_limits_this_installation_works_to_and_reaches_nothing()
    {
        var status = await JsonAsync(editor, StatusUrl);

        status.GetProperty("configured").GetBoolean().ShouldBeTrue();

        // The configured page size is 5000 and the answer is 100: what is published is what will
        // actually be asked for, not what somebody typed into a settings file.
        status.GetProperty("maxPageSize").GetInt32().ShouldBe(SpeologieOptions.RemotePageCeiling);
        status.GetProperty("maxSelection").GetInt32().ShouldBe(ConfiguredMaxSelection);
        status.GetProperty("portalUrl").GetString().ShouldBe("https://www.speologie.org");

        catalogue.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// An installation with no key is an ordinary installation. It says so, and it says so without
    /// asking anybody — the screens have to be able to explain themselves before a key exists.
    /// </summary>
    [Fact]
    public async Task Without_a_key_the_integration_reports_itself_absent_rather_than_failing()
    {
        var quiet = new CatalogueStub();
        using var unconfigured = new SilexGisApiFactory(
            connectionString,
            settings: null,
            configureServices: services => services
                .AddHttpClient(SpeologieClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => quiet));

        var email = $"spl-nokey-{Guid.NewGuid():N}"[..20] + "@t.local";
        await AuthHelper.CreateUserAsync(unconfigured, GlobalRoles.Editor, email);
        using var client = await AuthHelper.BearerClientAsync(unconfigured, email);

        var status = await JsonAsync(client, StatusUrl);
        status.GetProperty("configured").GetBoolean().ShouldBeFalse();

        // And a route that would need the key says which of the four things went wrong, rather
        // than sending a request with no key on it and reporting whatever came back.
        var search = await client.GetAsync($"{SearchUrl}?q=ursilor");
        var body = await BodyAsync(search);
        search.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(SpeologieException.NotConfiguredCode);

        quiet.Calls.ShouldBeEmpty();
    }

    // ---------- what is asked of the far end ----------

    /// <summary>
    /// A search that names nothing is refused here, before the catalogue is troubled with it.
    /// </summary>
    /// <remarks>
    /// The catalogue would answer it — a bare <c>pesteri</c> call is a page of the whole register —
    /// and answering it is how a small volunteer-run service gets walked end to end by a screen
    /// nobody thought was a crawler. The assertion that matters is the second one: the refusal
    /// happens before the call, not after it.
    /// </remarks>
    [Fact]
    public async Task A_search_naming_neither_a_name_nor_a_county_is_refused_before_anything_is_asked()
    {
        catalogue.Answers(_ => throw new InvalidOperationException("the catalogue must not be called"));

        var refused = await editor.GetAsync(SearchUrl);
        var body = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        CodeOf(body).ShouldBe(CatalogueEndpoints.SearchTooBroadCode);

        // Whitespace is not a term either.
        CodeOf(await BodyAsync(await editor.GetAsync($"{SearchUrl}?q=%20%20")))
            .ShouldBe(CatalogueEndpoints.SearchTooBroadCode);

        // A county has to be the two-letter code the catalogue actually matches on rather than a
        // county's name — but that is a precise question asked in the wrong vocabulary, not a
        // search that was too broad, and the two must not answer under the same code. Somebody
        // reading "too broad" after naming exactly one county goes looking for the thing they did
        // not do.
        CodeOf(await BodyAsync(await editor.GetAsync($"{SearchUrl}?county=Bihor")))
            .ShouldBe(CatalogueEndpoints.ValidationFailedCode);

        catalogue.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// One typed term is asked about under every spelling it has, in one request, and the answers
    /// are merged.
    /// </summary>
    /// <remarks>
    /// Romanian is written with comma-below letters, with the Turkish cedilla letters that decades
    /// of fonts produced instead, and often with neither — and the catalogue's search folds none of
    /// them together, so the two spellings return genuinely disjoint sets. Asking once per spelling
    /// would triple the traffic; asking once with aliased fields costs the far end one call, which
    /// is what this asserts. The description field is not asked for here at all: a hundred rows
    /// without it are a couple of hundred kilobytes and a hundred rows with it are megabytes,
    /// because one record's description in this catalogue can exceed a megabyte on its own.
    /// </remarks>
    [Fact]
    public async Task One_request_carries_every_spelling_and_never_asks_a_listing_for_the_description()
    {
        catalogue.AnswersWith(Data(new Dictionary<string, object?>
        {
            // Comma-below and cedilla return different caves — the whole reason both are asked.
            ["s0"] = new[] { Record(70201, $"Peștera Urșilor {tag}") },
            ["s1"] = new[] { Record(70202, $"Pestera Urşilor {tag}") },
            ["s2"] = Array.Empty<Dictionary<string, object?>>(),
        }));

        var found = await JsonAsync(editor, $"{SearchUrl}?q={Uri.EscapeDataString("urșilor")}");

        // One call, three questions.
        var call = catalogue.Only;
        call.Method.ShouldBe("POST");
        call.Url.ShouldBe("https://www.speologie.org/api/graphql");
        call.ApiKey.ShouldBe(FakeApiKey);

        call.Document.ShouldContain("s0: pesteri(");
        call.Document.ShouldContain("s1: pesteri(");
        call.Document.ShouldContain("s2: pesteri(");

        // Wide, but bounded — the width is what somebody else's service pays for.
        call.Document.ShouldNotContain($"s{SilexGis.Domain.Catalogue.RomanianText.MaxSpellings}: pesteri(");

        // The spellings themselves, in the variables the aliases read. What was typed leads, and
        // the wholly unaccented form comes second because a large part of this catalogue was
        // typed that way — for many caves it is the only spelling that matches at all.
        call.Text("q0").ShouldBe("urșilor");
        call.Text("q1").ShouldBe("ursilor");

        // The cedilla spelling is in there too, wherever the expansion placed it. Its position is
        // not the contract; its presence is, because it and the comma-below form return genuinely
        // disjoint sets of caves.
        call.Terms.ShouldContain("urşilor");

        // The measured reason a listing stays kilobytes. Asserted on the document rather than on
        // the response size, because the response here is whatever this test wrote.
        call.Document.ShouldNotContain("descriere");

        // Merged by catalogue id and ordered by it, so what came back under two spellings reads as
        // one result set rather than two half-answers.
        found.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32())
            .ShouldBe([70201, 70202]);

        // And the screen is told which spellings were actually searched for — a search that
        // quietly asked something other than what was typed is worse than one that did not.
        var reported = found.GetProperty("spellings").EnumerateArray().Select(x => x.GetString()).ToArray();
        reported[0].ShouldBe("urșilor");
        reported.ShouldContain("urşilor");
        reported.ShouldContain("ursilor");
        reported.Length.ShouldBeLessThanOrEqualTo(SilexGis.Domain.Catalogue.RomanianText.MaxSpellings);
    }

    /// <summary>
    /// However large a page is asked for, the catalogue is never asked for more than it serves.
    /// </summary>
    /// <remarks>
    /// The far end caps a page at 100 and clamps silently rather than refusing, so asking for a
    /// thousand would look like it worked and quietly return a hundred. The clamp is applied here
    /// as well, which is what makes the request honest and what keeps the ceiling in place the day
    /// the far end stops enforcing it. This installation is configured with a page size well above
    /// the ceiling, so the number in the request proves the clamp rather than the configuration.
    /// </remarks>
    [Fact]
    public async Task The_page_asked_of_the_catalogue_is_never_larger_than_the_catalogue_serves()
    {
        catalogue.AnswersWith(Data(new Dictionary<string, object?>
        {
            ["s0"] = Array.Empty<Dictionary<string, object?>>(),
        }));

        (await editor.GetAsync($"{SearchUrl}?q=avenul&page=3&pageSize=100000")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var call = catalogue.Only;
        call.Number("limit").ShouldBe(SpeologieOptions.RemotePageCeiling);

        // The offset follows the page the caller was actually served, not the one they typed.
        call.Number("offset").ShouldBe(2 * SpeologieOptions.RemotePageCeiling);
    }

    /// <summary>
    /// A county is passed through as the catalogue's own two-letter code, upper-cased, and combines
    /// with the term rather than replacing it.
    /// </summary>
    [Fact]
    public async Task A_county_is_sent_as_the_two_letter_code_the_catalogue_matches_on()
    {
        catalogue.AnswersWith(Data(new Dictionary<string, object?>
        {
            ["s0"] = Array.Empty<Dictionary<string, object?>>(),
        }));

        (await editor.GetAsync($"{SearchUrl}?q=avenul&county=bh")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var call = catalogue.Only;
        call.Text("judet").ShouldBe("BH");
        call.Document.ShouldContain("judet: $judet");
        call.Document.ShouldContain("q: $q0");
    }

    // ---------- the far end failing, four ways ----------

    /// <summary>
    /// The catalogue has three different ways of saying no and they are not the same problem.
    /// </summary>
    /// <remarks>
    /// A key it will not accept is something an administrator can fix. A service that cannot be
    /// reached is somebody else's outage. A request it understood and rejected is a defect here or
    /// a change at the far end. Collapsing them into one "catalogue error" sends whoever is on call
    /// to check a key that is fine, which is why each keeps its own code.
    /// </remarks>
    [Fact]
    public async Task The_catalogues_three_ways_of_refusing_are_reported_apart()
    {
        // 401 with an errors array. The status decides it: this is the only one an administrator
        // can act on, and it must not be read as "they rejected our query".
        catalogue.AnswersWith(
            """{"errors":[{"message":"Unauthorized - valid API key required"}]}""",
            HttpStatusCode.Unauthorized);
        await ShouldBeUnavailableAsync($"{SearchUrl}?q=ursilor", SpeologieException.UnauthorizedCode);

        // A key refused is not retried: trying a wrong key twice more is neither politer nor
        // likelier to work.
        catalogue.Calls.Count.ShouldBe(1);

        // Nothing answering at all. Tried again, a bounded number of times, and then given up on.
        catalogue.Reset();
        catalogue.Answers(_ => throw new HttpRequestException("connection refused"));
        await ShouldBeUnavailableAsync($"{SearchUrl}?q=ursilor", SpeologieException.UnavailableCode);
        catalogue.Calls.Count.ShouldBe(3);

        // HTTP 200 carrying an errors array and a null data member — the shape a field-level
        // rejection takes. Two hundred is not success here, and partial data is not used: a page
        // half-answered is a page nobody can tell is half-answered.
        catalogue.Reset();
        catalogue.AnswersWith("""{"errors":[{"message":"Provide exactly one of id or slug"}],"data":null}""");
        await ShouldBeUnavailableAsync($"{SearchUrl}?q=ursilor", SpeologieException.RejectedCode);
    }

    /// <summary>
    /// An identifier the catalogue does not know is answered as a missing cave, not as a failure.
    /// </summary>
    /// <remarks>
    /// The catalogue reports it as a null record inside <c>data</c>, with no errors array — which
    /// is a perfectly ordinary answer, and reading it as an outage would tell an administrator
    /// their key had stopped working every time somebody followed a stale link.
    /// </remarks>
    [Fact]
    public async Task An_identifier_the_catalogue_does_not_publish_is_a_missing_cave()
    {
        catalogue.AnswersWith(Data(new Dictionary<string, object?> { ["c0"] = null }));

        var response = await editor.GetAsync($"{SearchUrl}/999999999");
        var body = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, body);
        CodeOf(body).ShouldBe(CatalogueEndpoints.CaveNotFoundCode);
    }

    /// <summary>
    /// One cave in full: the description arrives as the catalogue's pasted markup and is answered
    /// as the plain text an import would store, converted by the code the import uses.
    /// </summary>
    [Fact]
    public async Task A_single_cave_is_answered_with_its_description_already_converted()
    {
        CatalogueHolds(Record(
            70301,
            $"Peștera cu Descriere {tag}",
            slug: $"pestera-cu-descriere-{tag}",
            descriere:
                "<p style='margin:0'>Intrarea&nbsp;este <em>largă</em></p>"
                + "<xml><w:WordDocument>residue</w:WordDocument></xml>"
                + "<ul><li>Galeria Mare</li><li>Sala Mică</li></ul>",
            clasificare: "clasaB"));

        var cave = await JsonAsync(editor, $"{SearchUrl}/70301");

        var description = cave.GetProperty("description").GetString()!;
        description.ShouldNotContain("<");
        description.ShouldNotContain("WordDocument");
        description.ShouldContain("Intrarea este largă");
        description.ShouldContain("• Galeria Mare");

        // The provenance line is part of the text, so a cave never loses where it came from.
        description.ShouldContain("speologie.org #70301");

        cave.GetProperty("protectionClass").GetString().ShouldBe("B");
        cave.GetProperty("url").GetString().ShouldBe($"https://www.speologie.org/pestera-cu-descriere-{tag}");
        cave.GetProperty("alreadyImported").GetBoolean().ShouldBeFalse();
    }

    // ---------- taking a cave into the registry ----------

    /// <summary>
    /// What an import of this catalogue produces, and the one thing that makes it different from
    /// every other import here: the cave arrives with no position.
    /// </summary>
    /// <remarks>
    /// The catalogue publishes no coordinates at all — not withheld, not approximate, the field
    /// does not exist. So a cave imported from it is in the registry and in search and on no map,
    /// which is a faithful record of what is known. Inventing a position would be worse.
    /// </remarks>
    [Fact]
    public async Task An_imported_cave_arrives_with_no_position_and_one_batch_that_undoes_it()
    {
        var title = $"Peștera Testului {tag}";
        CatalogueHolds(Record(
            70401,
            title,
            slug: $"pestera-testului-{tag}",
            judet: "BH",
            localitate: "Chișcău",
            munte: "craiului",
            lungime: 1200,
            denivelare: 45,
            denNegativa: -12,
            altitudine: 812,
            clasificare: "clasaB",
            roca: "05",
            scufundabila: "1",
            stiinta: "mineralogica,ursus",
            nrHidro: "4.15",
            bazinHidroId: 27,
            codAp: "2.613"));

        var result = await ImportedAsync(ImportBody([70401]));
        result.GetProperty("createdCount").GetInt32().ShouldBe(1);
        result.GetProperty("updatedCount").GetInt32().ShouldBe(0);
        result.GetProperty("failures").EnumerateArray().ShouldBeEmpty();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var feature = await db.Features.AsNoTracking().Include(f => f.Cave)
            .SingleAsync(f => f.Name == title && f.Kind == FeatureKind.Cave);

        // The whole point: no geometry, because the source has none to give.
        feature.Geom.ShouldBeNull();

        // The catalogue's own identifier, as text, which is the key a second import recognises
        // this cave by and the key the expression index is built on.
        var properties = JsonDocument.Parse(feature.Properties!).RootElement;
        properties.GetProperty(SpeologieMapping.Keys.Id).GetString().ShouldBe("70401");
        properties.GetProperty(SpeologieMapping.Keys.County).GetString().ShouldBe("BH");
        properties.GetProperty(SpeologieMapping.Keys.RockCode).GetString().ShouldBe("05");
        properties.GetProperty(SpeologieMapping.Keys.Sump).GetBoolean().ShouldBeTrue();

        // An altitude is a coordinate component and never goes in the bag, which is serialised
        // straight onto the wire on paths that have no protection filter to extend.
        properties.TryGetProperty("speologieAltitudine", out _).ShouldBeFalse();

        var cave = feature.Cave.ShouldNotBeNull();
        cave.SurveyedLength.ShouldBe(1200m);
        cave.Depth.ShouldBe(45m);

        // The source's sign convention for the downward range is not consistent; the magnitude is
        // what gets stored, so a negative one does not become a cave that goes upwards.
        cave.NegativeDepth.ShouldBe(12m);
        cave.Altitude.ShouldBe(812m);
        cave.ProtectionClass.ShouldBe("B");
        cave.Region.ShouldBe("craiului");

        // A locality is the redacted column rather than the description, because the description
        // is shown to everybody and naming the village above a protected cave gives the game away.
        cave.ClosestAddress.ShouldBe("Chișcău");

        var batches = await db.ImportBatches.AsNoTracking()
            .Where(b => b.ConfirmedByUserId == editorId).ToListAsync();
        var batch = batches.ShouldHaveSingleItem();
        batch.Source.ShouldBe(ImportSource.ExternalCatalogue);
        batch.CreatedCount.ShouldBe(1);
        batch.Id.ShouldBe(result.GetProperty("batchId").GetGuid());
    }

    /// <summary>
    /// The same catalogue entry imported twice is one cave, refreshed.
    /// </summary>
    /// <remarks>
    /// This is the failure that would be invisible: a second import that quietly made a second cave
    /// would look like it worked, and the duplicate would only surface later as two entries for the
    /// same hole. The refreshed title is asserted alongside the count so that "one cave" cannot be
    /// satisfied by a second run that did nothing at all.
    /// </remarks>
    [Fact]
    public async Task The_same_catalogue_entry_imported_twice_is_one_cave_refreshed()
    {
        var first = $"Peștera Veche {tag}";
        var second = $"Peștera Redenumită {tag}";

        CatalogueHolds(Record(70501, first, lungime: 300));
        (await ImportedAsync(ImportBody([70501]))).GetProperty("createdCount").GetInt32().ShouldBe(1);

        // The catalogue has since been corrected — a new name and a resurvey.
        CatalogueHolds(Record(70501, second, lungime: 460));
        var again = await ImportedAsync(ImportBody([70501]));

        again.GetProperty("createdCount").GetInt32().ShouldBe(0);
        again.GetProperty("updatedCount").GetInt32().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var caves = await db.Features.AsNoTracking().Include(f => f.Cave)
            .Where(f => f.Kind == FeatureKind.Cave && (f.Name == first || f.Name == second))
            .ToListAsync();

        var cave = caves.ShouldHaveSingleItem();
        cave.Name.ShouldBe(second);
        cave.Cave!.SurveyedLength.ShouldBe(460m);
    }

    /// <summary>
    /// A position somebody supplies becomes the cave's main entrance, which is the only way a cave
    /// has a position here at all — and a cave nobody placed stays where the catalogue left it.
    /// </summary>
    [Fact]
    public async Task A_position_given_on_import_becomes_the_caves_main_entrance()
    {
        var placedTitle = $"Peștera Plasată {tag}";
        var unplacedTitle = $"Peștera Nesituată {tag}";

        CatalogueHolds(
            Record(70601, placedTitle, altitudine: 940),
            Record(70602, unplacedTitle));

        var result = await ImportedAsync(ImportBody(
            [70601, 70602],
            new Dictionary<string, object?>
            {
                ["70601"] = new { longitude = 22.5875, latitude = 46.5625 },
            }));

        result.GetProperty("createdCount").GetInt32().ShouldBe(2);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var placed = await db.Features.AsNoTracking()
            .SingleAsync(f => f.Name == placedTitle && f.Kind == FeatureKind.Cave);
        placed.Geom.ShouldNotBeNull();
        placed.Geom!.Coordinate.X.ShouldBe(22.5875, 1e-9);
        placed.Geom.Coordinate.Y.ShouldBe(46.5625, 1e-9);

        var entrance = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.CaveFeatureId == placed.Id).ToListAsync();
        var only = entrance.ShouldHaveSingleItem();
        only.IsMain.ShouldBeTrue();

        // Somebody read a map and pointed at it — neither an instrument reading nor a guess.
        only.PositionQuality.ShouldBe(PositionQuality.Map);
        only.Altitude.ShouldBe(940m);

        // The one that was not placed is still geometry-less, so what put a point on the first is
        // the decision rather than the import.
        var unplaced = await db.Features.AsNoTracking()
            .SingleAsync(f => f.Name == unplacedTitle && f.Kind == FeatureKind.Cave);
        unplaced.Geom.ShouldBeNull();
        (await db.CaveEntrances.CountAsync(e => e.CaveFeatureId == unplaced.Id)).ShouldBe(0);
    }

    /// <summary>
    /// The confirmation undoes as one unit through the ordinary import-batch route — a catalogue
    /// import is a batch like any other, and does not need an undo of its own.
    /// </summary>
    [Fact]
    public async Task Reverting_the_batch_takes_back_the_cave_and_the_entrance_it_created()
    {
        var title = $"Peștera de Anulat {tag}";
        CatalogueHolds(Record(70701, title));

        var result = await ImportedAsync(ImportBody(
            [70701],
            new Dictionary<string, object?>
            {
                ["70701"] = new { longitude = 23.125, latitude = 45.375 },
            }));
        var batchId = result.GetProperty("batchId").GetGuid();

        var reverted = await editor.PostAsync($"/api/v1/import-batches/{batchId}/revert", null);
        reverted.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(reverted));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The cave is gone from the ordinary view of the registry...
        (await db.Features.CountAsync(f => f.Name == title)).ShouldBe(0);

        // ...and the row it left behind, plus the entrance under it, are stamped deleted rather
        // than erased: an undo of the whole subtree, which is what the batch promises.
        var cave = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(f => f.Name == title && f.Kind == FeatureKind.Cave);
        cave.DeletedAt.ShouldNotBeNull();

        var entranceIds = await db.CaveEntrances.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.CaveFeatureId == cave.Id).Select(e => e.Id).ToListAsync();
        entranceIds.ShouldHaveSingleItem();

        var entranceFeature = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(f => f.Id == entranceIds[0]);
        entranceFeature.DeletedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// The three refusals a confirmation makes before it writes anything.
    /// </summary>
    /// <remarks>
    /// The last is the one with a reason beyond tidiness: a person who has selected more caves than
    /// the ceiling allows has stopped reviewing them, and the wizard is for choosing caves rather
    /// than for taking the register. It is refused before the catalogue is asked about any of them.
    /// </remarks>
    [Fact]
    public async Task A_confirmation_that_names_nothing_or_too_much_is_refused_before_anything_is_written()
    {
        catalogue.Answers(_ => throw new InvalidOperationException("the catalogue must not be called"));

        var empty = await ImportAsync(ImportBody([]));
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(empty));

        // Half a position is not a position: a longitude with no latitude would place a cave on
        // the equator and look deliberate.
        var halfPlaced = await ImportAsync(ImportBody(
            [70801],
            new Dictionary<string, object?> { ["70801"] = new { longitude = 23.5 } }));
        var halfBody = await BodyAsync(halfPlaced);
        halfPlaced.StatusCode.ShouldBe(HttpStatusCode.BadRequest, halfBody);
        CodeOf(halfBody).ShouldBe("validation.failed");

        // One more than this installation allows in a single confirmation.
        var tooMany = Enumerable.Range(70810, ConfiguredMaxSelection + 1).ToArray();
        var refused = await ImportAsync(ImportBody(tooMany));
        var refusedBody = await BodyAsync(refused);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusedBody);
        CodeOf(refusedBody).ShouldBe("speologie.selection_too_large");

        catalogue.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// The cave list's new filter, which is what makes an imported cave findable at all.
    /// </summary>
    /// <remarks>
    /// A cave with no position is on no map and inside no bounding box. That used to be a rare
    /// accident; a cave imported from a register that publishes no coordinates arrives that way by
    /// nature, so there has to be a list that finds exactly those and a way to exclude them again.
    /// Both directions are asserted, because a filter that returned everything would satisfy one.
    /// </remarks>
    [Fact]
    public async Task An_imported_cave_with_no_position_is_what_the_unplaced_filter_finds()
    {
        var title = $"Peștera Nelocalizată {tag}";
        CatalogueHolds(Record(70901, title));
        await ImportedAsync(ImportBody([70901]));

        var unplaced = await JsonAsync(editor, $"/api/v1/caves?unplaced=true&search={tag}&pageSize=100");
        Names(unplaced).ShouldContain(title);

        var placed = await JsonAsync(editor, $"/api/v1/caves?unplaced=false&search={tag}&pageSize=100");
        Names(placed).ShouldNotContain(title);

        // Without the filter it is an ordinary cave of the registry, so what the filter did above
        // was select rather than hide.
        Names(await JsonAsync(editor, $"/api/v1/caves?search={tag}&pageSize=100")).ShouldContain(title);
    }

    // ---------- helpers ----------

    private static List<string?> Names(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())];

    private Task<HttpResponseMessage> ImportAsync(object body, HttpClient? client = null) =>
        (client ?? editor).PostAsJsonAsync(ImportUrl, body);

    private async Task<JsonElement> ImportedAsync(object body)
    {
        var response = await ImportAsync(body);
        var text = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static object ImportBody(int[] selection, IDictionary<string, object?>? decisions = null) => new
    {
        selection,
        decisions,
        visibility = "private",
        cavingGroupId = (Guid?)null,
        locationProtected = false,
        parentId = (Guid?)null,
    };

    private async Task ShouldBeUnavailableAsync(string url, string code)
    {
        var response = await editor.GetAsync(url);
        var body = await BodyAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        CodeOf(body).ShouldBe(code);
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static Task<string> BodyAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    /// <summary>
    /// Makes the stub answer detail queries out of the given records, the way the catalogue does:
    /// one aliased field per identifier asked about, and a null in place of an identifier it does
    /// not publish.
    /// </summary>
    private void CatalogueHolds(params Dictionary<string, object?>[] records)
    {
        var byId = records.ToDictionary(r => (int)r["id"]!);

        catalogue.Answers(call =>
        {
            var data = new Dictionary<string, object?>();

            foreach (var variable in call.Variables.EnumerateObject())
            {
                if (!variable.Name.StartsWith("id", StringComparison.Ordinal))
                {
                    continue;
                }

                data["c" + variable.Name[2..]] = byId.GetValueOrDefault(variable.Value.GetInt32());
            }

            return Answer(Data(data));
        });
    }

    /// <summary>
    /// One catalogue row, under the catalogue's own Romanian field names and in the shapes it
    /// actually uses: numbers declared as floats and returned as JSON integers, a boolean flag
    /// carried as the string "0" or "1", and a protection class as <c>clasaA</c>.
    /// </summary>
    private static Dictionary<string, object?> Record(
        int id,
        string title,
        string? slug = null,
        string? descriere = null,
        string? judet = null,
        string? localitate = null,
        string? munte = null,
        double? lungime = null,
        double? denivelare = null,
        double? denNegativa = null,
        double? altitudine = null,
        string? nrHidro = null,
        int? bazinHidroId = null,
        string? roca = null,
        string? scufundabila = null,
        string? clasificare = null,
        string? stiinta = null,
        bool? disparuta = null,
        string? codAp = null) => new()
        {
            ["id"] = id,
            ["title"] = title,
            ["slug"] = slug,
            ["descriere"] = descriere,
            ["judet"] = judet,
            ["localitate"] = localitate,
            ["munte"] = munte,
            ["lungime"] = lungime,
            ["denivelare"] = denivelare,
            ["denNegativa"] = denNegativa,
            ["altitudine"] = altitudine,
            ["nrHidro"] = nrHidro,
            ["bazinHidroId"] = bazinHidroId,
            ["roca"] = roca,
            ["scufundabila"] = scufundabila,
            ["clasificare"] = clasificare,
            ["stiinta"] = stiinta,
            ["disparuta"] = disparuta,
            ["codAp"] = codAp,
        };

    private static string Data(object? data) =>
        JsonSerializer.Serialize(new { data }, JsonSerializerOptions.Web);

    private static HttpResponseMessage Answer(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        catalogue.Dispose();
    }

    /// <summary>
    /// One request this installation sent to the catalogue, kept as what would have gone over the
    /// wire rather than as what a caller meant.
    /// </summary>
    private sealed record CatalogueCall(
        string Method, string Url, string? ApiKey, string? UserAgent, string Body, JsonElement Payload)
    {
        /// <summary>The GraphQL document, which is where the aliases and the field list live.</summary>
        public string Document =>
            Payload.TryGetProperty("query", out var query) ? query.GetString() ?? string.Empty : string.Empty;

        public JsonElement Variables => Payload.GetProperty("variables");

        public string? Text(string name) => Variables.GetProperty(name).GetString();

        /// <summary>
        /// Every spelling this request actually carried, whatever the expansion decided to send.
        /// Read from the variables rather than asserted position by position: which spelling lands
        /// in which slot is the expansion's business, and pinning it here would make every future
        /// improvement to the expansion look like a broken endpoint.
        /// </summary>
        public IReadOnlyList<string> Terms =>
        [
            .. Variables.EnumerateObject()
                .Where(p => p.Name.StartsWith('q') && p.Value.ValueKind == JsonValueKind.String)
                .Select(p => p.Value.GetString()!),
        ];

        public int Number(string name) => Variables.GetProperty(name).GetInt32();
    }

    /// <summary>
    /// Stands in for speologie.org: records every request and answers whatever the running test
    /// scripted. Nothing here reaches the network, and a test that forgets to script an answer gets
    /// an empty <c>data</c> object rather than a call going out.
    /// </summary>
    private sealed class CatalogueStub : HttpMessageHandler
    {
        private const string EmptyData = """{"data":{}}""";

        private readonly object gate = new();
        private readonly List<CatalogueCall> calls = [];

        private Func<CatalogueCall, HttpResponseMessage> answer = _ => Answer(EmptyData);

        public IReadOnlyList<CatalogueCall> Calls
        {
            get
            {
                lock (gate)
                {
                    return [.. calls];
                }
            }
        }

        /// <summary>The single request this test expected to cause, and a failure when there were more.</summary>
        public CatalogueCall Only
        {
            get
            {
                var recorded = Calls;
                recorded.Count.ShouldBe(1, $"expected exactly one call to the catalogue, saw {recorded.Count}");
                return recorded[0];
            }
        }

        public void Answers(Func<CatalogueCall, HttpResponseMessage> responder) => answer = responder;

        public void AnswersWith(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            Answers(_ => Answer(json, status));

        public void Reset()
        {
            lock (gate)
            {
                calls.Clear();
            }

            answer = _ => Answer(EmptyData);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var call = new CatalogueCall(
                request.Method.Method,
                request.RequestUri!.ToString(),
                Header(request, "X-API-Key"),
                Header(request, "User-Agent"),
                body,
                JsonDocument.Parse(body).RootElement.Clone());

            lock (gate)
            {
                calls.Add(call);
            }

            return answer(call);
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
