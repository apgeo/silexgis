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
/// Confirming a line of the list a trip works through, and the figure read off those
/// confirmations.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being pinned, and the second is why this suite exists. The first is that a
/// confirmation is a record: who said the permit was in hand, and when, kept as written and
/// unaffected by the line's text being corrected afterwards.
/// </para>
/// <para>
/// The second is that the figure derived from those confirmations decides nothing. How ready a
/// party is is not a state the trip is in: no write is refused because a list is unsettled, and —
/// the case that matters most — a trip with nothing confirmed is visible to exactly the people a
/// trip with everything confirmed is visible to. Who may read a row is settled by that row's
/// audience and the entries about it, in one place. A readiness figure joining that decision
/// would be a second answer to the same question, and a plan that vanished because somebody took
/// a confirmation back would be a disclosure rule nobody wrote.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripChecklistTickTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;     // Editor — plans the trips and runs them
    private HttpClient reader = null!;    // Viewer — may read what is open to any account
    private Guid ownerId;
    private Guid readerId;

    private long tripTypeId;
    private Guid checklistId;
    private List<Guid> itemIds = [];

    // A line of a different list, readable by the same callers and named by no trip purpose. It
    // is what makes "not on this trip's list" a real state rather than a missing row: an id that
    // names nothing at all would be refused for not existing, and would leave the refusal that
    // matters — a line that does exist, on a list this trip is not measured against — untested.
    private Guid otherListItemId;

    public TripChecklistTickTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tct-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tct-own-{suffix}@t.local");

        // A Viewer, deliberately. The seeded Editors group reads past visibility at the widest
        // scope by design, so anything built on an Editor being refused would prove nothing about
        // the audience the row itself states.
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tct-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tct-read-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Open to any account, because what is being tested here is the readiness figure and not
        // the list's own audience — that is pinned beside the list itself.
        var checklist = new Checklist
        {
            Title = $"Before we set off {suffix}",
            Description = "What has to be settled before the party leaves.",
            OwnerUserId = ownerId,
            Visibility = Visibility.Authenticated,
        };
        db.Checklists.Add(checklist);
        string[] lines = ["Permit obtained", "Key collected", "Callout arranged"];
        for (var i = 0; i < lines.Length; i++)
        {
            db.ChecklistItems.Add(new ChecklistItem
            {
                ChecklistId = checklist.Id,
                Text = lines[i],
                SortOrder = i,
            });
        }

        var otherList = new Checklist
        {
            Title = $"Some other list {suffix}",
            OwnerUserId = ownerId,
            Visibility = Visibility.Authenticated,
        };
        db.Checklists.Add(otherList);
        var otherLine = new ChecklistItem
        {
            ChecklistId = otherList.Id,
            Text = "Boat booked",
            SortOrder = 0,
        };
        db.ChecklistItems.Add(otherLine);

        var tripType = new TripType
        {
            Code = $"tct_{suffix}",
            Name = $"Prepared trip {suffix}",
            DefaultChecklistId = checklist.Id,
        };
        db.TripTypes.Add(tripType);
        await db.SaveChangesAsync();

        checklistId = checklist.Id;
        tripTypeId = tripType.Id;
        otherListItemId = otherLine.Id;
        itemIds = await db.ChecklistItems.AsNoTracking()
            .Where(x => x.ChecklistId == checklistId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();
    }

    /// <summary>
    /// A confirmation says who and when, and goes on saying it after the line it labels has been
    /// reworded. That is the whole reason a line is a row with a key of its own rather than an
    /// entry in a document: a confirmation names the key, so correcting the words strands nothing.
    /// </summary>
    [Fact]
    public async Task A_confirmation_records_who_and_when_and_survives_the_list_being_edited()
    {
        var tripId = await PlanTripAsync(owner, $"Permit trip {suffix}");

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var ticked = await owner.PutAsync(Line(tripId, itemIds[0]), null);
        ticked.StatusCode.ShouldBe(HttpStatusCode.OK, await ticked.Content.ReadAsStringAsync());

        var confirmed = await ReadChecklistAsync(owner, tripId);
        confirmed.GetProperty("ticked").GetInt32().ShouldBe(1);
        confirmed.GetProperty("total").GetInt32().ShouldBe(3);

        var permit = confirmed.GetProperty("items")[0];
        permit.GetProperty("ticked").GetBoolean().ShouldBeTrue();
        permit.GetProperty("tickedByUserId").GetGuid().ShouldBe(ownerId);
        var tickedAt = permit.GetProperty("tickedAt").GetDateTimeOffset();
        tickedAt.ShouldBeGreaterThan(before);
        tickedAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(5));

        // Confirming again is the same assertion said twice. The record keeps the first: who
        // settled the permit and when is the question, and an answer that moved every time
        // somebody re-opened the page would answer a different one.
        var again = await owner.PutAsync(Line(tripId, itemIds[0]), null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await ReadChecklistAsync(owner, tripId)).GetProperty("items")[0]
            .GetProperty("tickedAt").GetDateTimeOffset().ShouldBe(tickedAt);

        // The list is corrected under the trip: the same line, different words. Nothing about the
        // confirmation moves, because it never named the words.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var line = await db.ChecklistItems.SingleAsync(x => x.Id == itemIds[0]);
            line.Text = "Permit obtained from the landowner";
            await db.SaveChangesAsync();
        }

        var after = await ReadChecklistAsync(owner, tripId);
        after.GetProperty("ticked").GetInt32().ShouldBe(1);
        var reworded = after.GetProperty("items")[0];
        reworded.GetProperty("text").GetString().ShouldBe("Permit obtained from the landowner");
        reworded.GetProperty("ticked").GetBoolean().ShouldBeTrue();
        reworded.GetProperty("tickedByUserId").GetGuid().ShouldBe(ownerId);
        reworded.GetProperty("tickedAt").GetDateTimeOffset().ShouldBe(tickedAt);

        // And taking it back leaves the line standing with nothing said about it, rather than
        // leaving a confirmation nobody made.
        (await owner.DeleteAsync(Line(tripId, itemIds[0]))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = await ReadChecklistAsync(owner, tripId);
        withdrawn.GetProperty("ticked").GetInt32().ShouldBe(0);
        withdrawn.GetProperty("items")[0].GetProperty("ticked").GetBoolean().ShouldBeFalse();
        withdrawn.GetProperty("items")[0].GetProperty("tickedByUserId").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The case this suite exists for. Two trips with the same audience, one with nothing
    /// confirmed and one with everything confirmed, are read by exactly the same callers — and a
    /// third trip that really is shut is invisible to the same caller in the same reading, so that
    /// the first two being visible is a fact about the walk rather than about a walk that lets
    /// everything through.
    /// </summary>
    [Fact]
    public async Task How_settled_a_trip_is_never_decides_who_may_read_it()
    {
        var unsettled = await PlanTripAsync(owner, $"Nothing settled {suffix}");
        var settled = await PlanTripAsync(owner, $"All settled {suffix}");
        var shut = await PlanTripAsync(owner, $"Shut {suffix}", visibility: "private");

        foreach (var item in itemIds)
        {
            (await owner.PutAsync(Line(settled, item), null)).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }

        // Confirmed to be genuinely different states before anything is concluded from them
        // being read alike — two trips that were both empty would satisfy the comparison below
        // while proving nothing.
        (await ReadChecklistAsync(owner, unsettled)).GetProperty("ticked").GetInt32().ShouldBe(0);
        (await ReadChecklistAsync(owner, settled)).GetProperty("ticked").GetInt32().ShouldBe(3);

        foreach (var client in new[] { owner, reader })
        {
            (await client.GetAsync($"/api/v1/trip-logs/{unsettled}")).StatusCode
                .ShouldBe(HttpStatusCode.OK);
            (await client.GetAsync($"/api/v1/trip-logs/{settled}")).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }

        // The negative half, and it is a trip nobody granted anything on rather than a trip
        // nobody confirmed anything on: the audience is what withholds a trip, and it still does.
        (await reader.GetAsync($"/api/v1/trip-logs/{shut}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/trip-logs/{shut}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The same answer through the listing, which is the surface a readiness figure would be
        // most tempting to narrow: both trips are on it, in the same reading, whatever each has
        // settled — and the figure travels beside them rather than deciding anything about them.
        var listed = await ListedTripsAsync(reader);
        listed.ShouldContainKey(unsettled);
        listed.ShouldContainKey(settled);
        listed.ShouldNotContainKey(shut);
        listed[unsettled].ShouldBe((0, 3));
        listed[settled].ShouldBe((3, 3));
    }

    /// <summary>
    /// Nothing is refused for being unsettled. A plan whose paperwork is not done is saved,
    /// announced and prepared exactly as one whose paperwork is — the feature exists to make
    /// preparing a trip easier to see, and a party that has to finish the list before the
    /// application will take the plan is a party that keeps the plan somewhere else.
    /// </summary>
    [Fact]
    public async Task No_write_is_refused_because_a_list_is_unsettled()
    {
        var tripId = await PlanTripAsync(owner, $"Unsettled but savable {suffix}");
        (await ReadChecklistAsync(owner, tripId)).GetProperty("ticked").GetInt32().ShouldBe(0);

        var saved = await owner.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title = $"Unsettled but savable {suffix} (edited)",
            tripTypeId,
            tripDate = "2026-09-01",
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // And the lifecycle moves too. Where a plan has got to and how much of its list is
        // settled are two questions; only one of them has a vocabulary and a set of legal moves,
        // and it is not this one.
        var announced = await owner.PostWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}/state", new { state = "planned" });
        announced.StatusCode.ShouldBe(HttpStatusCode.OK, await announced.Content.ReadAsStringAsync());

        (await ReadChecklistAsync(owner, tripId)).GetProperty("ticked").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// Reading the list a trip works through takes the right to read the trip; confirming a line
    /// of it takes the right to run the trip. A caller who may read neither is told the trip is
    /// not there, so a refusal never says what is being prepared or that anything is.
    /// </summary>
    [Fact]
    public async Task Reading_takes_the_trip_and_confirming_takes_the_right_to_run_it()
    {
        var tripId = await PlanTripAsync(owner, $"Who may confirm {suffix}");
        var shut = await PlanTripAsync(owner, $"Shut to confirm {suffix}", visibility: "private");

        // Reading: allowed for a caller who may read the trip, and the same caller may not write.
        var read = await reader.GetAsync($"/api/v1/trip-logs/{tripId}/checklist");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());

        var refused = await reader.PutAsync(Line(tripId, itemIds[0]), null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).ShouldContain("trip_checklist.tick_forbidden");

        // The positive half of the same rule: the caller who may run the trip is not refused.
        (await owner.PutAsync(Line(tripId, itemIds[0]), null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // A trip this caller may not read is absent, on the reading route and on the write alike,
        // and a refusal that said "forbidden" here would be an admission the trip exists.
        (await reader.GetAsync($"/api/v1/trip-logs/{shut}/checklist")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        var blind = await reader.PutAsync(Line(shut, itemIds[0]), null);
        blind.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await blind.Content.ReadAsStringAsync()).ShouldContain("trip_log.not_found");

        // A line of some other list is not on this trip's list, so it is not found — a
        // confirmation against it would count towards nothing and mean nothing. The line used
        // here is a real one on a real list this caller may read, so the only thing that can
        // refuse it is the list it is on; an id naming no row would be refused for that instead
        // and would prove nothing about which list the trip is measured against.
        var stray = await owner.PutAsync(Line(tripId, otherListItemId), null);
        stray.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stray.Content.ReadAsStringAsync()).ShouldContain("trip_checklist.item_not_found");

        // And the confirmation it was refused was not quietly recorded anywhere.
        (await ReadChecklistAsync(owner, tripId)).GetProperty("ticked").GetInt32().ShouldBe(1);

        // An id that names nothing is refused too, and told apart from neither.
        var nothing = await owner.PutAsync(Line(tripId, Guid.CreateVersion7()), null);
        nothing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await nothing.Content.ReadAsStringAsync()).ShouldContain("trip_checklist.item_not_found");

        (await factory.CreateClient().GetAsync($"/api/v1/trip-logs/{tripId}/checklist")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A trip whose purpose names a list this caller may not read is told it has no list — the
    /// same answer as a trip whose purpose names none. A list answers to its own audience, and a
    /// reference from a trip is not consent to read it; telling the two cases apart would say a
    /// list exists.
    /// </summary>
    [Fact]
    public async Task A_list_the_caller_may_not_read_is_no_list_at_all()
    {
        var tripId = await PlanTripAsync(owner, $"Private list {suffix}");

        // The positive reading first, so that what follows is the audience narrowing and not the
        // route answering nothing to everybody.
        var open = await ReadChecklistAsync(reader, tripId);
        open.GetProperty("checklistId").GetGuid().ShouldBe(checklistId);
        open.GetProperty("total").GetInt32().ShouldBe(3);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var list = await db.Checklists.SingleAsync(x => x.Id == checklistId);
            list.Visibility = Visibility.Private;
            await db.SaveChangesAsync();
        }

        var withheld = await ReadChecklistAsync(reader, tripId);
        withheld.GetProperty("checklistId").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("items").GetArrayLength().ShouldBe(0);
        withheld.GetProperty("total").GetInt32().ShouldBe(0);

        // And no figure on the listing either, rather than a figure of zero — nothing settled and
        // nothing to settle are different answers, and only one of them is worth showing. The trip
        // itself is on the listing exactly as before: what the caller may not read is the list,
        // and withholding the trip over it would be a second rule about who sees a plan.
        var withoutTheList = await ListedTripsAsync(reader);
        withoutTheList.ShouldContainKey(tripId);
        withoutTheList[tripId].ShouldBeNull();

        // The owner, who may read the list, still gets both.
        (await ReadChecklistAsync(owner, tripId)).GetProperty("total").GetInt32().ShouldBe(3);
        (await ListedTripsAsync(owner))[tripId].ShouldBe((0, 3));
    }

    private string Line(Guid tripId, Guid itemId) =>
        $"/api/v1/trip-logs/{tripId}/checklist/items/{itemId}";

    private async Task<Guid> PlanTripAsync(
        HttpClient client, string title, string visibility = "authenticated")
    {
        var response = await client.PostAsJsonAsync("/api/v1/trip-logs/plans", new
        {
            title,
            tripTypeId,
            tripDate = "2026-09-01",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadChecklistAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}/checklist");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// The trips this caller may read that this class made, each with whatever readiness figure it
    /// carries — null where there is none. Narrowed by the suffix because the fixture shares one
    /// database with every other suite, so an unnarrowed reading would be a reading of the
    /// afternoon's other tests.
    /// </summary>
    private async Task<Dictionary<Guid, (int Ticked, int Total)?>> ListedTripsAsync(HttpClient client)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs?search={suffix}&pageSize=100");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var found = new Dictionary<Guid, (int Ticked, int Total)?>();
        foreach (var trip in body.GetProperty("items").EnumerateArray())
        {
            var readiness = trip.GetProperty("checklistReadiness");
            found[trip.GetProperty("id").GetGuid()] = readiness.ValueKind == JsonValueKind.Null
                ? null
                : (readiness.GetProperty("ticked").GetInt32(), readiness.GetProperty("total").GetInt32());
        }

        return found;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
