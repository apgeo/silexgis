// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a filtered trip listing adds up to: the figures the insights page draws.
/// <para>
/// One property is asserted over and over, because it is the only one that matters: a figure on
/// this answer and the rows the listing hands back under the same filter agree, <em>for a caller
/// who may read only part of the archive</em>. A chart that totals more than the list would show
/// is a statement about rows the reader was not shown, and it is the one sentence an application
/// that hides rows must never say by accident.
/// </para>
/// <para>
/// The withheld side is always a Viewer. The seeded Editors group reads past visibility by
/// design, so an Editor seeing nothing would prove nothing.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripStatsTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;  // Editor — creates everything
    private HttpClient reader = null!; // Viewer — sees only what its audience admits
    private Guid ownerUserId;
    private long surveyTypeId;
    private long explorationTypeId;
    private long participantRoleId;
    private long leaderRoleId;

    public TripStatsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ts-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ts-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            surveyTypeId = await db.TripTypes.Where(t => t.Code == "survey").Select(t => t.Id).FirstAsync();
            explorationTypeId = await db.TripTypes.Where(t => t.Code == "exploration").Select(t => t.Id).FirstAsync();
            participantRoleId = await db.TripParticipantRoles
                .Where(r => r.Code == "participant").Select(r => r.Id).FirstAsync();
            leaderRoleId = await db.TripParticipantRoles
                .Where(r => r.Code == "leader").Select(r => r.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ts-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"ts-read-{suffix}@t.local");
    }

    /// <summary>
    /// The one this whole surface is judged on. Two audiences over the same trips, and every
    /// figure the page would draw is checked against the rows the same caller's list hands back
    /// under the same filter.
    /// </summary>
    [Fact]
    public async Task Every_figure_agrees_with_the_list_under_the_same_filter_for_a_caller_who_cannot_see_everything()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Open survey one {marker}", surveyTypeId, "authenticated", tripDate: "2024-03-02");
        await CreateTripAsync($"Open survey two {marker}", surveyTypeId, "authenticated", tripDate: "2025-05-06");
        await CreateTripAsync($"Open survey three {marker}", surveyTypeId, "authenticated", tripDate: "2025-07-08");
        await CreateTripAsync($"Shut survey one {marker}", surveyTypeId, "private", tripDate: "2025-09-10");
        await CreateTripAsync($"Shut survey two {marker}", surveyTypeId, "private", tripDate: "2025-11-12");
        await CreateTripAsync($"Open dig {marker}", explorationTypeId, "authenticated", tripDate: "2024-04-04");

        // Fixture proof, in both directions: the withheld trips really are withheld from this
        // reader and really do exist, so smaller figures below are the audience walk and not a
        // fixture that never wrote them.
        var readerTotal = (await ListAsync(reader, $"search={marker}")).GetProperty("totalItems").GetInt32();
        var ownerTotal = (await ListAsync(owner, $"search={marker}")).GetProperty("totalItems").GetInt32();
        readerTotal.ShouldBe(4);
        ownerTotal.ShouldBe(6);

        var readerStats = await StatsAsync(reader, $"search={marker}");
        var ownerStats = await StatsAsync(owner, $"search={marker}");

        // The headline the page prints. Both halves are about the caller, so the fraction never
        // implies the filter is hiding what access is hiding.
        readerStats.GetProperty("matching").GetInt32().ShouldBe(readerTotal);
        ownerStats.GetProperty("matching").GetInt32().ShouldBe(ownerTotal);
        readerStats.GetProperty("overall").GetInt32()
            .ShouldBeLessThan(ownerStats.GetProperty("overall").GetInt32());

        // What the trips were for. The two callers get different numbers for the same type and
        // both are right — and each number is what that type really hands that caller.
        var readerSurvey = ValueCount(readerStats, "types", surveyTypeId.ToString());
        var ownerSurvey = ValueCount(ownerStats, "types", surveyTypeId.ToString());
        readerSurvey.ShouldBe(3);
        ownerSurvey.ShouldBe(5);
        (await ListAsync(reader, $"search={marker}&types={surveyTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(readerSurvey);
        (await ListAsync(owner, $"search={marker}&types={surveyTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(ownerSurvey);

        // The year series, against the same window the list takes. The withheld trips are all in
        // one year, so a year series taken past the audience walk would show up right here.
        YearTrips(readerStats, 2024).ShouldBe(2);
        YearTrips(readerStats, 2025).ShouldBe(2);
        YearTrips(ownerStats, 2025).ShouldBe(4);
        (await ListAsync(reader, $"search={marker}&from=2025-01-01&to=2025-12-31"))
            .GetProperty("totalItems").GetInt32().ShouldBe(YearTrips(readerStats, 2025));
        (await ListAsync(owner, $"search={marker}&from=2025-01-01&to=2025-12-31"))
            .GetProperty("totalItems").GetInt32().ShouldBe(YearTrips(ownerStats, 2025));

        // Every year of the span is a row, so a chart draws a flat stretch rather than closing a
        // quiet year up. The trips sum to the matching figure either way.
        Years(readerStats).Select(y => y.GetProperty("year").GetInt32()).ShouldBe([2024, 2025]);
        Years(readerStats).Sum(y => y.GetProperty("trips").GetInt32()).ShouldBe(readerTotal);
        Years(ownerStats).Sum(y => y.GetProperty("trips").GetInt32()).ShouldBe(ownerTotal);
    }

    /// <summary>
    /// A roster row is one person doing one job, so a leader who also surveyed is two rows and
    /// one person who went once.
    /// </summary>
    [Fact]
    public async Task Somebody_who_did_two_jobs_on_one_trip_is_one_person_who_went_once()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var tripId = await CreateTripAsync(
            $"Two jobs {marker}", surveyTypeId, "authenticated", people: [("Ana Doi", participantRoleId)]);
        var anaId = await SoleCaverIdAsync(tripId);
        await AddRoleAsync(tripId, anaId, leaderRoleId);

        // Fixture proof: Ana really does hold two jobs on the one trip, so a count of one below
        // is the roster being reduced to people rather than the second job never being written.
        (await RosterRowCountAsync(tripId)).ShouldBe(2);

        var stats = await StatsAsync(reader, $"search={marker}");
        ValueCount(stats, "participants", anaId.ToString()).ShouldBe(1);

        // And the number is what the person narrowing hands back on the list.
        (await ListAsync(reader, $"search={marker}&participantIds={anaId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);

        // Who was on a trip is one of the two dimensions a trip counts into more than once, so
        // the answer says so rather than leaving a reader to wonder why the bars overshoot.
        stats.GetProperty("participants").GetProperty("overlapping").GetBoolean().ShouldBeTrue();
        stats.GetProperty("types").GetProperty("overlapping").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// Where the trips went is counted through the containment hierarchy, exactly as the area
    /// narrowing and the area option counts walk it — and an area whose position is guarded is
    /// counted for nobody who may not place it.
    /// </summary>
    [Fact]
    public async Task Areas_are_counted_through_what_they_contain_and_a_guarded_one_is_counted_for_nobody_who_cannot_place_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var massif = await CreateGenericFeatureAsync();
        var subArea = await CreateGenericFeatureAsync(parentId: massif);
        var guarded = await CreateGenericFeatureAsync(locationProtected: true);

        var namedMassif = await CreateTripAsync($"On the massif {marker}", surveyTypeId, "authenticated");
        var namedSub = await CreateTripAsync($"In the valley {marker}", surveyTypeId, "authenticated");
        var namedGuarded = await CreateTripAsync($"Guarded ground {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Nowhere named {marker}", surveyTypeId, "authenticated");
        await NameFeatureAsync(namedMassif, massif, "trip-work-area");
        await NameFeatureAsync(namedSub, subArea, "trip-work-area");
        await NameFeatureAsync(namedGuarded, guarded, "trip-work-area");

        var stats = await StatsAsync(reader, $"search={marker}");

        // A trip that named the sub-area reached the massif above it, so the massif's bar and the
        // page selecting the massif are one question.
        var massifCount = ValueCount(stats, "areas", massif.ToString());
        massifCount.ShouldBe(2);
        ValueCount(stats, "areas", subArea.ToString()).ShouldBe(1);
        (await ListAsync(reader, $"search={marker}&areaIds={massif}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(massifCount);

        // The positive half that makes the refusal mean something: the owner may place the
        // guarded area, is shown it, and is narrowed by it.
        ValueCount(await StatsAsync(owner, $"search={marker}"), "areas", guarded.ToString()).ShouldBe(1);
        (await ListAsync(owner, $"search={marker}&areaIds={guarded}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);

        // …and for the reader it is neither named nor counted, though the trip itself is readable.
        (await ListAsync(reader, $"search={marker}")).GetProperty("totalItems").GetInt32().ShouldBe(4);
        ValueCount(stats, "areas", guarded.ToString()).ShouldBe(0);
        Values(stats, "areas").ShouldNotContain(guarded.ToString());

        // The trips holding nothing the reader may be told about are a value of their own rather
        // than being dropped: two of them here, the one that named nowhere and the one whose
        // ground this reader may not place.
        ValueCount(stats, "areas", string.Empty).ShouldBe(2);
    }

    /// <summary>
    /// The curve the page exists for: how many distinct areas the filtered trips had reached by
    /// the end of each year. Going back to ground already covered adds trips and adds no areas,
    /// which is the difference no other figure on the page states.
    /// </summary>
    [Fact]
    public async Task Cumulative_areas_climb_on_new_ground_and_flatten_on_ground_already_covered()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var first = await CreateGenericFeatureAsync();
        var second = await CreateGenericFeatureAsync();

        var opened = await CreateTripAsync($"First visit {marker}", surveyTypeId, "authenticated", tripDate: "2023-02-03");
        var widened = await CreateTripAsync($"New ground {marker}", surveyTypeId, "authenticated", tripDate: "2024-02-03");
        var revisitOne = await CreateTripAsync($"Back again {marker}", surveyTypeId, "authenticated", tripDate: "2025-02-03");
        var revisitTwo = await CreateTripAsync($"Back once more {marker}", surveyTypeId, "authenticated", tripDate: "2025-06-03");
        await NameFeatureAsync(opened, first, "trip-work-area");
        await NameFeatureAsync(widened, second, "trip-work-area");
        await NameFeatureAsync(revisitOne, first, "trip-work-area");
        await NameFeatureAsync(revisitTwo, second, "trip-work-area");

        var years = Years(await StatsAsync(reader, $"search={marker}"));
        years.Select(y => y.GetProperty("year").GetInt32()).ShouldBe([2023, 2024, 2025]);
        years.Select(y => y.GetProperty("trips").GetInt32()).ShouldBe([1, 1, 2]);
        years.Select(y => y.GetProperty("newAreas").GetInt32()).ShouldBe([1, 1, 0]);

        // The whole point: the busiest year of the three adds nothing to the running total.
        years.Select(y => y.GetProperty("areasSoFar").GetInt32()).ShouldBe([1, 2, 2]);
    }

    /// <summary>
    /// The same words the listing takes, refused with the same code — or a page would draw charts
    /// for a filter the list rejects. And nobody unsigned-in is answered at all.
    /// </summary>
    [Fact]
    public async Task A_word_the_listing_refuses_is_refused_here_with_the_same_code_and_a_stranger_is_not_answered()
    {
        var refused = await reader.GetAsync("/api/v1/trip-logs/stats?types=notanumber");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("trip_log.type_invalid");

        var badState = await reader.GetAsync("/api/v1/trip-logs/stats?states=napping");
        badState.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(badState)).ShouldBe("trip_log.state_invalid");

        // The positive half in the same test: the same route with a word it does take answers.
        (await reader.GetAsync($"/api/v1/trip-logs/stats?states=draft")).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        using var stranger = factory.CreateClient();
        (await stranger.GetAsync("/api/v1/trip-logs/stats")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Naming something the caller may not read answers with silence rather than a refusal, and
    /// the totals go silent with the page: charts drawn beside an empty list would say the rows
    /// are there.
    /// </summary>
    [Fact]
    public async Task A_filter_naming_something_this_caller_may_not_read_totals_nothing()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var trip = await CreateTripAsync($"Down the hole {marker}", surveyTypeId, "authenticated");
        var guardedCave = await CreateCaveAsync($"Guarded {marker}", locationProtected: true);
        await NameFeatureAsync(trip, guardedCave, "trip-visited");

        // Fixture proof: the naming happened, and the owner — who may place the cave — is totalled
        // through that door, so the reader's zero below is the gate and not an empty fixture.
        var ownerStats = await StatsAsync(owner, $"caveId={guardedCave}");
        ownerStats.GetProperty("matching").GetInt32().ShouldBe(1);

        var readerStats = await StatsAsync(reader, $"caveId={guardedCave}");
        readerStats.GetProperty("matching").GetInt32().ShouldBe(0);
        readerStats.GetProperty("overall").GetInt32().ShouldBe(0);
        Years(readerStats).ShouldBeEmpty();
        Values(readerStats, "types").ShouldBeEmpty();

        // …and the list says the same nothing through the same door.
        (await ListAsync(reader, $"caveId={guardedCave}")).GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    private static IReadOnlyList<JsonElement> Years(JsonElement stats) =>
        [.. stats.GetProperty("years").EnumerateArray()];

    private static int YearTrips(JsonElement stats, int year) =>
        Years(stats).Where(y => y.GetProperty("year").GetInt32() == year)
            .Select(y => y.GetProperty("trips").GetInt32())
            .FirstOrDefault();

    private static IReadOnlyList<string> Values(JsonElement stats, string dimension) =>
        [.. stats.GetProperty(dimension).GetProperty("values").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString()!)];

    private static int ValueCount(JsonElement stats, string dimension, string value) =>
        stats.GetProperty(dimension).GetProperty("values").EnumerateArray()
            .Where(x => x.GetProperty("value").GetString() == value)
            .Select(x => x.GetProperty("count").GetInt32())
            .FirstOrDefault();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/?pageSize=200&{query}"));

    private static async Task<JsonElement> StatsAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/stats?{query}"));

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    private async Task<Guid> CreateTripAsync(
        string title,
        long tripTypeId,
        string visibility,
        string tripDate = "2026-04-10",
        (string Name, long RoleId)[]? people = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate,
            tripTypeId,
            visibility,
            caveIds = Array.Empty<Guid>(),
            participants = (people ?? [])
                .Select(p => new { newCaverName = p.Name, roleId = p.RoleId }).ToArray(),
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string name, bool locationProtected = false)
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "public",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>An area feature for the trip role that names one. Written straight in, because
    /// what kind it is matters here and nothing else about it does — as does where it sits, since
    /// the containment chain is what an area narrowing and an area total both walk.</summary>
    private async Task<Guid> CreateGenericFeatureAsync(
        Guid? parentId = null, bool locationProtected = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        var id = Guid.NewGuid();
        var above = parentId is null
            ? []
            : await db.Features.AsNoTracking()
                .Where(f => f.Id == parentId).Select(f => f.AncestorIds).SingleAsync();
        Guid[] ancestors = [id, .. above];
        db.Features.Add(new Feature
        {
            Id = id,
            Name = $"Massif {Guid.NewGuid():N}"[..30],
            Kind = FeatureKind.Generic,
            FeatureTypeId = typeId,
            OwnerUserId = ownerUserId,
            Visibility = Visibility.Public,
            LocationProtected = locationProtected,
            IsProtectedEffective = locationProtected,
            AncestorIds = ancestors,
        });
        foreach (var ancestorId in ancestors)
        {
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = ancestorId });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task NameFeatureAsync(Guid tripId, Guid featureId, string roleCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await TripRoleLinks.NameFeatureAsync(db, tripId, featureId, roleCode, null, default)).ShouldBeTrue();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SoleCaverIdAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripId).Select(p => p.CaverId).Distinct().SingleAsync();
    }

    private async Task AddRoleAsync(Guid tripId, Guid caverId, long roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripLogParticipants.Add(new TripLogParticipant { TripLogId = tripId, CaverId = caverId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    /// <summary>Roster rows, unreduced — the fixture proof a reduced count cannot give.</summary>
    private async Task<int> RosterRowCountAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking().CountAsync(p => p.TripLogId == tripId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
