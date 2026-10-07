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
/// A published trip's whole life read through one link at chosen instants: followed, then history,
/// then gone — and at no instant both, or neither.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim is a partition.</b> While a published trip can be read at all it is on exactly one
/// of two sides: the parties being followed in its cave, or that cave's past trips. A trip on both
/// would be a party still underground whose whole track anybody can replay; a trip on neither is a
/// page that went blank the evening everybody came out. So every step below asks the four public
/// routes at <em>one</em> clock reading and asserts the two lists together — their union is the
/// trip and their intersection is empty — rather than each route on its own, which could not tell
/// a clean handover from a gap or an overlap one request wide.
/// </para>
/// <para>
/// <b>One clock, and it only moves when the test moves it.</b> The host reads an injected clock
/// that stands still between steps, so "the same instant" is literal: the four reads of a step see
/// one <c>now</c>, and an edge can be asked exactly — the last second before it and the instant
/// itself. Nothing is written behind the API to get there: the close is stamped by the request
/// that closes the watch, the link's end is the one the mint answered with, and time passes by the
/// clock alone. A test that backdated the rows instead would be measuring the rows it wrote.
/// </para>
/// <para>
/// The windows are short so the walk fits a day of clock time: a minute of grace after the watch
/// is closed, an hour of link, a day of archive. The trip's day is the clock's own day at the
/// start, because the link's end and the archive's end are both counted from the end of the trip's
/// last day and not from when anything was pressed — a trip dated weeks back would already be past
/// a one-day archive before the walk began.
/// </para>
/// <para>
/// Everything the coordinator does happens before the clock leaves the machine's time by more than
/// a few minutes, because a signed-in session is checked against real time and would not survive
/// the jump. After that every read is a visitor's, which is who these routes are for.
/// </para>
/// </remarks>
public sealed class TripHandoverTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    private const string Refused = "tracking.share_not_found";

    private readonly TestTimeProvider clock;
    private readonly SilexGisApiFactory host;
    private readonly string filesRoot;
    private readonly DateOnly tripDay;

    private HttpClient coordinator = null!;
    private HttpClient visitor = null!;
    private long caveTypeId;

    public TripHandoverTests(PostgresFixture postgres)
    {
        // Started on the machine's time so that signing in works, then frozen: from here on it
        // says what the test last set it to.
        clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        tripDay = DateOnly.FromDateTime(clock.Now.UtcDateTime);

        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"handover-{Guid.NewGuid():N}");
        var settings = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["TripTracking:ShareGraceAfterClose"] = Grace.ToString("c"),
            ["TripTracking:ShareLifetime"] = LinkLifetime.ToString("c"),
            ["TripPastTracks:Retention"] = Retention.ToString("c"),
        };
        host = new SilexGisApiFactory(postgres.ConnectionString, settings, services =>
        {
            // The survey file below is four bytes; a worker reading it would fail the model and
            // rewrite the stations seeded beside it.
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });
    }

    public async Task InitializeAsync()
    {
        var email = $"handover-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        _ = await AuthHelper.CreateUserAsync(host, GlobalRoles.Editor, email);
        coordinator = await AuthHelper.BearerClientAsync(host, email);
        visitor = host.CreateClient();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        host.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// One trip, one link, five crossings: started, closed, out of its grace, past its link's end,
    /// past the archive's end.
    /// </summary>
    /// <remarks>
    /// Each crossing is asked on both sides of its edge where the edge changes an answer. The two
    /// that change nothing a visitor sees are in the walk on purpose: closing a watch does not end
    /// the page, and a link running out does not take a finished trip out of the archive — the
    /// second being the difference between a link's own life and the life of what it once
    /// published.
    /// </remarks>
    [Fact]
    public async Task A_published_trip_is_followed_or_history_and_never_both_until_retention_ends_it()
    {
        var trip = await PublishedTripAsync("Handover walk");

        // The edges the walk steps across, in the order the clock will reach them. The link's end
        // is the one the server answered when the link was made; the archive's end is counted from
        // the end of the trip's last day. If the three were not in this order the steps below
        // would be asserting something other than what their comments say.
        var linkEnds = trip.ExpiresAt;
        var archiveEnds = EndOfDay(tripDay) + Retention;
        linkEnds.ShouldBeLessThan(archiveEnds);

        // 1. The watch is running.
        var started = await SidesAsync(trip.Token, trip.Trip);
        ShouldBeFollowed(started, "armed", trip.Trip);

        // 2. The party is out and the watch is closed — two minutes on, by the clock the host
        //    reads. A close stamped from any other clock would land outside its own one-minute
        //    grace as it was written, and the next reading would already be history.
        clock.Now += TimeSpan.FromMinutes(2);
        (await PutConfigAsync(trip.Trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var justClosed = await SidesAsync(trip.Token, trip.Trip);
        ShouldBeFollowed(justClosed, "closed", trip.Trip);
        var closedAt = justClosed.ClosedAt.ShouldNotBeNull();
        closedAt.ShouldBe(clock.Now, TimeSpan.FromMilliseconds(1));
        var graceEnds = closedAt + Grace;
        graceEnds.ShouldBeLessThan(linkEnds);

        // The last second of the grace: still the page, saying it is over.
        clock.Now = graceEnds - TimeSpan.FromSeconds(1);
        ShouldBeFollowed(await SidesAsync(trip.Token, trip.Trip), "closed", trip.Trip);

        // 3. The instant the grace ends: off the page and in the archive, with nothing between.
        clock.Now = graceEnds;
        ShouldBeHistory(await SidesAsync(trip.Token, trip.Trip), trip.Trip);

        // 4. The link runs out. Nothing a visitor sees changes: the trip was published, it is
        //    over, and it stays readable as a past trip through the link that published it.
        clock.Now = linkEnds - TimeSpan.FromSeconds(1);
        ShouldBeHistory(await SidesAsync(trip.Token, trip.Trip), trip.Trip);
        clock.Now = linkEnds;
        ShouldBeHistory(await SidesAsync(trip.Token, trip.Trip), trip.Trip);

        // 5. The archive's last second ...
        clock.Now = archiveEnds - TimeSpan.FromSeconds(1);
        ShouldBeHistory(await SidesAsync(trip.Token, trip.Trip), trip.Trip);

        // ... and its end. This is the only step at which the trip is on neither side, and the
        // link no longer opens anything: all four routes refuse it the way they refuse a token
        // nobody ever issued. The reading one second earlier, on the same token, is what makes
        // these four refusals mean "retention ended" rather than "the address was wrong".
        clock.Now = archiveEnds;
        ShouldBeRefusedEverywhere(await SidesAsync(trip.Token, trip.Trip));
    }

    /// <summary>
    /// <b>The one recorded exception to the partition.</b> A party whose watch is still running
    /// when its only link runs out is on neither side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately so, from both ends. It is not followed, because a link's end is the promise
    /// made to the people who were on the trip and a watch somebody forgot to close must not keep
    /// their page open past it. And it is not history, because nobody has said the party is out:
    /// the archive is anonymous and replayable, and a watch that is still running describes people
    /// who may be underground now. The way back in is a person's act — closing the watch — never
    /// the passing of time.
    /// </para>
    /// <para>
    /// Told apart from "the links of this cave stopped working" by a second trip of the same cave
    /// that was closed in time. Both links are made on the same day with the same lifetime, so
    /// they run out at the same instant; the only difference between the two trips is the state of
    /// the watch. At a reading before that instant each is on exactly one side; at the instant,
    /// the closed one is still history and the running one is nowhere.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_party_left_followed_while_its_only_link_runs_out_is_on_neither_side()
    {
        var cave = await CaveAsync();
        var model = await ModelAsync(cave);
        var forgotten = await PublishedTripAsync("Watch left running", cave, model);
        var finished = await PublishedTripAsync("Watch closed in time", cave, model);

        // The same end for both links, so the step below takes both across it at once.
        forgotten.ExpiresAt.ShouldBe(finished.ExpiresAt);
        var linksEnd = forgotten.ExpiresAt;
        var both = new[] { forgotten.Trip, finished.Trip };

        // One of the two parties comes out and its watch is closed; the other's is left running.
        clock.Now += TimeSpan.FromMinutes(2);
        (await PutConfigAsync(finished.Trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Before: the finished trip is out of its grace and both links still work. Whichever link
        // is asked, the cave's two published trips are split one to a side.
        clock.Now += TimeSpan.FromMinutes(2);
        clock.Now.ShouldBeLessThan(linksEnd);

        var throughForgotten = await SidesAsync(forgotten.Token, forgotten.Trip);
        ShouldSplit(throughForgotten, followed: [forgotten.Trip], history: [finished.Trip], all: both);
        throughForgotten.Envelope.ShouldBe(HttpStatusCode.OK);
        throughForgotten.State.ShouldBe("armed");
        throughForgotten.Track.ShouldBe(HttpStatusCode.NotFound);

        var throughFinished = await SidesAsync(finished.Token, finished.Trip);
        ShouldSplit(throughFinished, followed: [forgotten.Trip], history: [finished.Trip], all: both);
        throughFinished.Envelope.ShouldBe(HttpStatusCode.NotFound);
        throughFinished.Track.ShouldBe(HttpStatusCode.OK);

        // The instant both links run out, a day short of the archive's end.
        clock.Now = linksEnd;
        clock.Now.ShouldBeLessThan(EndOfDay(tripDay) + Retention);

        // The running watch's own link opens nothing at all.
        ShouldBeRefusedEverywhere(await SidesAsync(forgotten.Token, forgotten.Trip));

        // And it is not merely unreachable through its own link: asked through the other trip's
        // link, which still opens this cave's archive, it is on neither list and has no track to
        // play — while the trip that was closed is exactly where the first reading left it.
        var after = await SidesAsync(finished.Token, finished.Trip);
        after.LiveList.ShouldBe(HttpStatusCode.OK);
        after.PastList.ShouldBe(HttpStatusCode.OK);
        after.Live.ShouldBeEmpty();
        after.Past.ShouldBe(new[] { finished.Trip });
        after.Track.ShouldBe(HttpStatusCode.OK);
        after.Live.Concat(after.Past).ShouldNotContain(forgotten.Trip);
        (await visitor.GetAsync(PastTrack(finished.Token, forgotten.Trip)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- what one link answers at one clock reading ------------------------------------------

    /// <summary>
    /// The four public answers to one link, taken without the clock moving between them.
    /// </summary>
    /// <param name="Envelope">The followed page's status.</param>
    /// <param name="State">The watch's state as the followed page gives it; null when refused.</param>
    /// <param name="ClosedAt">The instant the followed page says the watch closed at, if any.</param>
    /// <param name="LiveList">The status of the list of parties being followed in the cave.</param>
    /// <param name="Live">The trips on that list; empty when it was refused.</param>
    /// <param name="LiveStates">The state each of those rows carries.</param>
    /// <param name="PastList">The status of the cave's list of past trips.</param>
    /// <param name="Past">The trips on that list; empty when it was refused.</param>
    /// <param name="Track">The status of the asked trip's past track.</param>
    /// <param name="Codes">The refusal code of every route that refused.</param>
    private sealed record Sides(
        HttpStatusCode Envelope,
        string? State,
        DateTimeOffset? ClosedAt,
        HttpStatusCode LiveList,
        List<Guid> Live,
        Dictionary<Guid, string?> LiveStates,
        HttpStatusCode PastList,
        List<Guid> Past,
        HttpStatusCode Track,
        List<string?> Codes);

    private async Task<Sides> SidesAsync(string token, Guid trip)
    {
        var reading = clock.Now;
        var codes = new List<string?>();

        var (envelopeStatus, envelope) = await ReadAsync(Followed(token), codes);
        var (liveStatus, live) = await ReadAsync(LiveList(token), codes);
        var (pastStatus, past) = await ReadAsync(PastList(token), codes);
        var (trackStatus, _) = await ReadAsync(PastTrack(token, trip), codes);

        // The whole point of the helper: had the clock moved under it, the four answers would be
        // about four instants and a gap or an overlap between them could hide.
        clock.Now.ShouldBe(reading);

        var closedAt = envelope is { } e && e.GetProperty("closedAt").ValueKind != JsonValueKind.Null
            ? e.GetProperty("closedAt").GetDateTimeOffset()
            : (DateTimeOffset?)null;

        // Read row by row rather than into a dictionary keyed by trip, which would throw on the
        // very thing the partition assertion is there to report: a trip listed twice.
        var liveIds = new List<Guid>();
        var liveStates = new Dictionary<Guid, string?>();
        if (live is { } liveBody)
        {
            foreach (var row in liveBody.GetProperty("trips").EnumerateArray())
            {
                var id = row.GetProperty("tripLogId").GetGuid();
                liveIds.Add(id);
                liveStates[id] = row.GetProperty("state").GetString();
            }
        }

        var pastIds = new List<Guid>();
        if (past is { } pastBody)
        {
            pastIds.AddRange(pastBody.GetProperty("trips").EnumerateArray()
                .Select(row => row.GetProperty("tripLogId").GetGuid()));
        }

        return new Sides(
            envelopeStatus,
            envelope?.GetProperty("state").GetString(),
            closedAt,
            liveStatus,
            liveIds,
            liveStates,
            pastStatus,
            pastIds,
            trackStatus,
            codes);
    }

    /// <summary>
    /// One read: its status, its body when it answered, and its refusal code when it did not. Any
    /// status other than the two this surface gives is a failure here rather than at the caller.
    /// </summary>
    private async Task<(HttpStatusCode Status, JsonElement? Body)> ReadAsync(string address, List<string?> codes)
    {
        var response = await visitor.GetAsync(address);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, text);
            codes.Add(JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
            return (HttpStatusCode.NotFound, null);
        }

        return (HttpStatusCode.OK, JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>
    /// The partition itself: both lists answer, every published trip of the cave is on one of them,
    /// and none is on both.
    /// </summary>
    private static void ShouldSplit(Sides at, Guid[] followed, Guid[] history, Guid[] all)
    {
        at.LiveList.ShouldBe(HttpStatusCode.OK);
        at.PastList.ShouldBe(HttpStatusCode.OK);

        // Lists, not sets: a trip listed twice on its own side is as wrong as one listed on both.
        at.Live.Concat(at.Past).ShouldBe(all, ignoreOrder: true);
        at.Live.Intersect(at.Past).ShouldBeEmpty();

        at.Live.ShouldBe(followed, ignoreOrder: true);
        at.Past.ShouldBe(history, ignoreOrder: true);
    }

    /// <summary>
    /// On the followed side only, and the page agrees: it opens, in the state the list row gives,
    /// and there is no past track of it to play.
    /// </summary>
    private static void ShouldBeFollowed(Sides at, string state, Guid trip)
    {
        ShouldSplit(at, followed: [trip], history: [], all: [trip]);
        at.Envelope.ShouldBe(HttpStatusCode.OK);
        at.State.ShouldBe(state);
        at.LiveStates[trip].ShouldBe(state);
        at.Track.ShouldBe(HttpStatusCode.NotFound);
        at.Codes.ShouldBe(new string?[] { Refused });
    }

    /// <summary>
    /// On the past side only, and the page agrees: it no longer opens, and the track plays.
    /// </summary>
    private static void ShouldBeHistory(Sides at, Guid trip)
    {
        ShouldSplit(at, followed: [], history: [trip], all: [trip]);
        at.Envelope.ShouldBe(HttpStatusCode.NotFound);
        at.Track.ShouldBe(HttpStatusCode.OK);
        at.Codes.ShouldBe(new string?[] { Refused });
    }

    /// <summary>All four routes refuse, with the one code this surface refuses with.</summary>
    private static void ShouldBeRefusedEverywhere(Sides at)
    {
        at.Envelope.ShouldBe(HttpStatusCode.NotFound);
        at.LiveList.ShouldBe(HttpStatusCode.NotFound);
        at.PastList.ShouldBe(HttpStatusCode.NotFound);
        at.Track.ShouldBe(HttpStatusCode.NotFound);
        at.Codes.ShouldBe(new string?[] { Refused, Refused, Refused, Refused });
    }

    /// <summary>
    /// Midnight UTC after a day: the instant a trip dated that day counts as over, which is where
    /// both a link's lifetime and the archive's retention are counted from.
    /// </summary>
    private static DateTimeOffset EndOfDay(DateOnly day) =>
        new(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    // ---- building the state through the API --------------------------------------------------

    private static string Followed(string token) => $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string LiveList(string token) => $"{Followed(token)}/live";

    private static string PastList(string token) => $"{Followed(token)}/past";

    private static string PastTrack(string token, Guid trip) => $"{Followed(token)}/past/{trip}";

    private sealed record Published(Guid Trip, string Token, DateTimeOffset ExpiresAt);

    /// <summary>
    /// A trip dated the clock's own day, its watch started on a survey of its cave, one caver
    /// placed, and a link made for it — all through the routes a coordinator uses.
    /// </summary>
    private async Task<Published> PublishedTripAsync(string title, Guid? cave = null, Guid? model = null)
    {
        var caveId = cave ?? await CaveAsync();
        var modelId = model ?? await ModelAsync(caveId);

        var created = await coordinator.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = tripDay.ToString("yyyy-MM-dd"),
            participants = new[] { new { newCaverName = $"Guest {Guid.NewGuid():N}"[..24] } },
            visibility = "authenticated",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var trip = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Guid caver;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caver = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
                .Select(p => p.CaverId).SingleAsync();
        }

        (await PutConfigAsync(trip, new { state = "armed", surveyModelId = modelId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var reported = await coordinator.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/events",
            new { caverIds = new[] { caver }, kind = "atStation", stationName = "cave.upper.2" });
        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync());

        var minted = await coordinator.PostAsync($"/api/v1/trip-logs/{trip}/tracking/shares", null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var link = await minted.Content.ReadFromJsonAsync<JsonElement>();
        return new Published(
            trip, link.GetProperty("token").GetString()!, link.GetProperty("expiresAt").GetDateTimeOffset());
    }

    private async Task<Guid> CaveAsync()
    {
        var response = await coordinator.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Handover Cave {Guid.NewGuid():N}"[..30],
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
    /// A survey created the real way, then marked ready with stations seeded straight into the
    /// graph tables — the part a worker would do from a real file, which this host has none of.
    /// </summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "handover.3d");
        var created = await coordinator.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = host.Services.CreateScope();
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
