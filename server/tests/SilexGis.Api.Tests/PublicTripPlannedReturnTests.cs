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
/// The hour a party planned to be out by, as the four published-trip routes tell it to somebody
/// without an account — which, on an installation as it is installed, is not at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these hold down.</b> An installation that has not switched the setting on tells nobody
/// the hour, whatever the trip records: the member is there and empty on the two reads about a
/// trip being followed, and absent from the two about finished trips. Switched on, those same two
/// reads carry the one instant and nothing else of the trip's overdue check — not its alarm hour,
/// not where the check stands — under any name or as any value, on any of the four.
/// </para>
/// <para>
/// <b>Every "nothing is told" here is read beside a "something is told".</b> Two hosts stand over
/// one database, one as installed and one with the setting on, and are asked about the same trip
/// in the same instant; so an empty member on the first is the setting's doing and not a trip that
/// had nothing to tell, and a search that finds no alarm hour is the same search that found the
/// planned one.
/// </para>
/// <para>
/// The clock is the test's and stands still, so two hosts asked a moment apart sign their
/// addresses in the same stretch of time and their validators differ only by what they answer.
/// </para>
/// </remarks>
public sealed class PublicTripPlannedReturnTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string PublishKey = "TripTracking:PublishExpectedReturn";

    /// <summary>No hour on the followed page and none on the followed list.</summary>
    private static readonly (DateTimeOffset? Follow, DateTimeOffset? Live) Untold = (null, null);

    private readonly SilexGisApiFactory asInstalled;
    private readonly SilexGisApiFactory publishing;
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient visitor = null!;
    private HttpClient visitorOfPublishing = null!;
    private long caveTypeId;

    public PublicTripPlannedReturnTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"tripplan-{Guid.NewGuid():N}");
        // The first host is given no word about the setting at all: what it answers is what an
        // installation answers that never heard of it.
        asInstalled = HostWith();
        publishing = HostWith((PublishKey, "true"));
    }

    /// <summary>
    /// A host over this class's database, file store, signing keys and clock, with the given
    /// settings on top.
    /// </summary>
    private SilexGisApiFactory HostWith(params (string Key, string Value)[] settings)
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
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(asInstalled, GlobalRoles.Editor, $"plan-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(asInstalled, $"plan-own-{suffix}@t.local");
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

    /// <summary>
    /// <b>The load-bearing one.</b> Both trips of the cave record a plan and a whole overdue check.
    /// As installed, no route tells the hour; with the setting on, the two reads about the trip
    /// being followed tell exactly that hour, the two about finished trips still tell nothing, and
    /// on neither host does any route carry the alarm hour or where the check stands.
    /// </summary>
    [Fact]
    public async Task The_planned_hour_is_told_only_where_the_installation_publishes_it_and_nothing_else_of_the_overdue_check_is_told_anywhere()
    {
        var cave = await PublishedCaveAsync();
        var followed = await PlanAsync(cave.Now.Trip, TimeSpan.FromHours(6), TripCalloutState.Overdue);
        var finished = await PlanAsync(cave.Then.Trip, TimeSpan.FromHours(5), TripCalloutState.StoodDown);

        // ---- as installed ------------------------------------------------------------------
        var off = new Dictionary<string, Answer>();
        foreach (var route in cave.Routes)
        {
            off[route] = await ReadAsync(visitor, route);
            off[route].Status.ShouldBe(HttpStatusCode.OK, route);
            ShouldTellNothingOfTheOverdueCheck(off[route], route, followed, finished);
            // Not the planned hour either, under any name: this installation publishes none.
            InstantsOf(off[route]).ShouldNotContain(followed.PlannedAt, route);
            InstantsOf(off[route]).ShouldNotContain(finished.PlannedAt, route);
        }

        // Present and empty where the member lives, so a page reads "no hour" and not "an older
        // server"; and not a member of a finished trip's shapes at all.
        var offEnvelope = Json(off[cave.Follow]);
        offEnvelope.GetProperty("expectedReturnAt").ValueKind.ShouldBe(JsonValueKind.Null);
        var offRow = Json(off[cave.Live]).GetProperty("trips").EnumerateArray().ShouldHaveSingleItem();
        offRow.GetProperty("tripLogId").GetGuid().ShouldBe(cave.Now.Trip);
        offRow.GetProperty("expectedReturnAt").ValueKind.ShouldBe(JsonValueKind.Null);
        NamesIn(off[cave.Past]).ShouldNotContain("expectedReturnAt");
        NamesIn(off[cave.PastTrip]).ShouldNotContain("expectedReturnAt");

        // ---- the same trips, the same instant, an installation that publishes it ------------
        var on = new Dictionary<string, Answer>();
        foreach (var route in cave.Routes)
        {
            on[route] = await ReadAsync(visitorOfPublishing, route);
            on[route].Status.ShouldBe(HttpStatusCode.OK, route);
            ShouldTellNothingOfTheOverdueCheck(on[route], route, followed, finished);
        }

        var onEnvelope = Json(on[cave.Follow]);
        onEnvelope.GetProperty("expectedReturnAt").GetDateTimeOffset().ShouldBe(followed.PlannedAt);
        var onRow = Json(on[cave.Live]).GetProperty("trips").EnumerateArray().ShouldHaveSingleItem();
        onRow.GetProperty("expectedReturnAt").GetDateTimeOffset().ShouldBe(followed.PlannedAt);
        // The search by value that every "tells nothing" line above and below rests on, shown to
        // find an hour that is in an answer: were it to stop reading instants the way they are
        // written, these two fail before any of the absences can pass for the wrong reason.
        InstantsOf(on[cave.Follow]).ShouldContain(followed.PlannedAt);
        InstantsOf(on[cave.Live]).ShouldContain(followed.PlannedAt);
        // A finished trip's plan is told to nobody, published or not: the two reads about trips
        // that are over have no such member and carry the hour under no other.
        foreach (var route in new[] { cave.Past, cave.PastTrip })
        {
            NamesIn(on[route]).ShouldNotContain("expectedReturnAt", route);
            InstantsOf(on[route]).ShouldNotContain(finished.PlannedAt, route);
            InstantsOf(on[route]).ShouldNotContain(followed.PlannedAt, route);
        }

        // ---- and that is the whole of the difference -----------------------------------------
        // The followed list hands out no signed address, so its two answers can be compared as
        // text: put the empty member back and the answer is byte for byte the installed one.
        var told = onRow.GetProperty("expectedReturnAt").GetRawText();
        on[cave.Live].Body.ShouldContain($"\"expectedReturnAt\":{told}");
        on[cave.Live].Body.Replace($"\"expectedReturnAt\":{told}", "\"expectedReturnAt\":null")
            .ShouldBe(off[cave.Live].Body);
        // The followed page signs its survey's address afresh on every read, so it is compared
        // member by member with the addresses left out.
        Comparable(onEnvelope, "expectedReturnAt").ShouldBe(Comparable(offEnvelope, "expectedReturnAt"));
        on[cave.Past].Body.ShouldBe(off[cave.Past].Body);

        // ---- a reader holding the installed answer is not told "nothing changed" -------------
        foreach (var route in new[] { cave.Follow, cave.Live })
        {
            on[route].Tag.ShouldNotBe(off[route].Tag, route);
            (await ReadAsync(visitor, route, off[route].Tag)).Status.ShouldBe(HttpStatusCode.NotModified, route);
            var asked = await ReadAsync(visitorOfPublishing, route, off[route].Tag);
            asked.Status.ShouldBe(HttpStatusCode.OK, route);
            asked.Body.ShouldNotBeEmpty();
        }

        // Where the setting changes nothing it changes no validator: the reads about finished
        // trips are the same answer on both.
        foreach (var route in new[] { cave.Past, cave.PastTrip })
        {
            on[route].Tag.ShouldBe(off[route].Tag, route);
            (await ReadAsync(visitorOfPublishing, route, off[route].Tag))
                .Status.ShouldBe(HttpStatusCode.NotModified, route);
        }
    }

    /// <summary>
    /// On an installation that publishes the hour, a plan that is not later than the moment the
    /// watch was started is told to nobody — it belongs to an earlier outing of the same record —
    /// and neither is a trip that records none. The same trip with an hour after the start is told,
    /// and a reader holding the answer from before is sent the new one.
    /// </summary>
    [Fact]
    public async Task A_plan_from_before_the_watch_was_started_is_not_told_and_neither_is_an_absent_one()
    {
        var cave = await PublishedCaveAsync();

        // No plan at all, and the member is empty on both reads.
        (await PlannedOnAsync(cave)).ShouldBe(Untold);

        // An hour before the watch was started; then exactly the moment it was.
        await PlanAsync(cave.Now.Trip, TimeSpan.FromHours(-1), TripCalloutState.Armed);
        (await PlannedOnAsync(cave)).ShouldBe(Untold);
        await PlanAsync(cave.Now.Trip, TimeSpan.Zero, TripCalloutState.Armed);
        (await PlannedOnAsync(cave)).ShouldBe(Untold);
        var held = (
            Follow: await ReadAsync(visitorOfPublishing, cave.Follow),
            Live: await ReadAsync(visitorOfPublishing, cave.Live));
        (await ReadAsync(visitorOfPublishing, cave.Live, held.Live.Tag)).Status.ShouldBe(HttpStatusCode.NotModified);

        // The same trip, the same host, an hour that is after the start: told on both.
        var later = await PlanAsync(cave.Now.Trip, TimeSpan.FromMinutes(90), TripCalloutState.Armed);
        (await PlannedOnAsync(cave)).ShouldBe((later.PlannedAt, later.PlannedAt));
        (await ReadAsync(visitorOfPublishing, cave.Follow, held.Follow.Tag)).Status.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(visitorOfPublishing, cave.Live, held.Live.Tag)).Status.ShouldBe(HttpStatusCode.OK);

        // And the installation beside it, asked about that same trip, still says nothing.
        Json(await ReadAsync(visitor, cave.Follow)).GetProperty("expectedReturnAt")
            .ValueKind.ShouldBe(JsonValueKind.Null);
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

    private static JsonElement Json(Answer answer) => JsonDocument.Parse(answer.Body).RootElement;

    /// <summary>The planned hour as the publishing host's followed page and followed list tell it.</summary>
    private async Task<(DateTimeOffset? Follow, DateTimeOffset? Live)> PlannedOnAsync(PublishedCave cave)
    {
        static DateTimeOffset? Read(JsonElement holder)
        {
            var member = holder.GetProperty("expectedReturnAt");
            return member.ValueKind == JsonValueKind.Null ? null : member.GetDateTimeOffset();
        }

        var follow = await ReadAsync(visitorOfPublishing, cave.Follow);
        follow.Status.ShouldBe(HttpStatusCode.OK);
        var live = await ReadAsync(visitorOfPublishing, cave.Live);
        live.Status.ShouldBe(HttpStatusCode.OK);
        var row = Json(live).GetProperty("trips").EnumerateArray()
            .Single(trip => trip.GetProperty("tripLogId").GetGuid() == cave.Now.Trip);
        return (Read(Json(follow)), Read(row));
    }

    /// <summary>
    /// Nothing of either trip's overdue check, by name or by value: no member anywhere in the
    /// answer whose name begins with "callout", no instant equal to an alarm hour, and no word
    /// that is where a check stands.
    /// </summary>
    private static void ShouldTellNothingOfTheOverdueCheck(Answer answer, string route, params Plan[] plans)
    {
        NamesIn(answer).Where(name => name.StartsWith("callout", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty(route);
        answer.Body.Contains("callout", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(route);

        var instants = InstantsOf(answer);
        var words = StringsIn(Json(answer)).ToList();
        foreach (var plan in plans)
        {
            instants.ShouldNotContain(plan.AlarmAt, route);
            words.ShouldNotContain(
                word => string.Equals(word, plan.State.ToString(), StringComparison.OrdinalIgnoreCase), route);
        }
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

    private static IEnumerable<string> StringsIn(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => node.EnumerateObject().SelectMany(m => StringsIn(m.Value)),
        JsonValueKind.Array => node.EnumerateArray().SelectMany(StringsIn),
        JsonValueKind.String => [node.GetString()!],
        _ => [],
    };

    /// <summary>
    /// Every value in the answer that reads as an instant, as the instant it names — so that an
    /// hour is found whatever member carries it and however its zone is written.
    /// </summary>
    private static List<DateTimeOffset> InstantsOf(Answer answer)
    {
        static IEnumerable<JsonElement> Walk(JsonElement node) => node.ValueKind switch
        {
            JsonValueKind.Object => node.EnumerateObject().SelectMany(m => Walk(m.Value)),
            JsonValueKind.Array => node.EnumerateArray().SelectMany(Walk),
            JsonValueKind.String => [node],
            _ => [],
        };

        return Walk(Json(answer))
            .Where(text => text.GetString()!.Contains('T') && text.TryGetDateTimeOffset(out _))
            .Select(text => text.GetDateTimeOffset())
            .ToList();
    }

    /// <summary>
    /// The followed page's answer as text that two reads of one state share: one member left out,
    /// and every address that is signed afresh on each read replaced by a mark.
    /// </summary>
    private static string Comparable(JsonElement envelope, string without)
    {
        static string Of(JsonElement node, string? without) => node.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(",", node.EnumerateObject()
                .Where(m => m.Name != without)
                .Select(m => $"\"{m.Name}\":{Of(m.Value, null)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(",", node.EnumerateArray().Select(e => Of(e, null))) + "]",
            JsonValueKind.String when node.GetString()!.Contains("token=", StringComparison.Ordinal) => "\"signed\"",
            _ => node.GetRawText(),
        };

        return Of(envelope, without);
    }

    // ---- seeding -----------------------------------------------------------------------------

    /// <summary>What a trip was made to record: its plan, its alarm and where its check stands.</summary>
    private sealed record Plan(DateTimeOffset PlannedAt, DateTimeOffset AlarmAt, TripCalloutState State);

    /// <summary>
    /// Gives the trip a plan <paramref name="afterStart"/> from the moment its watch was started,
    /// an alarm two hours and some odd minutes after that, and a check standing where asked.
    /// </summary>
    /// <remarks>
    /// Written to the trip's row rather than through the request that arranges a check, because
    /// that request cannot put a check into the states a sweep or a person later moves it to, and
    /// it is exactly a check that has gone off that must not be readable here.
    /// </remarks>
    private async Task<Plan> PlanAsync(Guid trip, TimeSpan afterStart, TripCalloutState state)
    {
        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var armedAt = await db.TripTrackings.Where(t => t.TripLogId == trip).Select(t => t.ArmedAt).SingleAsync();
        armedAt.ShouldNotBeNull();
        var row = await db.TripLogs.SingleAsync(t => t.Id == trip);
        var plan = new Plan(
            armedAt.Value + afterStart,
            armedAt.Value + afterStart + new TimeSpan(2, 13, 7),
            state);
        row.ExpectedReturnAt = plan.PlannedAt;
        row.CalloutAlarmAt = plan.AlarmAt;
        row.CalloutState = plan.State;
        await db.SaveChangesAsync();
        return plan;
    }

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private sealed record Published(Guid Trip, Guid Caver, string Token);

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

    private async Task<PublishedCave> PublishedCaveAsync()
    {
        var cave = await CaveAsync();
        var model = await ModelAsync(cave);
        var then = await PublishedTripAsync("Last week", model);
        await CloseAsync(then.Trip, clock.Now.AddDays(-5));
        var now = await PublishedTripAsync("Tonight", model);
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
        return new Published(trip, caver, link.GetProperty("token").GetString()!);
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
            name = $"Plan Cave {Guid.NewGuid():N}"[..30],
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
    /// out a signed address — then stations seeded straight into the graph tables.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "tripplan.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = asInstalled.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
        model.Status = SurveyModelStatus.Ready;
        model.SourceEpsg = 31700;
        db.SurveyStations.AddRange(
            Station(modelId, "cave.ent.0", "cave.ent", 350, SurveyStationFlags.Entrance),
            Station(modelId, "cave.upper.2", "cave.upper", 300, SurveyStationFlags.Underground));
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
