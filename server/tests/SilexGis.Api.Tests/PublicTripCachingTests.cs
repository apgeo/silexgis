// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.TripTracking;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a reader of the four published-trip routes may keep, and what it is told when it asks
/// again holding what it kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these hold down.</b> A reader that already holds the answer is told "unchanged" and is
/// sent nothing — and that saying is never a way round a refusal: a link taken back, or a cave
/// protected since, answers "not found" to a reader holding a perfectly good validator exactly as
/// it does to one holding nothing. Each refusal test first shows the same request being answered
/// "unchanged", so the refusal that follows is the route's and not a mistyped header's.
/// </para>
/// <para>
/// <b>The clock is the test's.</b> An answer that hands out a signed address is marked with the
/// stretch of time it was made in, so two reads a moment apart could fall either side of a
/// boundary on the machine's clock. Here the host's clock stands still unless a test moves it.
/// </para>
/// <para>
/// Protection is switched on through the write service that maintains the derived columns, never by
/// writing the column, which would leave what the routes actually read stale.
/// </para>
/// </remarks>
public sealed class PublicTripCachingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly TestTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public PublicTripCachingTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"tripcache-{Guid.NewGuid():N}");
        factory = HostWith();
    }

    /// <summary>
    /// A host over this class's database, file store and clock, with the given settings on top.
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
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cache-own-{suffix}@t.local");
        // Signed in while this host's clock and the machine's still agree; the clock then moves by
        // minutes at most, well inside a session.
        owner = await AuthHelper.BearerClientAsync(factory, $"cache-own-{suffix}@t.local");
        anonymous = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        try { Directory.Delete(filesRoot, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// <b>The load-bearing one.</b> On each of the four routes the answer says the reader may keep
    /// it and must ask before using it, a reader holding it is told so and sent nothing, and a
    /// reader holding something else is sent the answer.
    /// </summary>
    [Fact]
    public async Task A_reader_who_already_holds_the_answer_is_told_so_and_sent_nothing_on_every_route()
    {
        var cave = await PublishedCaveAsync();

        foreach (var route in cave.Routes)
        {
            var first = await ReadAsync(anonymous, route);
            first.Status.ShouldBe(HttpStatusCode.OK, route);
            first.Tag.ShouldNotBeNull(route);
            first.Tag.ShouldStartWith("W/\"");
            first.Kept.ShouldBe(MayKeepAndMustAsk, route);
            first.Body.ShouldNotBeEmpty();

            var again = await ReadAsync(anonymous, route, first.Tag);
            again.Status.ShouldBe(HttpStatusCode.NotModified, route);
            again.Body.ShouldBeEmpty();
            // Told again what it holds and on what terms, as an answer sent whole would say.
            again.Tag.ShouldBe(first.Tag);
            again.Kept.ShouldBe(MayKeepAndMustAsk, route);

            var holdingSomethingElse = await ReadAsync(anonymous, route, "W/\"00000000000000000000000000000000\"");
            holdingSomethingElse.Status.ShouldBe(HttpStatusCode.OK, route);
            holdingSomethingElse.Tag.ShouldBe(first.Tag);
            holdingSomethingElse.Body.ShouldNotBeEmpty();
        }

        // The followed page's answer hands out the survey at an address signed afresh on every
        // read, so the two answers above differed byte for byte and were still one answer: without
        // that, "unchanged" would never be said on the route that is read most.
        var one = await ReadAsync(anonymous, cave.Follow);
        var two = await ReadAsync(anonymous, cave.Follow);
        var addressOne = ModelUrlOf(one);
        addressOne.ShouldContain("token=");
        ModelUrlOf(two).ShouldNotBe(addressOne);
        two.Tag.ShouldBe(one.Tag);
    }

    /// <summary>
    /// What is reported about the party reaches a reader holding the answer from before it: the
    /// routes that carry the report get a new validator, and the one that does not keeps its own.
    /// </summary>
    [Fact]
    public async Task A_new_report_gives_the_routes_that_carry_it_a_new_validator_and_leaves_the_other_alone()
    {
        var cave = await PublishedCaveAsync();
        var follow = await ReadAsync(anonymous, cave.Follow);
        var live = await ReadAsync(anonymous, cave.Live);
        var past = await ReadAsync(anonymous, cave.Past);
        (await ReadAsync(anonymous, cave.Live, live.Tag)).Status.ShouldBe(HttpStatusCode.NotModified);

        // A second later, so the new report is the later one on any reading of the log.
        clock.Now += TimeSpan.FromSeconds(1);
        await ReportAsync(cave.Now.Trip, cave.Now.Caver, "cave.deep.3");

        var liveAfter = await ReadAsync(anonymous, cave.Live, live.Tag);
        liveAfter.Status.ShouldBe(HttpStatusCode.OK);
        liveAfter.Tag.ShouldNotBe(live.Tag);
        liveAfter.Body.ShouldContain("cave.deep.3");
        var followAfter = await ReadAsync(anonymous, cave.Follow, follow.Tag);
        followAfter.Status.ShouldBe(HttpStatusCode.OK);
        followAfter.Tag.ShouldNotBe(follow.Tag);
        followAfter.Body.ShouldContain("cave.deep.3");

        // The new answer is in turn the one a reader holds.
        (await ReadAsync(anonymous, cave.Live, liveAfter.Tag)).Status.ShouldBe(HttpStatusCode.NotModified);
        // The cave's past trips say nothing about tonight's party, and did not change.
        var pastAfter = await ReadAsync(anonymous, cave.Past, past.Tag);
        pastAfter.Status.ShouldBe(HttpStatusCode.NotModified);
        pastAfter.Tag.ShouldBe(past.Tag);
    }

    /// <summary>
    /// A link taken back is not found, on all four routes, to a reader holding the validator of
    /// the answer it was given a moment before.
    /// </summary>
    [Fact]
    public async Task A_link_taken_back_is_not_found_to_a_reader_holding_its_validator()
    {
        var cave = await PublishedCaveAsync();
        var held = await HeldAndConfirmedAsync(cave);

        (await owner.DeleteAsync($"{Shares(cave.Now.Trip)}/{cave.Now.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeRefusedWhateverTheReaderHoldsAsync(cave, held);
    }

    /// <summary>
    /// A cave whose position was protected after the reader got its copy is not found, on all four
    /// routes, to a reader holding the validator of that copy.
    /// </summary>
    [Fact]
    public async Task A_cave_protected_since_is_not_found_to_a_reader_holding_a_validator_from_before()
    {
        var cave = await PublishedCaveAsync();
        var held = await HeldAndConfirmedAsync(cave);

        await SetLocationProtectedAsync(cave.Cave, true);

        await ShouldBeRefusedWhateverTheReaderHoldsAsync(cave, held);
    }

    /// <summary>
    /// The validator matches in the forms it comes back in: made weak or strong by a cache, with
    /// the coding a compressing proxy applied appended, in a list, or as "whatever you have".
    /// </summary>
    [Fact]
    public async Task The_validator_matches_in_the_forms_a_proxy_or_a_cache_hands_it_back_in()
    {
        var cave = await PublishedCaveAsync();
        var first = await ReadAsync(anonymous, cave.Live);
        first.Tag.ShouldNotBeNull();
        first.Tag.ShouldStartWith("W/\"");
        var value = first.Tag![3..^1];

        string[] same =
        [
            $"\"{value}\"",
            $"W/\"{value}-gzip\"",
            $"\"{value}-br\"",
            $"W/\"{value}-zstd\"",
            $"\"00000000\", W/\"{value}\"",
            "*",
        ];
        foreach (var form in same)
        {
            (await ReadAsync(anonymous, cave.Live, form)).Status.ShouldBe(HttpStatusCode.NotModified, form);
        }

        string[] different = [$"W/\"{value}0\"", $"\"{value[..^1]}\"", "\"00000000\", \"11111111\"", "\"-gzip\""];
        foreach (var form in different)
        {
            (await ReadAsync(anonymous, cave.Live, form)).Status.ShouldBe(HttpStatusCode.OK, form);
        }
    }

    /// <summary>
    /// A kept answer never outlives the signed addresses in it: once the stretch of time it was
    /// made in is over, a reader holding it is sent the answer whole, with fresh addresses. An
    /// answer with no such address has nothing that runs out and is still "unchanged".
    /// </summary>
    [Fact]
    public async Task An_answer_that_hands_out_a_signed_address_is_sent_whole_again_once_its_stretch_of_time_is_over()
    {
        var cave = await PublishedCaveAsync();
        var follow = await ReadAsync(anonymous, cave.Follow);
        var track = await ReadAsync(anonymous, cave.PastTrip);
        var live = await ReadAsync(anonymous, cave.Live);
        var past = await ReadAsync(anonymous, cave.Past);
        ModelUrlOf(follow).ShouldContain("token=");
        ModelUrlOf(track).ShouldContain("token=");
        live.Body.ShouldNotContain("token=");
        past.Body.ShouldNotContain("token=");
        (await ReadAsync(anonymous, cave.Follow, follow.Tag)).Status.ShouldBe(HttpStatusCode.NotModified);
        (await ReadAsync(anonymous, cave.PastTrip, track.Tag)).Status.ShouldBe(HttpStatusCode.NotModified);

        clock.Now += PublicTripValidator.SignedAddressBucket;

        var followLater = await ReadAsync(anonymous, cave.Follow, follow.Tag);
        followLater.Status.ShouldBe(HttpStatusCode.OK);
        followLater.Tag.ShouldNotBe(follow.Tag);
        ModelUrlOf(followLater).ShouldNotBe(ModelUrlOf(follow));
        var trackLater = await ReadAsync(anonymous, cave.PastTrip, track.Tag);
        trackLater.Status.ShouldBe(HttpStatusCode.OK);
        trackLater.Tag.ShouldNotBe(track.Tag);

        var liveLater = await ReadAsync(anonymous, cave.Live, live.Tag);
        liveLater.Status.ShouldBe(HttpStatusCode.NotModified);
        liveLater.Tag.ShouldBe(live.Tag);
        (await ReadAsync(anonymous, cave.Past, past.Tag)).Status.ShouldBe(HttpStatusCode.NotModified);
    }

    /// <summary>
    /// A read the per-address window turns away is kept by nobody and carries no validator, like
    /// every other refusal here — and is turned away whatever the reader says it holds.
    /// </summary>
    [Fact]
    public async Task A_read_turned_away_by_the_window_is_kept_by_nobody()
    {
        var cave = await PublishedCaveAsync();

        using var tight = HostWith(("TripTracking:PublicRateLimitPerMinute", "1"));
        using var reader = tight.CreateClient();
        var answered = await ReadAsync(reader, cave.Live);
        answered.Status.ShouldBe(HttpStatusCode.OK);
        answered.Kept.ShouldBe(MayKeepAndMustAsk);

        var refused = await ReadAsync(reader, cave.Live, answered.Tag);
        refused.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Kept.ShouldBe(KeptByNobody);
        refused.Tag.ShouldBeNull();
    }

    // ---- what a reader sees ------------------------------------------------------------------

    /// <summary><c>private, no-cache</c>, with the directives in the order <see cref="KeptOf"/> puts them.</summary>
    private const string MayKeepAndMustAsk = "no-cache, private";

    private const string KeptByNobody = "no-store";

    private sealed record Answer(HttpStatusCode Status, string? Tag, string? Kept, string Body, string Shape);

    private static async Task<Answer> ReadAsync(HttpClient client, string url, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch).ShouldBeTrue();
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new Answer(
            response.StatusCode,
            response.Headers.ETag?.ToString(),
            KeptOf(response),
            body,
            ShapeOf(response.StatusCode, body));
    }

    /// <summary>
    /// What the answer says about keeping it, as its directives in alphabetical order — so that an
    /// assertion reads the whole of it and does not depend on the order a client spells it in.
    /// </summary>
    private static string? KeptOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Cache-Control", out var lines)
            ? string.Join(", ", lines
                .SelectMany(line => line.Split(','))
                .Select(directive => directive.Trim())
                .Where(directive => directive.Length > 0)
                .OrderBy(directive => directive, StringComparer.Ordinal))
            : null;

    /// <summary>
    /// A refusal as a caller can see it, with the trace identifier left out: that one differs per
    /// request by design, while every other member has to be the same whichever way the read was
    /// refused.
    /// </summary>
    private static string ShapeOf(HttpStatusCode status, string body)
    {
        if (status != HttpStatusCode.NotFound) return $"{(int)status}";
        var members = JsonDocument.Parse(body).RootElement.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");
        return $"{(int)status} {string.Join('&', members)}";
    }

    private static string ModelUrlOf(Answer answer) =>
        JsonDocument.Parse(answer.Body).RootElement.GetProperty("model").GetProperty("modelUrl").GetString()!;

    /// <summary>
    /// The validator a reader holds for each route — each shown to be answered "unchanged", so
    /// that a refusal of the same request afterwards cannot be the header's doing.
    /// </summary>
    private async Task<Dictionary<string, string>> HeldAndConfirmedAsync(PublishedCave cave)
    {
        var held = new Dictionary<string, string>();
        foreach (var route in cave.Routes)
        {
            var first = await ReadAsync(anonymous, route);
            first.Status.ShouldBe(HttpStatusCode.OK, route);
            first.Tag.ShouldNotBeNull(route);
            (await ReadAsync(anonymous, route, first.Tag)).Status.ShouldBe(HttpStatusCode.NotModified, route);
            (await ReadAsync(anonymous, route, "*")).Status.ShouldBe(HttpStatusCode.NotModified, route);
            held[route] = first.Tag!;
        }

        return held;
    }

    /// <summary>
    /// Every route answers what an invented token is answered — status, body, what may be kept,
    /// no validator — to a reader holding nothing, holding the validator of its last answer, and
    /// claiming to hold anything at all.
    /// </summary>
    private async Task ShouldBeRefusedWhateverTheReaderHoldsAsync(
        PublishedCave cave, Dictionary<string, string> held)
    {
        foreach (var route in cave.Routes)
        {
            var invented = await ReadAsync(
                anonymous, route.Replace(Uri.EscapeDataString(cave.Now.Token), "not-a-token-at-all"), held[route]);
            invented.Status.ShouldBe(HttpStatusCode.NotFound, route);
            invented.Kept.ShouldBe(KeptByNobody, route);
            invented.Tag.ShouldBeNull(route);

            foreach (var holding in new[] { null, held[route], "*" })
            {
                var refused = await ReadAsync(anonymous, route, holding);
                refused.Status.ShouldBe(HttpStatusCode.NotFound, $"{route} holding {holding}");
                refused.Shape.ShouldBe(invented.Shape, $"{route} holding {holding}");
                refused.Kept.ShouldBe(KeptByNobody, $"{route} holding {holding}");
                refused.Tag.ShouldBeNull($"{route} holding {holding}");
            }
        }
    }

    // ---- seeding -----------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private sealed record Published(Guid Trip, Guid Caver, Guid ShareId, string Token);

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
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caver = await db.TripLogParticipants.Where(p => p.TripLogId == trip)
                .Select(p => p.CaverId).Distinct().SingleAsync();
        }

        (await PutConfigAsync(trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReportAsync(trip, caver, "cave.upper.2");

        var minted = await owner.PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var link = JsonDocument.Parse(await minted.Content.ReadAsStringAsync()).RootElement;
        return new Published(trip, caver, link.GetProperty("id").GetGuid(), link.GetProperty("token").GetString()!);
    }

    private async Task ReportAsync(Guid trip, Guid caver, string station) =>
        (await owner.PostAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/tracking/events",
            new { caverIds = new[] { caver }, kind = "atStation", stationName = station }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    private async Task CloseAsync(Guid trip, DateTimeOffset closedAt)
    {
        (await PutConfigAsync(trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<Guid> CaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cache Cave {Guid.NewGuid():N}"[..30],
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
        form.Add(bytes, "file", "tripcache.3d");
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var modelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
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
