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
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a trip being planned tells the people it concerns, and what it never tells anybody.
/// <para>
/// Every plan here is created private and then opened to exactly the people a test needs, one
/// explicit grant at a time. That is the only fixture that proves anything about who is left out:
/// the group every ordinary account is put in reads past visibility at the widest scope, so an
/// account that "cannot see" a trip while holding that membership proves nothing. Everybody
/// refused here is a plain reader holding nothing, and the person who is told is asserted in the
/// same test as the person who is not.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripPlanNotificationTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!; // Editor; owns the plan, so writes it
    private HttpClient mate = null!;      // Viewer; given the read on the plan and nothing else
    private HttpClient stranger = null!;  // Viewer; given nothing at all
    private HttpClient keeper = null!;    // Editor; owns the caves, and never does the asking
    private HttpClient administrator = null!; // Admin; a full administrator, who can open anything

    private Guid organiserId;
    private Guid mateId;
    private Guid strangerId;
    private Guid keeperId;
    private Guid administratorId;

    private Guid organiserCaver;
    private Guid mateCaver;
    private Guid strangerCaver;

    /// <summary>Somebody in the directory who has never had an account, which is most people.</summary>
    private Guid accountlessCaver;

    private long caveTypeId;

    public TripPlanNotificationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        organiserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpn-org-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpn-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpn-str-{suffix}@t.local");
        keeperId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpn-keep-{suffix}@t.local");
        administratorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tpn-adm-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            organiserCaver = await CaverOfAsync(db, organiserId);
            mateCaver = await CaverOfAsync(db, mateId);
            strangerCaver = await CaverOfAsync(db, strangerId);
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

            var accountless = new Caver { FullName = $"Ana Accountless {suffix}" };
            db.Cavers.Add(accountless);
            await db.SaveChangesAsync();
            accountlessCaver = accountless.Id;
        }

        organiser = await AuthHelper.BearerClientAsync(factory, $"tpn-org-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"tpn-mate-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"tpn-str-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"tpn-keep-{suffix}@t.local");
        administrator = await AuthHelper.BearerClientAsync(factory, $"tpn-adm-{suffix}@t.local");
    }

    /// <summary>
    /// Being asked on a trip is not being given it. Somebody
    /// put on the list of a plan they hold no reading of is told nothing at all — the message
    /// would be useless to them and would still hand them the plan's title and its date, outside
    /// every filter the reading paths apply.
    /// </summary>
    [Fact]
    public async Task Somebody_who_may_not_read_the_plan_is_not_told_they_were_asked_on_it()
    {
        var trip = await CreatePlanAsync("Asked but shut out");
        await ProposeAsync(trip);

        // The reader and the outsider differ in exactly one thing: an entry saying the reader may
        // read this trip. Both are Viewers holding nothing else.
        await GrantReadAsync(trip, mateId);

        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanInvitation)).Count.ShouldBe(1);
        (await NoticesForAsync(strangerId, MessageTemplateCatalog.NotifyTripPlanInvitation)).ShouldBeEmpty();
    }

    /// <summary>
    /// An explicit refusal, not merely the absence of a grant: a reader who could otherwise open
    /// the plan and is denied it by a rule aimed at them is told nothing either, and the same
    /// invitation reaches the reader beside them.
    /// </summary>
    [Fact]
    public async Task A_reader_denied_the_plan_is_not_told_they_were_asked_on_it()
    {
        var trip = await CreatePlanAsync("Asked but denied");
        await ProposeAsync(trip);

        await GrantReadAsync(trip, mateId);
        await GrantReadAsync(trip, strangerId);
        await DenyReadAsync(trip, strangerId);

        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanInvitation)).Count.ShouldBe(1);
        (await NoticesForAsync(strangerId, MessageTemplateCatalog.NotifyTripPlanInvitation)).ShouldBeEmpty();
    }

    /// <summary>
    /// Nobody is told about their own action, and asking again is not asking again: a create route
    /// a client retries must not put a second message in the post.
    /// </summary>
    [Fact]
    public async Task Nobody_is_told_they_asked_themselves_and_asking_twice_sends_once()
    {
        var trip = await CreatePlanAsync("Asked myself");
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);

        // The organiser puts their own name on the list — a thing somebody organising a trip does
        // — and hears nothing about it, because they did it.
        (await InviteAsync(organiser, trip, organiserCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await NoticesForAsync(organiserId, MessageTemplateCatalog.NotifyTripPlanInvitation)).ShouldBeEmpty();

        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanInvitation)).Count.ShouldBe(1);
    }

    /// <summary>
    /// Somebody in the directory with no account is asked without error and told nothing: there is
    /// nowhere to tell them. The person beside them who holds an account is told, so the test is
    /// about the account and not about the invitation failing.
    /// </summary>
    [Fact]
    public async Task An_invitee_with_no_account_is_asked_without_error_and_told_nothing()
    {
        var trip = await CreatePlanAsync("Asked without an account");
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);

        (await InviteAsync(organiser, trip, accountlessCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var sent = await NoticesForTemplateAsync(MessageTemplateCatalog.NotifyTripPlanInvitation, trip);
        sent.Select(x => x.UserId).ShouldBe([mateId]);
    }

    /// <summary>
    /// A plan people are expecting to go on changes under them, and they are told — but a draft
    /// changing tells nobody, because telling somebody a draft changed would be telling them it
    /// exists, which is the one thing a draft does not do.
    /// </summary>
    [Fact]
    public async Task A_change_reaches_the_people_asked_but_a_draft_changing_reaches_nobody()
    {
        var trip = await CreatePlanAsync("Changed under them");
        await GrantReadAsync(trip, mateId);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        // On the same list and holding nothing on the trip, so the silence asserted at the end is
        // the readability filter refusing somebody who is genuinely a candidate, not the absence
        // of a candidate.
        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Still a draft: an edit to it is the author still deciding what it says.
        await EditAsync(trip, "Changed under them, first thought");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).ShouldBeEmpty();

        await ProposeAsync(trip);
        await EditAsync(trip, "Changed under them, floated");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(1);

        // And the outsider on the same trip's list, who may not read it, hears about none of it.
        (await NoticesForAsync(strangerId, MessageTemplateCatalog.NotifyTripPlanChanged)).ShouldBeEmpty();
    }

    /// <summary>
    /// Moving a plan is changing it, and the move people most need to hear about — a trip they are
    /// expecting on a date being put back — reaches them exactly as an edit to its text does. A
    /// plan leaving the workshop stays silent, because nobody was expecting it yet, and one going
    /// back into the workshop stays silent too.
    /// </summary>
    [Fact]
    public async Task Moving_a_plan_tells_the_people_expecting_it_while_the_workshop_stays_silent()
    {
        var trip = await CreatePlanAsync("Put back the evening before");
        await GrantReadAsync(trip, mateId);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        // On the same list and holding nothing on the trip: the outsider is a real candidate the
        // readability filter refuses, not an absent one.
        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Leaving the workshop: the first anybody outside it hears of the plan should not be that
        // it changed.
        await TransitionAsync(trip, "proposed");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).ShouldBeEmpty();

        await TransitionAsync(trip, "planned");
        await TransitionAsync(trip, "confirmed");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(2);

        // The one this exists for: a confirmed trip put back to a date not yet chosen.
        await TransitionAsync(trip, "delayed");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(3);

        // And back to the workshop, which nobody is expecting anything from any more.
        await TransitionAsync(trip, "draft");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(3);

        (await NoticesForAsync(strangerId, MessageTemplateCatalog.NotifyTripPlanChanged)).ShouldBeEmpty();
    }

    /// <summary>
    /// A write that carries back exactly what it was given tells nobody: no column moved, so there
    /// is no change to announce. Saving an untouched form and restoring the version already loaded
    /// both do this, and on a plan with twenty people asked they would otherwise be twenty
    /// messages about nothing — which is how a whole category earns being muted.
    /// </summary>
    [Fact]
    public async Task A_write_that_changes_nothing_tells_nobody()
    {
        var trip = await CreatePlanAsync("Saved without an edit");
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = Body("Saved without an edit, renamed", []);
        await PutAsync(trip, body);
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(1);

        // The very same body again: the title it names is already the title stored.
        await PutAsync(trip, body);
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(1);

        // A real edit beside it, so the silence above is the unchanged write and not a broken path.
        await EditAsync(trip, "Saved with an edit after all");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanChanged)).Count.ShouldBe(2);
    }

    /// <summary>
    /// A cave named onto a trip after the people were asked is the same pairing arriving in the
    /// other order — a plan floated before its objective is settled — and it reaches somebody who
    /// can grant the cave just as asking somebody onto a trip that already names one does.
    /// </summary>
    [Fact]
    public async Task A_cave_named_after_the_asking_still_tells_somebody_who_can_grant()
    {
        var caveName = $"Peștera Târzie {Guid.NewGuid():N}"[..34];
        var caveId = await CreateCaveAsync(caveName, keeper);
        await GrantCaveReadAsync(caveId, organiserId);

        // No objective yet, so nothing to weigh anybody against when they are asked.
        var trip = await CreatePlanAsync("A plan with no objective yet");
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CaveNoticesForAsync(keeperId, caveId)).ShouldBeEmpty();

        (await mate.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await EditAsync(trip, "A plan with an objective after all", caveId);

        var told = await CaveNoticesForAsync(keeperId, caveId);
        told.Count.ShouldBe(1);
        told[0].Placeholders.ShouldContain(caveName);
        (await CaveNoticesForAsync(mateId, caveId)).ShouldBeEmpty(
            "the person who cannot open the cave is never told its name");

        // Naming the same cave again names nothing new, so nothing is said twice.
        await EditAsync(trip, "A plan whose objective has not moved", caveId);
        (await CaveNoticesForAsync(keeperId, caveId)).Count.ShouldBe(1);
    }

    /// <summary>
    /// Calling a trip off tells the people expecting to go on it, while entering the cancelled
    /// state still announces nothing by itself — the two are different questions and the codebase
    /// keeps them apart. The notice is the cancel path's own, sent deliberately.
    /// </summary>
    [Fact]
    public async Task Calling_a_trip_off_tells_its_people_while_the_state_itself_still_announces_nothing()
    {
        var trip = await CreatePlanAsync("Called off");
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);
        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await TransitionAsync(trip, "cancelled");

        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanCancelled)).Count.ShouldBe(1);
        (await NoticesForAsync(strangerId, MessageTemplateCatalog.NotifyTripPlanCancelled)).ShouldBeEmpty();

        // The state-entry notice stays silent, which is what the untouched suppression rule says:
        // a cancelled trip does not announce the roster's existence to it.
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripParticipation)).ShouldBeEmpty();
    }

    /// <summary>
    /// Calling off a draft tells nobody: the notice would be the first anybody heard of the trip.
    /// The same person on the same list is told when the trip they were expecting is called off,
    /// so the silence is the draft's and not the notifier being broken.
    /// </summary>
    [Fact]
    public async Task Calling_off_a_draft_tells_nobody()
    {
        var draft = await CreatePlanAsync("Called off from the workshop");
        await GrantReadAsync(draft, mateId);
        (await InviteAsync(organiser, draft, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await TransitionAsync(draft, "cancelled");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanCancelled)).ShouldBeEmpty();

        var floated = await CreatePlanAsync("Called off after being floated");
        await ProposeAsync(floated);
        await GrantReadAsync(floated, mateId);
        (await InviteAsync(organiser, floated, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await TransitionAsync(floated, "cancelled");
        (await NoticesForAsync(mateId, MessageTemplateCatalog.NotifyTripPlanCancelled)).Count.ShouldBe(1);
    }

    /// <summary>
    /// No message a plan sends names a cave, by name or by id.
    /// The recipient here may read the trip and may not read the cave it is about, so a body that
    /// carried either would be the back door around cave visibility that an invitation must not
    /// become. Asserted against the stored message rather than against the template, so that a
    /// placeholder added later fails this test rather than shipping.
    /// </summary>
    [Fact]
    public async Task No_message_a_plan_sends_names_the_cave_it_is_about()
    {
        var caveName = $"Peștera Secretă {Guid.NewGuid():N}"[..34];
        var caveId = await CreateCaveAsync(caveName);

        var trip = await CreatePlanAsync("Somewhere in particular", caveId);
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);

        // The reader holds the trip and nothing on the cave, which is exactly the person the rule
        // exists for. Their reading of the trip is asserted so the test cannot pass by the trip
        // being unreadable too.
        (await mate.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mate.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await EditAsync(trip, "Somewhere in particular, moved", caveId);
        await TransitionAsync(trip, "cancelled");

        var sent = await NoticesForAsync(mateId);
        sent.Count.ShouldBe(3, "asked, changed, called off");
        foreach (var notice in sent)
        {
            notice.Placeholders.ShouldNotContain(caveName, Case.Insensitive, notice.TemplateKey);
            notice.Placeholders.ShouldNotContain(caveId.ToString(), Case.Insensitive, notice.TemplateKey);
        }
    }

    /// <summary>
    /// Being asked on a trip grants nothing, so somebody who can grant is told instead. The cave's
    /// owner is that somebody: owning a row confers every action on it, including the right to
    /// change who else may read it.
    /// <para>
    /// The person asked is a plain reader holding one explicit grant on the trip and nothing at
    /// all on the cave, and both halves are asserted here — that the owner was told, and that the
    /// person the message is about was not, because a notice naming a cave to somebody shut out of
    /// it would be the disclosure this whole path exists to avoid.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_cave_s_owner_is_told_when_somebody_asked_cannot_open_it()
    {
        var caveName = $"Peștera Păzită {Guid.NewGuid():N}"[..34];
        var caveId = await CreateCaveAsync(caveName, keeper);
        await GrantCaveReadAsync(caveId, organiserId);

        var trip = await CreatePlanAsync("A trip to somewhere shut", caveId);
        await ProposeAsync(trip);
        await GrantReadAsync(trip, mateId);

        // The two readings the test turns on, asserted rather than assumed.
        (await keeper.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mate.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var told = await CaveNoticesForAsync(keeperId, caveId);
        told.Count.ShouldBe(1);
        told[0].Category.ShouldBe(NotificationCategory.TripPlanning);
        told[0].Placeholders.ShouldContain(caveName);

        (await CaveNoticesForAsync(mateId, caveId)).ShouldBeEmpty(
            "the person who cannot open the cave is never told its name");
    }

    /// <summary>
    /// A full administrator is told too, because membership of that group is itself the grant and
    /// therefore reaches every row without an entry anywhere. It is also the audience that makes
    /// the notice worth sending at all when the owner is the one doing the asking.
    /// </summary>
    [Fact]
    public async Task A_full_administrator_is_told_as_well()
    {
        var caveName = $"Peștera Adm {Guid.NewGuid():N}"[..30];
        var caveId = await CreateCaveAsync(caveName, keeper);
        await GrantCaveReadAsync(caveId, organiserId);

        var trip = await CreatePlanAsync("A trip an administrator hears about", caveId);
        await ProposeAsync(trip);

        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CaveNoticesForAsync(administratorId, caveId)).Count.ShouldBe(1);
        (await CaveNoticesForAsync(strangerId, caveId)).ShouldBeEmpty(
            "the person who cannot open the cave is never told its name");
    }

    /// <summary>
    /// Nothing is sent when the person asked can already open the cave: there is nothing for
    /// anybody to grant, and a message asking for a grant nobody needs trains its readers to
    /// ignore the ones that matter. The same asking on the same trip of somebody who genuinely
    /// cannot open it does send, so the silence here is the rule and not a broken path.
    /// </summary>
    [Fact]
    public async Task Nobody_is_told_when_the_person_asked_can_already_open_the_cave()
    {
        var caveName = $"Peștera Deschisă {Guid.NewGuid():N}"[..34];
        var caveId = await CreateCaveAsync(caveName, keeper);
        await GrantCaveReadAsync(caveId, organiserId);
        await GrantCaveReadAsync(caveId, mateId);

        var trip = await CreatePlanAsync("A trip one of them can already reach", caveId);
        await ProposeAsync(trip);

        (await mate.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stranger.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await InviteAsync(organiser, trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CaveNoticesForAsync(keeperId, caveId)).ShouldBeEmpty(
            "nothing needs granting to somebody who can already open it");

        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CaveNoticesForAsync(keeperId, caveId)).Count.ShouldBe(1);
    }

    /// <summary>
    /// The notice names a cave, so it obeys the same rule every message about a protected row
    /// obeys: it goes only to somebody whose own access opens that row, decided freshly for each
    /// of them. An owner locked out of their own cave by an explicit refusal is told nothing —
    /// while the full administrator, who is the recovery path out of exactly that state, still is.
    /// A notifier that disclosed a cave in the course of protecting it would be worse than one
    /// that sent nothing at all.
    /// </summary>
    [Fact]
    public async Task Nobody_is_told_about_a_cave_they_cannot_read_themselves()
    {
        var caveName = $"Peștera Interzisă {Guid.NewGuid():N}"[..34];
        var caveId = await CreateCaveAsync(caveName, keeper);
        await GrantCaveReadAsync(caveId, organiserId);
        // A refusal written straight onto the cave: it beats owning it, which is the one way an
        // owner can genuinely be shut out of their own row.
        await DenyCaveReadAsync(caveId, keeperId);

        var trip = await CreatePlanAsync("A trip its own keeper cannot follow", caveId);
        await ProposeAsync(trip);

        (await keeper.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await administrator.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await InviteAsync(organiser, trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CaveNoticesForAsync(keeperId, caveId)).ShouldBeEmpty(
            "the owner cannot read this cave, so nothing may name it to them");
        (await CaveNoticesForAsync(administratorId, caveId)).Count.ShouldBe(1);
    }

    // ---- fixture ----

    private async Task<Guid> CreatePlanAsync(string title, params Guid[] caveIds)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", Body(title, caveIds));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private Task EditAsync(Guid tripId, string title, params Guid[] caveIds) =>
        PutAsync(tripId, Body(title, caveIds));

    /// <summary>
    /// Writes a body the caller holds, so the same one can be sent twice — which is what a form
    /// saved untouched and a version restored onto itself both do.
    /// </summary>
    private async Task PutAsync(Guid tripId, object body)
    {
        var response = await organiser.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private object Body(string title, Guid[] caveIds) => new
    {
        title = $"{title} {Guid.NewGuid():N}"[..40],
        tripDate = "2026-09-12",
        caveIds,
        participants = Array.Empty<object>(),
        visibility = "private",
    };

    private Task ProposeAsync(Guid tripId) => TransitionAsync(tripId, "proposed");

    private async Task TransitionAsync(Guid tripId, string state)
    {
        var response = await organiser.PostWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}/state", new { state });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(string name, HttpClient? owner = null)
    {
        var response = await (owner ?? organiser).PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid tripId, Guid caverId) =>
        client.PostAsJsonAsync($"/api/v1/trip-logs/{tripId}/invitations/", new { caverId });

    private static async Task<Guid> CaverOfAsync(SilexGisDbContext db, Guid userId) =>
        (await db.Cavers.FirstAsync(c => c.UserId == userId)).Id;

    private async Task GrantReadAsync(Guid tripId, Guid userId) =>
        await WriteEntryAsync(tripId, userId, AccessEffect.Allow);

    private async Task DenyReadAsync(Guid tripId, Guid userId) =>
        await WriteEntryAsync(tripId, userId, AccessEffect.Deny);

    private async Task WriteEntryAsync(Guid tripId, Guid userId, AccessEffect effect)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = AccessDomain.TripLogs,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is reserved for
            // the feature-domain foreign key.
            ScopeId = tripId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<NotificationOutboxEntry>> NoticesForAsync(Guid userId, string? templateKey = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var query = db.NotificationOutbox.AsNoTracking().Where(x => x.UserId == userId);
        if (templateKey is not null)
        {
            query = query.Where(x => x.TemplateKey == templateKey);
        }

        return await query.OrderBy(x => x.Id).ToListAsync();
    }

    private async Task GrantCaveReadAsync(Guid caveId, Guid userId) =>
        await WriteCaveEntryAsync(caveId, userId, AccessEffect.Allow);

    private async Task DenyCaveReadAsync(Guid caveId, Guid userId) =>
        await WriteCaveEntryAsync(caveId, userId, AccessEffect.Deny);

    private async Task WriteCaveEntryAsync(Guid caveId, Guid userId, AccessEffect effect)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // The feature domain anchors object scope in its own foreign key, not in the generic
            // scope id every other domain uses.
            ScopeFeatureId = caveId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The messages one person was sent about one cave. Narrowed in memory on the stored
    /// placeholders, which are jsonb and have no text-matching operator, and on the cave's id
    /// rather than the trip's — this is the one message about a trip that never names it.
    /// </summary>
    private async Task<List<NotificationOutboxEntry>> CaveNoticesForAsync(Guid userId, Guid caveId)
    {
        var sent = await NoticesForAsync(userId, MessageTemplateCatalog.NotifyTripInviteeCannotOpenCave);
        return [.. sent.Where(x => x.Placeholders.Contains(caveId.ToString(), StringComparison.Ordinal))];
    }

    private async Task<List<NotificationOutboxEntry>> NoticesForTemplateAsync(string templateKey, Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // Narrowed in memory rather than in the query: the stored placeholders are jsonb, which
        // has no text-matching operator, and the set here is one test's own messages.
        var sent = await db.NotificationOutbox.AsNoTracking()
            .Where(x => x.TemplateKey == templateKey)
            .OrderBy(x => x.Id)
            .ToListAsync();
        return [.. sent.Where(x => x.Placeholders.Contains(tripId.ToString(), StringComparison.Ordinal))];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        mate?.Dispose();
        stranger?.Dispose();
        keeper?.Dispose();
        administrator?.Dispose();
        factory.Dispose();
    }
}
