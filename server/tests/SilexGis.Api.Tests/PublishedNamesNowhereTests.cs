// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a published link says about the people on a trip, read as the bytes a visitor receives:
/// their names only where the installation publishes names, and never an identifier of theirs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why whole bodies.</b> Each public route has a test of its own that reads the <c>label</c> of
/// a participant and finds it null when names are switched off. That holds the field down and
/// nothing else: a name that travelled in a team's title, in a caption of a picture, in a member
/// added next year, would pass every one of them. An installation that switches names off has
/// been promised that the page names nobody, not that one field is empty — so the assertion here
/// is made on the raw text of every answer, for every word of every name.
/// </para>
/// <para>
/// <b>Two servers over one database.</b> The same rows are read through a host that publishes
/// names and through one that does not. With names on, every roster name is found in every answer
/// that carries people; that is what makes its absence from the other host's answer a fact about
/// the setting rather than about a roster that was empty, a search that could not match, or a
/// route that answered something else. Both hosts share one file store and one key ring, as two
/// processes of one installation would.
/// </para>
/// <para>
/// <b>The names are invented and are substrings of nothing else here.</b> Trip titles, the camp,
/// the team, the cave and the caption are worded so that no word of a roster name occurs in them,
/// and the generated parts of a title are hexadecimal. So a match is the name, and a failure of
/// the negative half is a disclosure and not a coincidence of the fixture.
/// </para>
/// <para>
/// A finished trip leaves the followed side at once on these hosts — no grace after its watch is
/// closed — so that the cave has a past trip to list and to replay without a row being backdated
/// behind the API.
/// </para>
/// </remarks>
public sealed class PublishedNamesNowhereTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The roster of both trips, as typed by the coordinator.</summary>
    private static readonly string[] People = ["Quorvix Dalthane", "Ysolde Brannick"];

    /// <summary>What a coordinator types for somebody who is not to appear under their name.</summary>
    private const string Caption = "Zephyrine K.";

    private const string CampName = "Autumn camp";
    private const string TeamTitle = "Rope party";

    private readonly SilexGisApiFactory naming;
    private readonly SilexGisApiFactory hushed;
    private readonly string filesRoot;

    private HttpClient coordinator = null!;
    private HttpClient visitor = null!;
    private long caveTypeId;

    public PublishedNamesNowhereTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"names-{Guid.NewGuid():N}");
        naming = HostWith(postgres.ConnectionString, publishRealNames: null);
        hushed = HostWith(postgres.ConnectionString, publishRealNames: false);
    }

    /// <summary>
    /// One of the two servers. They differ in the one setting and in nothing else, so a difference
    /// between their answers is that setting's doing.
    /// </summary>
    private SilexGisApiFactory HostWith(string connectionString, bool? publishRealNames)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["TripTracking:ShareGraceAfterClose"] = "00:00:00",
        };

        // Left unset on the first host on purpose: what an installation publishes when nobody
        // chose is part of what is being pinned.
        if (publishRealNames is { } value)
        {
            settings["TripTracking:PublishRealNames"] = value ? "true" : "false";
        }

        // Workers off: the survey file below is four bytes, and a worker reading it would fail the
        // model and rewrite the stations seeded beside it.
        return new SilexGisApiFactory(connectionString, settings, JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var email = $"names-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        _ = await AuthHelper.CreateUserAsync(naming, GlobalRoles.Editor, email);
        coordinator = await AuthHelper.BearerClientAsync(naming, email);
        visitor = naming.CreateClient();

        using var scope = naming.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        naming.Dispose();
        hushed.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The four public routes of one link, each read as text from both servers.
    /// </summary>
    /// <remarks>
    /// The list of past trips is in the walk although it carries nobody: a row there is a trip, a
    /// date and a head count. It is asserted to say so on both servers — the count is there and no
    /// word of a name is — because "this route never names anybody" is a property somebody could
    /// change, and a walk that skipped it would not notice.
    /// </remarks>
    [Fact]
    public async Task With_names_switched_off_no_word_of_a_roster_name_is_in_any_public_answer()
    {
        var published = await ArrangeAsync();
        using var unnamedVisitor = hushed.CreateClient();

        foreach (var route in Routes(published))
        {
            var named = await RawAsync(visitor, route.Address);
            var unnamed = await RawAsync(unnamedVisitor, route.Address);

            if (route.Party is { } party)
            {
                // The positive half. Each person is on this answer under the roster's name ...
                Labels(party(Parse(named)))
                    .ShouldBe(published.Roster.Select(p => (string?)p.Name), customMessage: route.Name);
                foreach (var name in People)
                {
                    named.Contains(name, StringComparison.Ordinal)
                        .ShouldBeTrue($"{route.Name}, names on: \"{name}\" should be in the answer");
                }

                // ... and the other server's answer is about the same two people, unnamed: still a
                // party of two in the same order, so what follows is not an empty answer passing.
                var quietParty = party(Parse(unnamed));
                Labels(quietParty).ShouldBe(new string?[] { null, null }, customMessage: route.Name);
                quietParty.Select(p => p.GetProperty("ordinal").GetInt32())
                    .ShouldBe(new[] { 1, 2 }, customMessage: route.Name);
            }
            else
            {
                // Nobody is carried at all, whichever way the setting is: a head count and no names.
                ShouldCarryNoWordOfAName(named, $"{route.Name}, names on");
                foreach (var body in new[] { named, unnamed })
                {
                    Parse(body).GetProperty("trips").EnumerateArray()
                        .Single(t => t.GetProperty("tripLogId").GetGuid() == published.Finished)
                        .GetProperty("participantCount").GetInt32().ShouldBe(People.Length, route.Name);
                }
            }

            ShouldCarryNoWordOfAName(unnamed, $"{route.Name}, names off");
        }
    }

    /// <summary>
    /// A caption is what a coordinator types for somebody who is not to appear under their own
    /// name. It is published whichever way the installation is set, and the name it stands in for
    /// is published by neither.
    /// </summary>
    /// <remarks>
    /// The second person carries no caption and is the control on the naming server: their roster
    /// name is still in the answer, so the first person's being absent from it is the caption
    /// outranking the name, and not names having gone missing altogether.
    /// </remarks>
    [Fact]
    public async Task A_typed_caption_is_published_and_the_roster_name_behind_it_is_not()
    {
        var published = await ArrangeAsync();
        var captioned = published.Roster[0];
        var plain = published.Roster[1];

        foreach (var trip in new[] { published.Followed, published.Finished })
        {
            var typed = await coordinator.PutAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/tracking/participants/{captioned.Caver}", new { label = Caption });
            typed.StatusCode.ShouldBe(HttpStatusCode.OK, await typed.Content.ReadAsStringAsync());
        }

        using var unnamedVisitor = hushed.CreateClient();
        foreach (var route in Routes(published))
        {
            var named = await RawAsync(visitor, route.Address);
            var unnamed = await RawAsync(unnamedVisitor, route.Address);

            if (route.Party is { } party)
            {
                Labels(party(Parse(named)))
                    .ShouldBe(new string?[] { Caption, plain.Name }, customMessage: route.Name);
                Labels(party(Parse(unnamed)))
                    .ShouldBe(new string?[] { Caption, null }, customMessage: route.Name);

                foreach (var (body, setting) in new[] { (named, "names on"), (unnamed, "names off") })
                {
                    body.Contains(Caption, StringComparison.Ordinal)
                        .ShouldBeTrue($"{route.Name}, {setting}: the caption should be in the answer");
                }

                named.Contains(plain.Name, StringComparison.Ordinal)
                    .ShouldBeTrue($"{route.Name}, names on: the uncaptioned person should still be named");
            }
            else
            {
                // The list of past trips carries nobody, so it carries nobody's caption either.
                named.Contains(Caption, StringComparison.Ordinal).ShouldBeFalse(route.Name);
                unnamed.Contains(Caption, StringComparison.Ordinal).ShouldBeFalse(route.Name);
            }

            // The name the caption stands in for: on neither server, in no answer.
            ShouldCarryNoWordOf(captioned.Name, named, $"{route.Name}, names on, captioned");
            ShouldCarryNoWordOfAName(unnamed, $"{route.Name}, names off, captioned");
        }
    }

    /// <summary>
    /// A row of the two public lists carries exactly the members recorded here, and no identifier
    /// of the cave, of its survey or of a person.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two lists are what a link shows of trips it was not made for, which is why they are the
    /// ones pinned: a member added to a row is published about every published trip of the cave at
    /// once. An equality, so that a member joining a row fails here the day it is added and is
    /// then recorded on purpose — the same bargain the census of anonymous routes makes for routes.
    /// </para>
    /// <para>
    /// The nested objects are pinned with the rows, because that is where an identifier of a
    /// person would arrive: on a participant, not on the trip. The identifiers that are there are
    /// there by decision — the trip's, the camp's, a team's — and none of them is a way back to a
    /// cave, a survey file or somebody's record.
    /// </para>
    /// <para>
    /// The values are searched for as well as the member names, since an identifier can travel
    /// under an innocent name or inside an address. That search is shown to be able to find one:
    /// the trip's own identifier is found in each answer by the same means.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_row_of_either_public_list_carries_exactly_the_recorded_members_and_no_inner_identifier()
    {
        var published = await ArrangeAsync();

        var liveText = await RawAsync(visitor, LiveList(published.Token));
        var live = Parse(liveText);
        Members(live).ShouldBe(["trips", "more"], ignoreOrder: true);
        var followed = live.GetProperty("trips").EnumerateArray().ShouldHaveSingleItem();
        followed.GetProperty("tripLogId").GetGuid().ShouldBe(published.Followed);
        Members(followed).ShouldBe(
            [
                "tripLogId", "expedition", "title", "tripDate", "tripDateEnd", "state", "armedAt",
                "closedAt", "positionsWithheld", "teams", "participants",
            ],
            ignoreOrder: true);
        Members(followed.GetProperty("expedition")).ShouldBe(["id", "name"], ignoreOrder: true);
        Members(followed.GetProperty("teams").EnumerateArray().ShouldHaveSingleItem())
            .ShouldBe(["id", "title"], ignoreOrder: true);
        var party = followed.GetProperty("participants").EnumerateArray().ToList();
        party.Count.ShouldBe(People.Length);
        foreach (var person in party)
        {
            Members(person).ShouldBe(
                [
                    "ordinal", "label", "teamId", "stationName", "depthM", "lastRecordedAt",
                    "positionRecordedAt", "positionOnOtherModel", "in", "out",
                ],
                ignoreOrder: true);
        }

        var pastText = await RawAsync(visitor, PastList(published.Token));
        var past = Parse(pastText);
        Members(past).ShouldBe(["trips", "more"], ignoreOrder: true);
        var finished = past.GetProperty("trips").EnumerateArray().ShouldHaveSingleItem();
        finished.GetProperty("tripLogId").GetGuid().ShouldBe(published.Finished);
        Members(finished).ShouldBe(
            [
                "tripLogId", "expedition", "title", "tripDate", "tripDateEnd", "closedAt",
                "participantCount", "playable",
            ],
            ignoreOrder: true);
        Members(finished.GetProperty("expedition")).ShouldBe(["id", "name"], ignoreOrder: true);

        Holds(liveText, published.Followed).ShouldBeTrue("the search finds an identifier that is there");
        Holds(pastText, published.Finished).ShouldBeTrue("the search finds an identifier that is there");
        foreach (var (text, list) in new[] { (liveText, "followed list"), (pastText, "past list") })
        {
            Holds(text, published.Cave).ShouldBeFalse($"{list}: the cave's identifier");
            Holds(text, published.Model).ShouldBeFalse($"{list}: the survey's identifier");
            foreach (var person in published.Roster)
            {
                Holds(text, person.Caver).ShouldBeFalse($"{list}: a person's identifier");
            }
        }
    }

    // ---- reading -----------------------------------------------------------------------------

    /// <param name="Name">What the route is called in a failure message.</param>
    /// <param name="Address">Its address for the link under test.</param>
    /// <param name="Party">
    /// Where the two people are in its answer, in the order the page numbers them; null for the
    /// one route that carries nobody.
    /// </param>
    private sealed record Route(string Name, string Address, Func<JsonElement, List<JsonElement>>? Party);

    private static Route[] Routes(Arranged published) =>
    [
        new("followed page", Followed(published.Token), body => PartyOf(body)),
        new("followed list", LiveList(published.Token), body => PartyOf(
            body.GetProperty("trips").EnumerateArray()
                .Single(t => t.GetProperty("tripLogId").GetGuid() == published.Followed))),
        new("past list", PastList(published.Token), null),
        new("past track", PastTrack(published.Token, published.Finished), body => PartyOf(body)),
    ];

    private static List<JsonElement> PartyOf(JsonElement holder) =>
        [.. holder.GetProperty("participants").EnumerateArray()
            .OrderBy(p => p.GetProperty("ordinal").GetInt32())];

    private static IEnumerable<string?> Labels(IEnumerable<JsonElement> party) =>
        party.Select(p => p.GetProperty("label").GetString()).ToList();

    private static List<string> Members(JsonElement value)
    {
        value.ValueKind.ShouldBe(JsonValueKind.Object);
        return [.. value.EnumerateObject().Select(p => p.Name)];
    }

    private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement.Clone();

    /// <summary>The answer as the text that crossed the wire; anything but a 200 fails here.</summary>
    private static async Task<string> RawAsync(HttpClient client, string address)
    {
        var response = await client.GetAsync(address);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{address}: {text}");
        return text;
    }

    private static void ShouldCarryNoWordOfAName(string body, string where)
    {
        foreach (var name in People)
        {
            ShouldCarryNoWordOf(name, body, where);
        }
    }

    /// <summary>
    /// Neither the name nor either word of it, in any letter case: a first name alone identifies
    /// somebody to the people who know the club, and a name folded into an address or a file name
    /// would not keep its capitals.
    /// </summary>
    private static void ShouldCarryNoWordOf(string name, string body, string where)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        words.Length.ShouldBe(2);
        foreach (var sought in words.Prepend(name))
        {
            body.Contains(sought, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse($"{where}: \"{sought}\" is in the answer");
        }
    }

    /// <summary>Whether a text holds an identifier, in either of the forms one is written in.</summary>
    private static bool Holds(string text, Guid id) =>
        text.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase)
        || text.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase);

    // ---- building the state through the API --------------------------------------------------

    private static string Followed(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string LiveList(string token) => $"{Followed(token)}/live";

    private static string PastList(string token) => $"{Followed(token)}/past";

    private static string PastTrack(string token, Guid trip) => $"{Followed(token)}/past/{trip}";

    private sealed record Person(Guid Caver, string Name);

    /// <param name="Token">The link of the trip that is being followed; every read goes through it.</param>
    /// <param name="Followed">The trip whose watch is running.</param>
    /// <param name="Finished">The trip of the same cave and the same people that is over.</param>
    /// <param name="Roster">The two people, in the order the pages number them.</param>
    private sealed record Arranged(
        string Token, Guid Followed, Guid Finished, Guid Cave, Guid Model, List<Person> Roster);

    /// <summary>
    /// One cave with two published trips of the same two people, both in one camp, each with a
    /// team: one still followed, one over. Read through the followed trip's link, the four routes
    /// then all answer with a body — the page and the followed list about the first trip, the past
    /// list and the past track about the second.
    /// </summary>
    private async Task<Arranged> ArrangeAsync()
    {
        var cave = await CaveAsync();
        var model = await ModelAsync(cave);

        var followed = await TripAsync(
            "Followed outing", People.Select(name => (object)new { newCaverName = name }));
        var roster = await RosterAsync(followed);
        // The roster holds exactly the names searched for, as rows: without this the positive
        // half above could only say that some text was found somewhere.
        roster.Select(p => p.Name).ShouldBe(People, ignoreOrder: true);

        var finished = await TripAsync(
            "Finished outing", roster.Select(p => (object)new { caverId = p.Caver }));
        (await RosterAsync(finished)).ShouldBe(roster);

        var camp = await coordinator.PostAsJsonAsync("/api/v1/expeditions", new
        {
            name = CampName,
            startDate = "2026-09-10",
            endDate = "2026-09-14",
            visibility = "authenticated",
        });
        camp.StatusCode.ShouldBe(HttpStatusCode.Created, await camp.Content.ReadAsStringAsync());
        var campId = (await camp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        string? token = null;
        foreach (var trip in new[] { followed, finished })
        {
            var joined = await coordinator.PostAsJsonAsync(
                $"/api/v1/expeditions/{campId}/trips", new { tripLogId = trip });
            joined.StatusCode.ShouldBe(HttpStatusCode.OK, await joined.Content.ReadAsStringAsync());

            (await PutConfigAsync(trip, new { state = "armed", surveyModelId = model }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            var team = await coordinator.PostAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/tracking/teams", new { title = TeamTitle });
            team.StatusCode.ShouldBe(HttpStatusCode.OK, await team.Content.ReadAsStringAsync());
            var teamId = (await team.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            await ReportAsync(trip, new
            {
                caverIds = new[] { roster[0].Caver }, kind = "atStation", stationName = "cave.upper.2", teamId,
            });
            await ReportAsync(trip, new
            {
                caverIds = new[] { roster[1].Caver }, kind = "atStation", stationName = "cave.deep.3",
            });

            var minted = await coordinator.PostAsync($"/api/v1/trip-logs/{trip}/tracking/shares", null);
            minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
            if (trip == followed)
            {
                token = (await minted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            }
        }

        // The second party is out. With no grace on these hosts that takes it off the followed
        // side and into the cave's past trips at once.
        (await PutConfigAsync(finished, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        return new Arranged(token.ShouldNotBeNull(), followed, finished, cave, model, roster);
    }

    private async Task<Guid> TripAsync(string title, IEnumerable<object> participants)
    {
        var created = await coordinator.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = participants.ToArray(),
            visibility = "authenticated",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// The trip's people with the roster's own name for each, in the order of their rows on the
    /// trip — which is the order the public pages number a party in.
    /// </summary>
    private async Task<List<Person>> RosterAsync(Guid trip)
    {
        using var scope = naming.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await (
            from participant in db.TripLogParticipants.AsNoTracking()
            join caver in db.Cavers.AsNoTracking() on participant.CaverId equals caver.Id
            where participant.TripLogId == trip
            orderby participant.Id
            select new { caver.Id, caver.FullName }).ToListAsync();
        rows.Count.ShouldBe(People.Length);
        return [.. rows.Select(r => new Person(r.Id, r.FullName))];
    }

    private async Task ReportAsync(Guid trip, object body)
    {
        var reported = await coordinator.PostAsJsonAsync($"/api/v1/trip-logs/{trip}/tracking/events", body);
        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CaveAsync()
    {
        var response = await coordinator.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Limestone hollow {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            // An open cave: a cave whose position is kept back is never published at all, and the
            // routes would refuse before there was a body to search.
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A survey created the real way, then marked ready with stations seeded straight into the
    /// graph tables — the part a worker would do from a real file, which this host has none of.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "outing.3d");
        var created = await coordinator.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = naming.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 31700;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground),
            Station(modelId, "cave.deep.3", "cave.deep", 230, SurveyStationFlags.Underground));
        await db.SaveChangesAsync();
        return modelId;
    }

    private static SurveyStation Station(
        Guid modelId, string name, string survey, double z, SurveyStationFlags flags) =>
        new()
        {
            SurveyModelId = modelId,
            Name = name,
            SurveyName = survey,
            Position = new Point(new CoordinateZ(25.5, 45.5, z)) { SRID = 4326 },
            Flags = flags,
        };

    private async Task<HttpResponseMessage> PutConfigAsync(Guid trip, object body)
    {
        var current = await coordinator.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await coordinator.SendAsync(request);
    }
}
