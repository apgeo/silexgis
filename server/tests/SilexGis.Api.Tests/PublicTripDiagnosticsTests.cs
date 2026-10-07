// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using SilexGis.Api.Features.TripTracking;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the installation tells its operator about the published-trip surface: why a read was
/// refused, how much is being read, and how long a turned-away reader should wait.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these hold down.</b> Every refusal on the published routes is one 404, on purpose, and
/// other classes hold that it stays one. These hold the other half: that the same refusals are
/// told apart where only the operator reads — one reason each, attached to the link's short handle
/// and never to its token — and that none of it leaks back into an answer.
/// </para>
/// <para>
/// Every absence asserted here stands beside the presence it could be confused with: no refusal
/// logged for a served read is asserted of a host that has just logged refusals; a token found in
/// no event is asserted of events that carry that same link's handle.
/// </para>
/// <para>
/// States the API will not move a published trip into — a link past its own end, a watch closed
/// long ago, a watch switched off — are written as the plain recorded facts they are. Protection is
/// switched on through the write service that maintains the derived columns, never by writing the
/// column, which would leave the effective flag stale and the test passing either way.
/// </para>
/// </remarks>
public sealed class PublicTripDiagnosticsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly LogCapture logs = new();
    private readonly string filesRoot;
    private readonly string connectionString;

    private HttpClient owner = null!;
    private HttpClient anonymous = null!;
    private long caveTypeId;

    public PublicTripDiagnosticsTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(AppContext.BaseDirectory, "test-data", $"tripdiag-{Guid.NewGuid():N}");
        factory = HostWith(logs);
    }

    private Dictionary<string, string?> HostSettings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    /// <summary>
    /// A host over this class's database and file store that writes what it logs into the given
    /// capture, configured as asked — how one link is read under two installations' settings.
    /// </summary>
    private SilexGisApiFactory HostWith(LogCapture capture, params (string Key, string Value)[] settings)
    {
        var host = HostSettings();
        foreach (var (key, value) in settings) host[key] = value;
        return new SilexGisApiFactory(connectionString, host, services =>
        {
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<ILogEventSink>(capture);
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"diag-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"diag-own-{suffix}@t.local");
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
    /// <b>The load-bearing one.</b> Six links that all answer the same 404, and the log says of
    /// each which of six different things it was — by the link's stored handle, and with the token
    /// in nothing that was logged.
    /// </summary>
    [Fact]
    public async Task Each_refused_read_is_logged_once_with_its_own_reason_and_the_links_stored_handle()
    {
        using var meter = new Readings(factory.Services);
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);

        var revoked = await PublishedTripAsync("Taken back", model);
        (await owner.DeleteAsync($"{Shares(revoked.Trip)}/{revoked.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var expired = await PublishedTripAsync("Run out", model);
        await ExpireAsync(expired.ShareId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var closed = await PublishedTripAsync("Over", model);
        await CloseAsync(closed.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        var stoodDown = await PublishedTripAsync("Stood down", model);
        await SetStateAsync(stoodDown.Trip, TripTrackingState.Off);

        // A cave of its own, so that guarding it withholds this link and none of the four above.
        var guardedCave = await CaveAsync(locationProtected: false);
        var withheld = await PublishedTripAsync("Guarded since", await ModelAsync(guardedCave));
        (await anonymous.GetAsync(Follow(withheld.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SetLocationProtectedAsync(guardedCave, true);

        const string invented = "not-a-token-at-all";

        // The followed page: one answer, six reasons.
        await ShouldBeRefusedAsync(anonymous, logs, Follow(revoked.Token), "follow", "revoked", await HandleAsync(revoked));
        await ShouldBeRefusedAsync(anonymous, logs, Follow(expired.Token), "follow", "expired", await HandleAsync(expired));
        await ShouldBeRefusedAsync(anonymous, logs, Follow(closed.Token), "follow", "closed_past_grace", await HandleAsync(closed));
        await ShouldBeRefusedAsync(anonymous, logs, Follow(stoodDown.Token), "follow", "watch_off", await HandleAsync(stoodDown));
        await ShouldBeRefusedAsync(anonymous, logs, Follow(withheld.Token), "follow", "cave_withheld", await HandleAsync(withheld));
        await ShouldBeRefusedAsync(anonymous, logs, Follow(invented), "follow", "unknown_link", HandleOf(invented));

        // The two lists share a gate that asks about both of a link's windows, so they name what
        // shut both — and the route's own word comes with it.
        await ShouldBeRefusedAsync(anonymous, logs, LiveList(revoked.Token), "live", "revoked", await HandleAsync(revoked));
        await ShouldBeRefusedAsync(anonymous, logs, PastList(expired.Token), "past", "expired", await HandleAsync(expired));
        await ShouldBeRefusedAsync(anonymous, logs, LiveList(withheld.Token), "live", "cave_withheld", await HandleAsync(withheld));
        await ShouldBeRefusedAsync(anonymous, logs, PastList(invented), "past", "unknown_link", HandleOf(invented));

        // A link that is good, asking for a past trip that is not one it may read.
        await ShouldBeRefusedAsync(
            anonymous, logs, $"{PastList(closed.Token)}/{Guid.CreateVersion7()}",
            "past_trip", "trip_not_in_archive", await HandleAsync(closed));

        // The counters moved with the events, one read each, under the same two words.
        meter.Reads("follow", "revoked").ShouldBe(1);
        meter.Reads("follow", "expired").ShouldBe(1);
        meter.Reads("follow", "closed_past_grace").ShouldBe(1);
        meter.Reads("follow", "watch_off").ShouldBe(1);
        meter.Reads("follow", "cave_withheld").ShouldBe(1);
        meter.Reads("follow", "unknown_link").ShouldBe(1);
        meter.Reads("live", "revoked").ShouldBe(1);
        meter.Reads("past_trip", "trip_not_in_archive").ShouldBe(1);
        // The one served read above — the link that was open before its cave was guarded.
        meter.Reads("follow", PublicTripDiagnostics.ServedOutcome).ShouldBe(1);
        meter.Limited().ShouldBe(0);

        // Nothing this host logged, in its message or in any property, carries a token. Asserted
        // of events that name these very links by handle, so it is not the emptiness of a capture
        // that never heard them.
        logs.Events.Count(IsRefusal).ShouldBe(11);
        foreach (var each in new[] { revoked, expired, closed, stoodDown, withheld })
        {
            ShouldNotCarry(logs, each.Token);
        }

        ShouldNotCarry(logs, invented);
    }

    /// <summary>
    /// A read that is answered logs no refusal and is counted as served — on a host that does log
    /// refusals, so the silence is not that of a host with nothing wired.
    /// </summary>
    [Fact]
    public async Task A_served_read_logs_no_refusal_and_is_counted_as_served()
    {
        using var meter = new Readings(factory.Services);
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var running = await PublishedTripAsync("Underground", model);
        var over = await PublishedTripAsync("Out", model);
        await CloseAsync(over.Trip, DateTimeOffset.UtcNow.AddDays(-5));

        logs.Clear();
        (await anonymous.GetAsync(Follow(running.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(LiveList(running.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(PastList(running.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"{PastList(running.Token)}/{over.Trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // A link whose own trip is over is refused by the followed page and served by both lists:
        // the watch being over is what makes a trip history, not a reason to shut its archive.
        (await anonymous.GetAsync(LiveList(over.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(PastList(over.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        logs.Events.Where(IsRefusal).ShouldBeEmpty();
        meter.Reads("follow", PublicTripDiagnostics.ServedOutcome).ShouldBe(1);
        meter.Reads("live", PublicTripDiagnostics.ServedOutcome).ShouldBe(2);
        meter.Reads("past", PublicTripDiagnostics.ServedOutcome).ShouldBe(2);
        meter.Reads("past_trip", PublicTripDiagnostics.ServedOutcome).ShouldBe(1);
        meter.Reads().ShouldBe(6);

        // And the same host does say so when it refuses.
        await ShouldBeRefusedAsync(
            anonymous, logs, Follow(over.Token), "follow", "closed_past_grace", await HandleAsync(over));
        ShouldNotCarry(logs, running.Token);
        ShouldNotCarry(logs, over.Token);

        // Every route under the published-trip window names itself, each by a word of its own:
        // a route added to the window without one would be counted under a word nobody chose.
        var words = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == "public-trip")
            .Select(e => e.Metadata.GetMetadata<PublicTripRoute>()?.Word)
            .ToList();
        words.ShouldBe(["follow", "live", "past", "past_trip"], ignoreOrder: true);
    }

    /// <summary>
    /// The archive's two settings, named when they are what refused — the switch on the archive's
    /// own routes whatever the link, because there the link is never looked up.
    /// </summary>
    [Fact]
    public async Task The_archive_switched_off_and_the_retention_passed_are_each_named_as_what_refused()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var old = await PublishedTripAsync("Long over", model, tripDate: "2026-01-04");
        await CloseAsync(old.Trip, DateTimeOffset.UtcNow.AddDays(-5));
        var running = await PublishedTripAsync("Underground", model);
        var revoked = await PublishedTripAsync("Taken back", model);
        (await owner.DeleteAsync($"{Shares(revoked.Trip)}/{revoked.ShareId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // As this class's own host is configured, the old link opens both lists.
        (await anonymous.GetAsync(PastList(old.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(LiveList(old.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var offLogs = new LogCapture();
        using (var off = HostWith(offLogs, ("TripPastTracks:Enabled", "false")))
        using (var visitor = off.CreateClient())
        {
            await ShouldBeRefusedAsync(visitor, offLogs, PastList(old.Token), "past", "archive_off", await HandleAsync(old));
            await ShouldBeRefusedAsync(visitor, offLogs, LiveList(old.Token), "live", "archive_off", await HandleAsync(old));
            await ShouldBeRefusedAsync(
                visitor, offLogs, $"{PastList(old.Token)}/{old.Trip}", "past_trip", "archive_off", await HandleAsync(old));
            // A running watch's link loses the archive and keeps its party.
            await ShouldBeRefusedAsync(
                visitor, offLogs, PastList(running.Token), "past", "archive_off", await HandleAsync(running));
            offLogs.Clear();
            (await visitor.GetAsync(LiveList(running.Token))).StatusCode.ShouldBe(HttpStatusCode.OK);
            offLogs.Events.Where(IsRefusal).ShouldBeEmpty();

            // The archive's own routes answer from the setting and never look the link up, so the
            // switch is the reason there for a link taken back and for an invented one too: what
            // the link itself is, is not known to a read that asked the database nothing.
            await ShouldBeRefusedAsync(
                visitor, offLogs, PastList(revoked.Token), "past", "archive_off", await HandleAsync(revoked));
            await ShouldBeRefusedAsync(
                visitor, offLogs, PastList("not-a-token-at-all"), "past", "archive_off", HandleOf("not-a-token-at-all"));
            // The same two links on the routes that do look them up are named for what they are,
            // on the same installation — which is where an operator reads a link's own state.
            await ShouldBeRefusedAsync(
                visitor, offLogs, LiveList(revoked.Token), "live", "revoked", await HandleAsync(revoked));
            await ShouldBeRefusedAsync(
                visitor, offLogs, Follow("not-a-token-at-all"), "follow", "unknown_link", HandleOf("not-a-token-at-all"));
        }

        var boundedLogs = new LogCapture();
        using (var bounded = HostWith(boundedLogs, ("TripPastTracks:Retention", "30.00:00:00")))
        using (var visitor = bounded.CreateClient())
        {
            await ShouldBeRefusedAsync(
                visitor, boundedLogs, PastList(old.Token), "past", "past_retention", await HandleAsync(old));
            await ShouldBeRefusedAsync(
                visitor, boundedLogs, LiveList(old.Token), "live", "past_retention", await HandleAsync(old));
            // The followed page never asked about the archive: its reason is the watch.
            await ShouldBeRefusedAsync(
                visitor, boundedLogs, Follow(old.Token), "follow", "closed_past_grace", await HandleAsync(old));
        }
    }

    /// <summary>
    /// A reader turned away by the per-address window is told how long to wait and is counted, and
    /// the refusal's body is the problem document it always was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is tightened to three on a host of its own, as the test that proves the window
    /// exists does, because the test server presents every client as one address and a tight
    /// window on the class's host would starve its other tests.
    /// </para>
    /// <para>
    /// What is not proved: that the window re-opens after the stated wait. The limiter keeps its
    /// own time and takes no clock a test could move, so that would be a test that sleeps a minute.
    /// The number is held to the window's length instead.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_read_past_the_window_is_told_how_long_to_wait_and_is_counted_and_keeps_its_body()
    {
        var cave = await CaveAsync(locationProtected: false);
        var model = await ModelAsync(cave);
        var published = await PublishedTripAsync("Watched", model);
        var playback = $"{PastList(published.Token)}/{Guid.CreateVersion7()}";

        var tightLogs = new LogCapture();
        using var tight = HostWith(
            tightLogs,
            ("TripTracking:PublicRateLimitPerMinute", "3"),
            ("Qr:RateLimitPerMinute", "1"));
        using var meter = new Readings(tight.Services);
        using var visitor = tight.CreateClient();

        foreach (var address in new[] { Follow(published.Token), LiveList(published.Token), PastList(published.Token) })
        {
            var served = await visitor.GetAsync(address);
            served.StatusCode.ShouldBe(HttpStatusCode.OK);
            // Only a refusal says when to come back.
            served.Headers.Contains("Retry-After").ShouldBeFalse(address.Replace(published.Token, "…"));
        }

        var refused = await visitor.GetAsync(playback);
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, await refused.Content.ReadAsStringAsync());

        // Whole seconds, and no longer than the window itself.
        refused.Headers.TryGetValues("Retry-After", out var waits).ShouldBeTrue();
        var wait = waits!.ShouldHaveSingleItem();
        int.TryParse(wait, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds).ShouldBeTrue(wait);
        seconds.ShouldBeInRange(1, 60);

        // The body is still the limiter's problem document, with no code — a header was added and
        // nothing was written in its place.
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().ShouldBe(429);
        body.TryGetProperty("code", out _).ShouldBeFalse();

        // Counted as turned away on the route it was aimed at, and not as a read: it reached no
        // route, so there was nothing to serve or refuse and nothing to log.
        meter.Limited("past_trip").ShouldBe(1);
        meter.Limited().ShouldBe(1);
        meter.Reads().ShouldBe(3);
        tightLogs.Events.Where(IsRefusal).ShouldBeEmpty();
        ShouldNotCarry(tightLogs, published.Token);

        // The other windows of the application are left as they were: a scan turned away by the
        // printed-code window is told nothing about waiting and is not counted here.
        HttpResponseMessage? scan = null;
        for (var i = 0; i < 5 && scan?.StatusCode != HttpStatusCode.TooManyRequests; i++)
        {
            scan = await visitor.GetAsync($"/api/v1/public/qr/probe{i}");
        }

        scan!.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        scan.Headers.Contains("Retry-After").ShouldBeFalse();
        meter.Limited().ShouldBe(1);
    }

    // ---- what was logged ---------------------------------------------------------------------

    private static bool IsRefusal(LogEvent e) =>
        e.MessageTemplate.Text == PublicTripDiagnostics.RefusedMessage;

    /// <summary>
    /// Reads one address, expects the surface's single 404, and expects exactly one refusal event
    /// for it: at Information, naming this route, this reason and this handle, and nothing else.
    /// </summary>
    private static async Task ShouldBeRefusedAsync(
        HttpClient client, LogCapture capture, string address, string route, string reason, string handle)
    {
        var before = capture.Events.Count(IsRefusal);
        var response = await client.GetAsync(address);
        var body = await response.Content.ReadAsStringAsync();
        var what = $"{route} / {reason}";

        // The answer is the one every refusal here gives, whatever the log goes on to say.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, what);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()
            .ShouldBe("tracking.share_not_found", what);
        body.ShouldNotContain(reason, Case.Insensitive, what);
        response.Headers.Contains("Retry-After").ShouldBeFalse(what);

        var refusals = capture.Events.Where(IsRefusal).ToList();
        refusals.Count.ShouldBe(before + 1, what);
        var logged = refusals[^1];
        logged.Level.ShouldBe(LogEventLevel.Information, what);
        Text(logged, "Route").ShouldBe(route, what);
        Text(logged, "Reason").ShouldBe(reason, what);
        Text(logged, "LinkHandle").ShouldBe(handle, what);
        logged.RenderMessage().ShouldContain(handle, Case.Sensitive, what);
    }

    private static string? Text(LogEvent e, string property) =>
        e.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;

    /// <summary>
    /// The token is in nothing this capture holds: no rendered message, no template, no property
    /// under any name, no exception.
    /// </summary>
    private static void ShouldNotCarry(LogCapture capture, string token)
    {
        var events = capture.Events;
        events.ShouldNotBeEmpty();
        foreach (var e in events)
        {
            e.RenderMessage().ShouldNotContain(token);
            e.MessageTemplate.Text.ShouldNotContain(token);
            foreach (var (name, value) in e.Properties)
            {
                value.ToString().ShouldNotContain(token, Case.Sensitive, name);
            }

            (e.Exception?.ToString() ?? string.Empty).ShouldNotContain(token);
        }
    }

    /// <summary>The handle of a link as its row stores it: the first characters of the stored hash.</summary>
    private async Task<string> HandleAsync(Published link)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.TripTrackingShares.AsNoTracking()
            .Where(s => s.Id == link.ShareId).Select(s => s.TokenHash).SingleAsync();
        // The same length the administrator's list of published links shows, so a line in the log
        // and a row on that page name one link the same way.
        return stored[..8];
    }

    /// <summary>The handle of a token no row stores, worked out the way a row would store it.</summary>
    private static string HandleOf(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..8];

    /// <summary>
    /// What one application's published-trip counters were moved by, heard through the framework's
    /// own listener.
    /// </summary>
    /// <remarks>
    /// Narrowed to the meter of the given application, not merely to the meter's name: several
    /// applications run in this process at once, each with a meter of that name, and a listener
    /// keyed on the name alone would add another class's reads into this one's counts.
    /// </remarks>
    private sealed class Readings : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<(string Instrument, long Value, string? Route, string? Outcome)> heard = [];

        public Readings(IServiceProvider application)
        {
            var meters = application.GetRequiredService<IMeterFactory>();
            listener.InstrumentPublished = (instrument, subscribe) =>
            {
                if (instrument.Meter.Name == PublicTripDiagnostics.MeterName
                    && ReferenceEquals(instrument.Meter.Scope, meters))
                {
                    subscribe.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                string? route = null, outcome = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == PublicTripDiagnostics.RouteTag) route = tag.Value as string;
                    if (tag.Key == PublicTripDiagnostics.OutcomeTag) outcome = tag.Value as string;
                }

                lock (heard) heard.Add((instrument.Name, value, route, outcome));
            });
            listener.Start();
        }

        public long Reads(string? route = null, string? outcome = null)
        {
            lock (heard)
            {
                return heard
                    .Where(h => h.Instrument == PublicTripDiagnostics.ReadsCounterName
                        && (route is null || h.Route == route)
                        && (outcome is null || h.Outcome == outcome))
                    .Sum(h => h.Value);
            }
        }

        public long Limited(string? route = null)
        {
            lock (heard)
            {
                return heard
                    .Where(h => h.Instrument == PublicTripDiagnostics.LimitedCounterName
                        && (route is null || h.Route == route))
                    .Sum(h => h.Value);
            }
        }

        public void Dispose() => listener.Dispose();
    }

    // ---- addresses ---------------------------------------------------------------------------

    private static string Shares(Guid trip) => $"/api/v1/trip-logs/{trip}/tracking/shares";

    private static string Follow(string token) =>
        $"/api/v1/public/trips/{Uri.EscapeDataString(token)}";

    private static string LiveList(string token) => $"{Follow(token)}/live";

    private static string PastList(string token) => $"{Follow(token)}/past";

    // ---- seeding -----------------------------------------------------------------------------

    private sealed record Published(Guid Trip, Guid ShareId, string Token);

    /// <summary>A trip armed on a survey model — and so on that model's cave — one caver placed, and published.</summary>
    private async Task<Published> PublishedTripAsync(
        string title, Guid model, string tripDate = "2026-09-12")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate,
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
                .Select(p => p.CaverId).SingleAsync();
        }

        (await PutConfigAsync(owner, trip, new { state = "armed", surveyModelId = model }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/tracking/events",
                new { caverIds = new[] { caver }, kind = "atStation", stationName = "cave.upper.2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var minted = await owner.PostAsync(Shares(trip), null);
        minted.StatusCode.ShouldBe(HttpStatusCode.Created, await minted.Content.ReadAsStringAsync());
        var link = JsonDocument.Parse(await minted.Content.ReadAsStringAsync()).RootElement;
        return new Published(trip, link.GetProperty("id").GetGuid(), link.GetProperty("token").GetString()!);
    }

    /// <summary>Closes the watch through the API, then backdates the instant it closed at.</summary>
    private async Task CloseAsync(Guid trip, DateTimeOffset closedAt)
    {
        (await PutConfigAsync(owner, trip, new { state = "closed" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.ClosedAt = closedAt;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Backdates one link's own end — the part of the window no request can reach, since an expiry
    /// is fixed when the link is minted. A plain recorded fact, read against the clock on every
    /// request, so a row written this way is exactly a link that ran out then.
    /// </summary>
    private async Task ExpireAsync(Guid shareId, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var share = await db.TripTrackingShares.SingleAsync(s => s.Id == shareId);
        share.ExpiresAt = at;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Writes a watch's state directly, for the one state the API will not move a published watch
    /// into. A plain recorded fact with nothing derived from it.
    /// </summary>
    private async Task SetStateAsync(Guid trip, TripTrackingState state)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tracking = await db.TripTrackings.SingleAsync(t => t.TripLogId == trip);
        tracking.State = state;
        await db.SaveChangesAsync();
    }

    private async Task SetLocationProtectedAsync(Guid caveFeatureId, bool value)
    {
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        await writer.SetLocationProtectedAsync(caveFeatureId, value);
        await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
    }

    private async Task<Guid> CaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Diag Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A survey model created the real way, then stations seeded straight into the graph tables.</summary>
    private async Task<Guid> ModelAsync(Guid caveId)
    {
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new("application/octet-stream");
        form.Add(bytes, "file", "tripdiag.3d");
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

    private static async Task<HttpResponseMessage> PutConfigAsync(HttpClient client, Guid trip, object body)
    {
        var current = await client.GetAsync($"/api/v1/trip-logs/{trip}/tracking");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/trip-logs/{trip}/tracking")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", current.Headers.ETag!.ToString());
        return await client.SendAsync(request);
    }
}
