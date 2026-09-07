// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A camp's own timeline, and what it may say on it. The rows that hang off a camp — which trip
/// joined it, who was there and for which days — are rooted at the camp, so they reach everybody
/// who may read the camp: an audience wider than the trips gathered into it, each governed in its
/// own right, and wider than the people it names, told only to a caller who may read people at
/// all. Both rules were written where the rows are redacted and could until now only be checked
/// there, because the timeline route would not take a camp as its subject; these drive them from
/// the outside, which is the only place the leak would actually have happened.
/// </summary>
public sealed class ExpeditionTimelineTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;

    // A plain reader, and it has to be: the seeded Editors group holds every content domain at
    // the widest scope, so an Editor who "cannot see" a trip proves nothing about visibility.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    public ExpeditionTimelineTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xt-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xt-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xt-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xt-out-{suffix}@t.local");
    }

    /// <summary>
    /// The camp is a history subject at all — the thing the two rules below could not be driven
    /// through, because the route parses its subject against the shared entity vocabulary and
    /// then asks whether the caller may read that entity. A camp nobody granted is answered the
    /// way a camp that does not exist is, which is what keeps the timeline from being a way to
    /// find out that a private camp is there.
    /// </summary>
    [Fact]
    public async Task A_camps_timeline_is_its_readers_and_is_not_found_by_anybody_else()
    {
        var camp = await CreateCampAsync("Timeline camp");

        var (_, events) = await HistoryAsync(owner, "expedition", camp);
        events.ShouldContain(e => EntityType(e) == nameof(Expedition) && Action(e) == "created");

        var refused = await outsider.GetAsync($"/api/v1/history?entityType=expedition&entityId={camp}");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, await refused.Content.ReadAsStringAsync());

        // And the same caller, granted the read over that one camp, is told the camp's own story.
        await GrantAsync(AccessDomain.Expeditions, camp, outsiderId, AccessAction.Read);
        var (_, granted) = await HistoryAsync(outsider, "expedition", camp);
        granted.ShouldContain(e => EntityType(e) == nameof(Expedition) && Action(e) == "created");
    }

    /// <summary>
    /// A membership row names the trip that joined only to somebody who may read that trip. The
    /// camp's trip listing and its roll-up both withhold a member the caller may not read; the
    /// timeline saying otherwise would hand the id over by a side door, which is exactly the
    /// enumeration those two are composed to prevent.
    /// </summary>
    [Fact]
    public async Task A_membership_row_names_its_trip_only_to_a_reader_of_that_trip()
    {
        var camp = await CreateCampAsync("Camp with a private trip in it");
        var closed = await CreateTripAsync("A trip the outsider may not read");
        var open = await CreateTripAsync("A trip the outsider may read");
        await JoinAsync(camp, closed);
        await JoinAsync(camp, open);

        // The outsider may read the camp and one of its two trips, and holds nothing over the
        // other — no grant of any kind, which is the state the withholding is about.
        await GrantAsync(AccessDomain.Expeditions, camp, outsiderId, AccessAction.Read);
        await GrantAsync(AccessDomain.TripLogs, open, outsiderId, AccessAction.Read);

        var (body, events) = await HistoryAsync(outsider, "expedition", camp);
        var memberships = events.Where(e => EntityType(e) == nameof(ExpeditionTrip)).ToList();
        memberships.Count.ShouldBe(2, body);

        // The events stay — two trips joined this camp, and when — while which trip one of them
        // was does not, and the id appears nowhere in the payload rather than merely being
        // absent from the property the page reads.
        var withheld = memberships.Single(e => Redacted(e).Contains(nameof(ExpeditionTrip.TripLogId)));
        HasChange(withheld, nameof(ExpeditionTrip.TripLogId)).ShouldBeFalse();
        HasChange(withheld, nameof(ExpeditionTrip.JoinedAt)).ShouldBeTrue();
        body.ShouldNotContain(closed.ToString());

        // And the trip the outsider may read is named, which is what proves the removal is
        // driven by the permission rather than by the property name.
        var named = memberships.Single(e => Redacted(e).Length == 0);
        NewText(named, nameof(ExpeditionTrip.TripLogId)).ShouldBe(open.ToString());

        // The camp's owner, who may read both, is told both — the same page, the same rows.
        var (ownerBody, ownerEvents) = await HistoryAsync(owner, "expedition", camp);
        ownerEvents.Where(e => EntityType(e) == nameof(ExpeditionTrip))
            .ShouldAllBe(e => Redacted(e).Length == 0);
        ownerBody.ShouldContain(closed.ToString());
    }

    /// <summary>
    /// The live roster is stricter than the camp that holds it on purpose: it takes the read over
    /// the camp and the read over people, and refuses the caller holding only the first — the
    /// whole listing, with its own reason given, that rows with the names struck out would still
    /// say how many people were there and when. A roster row on the camp's timeline reaches
    /// everybody who may read the camp, so it withholds exactly the same thing under the same
    /// condition; anything less and the timeline is the way around a refusal the roster route
    /// makes deliberately.
    /// </summary>
    [Fact]
    public async Task A_roster_row_says_nothing_to_a_caller_the_live_roster_refuses()
    {
        var camp = await CreateCampAsync("Camp with a roster");
        var caver = await CreateCaverAsync("Kept the base camp");
        await AddToRosterAsync(camp, caver);

        await GrantAsync(AccessDomain.Expeditions, camp, outsiderId, AccessAction.Read);

        // Reading people is a right the seeded All Users group carries, so the caller who may not
        // read them is made explicitly: a deny of that read, at the widest scope, which is what a
        // club that keeps its directory to itself would write.
        await DenyPeopleAsync(outsiderId);

        // The live roster refuses that caller outright, and the timeline may not be softer.
        var roster = await outsider.GetAsync($"/api/v1/expeditions/{camp}/roster/");
        roster.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await roster.Content.ReadAsStringAsync());

        // The event survives — somebody edited the roster, and when — so the page can show an
        // honest hidden row rather than pretending the edit never happened. Everything the stay
        // recorded goes with the name: the dates say how many people were there and when, and the
        // note beside the name is free text that routinely gives the name straight back.
        var (body, events) = await HistoryAsync(outsider, "expedition", camp);
        var stay = events.Single(e => EntityType(e) == nameof(ExpeditionRosterEntry));
        Redacted(stay).ShouldContain(nameof(ExpeditionRosterEntry.CaverId));
        HasChange(stay, nameof(ExpeditionRosterEntry.CaverId)).ShouldBeFalse();
        HasChange(stay, nameof(ExpeditionRosterEntry.FromDate)).ShouldBeFalse();
        HasChange(stay, nameof(ExpeditionRosterEntry.ToDate)).ShouldBeFalse();
        body.ShouldNotContain(caver.ToString());

        // And the camp's owner, who may read people, is told all of it — the removal is driven by
        // the permission rather than by the entity type.
        var (ownerBody, ownerEvents) = await HistoryAsync(owner, "expedition", camp);
        var ownerStay = ownerEvents.Single(e => EntityType(e) == nameof(ExpeditionRosterEntry));
        Redacted(ownerStay).ShouldBeEmpty();
        HasChange(ownerStay, nameof(ExpeditionRosterEntry.FromDate)).ShouldBeTrue();
        ownerBody.ShouldContain(caver.ToString());
    }

    // ---- API helpers

    private async Task<(string Body, List<JsonElement> Events)> HistoryAsync(
        HttpClient client, string entityType, Guid entityId)
    {
        var response = await client.GetAsync(
            $"/api/v1/history?entityType={entityType}&entityId={entityId}&pageSize=200");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var items = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().ToList();
        return (body, items);
    }

    private async Task<Guid> CreateCampAsync(string name)
    {
        var body = new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "A camp.",
            startDate = "2026-07-18",
            endDate = "2026-08-01",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        };
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var body = new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-07-20",
            participants = Array.Empty<object>(),
            visibility = "private",
            hadIncident = false,
        };
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task JoinAsync(Guid expeditionId, Guid tripLogId)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{expeditionId}/trips", new { tripLogId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaverAsync(string what)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caver = new Caver { FullName = $"{what} {Guid.NewGuid().ToString("N")[..6]}" };
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();
        return caver.Id;
    }

    /// <summary>A stay on the camp's roster, written through the route so the row is audited.</summary>
    private async Task AddToRosterAsync(Guid expeditionId, Guid caverId)
    {
        var roles = await owner.GetAsync("/api/v1/expedition-roster-roles");
        var rolesBody = await roles.Content.ReadAsStringAsync();
        roles.StatusCode.ShouldBe(HttpStatusCode.OK, rolesBody);
        var roleId = JsonDocument.Parse(rolesBody).RootElement
            .EnumerateArray().First().GetProperty("id").GetInt64();

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{expeditionId}/roster/",
            new { caverId, roleId, fromDate = "2026-07-18", toDate = "2026-07-25", note = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A rule on one row, written straight to the table — the shape the route writes.</summary>
    private async Task GrantAsync(AccessDomain domain, Guid objectId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = objectId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Takes away the read over people that every signed-in account is seeded with.</summary>
    private async Task DenyPeopleAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.Cavers,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    // ---- event accessors

    private static string EntityType(JsonElement e) => e.GetProperty("entityType").GetString()!;

    private static string Action(JsonElement e) => e.GetProperty("action").GetString()!;

    private static bool HasChange(JsonElement e, string property)
    {
        var changes = e.GetProperty("changes");
        return changes.ValueKind == JsonValueKind.Object && changes.TryGetProperty(property, out _);
    }

    private static string? NewText(JsonElement e, string property) =>
        e.GetProperty("changes").GetProperty(property).GetProperty("new").GetString();

    private static string[] Redacted(JsonElement e) =>
        [.. e.GetProperty("redactedProperties").EnumerateArray().Select(x => x.GetString()!)];

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
