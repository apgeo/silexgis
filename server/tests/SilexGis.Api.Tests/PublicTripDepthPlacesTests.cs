// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The names a cave has given its depths, as the four published-trip routes tell them to somebody
/// without an account — which, on an installation as it is installed, is not at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these hold down.</b> An installation that has not switched the setting on sends the
/// list empty with every survey it hands over, whatever the cave has declared, and reads no
/// declaration to do so. Switched on, the two reads that hand a survey over carry the declarations
/// that have a name and whose station that survey holds — shallowest first, no more than the
/// stated bound — and the two that hand over no survey are the same bytes they were.
/// </para>
/// <para>
/// <b>Every "nothing is told" here is read beside a "something is told".</b> Two hosts stand over
/// one database, one as installed and one with the setting on, and are asked about the same trips
/// in the same instant; so an empty list on the first is the setting's doing and not a cave that
/// had declared nothing, and a search that finds no trace of a name is run over answers known to
/// carry the others.
/// </para>
/// <para>
/// The clock is the test's and stands still, so two hosts asked a moment apart sign their
/// addresses in the same stretch of time and their validators differ only by what they answer.
/// Both hosts count the commands they send to the database, which is how the cost of the list is
/// measured rather than argued.
/// </para>
/// </remarks>
public sealed class PublicTripDepthPlacesTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string PublishKey = "TripTracking:PublishDepthPlaces";

    private readonly SilexGisApiFactory asInstalled;
    private readonly SilexGisApiFactory publishing;
    private readonly CommandCounter installedCommands = new();
    private readonly CommandCounter publishingCommands = new();
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient visitor = null!;
    private HttpClient visitorOfPublishing = null!;
    private long caveTypeId;

    public PublicTripDepthPlacesTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"tripplaces-{Guid.NewGuid():N}");
        // The first host is given no word about the setting at all: what it answers is what an
        // installation answers that never heard of it.
        asInstalled = HostWith(installedCommands);
        publishing = HostWith(publishingCommands, (PublishKey, "true"));
    }

    /// <summary>
    /// A host over this class's database, file store, signing keys and clock, counting its
    /// database commands, with the given settings on top.
    /// </summary>
    private SilexGisApiFactory HostWith(CommandCounter counter, params (string Key, string Value)[] settings)
    {
        var host = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        };
        foreach (var (key, value) in settings) host[key] = value;
        return new SilexGisApiFactory(connectionString, host, services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(counter));
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(asInstalled, GlobalRoles.Editor, $"places-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(asInstalled, $"places-own-{suffix}@t.local");
        visitor = asInstalled.CreateClient();
        visitorOfPublishing = publishing.CreateClient();

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        publishing.Dispose();
        asInstalled.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    // Every name a declaration of these tests carries. None shares a word with a station, a title
    // or a person of the fixture, so finding one in an answer can only mean it was published — and
    // each is plain ASCII, because an answer writes any other letter as an escape and a search of
    // its text for the letter itself would then find nothing whether or not the name was there.
    private const string Entrance = "Balconul Zefir";
    private const string Hall = "Sala Quorn";
    private const string Sump = "Sifonul Vexat";
    private const string OnTheOlderSurveyOnly = "Galeria Ombrix";
    private const string OnNoSurvey = "Putul Yarrow";

    private static readonly string[] EveryLabel = [Entrance, Hall, Sump, OnTheOlderSurveyOnly, OnNoSurvey];

    /// <summary>
    /// <b>The load-bearing one.</b> The cave has declared five named depths and one unnamed. As
    /// installed, no route carries any of the names and neither survey comes with a list at all;
    /// with the setting on, each survey comes with exactly the named declarations whose station
    /// it holds, in order of depth — the followed trip's survey and the finished trip's older one
    /// answering differently about the place only the older one has — and the two reads that hand
    /// over no survey are byte for byte what they were.
    /// </summary>
    [Fact]
    public async Task Named_depths_are_told_only_where_the_installation_publishes_them_and_only_for_stations_of_the_survey_served()
    {
        var cave = await PublishedCaveAsync();
        await DeclareAsync(cave.Cave, 120m, "cave.deep.3", Sump);
        await DeclareAsync(cave.Cave, 20m, "cave.ent.0", Entrance);
        await DeclareAsync(cave.Cave, 50m, "cave.upper.2", Hall);
        // A station of both surveys, and nobody gave the depth a name.
        await DeclareAsync(cave.Cave, 80m, "cave.mid.1", null);
        // Named, on a station only the finished trip's survey holds; and on one neither holds.
        await DeclareAsync(cave.Cave, 95m, "cave.old.7", OnTheOlderSurveyOnly);
        await DeclareAsync(cave.Cave, 130m, "cave.gone.9", OnNoSurvey);

        // ---- as installed ------------------------------------------------------------------
        var off = new Dictionary<string, Answer>();
        foreach (var route in cave.Routes)
        {
            off[route] = await ReadAsync(visitor, route);
            off[route].Status.ShouldBe(HttpStatusCode.OK, route);
            foreach (var label in EveryLabel) off[route].Body.ShouldNotContain(label, Case.Insensitive, route);
        }

        // Null where a survey is handed over — never a list with nothing in it, which a page
        // keeping names of its own could read as "this cave has none"; and not a member of the
        // two lists, which hand no survey over.
        ShouldSendNoListOfPlaces(off[cave.Follow]);
        ShouldSendNoListOfPlaces(off[cave.PastTrip]);
        NamesIn(off[cave.Live]).ShouldNotContain("places");
        NamesIn(off[cave.Past]).ShouldNotContain("places");

        // ---- the same trips, the same instant, an installation that publishes them -----------
        var on = new Dictionary<string, Answer>();
        foreach (var route in cave.Routes)
        {
            on[route] = await ReadAsync(visitorOfPublishing, route);
            on[route].Status.ShouldBe(HttpStatusCode.OK, route);
            // Named for a station no survey of the cave holds: told by nobody, to nobody.
            on[route].Body.ShouldNotContain(OnNoSurvey, Case.Insensitive, route);
        }

        // The followed trip is drawn on the newer survey, which has no station of the old gallery.
        PlacesOf(on[cave.Follow]).ShouldBe(
        [
            ("cave.ent.0", 20m, Entrance),
            ("cave.upper.2", 50m, Hall),
            ("cave.deep.3", 120m, Sump),
        ]);
        on[cave.Follow].Body.ShouldNotContain(OnTheOlderSurveyOnly, Case.Insensitive);
        // The search of an answer's text that found no name above does find one that is there.
        on[cave.Follow].Body.ShouldContain(Hall);
        // The finished trip is drawn on the survey it was followed on, which has.
        PlacesOf(on[cave.PastTrip]).ShouldBe(
        [
            ("cave.ent.0", 20m, Entrance),
            ("cave.upper.2", 50m, Hall),
            ("cave.old.7", 95m, OnTheOlderSurveyOnly),
            ("cave.deep.3", 120m, Sump),
        ]);

        // ---- and that is the whole of the difference -----------------------------------------
        // The two lists hand out no survey and no signed address: the same bytes on both hosts.
        on[cave.Live].Body.ShouldBe(off[cave.Live].Body);
        on[cave.Past].Body.ShouldBe(off[cave.Past].Body);
        // The other two sign their survey's address afresh on every read, so they are compared
        // member by member with the addresses and the one new list left out.
        foreach (var route in new[] { cave.Follow, cave.PastTrip })
        {
            Comparable(Json(on[route]), "places").ShouldBe(Comparable(Json(off[route]), "places"), route);
        }

        // ---- a reader holding the installed answer is not told "nothing changed" -------------
        foreach (var route in new[] { cave.Follow, cave.PastTrip })
        {
            on[route].Tag.ShouldNotBe(off[route].Tag, route);
            (await ReadAsync(visitor, route, off[route].Tag)).Status.ShouldBe(HttpStatusCode.NotModified, route);
            var asked = await ReadAsync(visitorOfPublishing, route, off[route].Tag);
            asked.Status.ShouldBe(HttpStatusCode.OK, route);
            asked.Body.ShouldNotBeEmpty();
        }

        // Where the setting changes nothing it changes no validator.
        foreach (var route in new[] { cave.Live, cave.Past })
        {
            on[route].Tag.ShouldBe(off[route].Tag, route);
            (await ReadAsync(visitorOfPublishing, route, off[route].Tag))
                .Status.ShouldBe(HttpStatusCode.NotModified, route);
        }

        // ---- a name taken back is taken off the page, and the reader is sent the new answer ---
        await DeclareAsync(cave.Cave, 50m, "cave.upper.2", null);
        var after = await ReadAsync(visitorOfPublishing, cave.Follow, on[cave.Follow].Tag);
        after.Status.ShouldBe(HttpStatusCode.OK);
        PlacesOf(after).Select(p => p.Label).ShouldBe([Entrance, Sump]);
    }

    /// <summary>
    /// <b>Nothing to say is said as null, never as a list with no entries</b> — read off the bytes
    /// of the answer, because a page outside this application may take any list here as replacing
    /// the names it keeps itself. Three states say nothing: an installation that does not publish
    /// the names (whatever the cave has declared), one that does for a cave that has named no
    /// depth, and one that does for a cave whose only names are for stations the survey being
    /// handed over does not hold. In each the two reads that hand a survey over carry
    /// <c>"places":null</c> and no array under that name; one name on a station of the survey and
    /// the same read carries a list of one.
    /// </summary>
    [Fact]
    public async Task With_nothing_to_tell_the_named_depths_are_null_in_the_answer_and_never_an_empty_list()
    {
        var cave = await PublishedCaveAsync();
        string[] handOverASurvey = [cave.Follow, cave.PastTrip];

        // ---- publishing, and the cave has declared nothing ----------------------------------
        var nothingDeclared = new Dictionary<string, Answer>();
        foreach (var route in handOverASurvey)
        {
            nothingDeclared[route] = await ReadAsync(visitorOfPublishing, route);
            ShouldSendNoListOfPlaces(nothingDeclared[route]);
            // An installation that turned the setting on and has nothing to say answers what one
            // that never heard of it answers, member for member — and under the same validator,
            // so a reader's copy from before the setting was turned on is still confirmed.
            var installed = await ReadAsync(visitor, route);
            ShouldSendNoListOfPlaces(installed);
            Comparable(Json(nothingDeclared[route]), without: "").ShouldBe(Comparable(Json(installed), without: ""), route);
            nothingDeclared[route].Tag.ShouldBe(installed.Tag, route);
        }

        // ---- publishing, a depth declared and nobody gave it a name --------------------------
        await DeclareAsync(cave.Cave, 80m, "cave.mid.1", null);
        foreach (var route in handOverASurvey) ShouldSendNoListOfPlaces(await ReadAsync(visitorOfPublishing, route));

        // ---- publishing, named — on a station neither survey of the cave holds ---------------
        await DeclareAsync(cave.Cave, 130m, "cave.gone.9", OnNoSurvey);
        foreach (var route in handOverASurvey)
        {
            var answer = await ReadAsync(visitorOfPublishing, route);
            ShouldSendNoListOfPlaces(answer);
            answer.Body.ShouldNotContain(OnNoSurvey, Case.Insensitive, route);
            answer.Tag.ShouldBe(nothingDeclared[route].Tag, route);
        }

        // ---- publishing, named — on a station only the finished trip's survey holds ----------
        await DeclareAsync(cave.Cave, 95m, "cave.old.7", OnTheOlderSurveyOnly);
        ShouldSendNoListOfPlaces(await ReadAsync(visitorOfPublishing, cave.Follow));
        PlacesOf(await ReadAsync(visitorOfPublishing, cave.PastTrip))
            .ShouldBe([("cave.old.7", 95m, OnTheOlderSurveyOnly)]);

        // ---- and one name on a station both hold: a list, where there is something in it -----
        await DeclareAsync(cave.Cave, 50m, "cave.upper.2", Hall);
        foreach (var route in handOverASurvey)
        {
            var told = await ReadAsync(visitorOfPublishing, route);
            PlacesOf(told).ShouldContain(("cave.upper.2", 50m, Hall), route);
            told.Body.ShouldContain("\"places\":[{", Case.Sensitive, route);
            told.Tag.ShouldNotBe(nothingDeclared[route].Tag, route);

            // The installation beside it, the same cave with all of that declared: still null.
            var installed = await ReadAsync(visitor, route);
            ShouldSendNoListOfPlaces(installed);
            foreach (var label in EveryLabel) installed.Body.ShouldNotContain(label, Case.Insensitive, route);
            installed.Tag.ShouldNotBe(told.Tag, route);
        }
    }

    /// <summary>
    /// What the list costs the database, counted: nothing where it is not published, one read
    /// where the cave has named nothing, two where it has — and still two, and no more than the
    /// stated bound of names, when a cave has named more places than the bound.
    /// </summary>
    [Fact]
    public async Task The_list_costs_one_read_without_named_depths_two_with_and_is_bounded_however_many_a_cave_declares()
    {
        var cave = await PublishedCaveAsync();

        // Each host once before it is counted, so nothing a first request sets up is in the count.
        (await ReadAsync(visitor, cave.Follow)).Status.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(visitorOfPublishing, cave.Follow)).Status.ShouldBe(HttpStatusCode.OK);

        // ---- nothing declared ----------------------------------------------------------------
        var baseline = await CountedAsync(installedCommands, visitor, cave.Follow);
        var askedOnly = await CountedAsync(publishingCommands, visitorOfPublishing, cave.Follow);
        baseline.ShouldBeGreaterThan(0);
        askedOnly.ShouldBe(baseline + 1);

        // A depth nobody named is not a reason to look at the stations either.
        await DeclareAsync(cave.Cave, 80m, "cave.mid.1", null);
        (await CountedAsync(publishingCommands, visitorOfPublishing, cave.Follow)).ShouldBe(baseline + 1);

        // ---- one named depth -----------------------------------------------------------------
        await DeclareAsync(cave.Cave, 50m, "cave.upper.2", Hall);
        (await CountedAsync(installedCommands, visitor, cave.Follow)).ShouldBe(baseline);
        (await CountedAsync(publishingCommands, visitorOfPublishing, cave.Follow)).ShouldBe(baseline + 2);
        PlacesOf(await ReadAsync(visitorOfPublishing, cave.Follow)).ShouldBe([("cave.upper.2", 50m, Hall)]);

        // ---- more named depths than a published survey is ever handed --------------------------
        var beyond = TrackingDepthPlacements.MaxPublishedPlaces + 5;
        using (var scope = asInstalled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            // From 200 m down, a metre apart, deepest written first so that the order of the
            // answer cannot be the order of the rows.
            db.CaveDepthPlaces.AddRange(Enumerable.Range(0, beyond - 1).Reverse().Select(i => new CaveDepthPlace
            {
                CaveFeatureId = cave.Cave,
                DepthM = 200m + i,
                ViewerStationName = "cave.deep.3",
                PlaceLabel = $"Treapta {i + 1}",
            }));
            await db.SaveChangesAsync();
        }

        (await CountedAsync(installedCommands, visitor, cave.Follow)).ShouldBe(baseline);
        (await CountedAsync(publishingCommands, visitorOfPublishing, cave.Follow)).ShouldBe(baseline + 2);

        var capped = PlacesOf(await ReadAsync(visitorOfPublishing, cave.Follow));
        capped.Count.ShouldBe(TrackingDepthPlacements.MaxPublishedPlaces);
        capped.Select(p => p.DepthM).ShouldBe(capped.Select(p => p.DepthM).Order());
        capped[0].ShouldBe(("cave.upper.2", 50m, Hall));
        // The deepest five are the ones left out.
        capped[^1].DepthM.ShouldBe(200m + beyond - 2 - 5);
        // And on the installation beside it the same cave still comes with no list.
        ShouldSendNoListOfPlaces(await ReadAsync(visitor, cave.Follow));

        // The replay hands the same survey over and pays the same two reads for the same list.
        (await ReadAsync(visitor, cave.PastTrip)).Status.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(visitorOfPublishing, cave.PastTrip)).Status.ShouldBe(HttpStatusCode.OK);
        var replayInstalled = await CountedAsync(installedCommands, visitor, cave.PastTrip);
        (await CountedAsync(publishingCommands, visitorOfPublishing, cave.PastTrip)).ShouldBe(replayInstalled + 2);
    }

    /// <summary>
    /// A cave whose position becomes protected after its trips were published has no published
    /// answer at all, so its names travel nowhere — on an installation that publishes them, where
    /// a moment earlier the same link told them.
    /// </summary>
    [Fact]
    public async Task A_protected_cave_has_no_published_answer_for_its_named_depths_to_travel_in()
    {
        var cave = await PublishedCaveAsync();
        await DeclareAsync(cave.Cave, 50m, "cave.upper.2", Hall);

        // Told, while the cave may be published.
        PlacesOf(await ReadAsync(visitorOfPublishing, cave.Follow)).ShouldBe([("cave.upper.2", 50m, Hall)]);
        PlacesOf(await ReadAsync(visitorOfPublishing, cave.PastTrip)).ShouldBe([("cave.upper.2", 50m, Hall)]);

        using (var scope = asInstalled.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            await writer.SetLocationProtectedAsync(cave.Cave, true);
            await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
        }

        foreach (var client in new[] { visitor, visitorOfPublishing })
        {
            foreach (var route in cave.Routes)
            {
                var refused = await ReadAsync(client, route);
                refused.Status.ShouldBe(HttpStatusCode.NotFound, route);
                refused.Body.ShouldNotContain(Hall, Case.Insensitive, route);
                NamesIn(refused).ShouldNotContain("places", route);
            }
        }
    }

    // ---- what a reader sees ------------------------------------------------------------------

    private sealed record Answer(HttpStatusCode Status, string? Tag, string Body);

    private static async Task<Answer> ReadAsync(HttpClient client, string url, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch).ShouldBeTrue();
        }

        using var response = await client.SendAsync(request);
        return new Answer(
            response.StatusCode, response.Headers.ETag?.ToString(), await response.Content.ReadAsStringAsync());
    }

    /// <summary>How many commands the host sent to the database to answer one read.</summary>
    private static async Task<int> CountedAsync(CommandCounter counter, HttpClient client, string url)
    {
        counter.Reset();
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        return counter.Count;
    }

    private static JsonElement Json(Answer answer) => JsonDocument.Parse(answer.Body).RootElement;

    /// <summary>
    /// The answer hands a survey over and says nothing about named depths, <b>judged on its bytes
    /// as well as on what they parse to</b>: the member is there as null, the way this answer
    /// writes every member it has nothing for, and no array stands under that name anywhere.
    /// </summary>
    private static void ShouldSendNoListOfPlaces(Answer answer)
    {
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        var model = Json(answer).GetProperty("model");
        model.ValueKind.ShouldBe(JsonValueKind.Object);
        model.TryGetProperty("places", out var places).ShouldBeTrue();
        places.ValueKind.ShouldBe(JsonValueKind.Null);
        // Its neighbours that have nothing to say are written the same way, so "null and present"
        // is this answer's own habit and not a special case of this member.
        model.GetProperty("meshUrl").ValueKind.ShouldBe(JsonValueKind.Null);

        // The bytes: the response is written compact, so these are exact spellings.
        answer.Body.ShouldContain("\"places\":null", Case.Sensitive);
        answer.Body.ShouldNotContain("\"places\":[", Case.Sensitive);
        NamesIn(answer).Count(name => name == "places").ShouldBe(1);
    }

    /// <summary>
    /// The named depths that came with the survey of an answer that hands one over: a list, and
    /// one with something in it — an answer with nothing to tell sends no list at all.
    /// </summary>
    private static List<(string Station, decimal DepthM, string Label)> PlacesOf(Answer answer)
    {
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        var places = Json(answer).GetProperty("model").GetProperty("places");
        places.ValueKind.ShouldBe(JsonValueKind.Array);
        places.GetArrayLength().ShouldBeGreaterThan(0);
        return [.. places.EnumerateArray().Select(place =>
        {
            // Exactly these three members: no id of the declaration, nothing about who wrote it.
            place.EnumerateObject().Select(m => m.Name).ShouldBe(["station", "depthM", "label"]);
            return (
                place.GetProperty("station").GetString()!,
                place.GetProperty("depthM").GetDecimal(),
                place.GetProperty("label").GetString()!);
        })];
    }

    /// <summary>Every member name in the answer, at any depth.</summary>
    private static List<string> NamesIn(Answer answer)
    {
        static IEnumerable<string> Walk(JsonElement node) => node.ValueKind switch
        {
            JsonValueKind.Object => node.EnumerateObject().SelectMany(m => Walk(m.Value).Prepend(m.Name)),
            JsonValueKind.Array => node.EnumerateArray().SelectMany(Walk),
            _ => [],
        };

        return Walk(Json(answer)).ToList();
    }

    /// <summary>
    /// An answer as text that two reads of one state share: one member left out wherever it
    /// stands, and every address that is signed afresh on each read replaced by a mark.
    /// </summary>
    private static string Comparable(JsonElement node, string without) => node.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", node.EnumerateObject()
            .Where(m => m.Name != without)
            .Select(m => $"\"{m.Name}\":{Comparable(m.Value, without)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",", node.EnumerateArray().Select(e => Comparable(e, without))) + "]",
        JsonValueKind.String when node.GetString()!.Contains("token=", StringComparison.Ordinal) => "\"signed\"",
        _ => node.GetRawText(),
    };

    /// <summary>Every command a host sends to the database, counted and nothing else.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Reset() => Interlocked.Exchange(ref count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return new(result);
        }
    }

    // ---- seeding -----------------------------------------------------------------------------

    private async Task DeclareAsync(Guid cave, decimal depth, string station, string? label)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/caves/{cave}/depth-places",
            new { depthM = depth, stationName = station, placeLabel = label });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private sealed record Published(Guid Trip, string Token);

    /// <summary>
    /// One cave with a party in it now and a trip that is over, both published, and the four
    /// addresses the first trip's link reads.
    /// </summary>
    private sealed record PublishedCave(Guid Cave, Published Now, Published Then)
    {
        public string Follow => $"/api/v1/public/trips/{Uri.EscapeDataString(Now.Token)}";

        public string Live => $"{Follow}/live";

        public string Past => $"{Follow}/past";

        public string PastTrip => $"{Past}/{Then.Trip}";

        public string[] Routes => [Follow, Live, Past, PastTrip];
    }

    /// <summary>
    /// A cave surveyed twice. The trip that is over was followed on the older survey, which has a
    /// station the newer one has since lost; the trip being followed now is on the newer one.
    /// </summary>
    private async Task<PublishedCave> PublishedCaveAsync()
    {
        var cave = await CaveAsync();
        var older = await ModelAsync(cave, "cave.old.7");
        var then = await PublishedTripAsync("Last week", older);
        await CloseAsync(then.Trip, clock.Now.AddDays(-5));
        var newer = await ModelAsync(cave);
        var now = await PublishedTripAsync("Tonight", newer);
        return new PublishedCave(cave, now, then);
    }

    /// <summary>A trip armed on the model, one caver placed, and published.</summary>
    private async Task<Published> PublishedTripAsync(string title, Guid model)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = new[] { new { newCaverName = $"Guest {Guid.NewGuid():N}"[..24] } },
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var trip = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        Guid caver;
        using (var scope = asInstalled.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caver = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
                .Select(p => p.CaverId).Distinct().SingleAsync();
        }

        (await PutConfigAsync(trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/events",
            new { caverIds = new[] { caver }, kind = "atStation", stationName = "cave.upper.2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var minted = await owner.PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var link = JsonDocument.Parse(await minted.Content.ReadAsStringAsync()).RootElement;
        return new Published(trip, link.GetProperty("token").GetString()!);
    }

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    private async Task CloseAsync(Guid trip, DateTimeOffset closedAt)
    {
        (await PutConfigAsync(trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Places Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A survey model created the real way — so it has a file, and the answers that draw it hand
    /// out a signed address — then stations seeded straight into the graph tables: the four every
    /// survey of this fixture has, and any more that are named.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId, params string[] alsoHolding)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "tripplaces.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 31700;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.mid.1", 270, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", 230, SurveyStationFlags.Underground));
        db.SurveyStations.AddRange(
            alsoHolding.Select(name => Station(modelId, name, 255, SurveyStationFlags.Underground)));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(Guid modelId, string name, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = name[..name.LastIndexOf('.')],
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    private async Task<HttpResponseMessage> PutConfigAsync(Guid trip, object body)
    {
        var current = await owner.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await owner.SendAsync(request);
    }
}
