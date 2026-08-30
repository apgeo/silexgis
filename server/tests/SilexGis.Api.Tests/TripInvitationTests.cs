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
/// Who was asked on a trip, what they have said, and who may say it for them.
/// <para>
/// The plan every test here works on is created private and then opened to exactly the people a
/// test needs, one explicit grant at a time. That is deliberate and it is the only fixture that
/// proves anything: the group every ordinary account is put in reads and writes past visibility
/// at the widest scope, so an account that "cannot see" a trip while holding that membership
/// proves nothing at all. Everybody refused here is a plain reader holding nothing, and every
/// refusal is asserted beside the permission it is the absence of.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripInvitationTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!; // Editor; owns the plan, so writes it
    private HttpClient mate = null!;      // Viewer; given the read on the plan and nothing else
    private HttpClient stranger = null!;  // Viewer; given nothing at all
    private HttpClient admin = null!;     // Full administrator

    private Guid organiserId;
    private Guid mateId;
    private Guid strangerId;

    private Guid organiserCaver;
    private Guid mateCaver;
    private Guid strangerCaver;
    private Guid adminCaver;

    /// <summary>Somebody in the directory who has never had an account, which is most people.</summary>
    private Guid accountlessCaver;

    public TripInvitationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        organiserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ti-org-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ti-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ti-str-{suffix}@t.local");
        var adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"ti-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            organiserCaver = await CaverOfAsync(db, organiserId);
            mateCaver = await CaverOfAsync(db, mateId);
            strangerCaver = await CaverOfAsync(db, strangerId);
            adminCaver = await CaverOfAsync(db, adminId);

            var accountless = new Caver { FullName = $"Ana Accountless {suffix}" };
            db.Cavers.Add(accountless);
            await db.SaveChangesAsync();
            accountlessCaver = accountless.Id;
        }

        organiser = await AuthHelper.BearerClientAsync(factory, $"ti-org-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"ti-mate-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"ti-str-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"ti-adm-{suffix}@t.local");
    }

    /// <summary>
    /// The whole point of letting a reader answer: the member who sees the trip is the member
    /// whose answer it is, and no right over the trip is asked for on top. The same member has no
    /// business answering for anybody else — putting words in another person's mouth on a plan
    /// that may be read while somebody is looking for a caving party is not a reader's power.
    /// </summary>
    [Fact]
    public async Task A_reader_answers_for_themselves_and_for_nobody_else()
    {
        var trip = await CreatePlanAsync("Reader answers");
        await GrantReadAsync(trip, mateId);

        var mine = await AnswerAsync(mate, trip, mateCaver, "yes");
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());
        var answered = await BodyAsync(mine);
        answered.GetProperty("response").GetString().ShouldBe("yes");
        answered.GetProperty("caverId").GetGuid().ShouldBe(mateCaver);
        answered.GetProperty("respondedByUserId").GetGuid().ShouldBe(mateId);
        answered.GetProperty("mayAnswer").GetBoolean().ShouldBeTrue();

        var theirs = await AnswerAsync(mate, trip, strangerCaver, "yes");
        theirs.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await theirs.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.answer_forbidden");

        // And the list says so before it is tried: the server answers whose answer this reader
        // may give rather than leaving the surface to guess at a rule with three ways into it.
        var listed = await ListAsync(mate, trip);
        listed.Single(x => x.GetProperty("caverId").GetGuid() == mateCaver)
            .GetProperty("mayAnswer").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// Somebody has to be able to write down the answer of a member who telephoned, so whoever
    /// runs the trip answers for anybody — including for the majority of the directory who have
    /// no account and could otherwise never be recorded. The row keeps both facts apart: who the
    /// answer is about, and who wrote it down.
    /// </summary>
    [Fact]
    public async Task Whoever_runs_the_trip_answers_for_anybody_and_a_reader_cannot_put_it_straight()
    {
        var trip = await CreatePlanAsync("Organiser answers");
        await GrantReadAsync(trip, mateId);

        var forMate = await AnswerAsync(organiser, trip, mateCaver, "yes", "phoned on Tuesday");
        forMate.StatusCode.ShouldBe(HttpStatusCode.OK, await forMate.Content.ReadAsStringAsync());
        var written = await BodyAsync(forMate);
        written.GetProperty("caverId").GetGuid().ShouldBe(mateCaver);
        written.GetProperty("respondedByUserId").GetGuid().ShouldBe(organiserId);
        written.GetProperty("note").GetString().ShouldBe("phoned on Tuesday");

        var forNobody = await AnswerAsync(organiser, trip, accountlessCaver, "maybe");
        forNobody.StatusCode.ShouldBe(HttpStatusCode.OK, await forNobody.Content.ReadAsStringAsync());
        (await BodyAsync(forNobody)).GetProperty("response").GetString().ShouldBe("maybe");

        // The reader may rewrite their own answer as often as they like, and nobody else's.
        (await AnswerAsync(mate, trip, mateCaver, "no")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(mate, trip, organiserCaver, "no")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AnswerAsync(mate, trip, accountlessCaver, "no")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// An answer that has to be put straight on a trip nobody is left to run is a thing an
    /// administrator does. The reader beside them holds the read on the same plan and cannot.
    /// </summary>
    [Fact]
    public async Task A_full_administrator_puts_an_answer_straight_where_a_plain_reader_cannot()
    {
        var trip = await CreatePlanAsync("Administrator answers");
        await GrantReadAsync(trip, mateId);
        (await AnswerAsync(mate, trip, mateCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var corrected = await AnswerAsync(admin, trip, mateCaver, "no", "withdrew at the weekend");
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        (await BodyAsync(corrected)).GetProperty("response").GetString().ShouldBe("no");

        (await AnswerAsync(mate, trip, adminCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A plan nobody has been shown is a plan that does not exist, on the writes as much as on the
    /// reading: a refusal that said "not yours" would confirm the trip is there, and confirm one
    /// row at a time which people are being considered for it. The positive half is the same
    /// account after one explicit grant — and it shows what the grant is and is not, because
    /// reading a plan lets somebody answer for themselves and never lets them edit the list.
    /// </summary>
    [Fact]
    public async Task Somebody_never_shown_the_plan_is_told_there_is_none_and_the_read_alone_does_not_edit_the_list()
    {
        var trip = await CreatePlanAsync("Unshown");

        (await stranger.GetAsync(Path(trip))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AnswerAsync(stranger, trip, strangerCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await InviteAsync(stranger, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await GrantReadAsync(trip, strangerId);

        (await stranger.GetAsync(Path(trip))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(stranger, trip, strangerCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var invited = await InviteAsync(stranger, trip, mateCaver);
        invited.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await invited.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.invite_forbidden");
    }

    /// <summary>
    /// One person, one trip, one standing answer — so changing your mind rewrites what you said
    /// rather than leaving you holding two answers that disagree. The key is the database's and
    /// not the endpoint's, so a second row is refused however it is attempted.
    /// </summary>
    [Fact]
    public async Task One_person_holds_one_answer_about_one_trip()
    {
        var trip = await CreatePlanAsync("One answer");
        await GrantReadAsync(trip, mateId);

        (await AnswerAsync(mate, trip, mateCaver, "maybe")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = (await BodyAsync(await AnswerAsync(mate, trip, mateCaver, "maybe")))
            .GetProperty("respondedAt").GetDateTimeOffset();

        var changed = await BodyAsync(await AnswerAsync(mate, trip, mateCaver, "yes"));
        changed.GetProperty("response").GetString().ShouldBe("yes");

        // Restamped, because somebody who said maybe and later yes joined the queue when they
        // said yes — the order a trip fills up in is the order of the answers standing now.
        changed.GetProperty("respondedAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(first);

        var rows = await ListAsync(mate, trip);
        rows.Count(x => x.GetProperty("caverId").GetGuid() == mateCaver).ShouldBe(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripInvitations.Add(new TripInvitation { TripLogId = trip, CaverId = mateCaver });
        await Should.ThrowAsync<DbUpdateException>(db.SaveChangesAsync());
    }

    /// <summary>
    /// Being asked and answering are facts about the trip, so they surface on the trip's own
    /// timeline rather than in a history of their own that nobody would think to open — and the
    /// person who cannot read the trip is shown neither.
    /// </summary>
    [Fact]
    public async Task Being_asked_and_answering_land_on_the_trip_s_own_timeline()
    {
        var trip = await CreatePlanAsync("Timeline");
        await GrantReadAsync(trip, mateId);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AnswerAsync(mate, trip, mateCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var timeline = await organiser.GetAsync($"/api/v1/history?entityType=tripLog&entityId={trip}&pageSize=200");
        var body = await timeline.Content.ReadAsStringAsync();
        timeline.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var events = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityType").GetString() == nameof(TripInvitation))
            .ToList();
        events.Select(e => e.GetProperty("action").GetString()).ShouldContain("created");
        events.Select(e => e.GetProperty("action").GetString()).ShouldContain("updated");

        (await stranger.GetAsync($"/api/v1/history?entityType=tripLog&entityId={trip}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Somebody who was never asked still answers — a member who sees a trip their club is
    /// running and says they are coming — which is why the invitation is a set of fields on the
    /// answer's own row rather than a row of its own. Asking them afterwards records that they
    /// were asked and leaves what they said exactly as it stands.
    /// </summary>
    [Fact]
    public async Task An_answer_needs_no_invitation_and_being_asked_afterwards_does_not_overwrite_it()
    {
        var trip = await CreatePlanAsync("Uninvited");
        await GrantReadAsync(trip, mateId);

        var uninvited = await BodyAsync(await AnswerAsync(mate, trip, mateCaver, "yes"));
        uninvited.GetProperty("invitedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        uninvited.GetProperty("response").GetString().ShouldBe("yes");

        var asked = await InviteAsync(organiser, trip, mateCaver);
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var stamped = await BodyAsync(asked);
        stamped.GetProperty("invitedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        stamped.GetProperty("invitedByUserId").GetGuid().ShouldBe(organiserId);
        stamped.GetProperty("response").GetString().ShouldBe("yes");

        // Somebody genuinely new is a new row, and is on the list without having said anything.
        var fresh = await InviteAsync(organiser, trip, strangerCaver);
        fresh.StatusCode.ShouldBe(HttpStatusCode.Created, await fresh.Content.ReadAsStringAsync());
        var pending = await BodyAsync(fresh);
        pending.GetProperty("response").GetString().ShouldBe("pending");
        pending.GetProperty("respondedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The shapes a request has to have, and the account it has to come from. The empty body is
    /// the one that matters: the first value of the answer vocabulary is "has not answered", so a
    /// field that was not nullable would read a body saying nothing as somebody taking their
    /// answer back and report success.
    /// </summary>
    [Fact]
    public async Task An_answer_that_says_nothing_is_refused_and_so_is_one_naming_nobody()
    {
        var trip = await CreatePlanAsync("Refusals");

        var silent = await organiser.PutAsJsonAsync(Path(trip, mateCaver), new { });
        silent.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await silent.Content.ReadAsStringAsync());

        var nonsense = await AnswerAsync(organiser, trip, Guid.NewGuid(), "yes");
        nonsense.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await nonsense.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.caver_unknown");

        var tooLong = await organiser.PutAsJsonAsync(
            Path(trip, mateCaver), new { response = "yes", note = new string('x', 501) });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The same request from the same account, with only the thing that was wrong put right.
        (await AnswerAsync(organiser, trip, mateCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(Path(trip))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync(Path(trip, mateCaver), new { response = "yes" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Somebody put on the list by mistake comes off it, and only whoever runs the trip may take
    /// them off. The alternative — writing a "no" in their name — would be a refusal they never
    /// gave, recorded on a plan that may be read while somebody is looking for a caving party,
    /// which is exactly the falsification keeping the subject and the writer apart exists to stop.
    /// </summary>
    [Fact]
    public async Task Whoever_runs_the_trip_takes_somebody_off_the_list_and_a_reader_cannot()
    {
        var trip = await CreatePlanAsync("Taken off");
        await GrantReadAsync(trip, mateId);

        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AnswerAsync(mate, trip, mateCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A reader with the plan open may not edit who is on it — not even the person they could
        // answer for, which is themselves.
        var byReader = await mate.DeleteAsync(RemovePath(trip, mateCaver));
        byReader.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await byReader.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.remove_forbidden");
        (await ListAsync(organiser, trip)).Count.ShouldBe(2);

        // Somebody who cannot read the plan at all is told nothing about it, on this route as on
        // every other.
        (await stranger.DeleteAsync(RemovePath(trip, strangerCaver)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var byOrganiser = await organiser.DeleteAsync(RemovePath(trip, strangerCaver));
        byOrganiser.StatusCode.ShouldBe(HttpStatusCode.NoContent, await byOrganiser.Content.ReadAsStringAsync());

        var left = await ListAsync(organiser, trip);
        left.Count.ShouldBe(1);
        left.Single().GetProperty("caverId").GetGuid().ShouldBe(mateCaver);

        // The row is gone rather than marked, so a second removal has nothing to remove and says
        // so with a code of its own — not the one that means "no such person in the directory".
        var again = await organiser.DeleteAsync(RemovePath(trip, strangerCaver));
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await again.Content.ReadAsStringAsync()).ShouldContain("trip_invitation.not_on_list");
    }

    /// <summary>
    /// A body carrying no answer is refused under a code that names the answer, not the note: a
    /// client mapping the note's code highlights an empty field on a request whose note was fine.
    /// </summary>
    [Fact]
    public async Task An_answer_missing_and_a_note_too_long_are_two_different_refusals()
    {
        var trip = await CreatePlanAsync("Two refusals");

        var silent = await organiser.PutAsJsonAsync(Path(trip, mateCaver), new { response = (string?)null });
        silent.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await silent.Content.ReadAsStringAsync());
        var silentBody = await silent.Content.ReadAsStringAsync();
        silentBody.ShouldContain("Response");

        // The note was not what was wrong with it, and nothing in the refusal says it was — a
        // client that read the note's name here would put a mark against an empty field.
        silentBody.ShouldNotContain("Note");
        silentBody.ShouldNotContain("trip_invitation.note_invalid");

        var tooLong = await organiser.PutAsJsonAsync(
            Path(trip, mateCaver), new { response = "yes", note = new string('x', 501) });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).ShouldContain("Note");

        // And the same request with both put right is accepted, so neither refusal is a route that
        // says no to everything.
        (await AnswerAsync(organiser, trip, mateCaver, "yes", "back before dark"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers

    private static string RemovePath(Guid tripId, Guid caverId) =>
        $"/api/v1/trip-logs/{tripId}/invitations/{caverId}";

    private static string Path(Guid tripId) => $"/api/v1/trip-logs/{tripId}/invitations/";

    private static string Path(Guid tripId, Guid caverId) =>
        $"/api/v1/trip-logs/{tripId}/invitations/{caverId}/response";

    private static Task<HttpResponseMessage> AnswerAsync(
        HttpClient client, Guid tripId, Guid caverId, string response, string? note = null) =>
        client.PutAsJsonAsync(Path(tripId, caverId), new { response, note });

    private static Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid tripId, Guid caverId) =>
        client.PostAsJsonAsync(Path(tripId), new { caverId });

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<Guid> CaverOfAsync(SilexGisDbContext db, Guid userId) =>
        (await db.Cavers.FirstAsync(c => c.UserId == userId)).Id;

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync(Path(tripId));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("invitations").EnumerateArray()];
    }

    /// <summary>
    /// A plan written up through the door that leaves it private, so that everybody who can read
    /// it below can read it because a grant says so and for no other reason.
    /// </summary>
    private async Task<Guid> CreatePlanAsync(string title)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2026-09-12",
            participants = Array.Empty<object>(),
            visibility = "private",
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
        admin?.Dispose();
        factory.Dispose();
    }
}
