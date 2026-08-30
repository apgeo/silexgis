// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a camp having trips in it means: both doors into the membership, the rule that a trip is
/// in at most one camp, the camp's own trip listing showing only what its reader may read, and
/// what deleting either end does and does not take with it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionMembershipTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;

    // A plain reader, and it has to be: the seeded Editors group holds every content domain at
    // the widest scope, so an Editor who "cannot see" a trip proves nothing about visibility.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    public ExpeditionMembershipTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xm-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xm-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xm-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xm-out-{suffix}@t.local");
    }

    [Fact]
    public async Task A_trip_joins_a_camp_and_leaves_it_again()
    {
        var camp = await CreateCampAsync("Bihor summer camp");
        var member = await CreateTripAsync("Down the pot");
        var stranger = await CreateTripAsync("A trip in no camp");

        var joined = await JoinAsync(camp, member);
        joined.GetProperty("expeditionId").GetGuid().ShouldBe(camp);
        joined.GetProperty("tripLogId").GetGuid().ShouldBe(member);
        joined.GetProperty("movedFromAnotherExpedition").GetBoolean().ShouldBeFalse();

        (await CampOfAsync(owner, member)).ShouldBe(camp);
        (await MembersAsync(owner, camp)).ShouldBe([member]);

        var left = await owner.DeleteAsync($"/api/v1/expeditions/{camp}/trips/{member}");
        left.StatusCode.ShouldBe(HttpStatusCode.NoContent, await left.Content.ReadAsStringAsync());

        (await CampOfAsync(owner, member)).ShouldBeNull();
        (await MembersAsync(owner, camp)).ShouldBeEmpty();

        // The trip that never joined was never affected either way, which is the other half of
        // "the listing holds its members and only them".
        (await CampOfAsync(owner, stranger)).ShouldBeNull();
    }

    [Fact]
    public async Task Joining_a_second_camp_takes_the_trip_out_of_the_first()
    {
        var first = await CreateCampAsync("First camp");
        var second = await CreateCampAsync("Second camp");
        var trip = await CreateTripAsync("A trip that changed camps");

        await JoinAsync(first, trip);
        var moved = await JoinAsync(second, trip);

        // The move is told, and told without naming the camp it came out of: the caller may have
        // no right to read that one, and a flag says what happened without handing over an id.
        moved.GetProperty("movedFromAnotherExpedition").GetBoolean().ShouldBeTrue();
        moved.GetProperty("expeditionId").GetGuid().ShouldBe(second);

        (await MembersAsync(owner, first)).ShouldBeEmpty();
        (await MembersAsync(owner, second)).ShouldBe([trip]);
        (await MembershipRowCountAsync(trip)).ShouldBe(1);
    }

    [Fact]
    public async Task A_trips_camp_is_written_and_cleared_from_the_trips_own_side()
    {
        var camp = await CreateCampAsync("Camp written from the trip");
        var trip = await CreateTripAsync("A trip that chose its camp");

        var set = await owner.PutAsJsonAsync($"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = camp });
        set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        (await Json(set)).GetProperty("expeditionId").GetGuid().ShouldBe(camp);
        (await CampOfAsync(owner, trip)).ShouldBe(camp);

        // Naming no camp is the whole request saying "in none" — this endpoint writes one fact,
        // so there is no "not editing it" reading for an absent field to carry.
        var cleared = await owner.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = (Guid?)null });
        cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cleared.Content.ReadAsStringAsync());
        (await CampOfAsync(owner, trip)).ShouldBeNull();
        (await MembershipRowCountAsync(trip)).ShouldBe(0);
    }

    [Fact]
    public async Task A_camps_trip_list_shows_only_the_trips_the_caller_may_read()
    {
        // The camp is readable by anybody signed in, so what differs between the two callers is
        // the trips and nothing else.
        var camp = await CreateCampAsync("Shared camp", visibility: "authenticated");
        var open = await CreateTripAsync("A trip anybody signed in may read", visibility: "authenticated");
        var closed = await CreateTripAsync("A trip only its owner may read");
        await JoinAsync(camp, open);
        await JoinAsync(camp, closed);

        // The unreadable state, constructed rather than assumed: the outsider is a plain reader
        // with no grant on the private trip, and the trip refuses them outright.
        (await outsider.GetAsync($"/api/v1/trip-logs/{closed}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await MembersAsync(owner, camp)).ShouldBe([open, closed], ignoreOrder: true);
        (await MembersAsync(outsider, camp)).ShouldBe([open]);
    }

    [Fact]
    public async Task A_camp_the_caller_may_not_read_gathers_nothing_as_far_as_they_are_told()
    {
        var camp = await CreateCampAsync("A camp of my own");
        var trip = await CreateTripAsync("A trip anybody signed in may read", visibility: "authenticated");
        await JoinAsync(camp, trip);

        // The trip is readable to them — that is the positive half, and it is what makes the
        // empty answer below mean "you may not read that camp" rather than "you may not read
        // that trip". An id that answered differently from one that does not exist would be an
        // id anybody could go looking for.
        (await outsider.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CampOfAsync(outsider, trip)).ShouldBeNull();

        (await MembersAsync(owner, camp)).ShouldBe([trip]);
        (await MembersAsync(outsider, camp)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Putting_a_trip_in_a_camp_takes_the_right_to_write_both()
    {
        var mine = await CreateCampAsync("A camp of my own");
        var shared = await CreateCampAsync("A camp somebody else may write", visibility: "authenticated");
        var trip = await CreateTripAsync("A trip anybody signed in may read", visibility: "authenticated");

        // A camp they cannot read at all is a camp that does not exist as far as they are told.
        var unseen = await outsider.PostAsJsonAsync($"/api/v1/expeditions/{mine}/trips", new { tripLogId = trip });
        unseen.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(unseen)).ShouldBe("expedition.not_found");

        // A camp they may read but not write is refused as a refusal, not as an absence.
        var unwritable = await outsider.PostAsJsonAsync($"/api/v1/expeditions/{shared}/trips", new { tripLogId = trip });
        unwritable.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The grant lands, shown by an act that takes authority over the camp and nothing else:
        // evicting a trip this caller has no right to change. Without this the refusal below
        // would be explained entirely by the camp check, and the test would pass while proving
        // nothing about the trip check it exists for.
        await GrantAsync(AccessDomain.Expeditions, shared, outsiderId, AccessAction.Read | AccessAction.Write);
        await JoinAsync(shared, trip);
        var evicted = await outsider.DeleteAsync($"/api/v1/expeditions/{shared}/trips/{trip}");
        evicted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await evicted.Content.ReadAsStringAsync());

        // Given the camp outright, the trip is still the other half: a camp does not conscript a
        // trip its organiser has no right to change.
        var stillRefused = await outsider.PostAsJsonAsync(
            $"/api/v1/expeditions/{shared}/trips", new { tripLogId = trip });
        stillRefused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MembershipRowCountAsync(trip)).ShouldBe(0);

        // And the same caller, holding both, does it — so the refusal above is pinned to the
        // trip and not to anything else about who was asking.
        await GrantAsync(AccessDomain.TripLogs, trip, outsiderId, AccessAction.Read | AccessAction.Write);
        var allowed = await outsider.PostAsJsonAsync(
            $"/api/v1/expeditions/{shared}/trips", new { tripLogId = trip });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await Json(allowed)).GetProperty("tripLogId").GetGuid().ShouldBe(trip);
    }

    /// <summary>
    /// The trip's own door into the same relationship, and the camp half of its ladder. Joining
    /// is one rule with one set of refusals whichever side it is written from; the day one door
    /// grows a check the other lacks is the day the same act is allowed from one page and refused
    /// from the other, so both doors are driven here.
    /// </summary>
    [Fact]
    public async Task Setting_a_trips_camp_from_its_own_side_takes_the_right_to_write_the_camp()
    {
        var mine = await CreateCampAsync("A camp of my own");
        var shared = await CreateCampAsync("A camp somebody else may write", visibility: "authenticated");
        var trip = await CreateTripAsync("A trip somebody else may write", visibility: "authenticated");
        await GrantAsync(AccessDomain.TripLogs, trip, outsiderId, AccessAction.Read | AccessAction.Write);

        // Holding the trip is not holding the camp: a camp they may read but not write is refused
        // as a refusal, exactly as the camp's own door refuses it.
        var unwritable = await outsider.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = shared });
        unwritable.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await unwritable.Content.ReadAsStringAsync());

        // And a camp they cannot read at all is a camp that does not exist as far as they are
        // told — the same words the other door answers in.
        var unseen = await outsider.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = mine });
        unseen.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(unseen)).ShouldBe("expedition.not_found");
        (await MembershipRowCountAsync(trip)).ShouldBe(0);

        // Given the camp as well, the same caller does it.
        await GrantAsync(AccessDomain.Expeditions, shared, outsiderId, AccessAction.Read | AccessAction.Write);
        var allowed = await outsider.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = shared });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await CampOfAsync(owner, trip)).ShouldBe(shared);
    }

    [Fact]
    public async Task Setting_a_trips_camp_from_its_own_side_takes_the_right_to_write_the_trip()
    {
        var camp = await CreateCampAsync("A camp somebody else may write", visibility: "authenticated");
        var trip = await CreateTripAsync("A trip nobody else may write", visibility: "authenticated");
        await GrantAsync(AccessDomain.Expeditions, camp, outsiderId, AccessAction.Read | AccessAction.Write);

        // The camp is theirs to write and the trip is not, so the trip is what refuses — in the
        // trip's own words, because the trip is what they may read and not change.
        var joining = await outsider.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = camp });
        joining.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await joining.Content.ReadAsStringAsync());
        (await MembershipRowCountAsync(trip)).ShouldBe(0);

        // Taking a trip out from its own side takes the same authority over the trip, so a caller
        // who cannot write it cannot clear its camp either.
        await JoinAsync(camp, trip);
        var clearing = await outsider.PutAsJsonAsync(
            $"/api/v1/trip-logs/{trip}/expedition", new { expeditionId = (Guid?)null });
        clearing.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await clearing.Content.ReadAsStringAsync());
        (await CampOfAsync(owner, trip)).ShouldBe(camp);
    }

    /// <summary>
    /// The camp door's own two remaining answers: a trip that is not in this camp, and a caller
    /// who may write neither end. Ending a membership takes one of the two rights, so somebody
    /// holding neither is refused — otherwise anybody who could read both could break the tie.
    /// </summary>
    [Fact]
    public async Task Taking_a_trip_out_needs_authority_over_one_end_and_a_membership_to_end()
    {
        var camp = await CreateCampAsync("A camp anybody signed in may read", visibility: "authenticated");
        var member = await CreateTripAsync("A trip in the camp", visibility: "authenticated");
        var stranger = await CreateTripAsync("A trip in no camp", visibility: "authenticated");
        await JoinAsync(camp, member);

        // A trip that exists, is readable, and is simply not in this camp: told so under a code
        // of its own, rather than as a missing trip or a refusal.
        var notAMember = await owner.DeleteAsync($"/api/v1/expeditions/{camp}/trips/{stranger}");
        notAMember.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(notAMember)).ShouldBe("expedition.trip_not_in_expedition");

        // A caller who may read both and write neither may not end the membership.
        var refused = await outsider.DeleteAsync($"/api/v1/expeditions/{camp}/trips/{member}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await MembersAsync(owner, camp)).ShouldBe([member]);

        // The trip alone is enough — the camp's organiser is not the only party who may end it.
        await GrantAsync(AccessDomain.TripLogs, member, outsiderId, AccessAction.Read | AccessAction.Write);
        var ended = await outsider.DeleteAsync($"/api/v1/expeditions/{camp}/trips/{member}");
        ended.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ended.Content.ReadAsStringAsync());
        (await MembersAsync(owner, camp)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_trip_takes_its_place_in_the_camp_with_it()
    {
        var camp = await CreateCampAsync("A camp that loses a trip");
        var trip = await CreateTripAsync("A trip that gets deleted");
        var survivor = await CreateTripAsync("A trip that does not");
        await JoinAsync(camp, trip);
        await JoinAsync(camp, survivor);

        var deleted = await owner.DeleteAsync($"/api/v1/trip-logs/{trip}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        (await MembershipRowCountAsync(trip)).ShouldBe(0);
        (await MembersAsync(owner, camp)).ShouldBe([survivor]);
        (await owner.GetAsync($"/api/v1/expeditions/{camp}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_a_camp_leaves_its_trips_standing()
    {
        var camp = await CreateCampAsync("A camp that gets deleted");
        var first = await CreateTripAsync("One of its trips");
        var second = await CreateTripAsync("Another of its trips");
        await JoinAsync(camp, first);
        await JoinAsync(camp, second);

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{camp}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        (await owner.GetAsync($"/api/v1/expeditions/{camp}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The trips are what the people who wrote them still have, and they read as trips in no
        // camp rather than as trips pointing at one that is gone.
        (await owner.GetAsync($"/api/v1/trip-logs/{first}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetAsync($"/api/v1/trip-logs/{second}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CampOfAsync(owner, first)).ShouldBeNull();
        (await MembershipRowCountAsync(first)).ShouldBe(0);
        (await MembershipRowCountAsync(second)).ShouldBe(0);
    }

    private async Task<Guid> CreateCampAsync(string name, string visibility = "private")
    {
        var body = new
        {
            name = $"{name} {Guid.NewGuid():N}",
            description = "A camp.",
            startDate = "2026-07-18",
            endDate = "2026-08-01",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility,
        };
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title, string visibility = "private")
    {
        var body = new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-07-20",
            participants = Array.Empty<object>(),
            visibility,
            hadIncident = false,
        };
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> JoinAsync(Guid expeditionId, Guid tripLogId)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{expeditionId}/trips", new { tripLogId });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>The camp's own trip listing: the trip list narrowed to this camp's members.</summary>
    private static async Task<List<Guid>> MembersAsync(HttpClient client, Guid expeditionId)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/?pageSize=500&expeditionId={expeditionId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        var items = body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        // The total is counted on the same narrowed statement, so a page that lists three rows
        // and says there are ninety is a filter that ran after the count.
        body.GetProperty("totalItems").GetInt32().ShouldBe(items.Count);
        return items;
    }

    private static async Task<Guid?> CampOfAsync(HttpClient client, Guid tripLogId)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{tripLogId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var camp = (await Json(response)).GetProperty("expeditionId");
        return camp.ValueKind == JsonValueKind.Null ? null : camp.GetGuid();
    }

    private async Task<int> MembershipRowCountAsync(Guid tripLogId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ExpeditionTrips.CountAsync(x => x.TripLogId == tripLogId);
    }

    /// <summary>
    /// A rule on one row, written straight to the table: the per-object access route does not know
    /// expeditions yet, so this is what an object-scoped rule looks like until then.
    /// </summary>
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

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
