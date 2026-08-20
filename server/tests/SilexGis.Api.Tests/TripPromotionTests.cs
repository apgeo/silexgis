// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.TripLogs;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Turning what people said about a trip into the record of who was on it.
/// </summary>
/// <remarks>
/// <para>
/// Two records, and the whole point of the act is that it is the only bridge between them: the
/// answers are what people meant to do, the trip's list of people is what happened. Everything
/// that counts attendance, tallies hours underground or credits somebody with a first visit reads
/// the second and never the first, so a test here that only checked the numbers coming back would
/// miss the thing that matters — what landed in the table those readers use.
/// </para>
/// <para>
/// Every plan below is created private and opened one explicit grant at a time, and the person
/// refused is always a plain reader holding nothing. The group ordinary accounts are put in reads
/// and writes past visibility at the widest scope, so an account that "cannot" do something while
/// holding that membership proves nothing at all — and every refusal is asserted beside the grant
/// it is the absence of, so a route that refused everybody would fail here rather than look safe.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TripPromotionTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!; // Editor; owns each plan, so writes it
    private HttpClient mate = null!;      // Viewer; given the read on a plan and nothing else
    private HttpClient stranger = null!;  // Viewer; given nothing at all

    private Guid mateId;
    private Guid mateCaver;
    private Guid partnerId;
    private Guid partnerCaver;
    private Guid latecomerId;
    private Guid latecomerCaver;

    /// <summary>People in the directory with no account between them, which is most of a club.</summary>
    private readonly List<Guid> cavers = [];

    public TripPromotionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpr-org-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpr-mate-{suffix}@t.local");
        partnerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpr-prt-{suffix}@t.local");
        latecomerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpr-late-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpr-str-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            mateCaver = (await db.Cavers.FirstAsync(c => c.UserId == mateId)).Id;
            partnerCaver = (await db.Cavers.FirstAsync(c => c.UserId == partnerId)).Id;
            latecomerCaver = (await db.Cavers.FirstAsync(c => c.UserId == latecomerId)).Id;

            for (var i = 0; i < 6; i++)
            {
                var caver = new Caver { FullName = $"Promoted {i:00} {suffix}" };
                db.Cavers.Add(caver);
                cavers.Add(caver.Id);
            }

            await db.SaveChangesAsync();
        }

        organiser = await AuthHelper.BearerClientAsync(factory, $"tpr-org-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"tpr-mate-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"tpr-str-{suffix}@t.local");
    }

    /// <summary>
    /// The heart of it. Whoever holds a place on the trip is written into its list of people;
    /// whoever said yes and is waiting for a place, and whoever declined, are not — and both
    /// halves are asserted together, because writing everybody in would satisfy "the ones who
    /// said yes are on it" while quietly putting the whole waiting list into every count of who
    /// was underground.
    /// </summary>
    [Fact]
    public async Task Whoever_holds_a_place_is_written_in_and_the_waiting_and_the_declined_are_not()
    {
        var trip = await CreatePlanAsync("Room for two", maxParticipants: 2);

        (await AnswerAsync(trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(trip, cavers[1], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(trip, cavers[2], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(trip, cavers[3], "no")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await MoveAsync(trip, "done");

        var result = await PromoteAsync(organiser, trip);
        result.GetProperty("attending").GetInt32().ShouldBe(2);
        result.GetProperty("promoted").GetInt32().ShouldBe(2);
        result.GetProperty("alreadyNamed").GetInt32().ShouldBe(0);

        var roster = await RosterAsync(trip);
        roster.Count.ShouldBe(2);
        roster.ShouldAllBe(r => r.Role == TripParticipantRoleSeeds.ParticipantCode);

        // The two who got in are there, which is what stops a promotion that wrote nobody at all
        // from passing the two refusals below.
        roster.Select(r => r.CaverId).ShouldContain(cavers[0]);
        roster.Select(r => r.CaverId).ShouldContain(cavers[1]);

        // The third yes was over the limit and is waiting, not on the trip; the fourth said no.
        // Their answers are still on the list — nothing was destroyed, they were simply not
        // written into the record of who went.
        roster.Select(r => r.CaverId).ShouldNotContain(cavers[2]);
        roster.Select(r => r.CaverId).ShouldNotContain(cavers[3]);
        (await ListRowsAsync(trip)).Count.ShouldBe(4);
    }

    /// <summary>
    /// Nobody is told. Being named on a trip through its own write path sends word and should —
    /// that is news. This does not: everybody it writes in asked to be, was told when they were
    /// asked, and answered. The ordinary path is exercised in the same test, on the same trip, in
    /// the same state, so a run where notifications were switched off wholesale fails here rather
    /// than reading as the silence this act is supposed to keep.
    /// </summary>
    [Fact]
    public async Task Writing_the_answers_in_tells_nobody_though_naming_somebody_on_the_trip_does()
    {
        var trip = await CreatePlanAsync("Told once, not twice", maxParticipants: null);

        // All three are let in on the plan first, because being told about a trip takes being able
        // to read it: without the grant every one of these accounts would be silent for a reason
        // that has nothing to do with the act under test, and the silence would prove nothing.
        await GrantReadAsync(trip, mateId);
        await GrantReadAsync(trip, partnerId);
        await GrantReadAsync(trip, latecomerId);

        (await AnswerAsync(trip, mateCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(trip, partnerCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MoveAsync(trip, "done");

        var before = await NotifiedAsync(mateId, partnerId, latecomerId);

        var result = await PromoteAsync(organiser, trip);
        result.GetProperty("promoted").GetInt32().ShouldBe(2);

        var after = await NotifiedAsync(mateId, partnerId, latecomerId);
        after[mateId].ShouldBe(before[mateId]);
        after[partnerId].ShouldBe(before[partnerId]);

        // And the ordinary way onto a trip still tells the person it adds. Same trip, same state,
        // same moment: the two already on it gain nothing, because they were already there, and
        // the one this write actually adds is told.
        var write = await organiser.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
        {
            title = $"Told once, not twice {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = new object[]
            {
                new { caverId = mateCaver },
                new { caverId = partnerCaver },
                new { caverId = latecomerCaver },
            },
            visibility = "private",
        });
        write.StatusCode.ShouldBe(HttpStatusCode.OK, await write.Content.ReadAsStringAsync());

        var told = await NotifiedAsync(mateId, partnerId, latecomerId);
        told[latecomerId].ShouldBe(before[latecomerId] + 1);
        told[mateId].ShouldBe(before[mateId]);
        told[partnerId].ShouldBe(before[partnerId]);
    }

    /// <summary>
    /// Writing the answers into the trip is running the trip. A reader who was let in on the plan
    /// may see who is coming and may answer for themselves, and neither of those is authority to
    /// say who was on it — while somebody who cannot read the plan at all is answered as though
    /// it were not there, so a refusal never confirms a trip exists.
    /// </summary>
    [Fact]
    public async Task Writing_the_answers_in_takes_the_right_to_write_the_trip()
    {
        var trip = await CreatePlanAsync("Not yours to close", maxParticipants: null);
        (await AnswerAsync(trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MoveAsync(trip, "done");

        // Nothing at all: the trip is not there as far as this caller is concerned.
        var absent = await stranger.PostAsync(PromotePath(trip), null);
        var absentBody = await absent.Content.ReadAsStringAsync();
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound, absentBody);
        absentBody.ShouldContain(TripInvitationEndpoints.TripNotFoundCode);

        // Let in on the plan and no further: the list is readable, the act is not.
        await GrantReadAsync(trip, mateId);
        (await mate.GetAsync($"/api/v1/trip-logs/{trip}/invitations/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await mate.PostAsync(PromotePath(trip), null);
        var refusedBody = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, refusedBody);
        refusedBody.ShouldContain(TripInvitationEndpoints.PromoteForbiddenCode);

        // Whoever writes the trip does it, which is what makes the two refusals above a rule about
        // who is asking rather than a route that never worked.
        var allowed = await organiser.PostAsync(PromotePath(trip), null);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await RosterAsync(trip)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A trip's list of people is the record of who was on it, so it is not written for an
    /// afternoon still ahead. Stating as fact who went on a trip that has not happened would put
    /// hours underground and first visits into a person's history for a day nobody has lived yet.
    /// </summary>
    [Fact]
    public async Task A_trip_that_has_not_happened_yet_has_no_list_of_who_was_on_it()
    {
        var trip = await CreatePlanAsync("Still ahead", maxParticipants: null);
        (await AnswerAsync(trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var early = await organiser.PostAsync(PromotePath(trip), null);
        var earlyBody = await early.Content.ReadAsStringAsync();
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict, earlyBody);
        earlyBody.ShouldContain(TripInvitationEndpoints.PromoteTooEarlyCode);
        (await RosterAsync(trip)).ShouldBeEmpty();

        // The same caller, the same answers, once the trip has happened — so the refusal is about
        // the trip lying ahead and not about the request.
        await MoveAsync(trip, "done");
        (await PromoteAsync(organiser, trip)).GetProperty("promoted").GetInt32().ShouldBe(1);
        (await RosterAsync(trip)).Count.ShouldBe(1);

        // And a trip already announced is still open to it: announcing is only reachable from
        // finished, so the trip has certainly happened, and somebody who wrote it up before
        // recording who went would otherwise have to send it back to the workshop to do so.
        (await AnswerAsync(trip, cavers[1], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MoveAsync(trip, "published");
        (await PromoteAsync(organiser, trip)).GetProperty("promoted").GetInt32().ShouldBe(1);
        (await RosterAsync(trip)).Count.ShouldBe(2);
    }

    /// <summary>
    /// Doing it again changes nothing, and somebody already recorded as having done a job on the
    /// trip keeps that job rather than gaining a plainer row beside it. One person on one trip is
    /// one row per job, so a second row would be counted as a second person by everything that
    /// counts rows — and the act still leaves a line on the trip's trail even when it wrote
    /// nobody, because "somebody did this and it changed nothing" is what a person reconstructing
    /// how a name got onto a trip needs to be able to read.
    /// </summary>
    [Fact]
    public async Task Doing_it_twice_writes_nobody_twice_and_leaves_another_job_alone()
    {
        var trip = await CreatePlanAsync("Led and promoted", maxParticipants: null);
        var leaderRole = await RoleIdAsync("leader");

        var named = await organiser.PutWithIfMatchAsync($"/api/v1/trip-logs/{trip}", new
        {
            title = $"Led and promoted {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = new object[] { new { caverId = cavers[0], roleId = leaderRole } },
            visibility = "private",
        });
        named.StatusCode.ShouldBe(HttpStatusCode.OK, await named.Content.ReadAsStringAsync());

        (await AnswerAsync(trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(trip, cavers[1], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MoveAsync(trip, "done");

        var first = await PromoteAsync(organiser, trip);
        first.GetProperty("attending").GetInt32().ShouldBe(2);
        first.GetProperty("promoted").GetInt32().ShouldBe(1);
        first.GetProperty("alreadyNamed").GetInt32().ShouldBe(1);

        var roster = await RosterAsync(trip);
        roster.Count.ShouldBe(2);
        roster.Single(r => r.CaverId == cavers[0]).Role.ShouldBe("leader");
        roster.Single(r => r.CaverId == cavers[1]).Role.ShouldBe(TripParticipantRoleSeeds.ParticipantCode);

        var second = await PromoteAsync(organiser, trip);
        second.GetProperty("promoted").GetInt32().ShouldBe(0);
        second.GetProperty("alreadyNamed").GetInt32().ShouldBe(2);
        (await RosterAsync(trip)).Count.ShouldBe(2);

        // Both acts are on the trip's own trail, including the one that wrote nobody.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var key = trip.ToString();
        // Read back and sifted here rather than in the query: the change set is stored as a JSON
        // document, and asking the database to match text inside one is not a thing it will do.
        var trail = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(TripLog) && a.EntityId == key)
            .ToListAsync();
        var acts = trail.Where(a => a.Changes?.Contains("Roster", StringComparison.Ordinal) == true).ToList();
        acts.Count.ShouldBe(2);
        acts.ShouldAllBe(a => a.Action == AuditActions.Updated);
        acts.ShouldAllBe(a => a.Changes!.Contains("promoted"));

        // Counts of what the act did, and no "before" invented beside any of them. A prior value
        // here would be read as a field that held it, and the history offers those back as
        // something to restore — putting "Roster" back to nothing is not a thing anybody can mean.
        acts.ShouldAllBe(a => !a.Changes!.Contains("\"old\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// Promotion is a second writer of the trip's list of people, so it moves the trip's version.
    /// The trip's own write path reconciles that list whole — everybody not named in the request
    /// is removed — and its only guard against a lost update is the version somebody last loaded.
    /// Leaving the version where it was would let a title correction typed before the promotion
    /// ran pass the precondition and silently delete every row the promotion wrote.
    /// </summary>
    [Fact]
    public async Task Promoting_moves_the_trips_version_so_a_stale_edit_cannot_erase_what_it_wrote()
    {
        var trip = await CreatePlanAsync("Stale edit", maxParticipants: null);

        (await AnswerAsync(trip, cavers[0], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(trip, cavers[1], "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await MoveAsync(trip, "done");

        // What an organiser with the trip open is holding: the version as it stood after every
        // other change to the trip and before anybody turned the answers into its list of people,
        // so nothing but the promotion can be what moves it.
        var loaded = await organiser.GetAsync($"/api/v1/trip-logs/{trip}");
        loaded.StatusCode.ShouldBe(HttpStatusCode.OK, await loaded.Content.ReadAsStringAsync());
        var stale = loaded.Headers.ETag?.Tag;
        stale.ShouldNotBeNullOrEmpty();

        (await PromoteAsync(organiser, trip)).GetProperty("promoted").GetInt32().ShouldBe(2);
        (await RosterAsync(trip)).Count.ShouldBe(2);

        // The stale edit carries the roster its author saw, which was nobody. It is refused on the
        // precondition rather than accepted and allowed to reconcile the list down to nothing.
        var staleEdit = await organiser.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{trip}",
            new
            {
                title = $"Stale edit corrected {Guid.NewGuid():N}",
                tripDate = "2026-09-12",
                participants = Array.Empty<object>(),
                visibility = "private",
            },
            stale!);
        staleEdit.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionFailed, await staleEdit.Content.ReadAsStringAsync());
        (await RosterAsync(trip)).Count.ShouldBe(2);

        // And the same edit, made against the version the trip is actually at, goes through — so
        // this is a test of a moved version and not of a route that has stopped accepting edits.
        var reloaded = await organiser.GetAsync($"/api/v1/trip-logs/{trip}");
        var fresh = reloaded.Headers.ETag?.Tag;
        fresh.ShouldNotBe(stale);

        var informed = await organiser.PutWithIfMatchAsync(
            $"/api/v1/trip-logs/{trip}",
            new
            {
                title = $"Stale edit corrected {Guid.NewGuid():N}",
                tripDate = "2026-09-12",
                participants = Array.Empty<object>(),
                visibility = "private",
            },
            fresh!);
        informed.StatusCode.ShouldBe(HttpStatusCode.OK, await informed.Content.ReadAsStringAsync());
    }

    // ---- helpers

    private static string PromotePath(Guid tripId) => $"/api/v1/trip-logs/{tripId}/invitations/promote";

    private Task<HttpResponseMessage> AnswerAsync(Guid tripId, Guid caverId, string response) =>
        organiser.PutAsJsonAsync(
            $"/api/v1/trip-logs/{tripId}/invitations/{caverId}/response", new { response });

    private async Task MoveAsync(Guid tripId, string state)
    {
        var response = await organiser.PostWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}/state", new { state });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> PromoteAsync(HttpClient client, Guid tripId)
    {
        var response = await client.PostAsync(PromotePath(tripId), null);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<List<JsonElement>> ListRowsAsync(Guid tripId)
    {
        var response = await organiser.GetAsync($"/api/v1/trip-logs/{tripId}/invitations/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return
        [
            .. JsonDocument.Parse(payload).RootElement.Clone()
                .GetProperty("invitations").EnumerateArray(),
        ];
    }

    /// <summary>
    /// The trip's list of people straight out of the table, because that is the table every count
    /// of attendance reads and the DTO is one reading of it rather than the fact itself.
    /// </summary>
    private async Task<List<RosterRow>> RosterAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from participant in db.TripLogParticipants.AsNoTracking()
                      join role in db.TripParticipantRoles.AsNoTracking()
                        on participant.RoleId equals role.Id
                      where participant.TripLogId == tripId
                      select new RosterRow(participant.CaverId, role.Code))
            .ToListAsync();
    }

    private async Task<long> RoleIdAsync(string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripParticipantRoles.AsNoTracking()
            .Where(r => r.Code == code).Select(r => r.Id).FirstAsync();
    }

    /// <summary>How many trip-participation messages each of these accounts has ever been queued.</summary>
    private async Task<Dictionary<Guid, int>> NotifiedAsync(params Guid[] userIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var counts = new Dictionary<Guid, int>();
        foreach (var userId in userIds)
        {
            counts[userId] = await db.NotificationOutbox.AsNoTracking()
                .CountAsync(n => n.UserId == userId
                    && n.Category == NotificationCategory.TripParticipation);
        }

        return counts;
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
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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
        stranger?.Dispose();
        factory.Dispose();
    }

    private sealed record RosterRow(Guid CaverId, string Role);
}
