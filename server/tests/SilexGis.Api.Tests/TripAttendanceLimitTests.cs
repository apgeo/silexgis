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
/// How many a trip has room for, who is on it, and who is waiting.
/// </summary>
/// <remarks>
/// <para>
/// The limit refuses nothing, so every test here asserts that the answer past it was written down
/// as well as that it is waiting. Those are two different claims and only the pair of them is the
/// feature: a write refused at the door would satisfy "the ninth is not on the trip" while
/// destroying exactly the record a waiting list exists to keep.
/// </para>
/// <para>
/// Every plan is created private and opened one explicit grant at a time, and the person refused
/// is always a plain reader holding nothing. The group ordinary accounts are put in reads and
/// writes past visibility at the widest scope, so an account that "cannot" do something while
/// holding that membership proves nothing at all.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripAttendanceLimitTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!; // Editor; owns each plan, so writes it
    private HttpClient mate = null!;      // Viewer; given the read on the plan and nothing else

    private Guid mateId;
    private Guid mateCaver;

    /// <summary>
    /// Ten people in the directory with no account between them, which is what a club's roster
    /// mostly is. Answering for them is the organiser's to do, which is what the fixture needs.
    /// </summary>
    private readonly List<Guid> cavers = [];

    public TripAttendanceLimitTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tal-org-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tal-mate-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            mateCaver = (await db.Cavers.FirstAsync(c => c.UserId == mateId)).Id;

            for (var i = 0; i < 10; i++)
            {
                var caver = new Caver { FullName = $"Queue {i:00} {suffix}" };
                db.Cavers.Add(caver);
                cavers.Add(caver.Id);
            }

            await db.SaveChangesAsync();
        }

        organiser = await AuthHelper.BearerClientAsync(factory, $"tal-org-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"tal-mate-{suffix}@t.local");
    }

    /// <summary>
    /// The heart of it. A trip with room for eight is told nine times that somebody is coming, and
    /// says yes to all nine: who else wanted to come, and in what order they said so, is precisely
    /// the record the limit makes worth keeping, and a validator that refused the ninth would
    /// destroy it at the door and leave whoever runs the trip a silence to choose from.
    /// </summary>
    [Fact]
    public async Task The_ninth_yes_is_written_down_and_waits_rather_than_being_refused()
    {
        var trip = await CreatePlanAsync("Room for eight", maxParticipants: 8);

        foreach (var caver in cavers.Take(9))
        {
            var response = await AnswerAsync(organiser, trip, caver, "yes");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        var list = await ReadListAsync(organiser, trip);
        var rows = Rows(list);

        // Nine answers stored, none of them refused.
        rows.Count.ShouldBe(9);
        rows.ShouldAllBe(x => x.GetProperty("response").GetString() == "yes");

        // Eight of them are on the trip. Asserted alongside the refusal below, because a ranking
        // that admitted nobody at all would satisfy "the ninth is waiting" on its own and read as
        // a stricter limit rather than as a broken one.
        list.GetProperty("attendingCount").GetInt32().ShouldBe(8);
        list.GetProperty("waitingCount").GetInt32().ShouldBe(1);
        list.GetProperty("maxParticipants").GetInt32().ShouldBe(8);

        var ninth = rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[8]);
        ninth.GetProperty("place").GetInt32().ShouldBe(9);
        ninth.GetProperty("attending").GetBoolean().ShouldBeFalse();

        var eighth = rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[7]);
        eighth.GetProperty("place").GetInt32().ShouldBe(8);
        eighth.GetProperty("attending").GetBoolean().ShouldBeTrue();

        // Nothing about the queue was written down. The place and the waiting are worked out from
        // the answers every time they are read, so there is only ever one record of who is coming
        // and it cannot go stale against itself.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.TripInvitations.AsNoTracking().Where(x => x.TripLogId == trip).ToListAsync();
        stored.Count.ShouldBe(9);
        stored.ShouldAllBe(x => x.SelectedAt == null);
        typeof(TripInvitation).GetProperties()
            .Select(p => p.Name)
            .ShouldNotContain(name => name.Contains("Place", StringComparison.Ordinal)
                || name.Contains("Waiting", StringComparison.Ordinal)
                || name.Contains("Position", StringComparison.Ordinal));
    }

    /// <summary>
    /// Somebody who said "maybe" in March and "yes" in June joined the queue in June. The row was
    /// created first and is the oldest of the three, so anything ordering by when the row appeared
    /// — or by its key — would put them at the front of a trip they only committed to last.
    /// </summary>
    [Fact]
    public async Task Somebody_who_said_maybe_and_later_yes_joins_the_queue_when_they_said_yes()
    {
        var trip = await CreatePlanAsync("Room for two", maxParticipants: 2);

        // The undecided one answers first, so their row is the oldest on the trip.
        (await AnswerAsync(organiser, trip, cavers[0], "maybe")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(organiser, trip, cavers[1], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(organiser, trip, cavers[2], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // ...and commits last.
        var late = await AnswerAsync(organiser, trip, cavers[0], "yes");
        late.StatusCode.ShouldBe(HttpStatusCode.OK, await late.Content.ReadAsStringAsync());

        var list = await ReadListAsync(organiser, trip);
        var rows = Rows(list);

        var changedMind = rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[0]);
        changedMind.GetProperty("place").GetInt32().ShouldBe(3);
        changedMind.GetProperty("attending").GetBoolean().ShouldBeFalse();

        // The two who committed while the first was still undecided are the two on the trip.
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        list.GetProperty("attendingCount").GetInt32().ShouldBe(2);
        list.GetProperty("waitingCount").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// Whoever runs a trip has reasons the order cannot express — the one member with the key, the
    /// two who can drive — so they may pick somebody out of it. What that must not do is rewrite
    /// the order underneath: a hand-chosen team that hid who signed up first would leave nobody
    /// able to see they had been passed over.
    /// </summary>
    [Fact]
    public async Task Picking_somebody_puts_them_on_the_trip_without_disturbing_the_order()
    {
        var trip = await CreatePlanAsync("Picked team", maxParticipants: 2);

        foreach (var caver in cavers.Take(3))
        {
            (await AnswerAsync(organiser, trip, caver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Before: the first two are in on the strength of having answered first.
        var before = Rows(await ReadListAsync(organiser, trip));
        before.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        before.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2])
            .GetProperty("attending").GetBoolean().ShouldBeFalse();

        var picked = await SelectAsync(organiser, trip, cavers[2], selected: true);
        picked.StatusCode.ShouldBe(HttpStatusCode.OK, await picked.Content.ReadAsStringAsync());

        var list = await ReadListAsync(organiser, trip);
        var rows = Rows(list);

        var third = rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2]);
        third.GetProperty("attending").GetBoolean().ShouldBeTrue();
        third.GetProperty("selectedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // The pick displaces the last person who would otherwise have got in, not the first.
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[0])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("attending").GetBoolean().ShouldBeFalse();

        // And the order is exactly the order it was: everybody still holds the place they signed
        // up for, so the queue stays readable underneath the choice made over it.
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[0])
            .GetProperty("place").GetInt32().ShouldBe(1);
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("place").GetInt32().ShouldBe(2);
        third.GetProperty("place").GetInt32().ShouldBe(3);
        list.GetProperty("attendingCount").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// Choosing the team is running the trip. A reader may say whether they themselves are coming
    /// — that is the whole point of letting a reader answer — but putting their own name in front
    /// of everybody who signed up before them is the one thing the order exists to prevent.
    /// </summary>
    [Fact]
    public async Task Only_whoever_runs_the_trip_picks_the_team()
    {
        var trip = await CreatePlanAsync("Who picks", maxParticipants: 1);
        await GrantReadAsync(trip, mateId);

        (await AnswerAsync(organiser, trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(mate, trip, mateCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var theirs = await SelectAsync(mate, trip, mateCaver, selected: true);
        theirs.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await theirs.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.select_forbidden");

        // Still second, and still waiting — the refusal changed nothing about the order.
        var afterRefusal = Rows(await ReadListAsync(mate, trip));
        afterRefusal.Single(x => x.GetProperty("caverId").GetGuid() == mateCaver)
            .GetProperty("attending").GetBoolean().ShouldBeFalse();

        // And whoever runs the trip may do exactly what the reader could not, so this is a test of
        // the permission and not of a route that refuses everybody.
        var theirsFromTheOrganiser = await SelectAsync(organiser, trip, mateCaver, selected: true);
        theirsFromTheOrganiser.StatusCode.ShouldBe(
            HttpStatusCode.OK, await theirsFromTheOrganiser.Content.ReadAsStringAsync());

        var picked = Rows(await ReadListAsync(mate, trip));
        picked.Single(x => x.GetProperty("caverId").GetGuid() == mateCaver)
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        picked.Single(x => x.GetProperty("caverId").GetGuid() == cavers[0])
            .GetProperty("attending").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A trip states no limit until somebody says otherwise, and then everybody who says they are
    /// coming is coming. Nought is not a small trip — it is a refusal expressed as a number — and
    /// is refused as a shape.
    /// </summary>
    [Fact]
    public async Task A_trip_without_a_stated_limit_takes_everybody_and_a_limit_of_nought_is_not_a_limit()
    {
        var trip = await CreatePlanAsync("No limit", maxParticipants: null);

        foreach (var caver in cavers.Take(5))
        {
            (await AnswerAsync(organiser, trip, caver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var list = await ReadListAsync(organiser, trip);
        list.GetProperty("maxParticipants").ValueKind.ShouldBe(JsonValueKind.Null);
        list.GetProperty("attendingCount").GetInt32().ShouldBe(5);
        list.GetProperty("waitingCount").GetInt32().ShouldBe(0);
        Rows(list).ShouldAllBe(x => x.GetProperty("attending").GetBoolean());

        var refused = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Room for nobody {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = Array.Empty<object>(),
            visibility = "private",
            maxParticipants = 0,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Picking somebody who declined would put them on a trip against what they themselves said
    /// about it, leaving the row holding two contradictory facts with nothing to say which was
    /// meant.
    /// </summary>
    [Fact]
    public async Task Only_somebody_who_said_they_are_coming_can_be_picked()
    {
        var trip = await CreatePlanAsync("Picked from the noes", maxParticipants: 1);

        (await AnswerAsync(organiser, trip, cavers[0], "no")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(organiser, trip, cavers[1], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await SelectAsync(organiser, trip, cavers[0], selected: true);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.select_not_attending");

        // The same organiser picks the person who did say yes, so the refusal is about the answer
        // rather than about the route.
        (await SelectAsync(organiser, trip, cavers[1], selected: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the pick comes back off cleanly, which is what stops a stamp becoming a fact nobody
        // can undo.
        (await SelectAsync(organiser, trip, cavers[1], selected: false))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        Rows(await ReadListAsync(organiser, trip))
            .Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("selectedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A pick does not survive the answer it was made about. Somebody who was picked and then took
    /// their name back rejoins the queue at the back if they come round again — because the pick
    /// they would otherwise re-enter on is one nobody made after they withdrew, and the row would
    /// meanwhile be carrying a stamp saying "chosen" beside an answer saying "not coming", which
    /// the picking route refuses to write and so must not be reachable by any other route either.
    /// </summary>
    [Fact]
    public async Task A_pick_is_lost_when_the_answer_changes_and_is_not_regained_by_answering_yes_again()
    {
        var trip = await CreatePlanAsync("Withdrawn pick", maxParticipants: 2);

        // Three yeses in order, then the last of them picked out from behind the limit: two are on
        // the trip and the one in the middle is waiting.
        foreach (var caver in cavers.Take(3))
        {
            (await AnswerAsync(organiser, trip, caver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await SelectAsync(organiser, trip, cavers[2], selected: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var picked = Rows(await ReadListAsync(organiser, trip));
        picked.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        picked.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("attending").GetBoolean().ShouldBeFalse();

        // They take their name back. The stamp goes with it rather than waiting quietly on a row
        // that now says they are not coming.
        (await AnswerAsync(organiser, trip, cavers[2], "no")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withdrawn = Rows(await ReadListAsync(organiser, trip));
        withdrawn.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2])
            .GetProperty("selectedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // And the place they were holding goes to whoever was waiting for it.
        withdrawn.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();

        // Weeks later they come round again. They join the queue where the yes puts them, behind
        // everybody who is already in — not back in front of them on a choice nobody made, and not
        // as a third person on a trip with room for two.
        (await AnswerAsync(organiser, trip, cavers[2], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await ReadListAsync(organiser, trip);
        again.GetProperty("attendingCount").GetInt32().ShouldBe(2);
        again.GetProperty("waitingCount").GetInt32().ShouldBe(1);

        var rows = Rows(again);
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2])
            .GetProperty("attending").GetBoolean().ShouldBeFalse();
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[2])
            .GetProperty("place").GetInt32().ShouldBe(3);

        // The two who never moved are still on it, so this is a test of one person losing a place
        // rather than of a limit that stopped admitting anybody.
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[0])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        rows.Single(x => x.GetProperty("caverId").GetGuid() == cavers[1])
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// Picking somebody the list says nothing about is not the same refusal as picking somebody
    /// who is not in the club's directory at all, and the two carry different codes: a client told
    /// the second sends its user to add a person who is already there.
    /// </summary>
    [Fact]
    public async Task Picking_somebody_who_has_said_nothing_is_a_different_refusal_from_picking_a_stranger()
    {
        var trip = await CreatePlanAsync("Nothing said", maxParticipants: 2);

        var silent = await SelectAsync(organiser, trip, cavers[0], selected: true);
        silent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await silent.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.not_on_list");

        var stranger = await AnswerAsync(organiser, trip, Guid.NewGuid(), "yes");
        stranger.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await stranger.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.caver_unknown");

        // The same person, once they have said yes, is picked without complaint — so the refusal
        // above is about the missing answer and not about the route.
        (await AnswerAsync(organiser, trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SelectAsync(organiser, trip, cavers[0], selected: true))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers

    private static string ListPath(Guid tripId) => $"/api/v1/trip-logs/{tripId}/invitations/";

    private static Task<HttpResponseMessage> AnswerAsync(
        HttpClient client, Guid tripId, Guid caverId, string response) =>
        client.PutAsJsonAsync(
            $"/api/v1/trip-logs/{tripId}/invitations/{caverId}/response", new { response });

    private static Task<HttpResponseMessage> SelectAsync(
        HttpClient client, Guid tripId, Guid caverId, bool selected) =>
        client.PutAsJsonAsync(
            $"/api/v1/trip-logs/{tripId}/invitations/{caverId}/selection", new { selected });

    private static List<JsonElement> Rows(JsonElement list) =>
        [.. list.GetProperty("invitations").EnumerateArray()];

    private static async Task<JsonElement> ReadListAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync(ListPath(tripId));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>
    /// A plan written up through the door that leaves it private, so everybody who can read it
    /// below can read it because a grant says so and for no other reason.
    /// </summary>
    private async Task<Guid> CreatePlanAsync(string title, int? maxParticipants)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = Array.Empty<object>(),
            visibility = "private",
            maxParticipants,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);

        var created = JsonDocument.Parse(payload).RootElement;
        if (maxParticipants is null)
        {
            created.GetProperty("maxParticipants").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        else
        {
            created.GetProperty("maxParticipants").GetInt32().ShouldBe(maxParticipants.Value);
        }

        return created.GetProperty("id").GetGuid();
    }

    private async Task GrantReadAsync(Guid tripId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is reserved for
            // the feature-domain foreign key.
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        mate?.Dispose();
        factory.Dispose();
    }
}
