// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
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
/// Who was asked to a club event, what they have said, and who may say it for them.
/// <para>
/// The event every test here works on is created private and then opened to exactly the people a
/// test needs, one explicit grant at a time. That is deliberate and it is the only fixture that
/// proves anything: the group every ordinary account is put in reads and writes past visibility at
/// the widest scope, so an account that "cannot see" an event while holding that membership proves
/// nothing at all. Everybody refused here is a plain reader holding nothing, and every refusal is
/// asserted beside the permission it is the absence of.
/// </para>
/// <para>
/// The answers are the trip's answers, over the same rows and the same mechanism. What is asserted
/// here is that the second subject behaves as the first does and that the kind of event gates the
/// whole group — not the answering rules themselves, which have one home and one suite.
/// </para>
/// </summary>
public sealed class EventInvitationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient secretary = null!; // Editor; owns the event, so writes it
    private HttpClient member = null!;    // Viewer; given the read on the event and nothing else
    private HttpClient stranger = null!;  // Viewer; given nothing at all
    private HttpClient anonymous = null!; // No credentials at all

    private Guid secretaryId;
    private Guid memberId;
    private Guid strangerId;

    private Guid secretaryCaver;
    private Guid memberCaver;
    private Guid strangerCaver;

    /// <summary>Somebody in the directory who has never had an account, which is most people.</summary>
    private Guid accountlessCaver;

    public EventInvitationTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        secretaryId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ei-sec-{suffix}@t.local");
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ei-mem-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ei-str-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            secretaryCaver = await CaverOfAsync(db, secretaryId);
            memberCaver = await CaverOfAsync(db, memberId);
            strangerCaver = await CaverOfAsync(db, strangerId);

            var accountless = new Caver { FullName = $"Ana Accountless {suffix}" };
            db.Cavers.Add(accountless);
            await db.SaveChangesAsync();
            accountlessCaver = accountless.Id;
        }

        secretary = await AuthHelper.BearerClientAsync(factory, $"ei-sec-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"ei-mem-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"ei-str-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// The whole group, once, on the event: asked, answered, picked, un-picked and taken off. Each
    /// verb answers as its trip twin does, which is what "one mechanism, two subjects" has to mean
    /// for it to be worth having.
    /// </summary>
    [Fact]
    public async Task Every_verb_on_an_event_answers_the_way_its_trip_twin_does()
    {
        var evening = await CreateEventAsync("Club night");
        await GrantReadAsync(evening, memberId);

        // Asked. The row is created, carries the stamps, and answers nothing yet.
        var asked = await InviteAsync(secretary, evening, memberCaver);
        asked.StatusCode.ShouldBe(HttpStatusCode.Created, await asked.Content.ReadAsStringAsync());
        var invited = await BodyAsync(asked);
        invited.GetProperty("eventId").GetGuid().ShouldBe(evening);
        invited.GetProperty("response").GetString().ShouldBe("pending");
        invited.GetProperty("invitedByUserId").GetGuid().ShouldBe(secretaryId);
        invited.GetProperty("place").ValueKind.ShouldBe(JsonValueKind.Null);

        // Asking again is a reminder and undoes nothing. Both stamps are read back off the stored
        // row rather than one being the value just written, so what is compared is the same number
        // to the same precision and a difference can only mean the row was restamped.
        var firstStamp = await StampAsync(secretary, evening, memberCaver);
        var again = await InviteAsync(secretary, evening, memberCaver);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await StampAsync(secretary, evening, memberCaver)).ShouldBe(firstStamp);

        // Answered, by the person it is about, holding nothing but the read.
        var said = await AnswerAsync(member, evening, memberCaver, "yes", "bringing the projector");
        said.StatusCode.ShouldBe(HttpStatusCode.OK, await said.Content.ReadAsStringAsync());
        var answer = await BodyAsync(said);
        answer.GetProperty("response").GetString().ShouldBe("yes");
        answer.GetProperty("note").GetString().ShouldBe("bringing the projector");
        answer.GetProperty("respondedByUserId").GetGuid().ShouldBe(memberId);
        answer.GetProperty("place").GetInt32().ShouldBe(1);
        answer.GetProperty("attending").GetBoolean().ShouldBeTrue();

        // Picked, and un-picked, by whoever runs the event.
        var picked = await SelectAsync(secretary, evening, memberCaver, true);
        picked.StatusCode.ShouldBe(HttpStatusCode.OK, await picked.Content.ReadAsStringAsync());
        (await BodyAsync(picked)).GetProperty("selectedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var released = await SelectAsync(secretary, evening, memberCaver, false);
        released.StatusCode.ShouldBe(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        (await BodyAsync(released)).GetProperty("selectedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // Listed, with the counts beside it.
        var list = await ListBodyAsync(secretary, evening);
        list.GetProperty("eventId").GetGuid().ShouldBe(evening);
        list.GetProperty("attendingCount").GetInt32().ShouldBe(1);
        list.GetProperty("waitingCount").GetInt32().ShouldBe(0);
        list.GetProperty("invitations").GetArrayLength().ShouldBe(1);

        // Taken off entirely, answer and all.
        var removed = await secretary.DeleteAsync(Person(evening, memberCaver));
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
        (await ListBodyAsync(secretary, evening)).GetProperty("invitations").GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// A limit never refuses an answer; it decides who is in and who is waiting, and both are
    /// worked out from the answers every time they are asked. The same rule the trip's list holds,
    /// over the same expression — and the event's own capacity is what it is counted against.
    /// </summary>
    [Fact]
    public async Task A_limit_holds_the_later_answers_back_without_refusing_them()
    {
        var evening = await CreateEventAsync("One seat", maxParticipants: 1);
        await GrantReadAsync(evening, memberId);
        await GrantReadAsync(evening, strangerId);

        (await AnswerAsync(member, evening, memberCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await AnswerAsync(stranger, evening, strangerCaver, "yes");
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());

        var list = await ListBodyAsync(secretary, evening);
        list.GetProperty("maxParticipants").GetInt32().ShouldBe(1);
        list.GetProperty("attendingCount").GetInt32().ShouldBe(1);
        list.GetProperty("waitingCount").GetInt32().ShouldBe(1);

        var rows = list.GetProperty("invitations").EnumerateArray().ToList();
        rows.Single(x => x.GetProperty("caverId").GetGuid() == memberCaver)
            .GetProperty("attending").GetBoolean().ShouldBeTrue();
        rows.Single(x => x.GetProperty("caverId").GetGuid() == strangerCaver)
            .GetProperty("attending").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The same answers, given in the same order, number the same people the same way whichever
    /// kind of thing they are about. There is one expression that decides the sign-up order and
    /// one that decides who holds a place, and this is what says both are still shared: a second
    /// copy written for events would pass its own tests and disagree with this one.
    /// </summary>
    [Fact]
    public async Task The_same_answers_are_ranked_the_same_way_on_an_event_as_on_a_trip()
    {
        var order = new[] { secretaryCaver, memberCaver, strangerCaver, accountlessCaver };

        var evening = await CreateEventAsync("Ranked evening", maxParticipants: 2);
        foreach (var caver in order)
        {
            (await AnswerAsync(secretary, evening, caver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var trip = await CreateTripAsync("Ranked trip", maxParticipants: 2);
        foreach (var caver in order)
        {
            var answered = await secretary.PutAsJsonAsync(
                $"/api/v1/trip-logs/{trip}/invitations/{caver}/response", new { response = "yes" });
            answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync());
        }

        var onTheEvent = await PlacesAsync(await ListBodyAsync(secretary, evening));
        var onTheTrip = await PlacesAsync(await TripListBodyAsync(secretary, trip));

        onTheEvent.ShouldBe(onTheTrip);

        // And the ranking itself is the one being compared, not two empty lists agreeing.
        onTheEvent.ShouldBe(
        [
            (order[0], 1, true),
            (order[1], 2, true),
            (order[2], 3, false),
            (order[3], 4, false),
        ]);
    }

    /// <summary>
    /// A deadline is a date rather than a gathering, so every route in the group refuses it — the
    /// reads as well as the writes. A read that answered an empty list would say "nobody has said
    /// anything yet" about a thing nobody can say anything about, and a surface would draw a
    /// sign-up sheet on it.
    /// <para>
    /// The refusal carries its own code, and this asserts that it is neither of the two answers
    /// that would have been easier: not a 404, which would deny an event the same handler has just
    /// decided the caller may read, and not a bare 400 with nothing on it to tell apart from any
    /// other refusal.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_deadline_refuses_every_route_with_its_own_words()
    {
        var deadline = await CreateEventAsync("Permit renewal", kind: "deadline");
        var gathering = await CreateEventAsync("The evening it was announced at");

        var refusals = new[]
        {
            await secretary.GetAsync(Group(deadline)),
            await InviteAsync(secretary, deadline, memberCaver),
            await AnswerAsync(secretary, deadline, memberCaver, "yes"),
            await SelectAsync(secretary, deadline, memberCaver, true),
            await secretary.DeleteAsync(Person(deadline, memberCaver)),
        };

        foreach (var refusal in refusals)
        {
            var payload = await refusal.Content.ReadAsStringAsync();
            refusal.StatusCode.ShouldBe(HttpStatusCode.Conflict, payload);
            payload.ShouldContain("event_invitation.kind_takes_no_responses");
        }

        // A kind that is asked answers all five, so the refusals above are about the kind and not
        // about the group being shut.
        (await secretary.GetAsync(Group(gathering))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InviteAsync(secretary, gathering, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AnswerAsync(secretary, gathering, memberCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SelectAsync(secretary, gathering, memberCaver, true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await secretary.DeleteAsync(Person(gathering, memberCaver)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// An event that has been answered cannot be edited into a kind nobody is asked to.
    /// <para>
    /// Nothing is destroyed by allowing it, and that is exactly why it has to be refused: every
    /// route in the responses group refuses a kind that takes no answers, and the surface stops
    /// drawing the tab, so the answers already given would survive in the table with no door onto
    /// them, no count of them, and nothing anywhere saying they are there. A mis-picked option in
    /// a drop-down is an ordinary slip, and this is the difference between it being reversible and
    /// it being invisible.
    /// </para>
    /// <para>
    /// The refusal is asserted to be a conflict with its own code — neither the 404 that would
    /// deny an event the caller may plainly read and write, nor a bare 400 indistinguishable from
    /// a validation failure. The two arms that must keep working are asserted beside it: the same
    /// move on an event nobody has answered is allowed, and an edit that leaves the kind alone is
    /// untouched by the check.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_answered_event_refuses_a_kind_that_would_strand_its_answers()
    {
        var evening = await CreateEventAsync("Answered evening");
        (await InviteAsync(secretary, evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AnswerAsync(secretary, evening, memberCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await secretary.PutWithIfMatchAsync($"/api/v1/events/{evening}", Edit("deadline"));
        var refusedPayload = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, refusedPayload);
        refusedPayload.ShouldContain("event.kind_has_responses");

        // Refused and not half-applied: the event is still the kind it was, and its answers are
        // still reachable through the group that would have shut.
        var stillAsked = await secretary.GetAsync(Group(evening));
        stillAsked.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The check is about the answers and not about the move: an edit that keeps an answerable
        // kind passes, and so does the same move on an event nobody has answered.
        (await secretary.PutWithIfMatchAsync($"/api/v1/events/{evening}", Edit("training")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var quiet = await CreateEventAsync("Nobody answered");
        (await secretary.PutWithIfMatchAsync($"/api/v1/events/{quiet}", Edit("deadline")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The body of a full edit, which carries the kind whether or not it is changing.</summary>
    private static object Edit(string kind) => new
    {
        title = $"Edited {Guid.NewGuid():N}",
        kind,
        startDate = "2054-09-12",
        visibility = "private",
    };

    /// <summary>
    /// An event somebody may not read is missing on every route in the group, writes included, so
    /// that a refusal never confirms that it exists — nor which people are being considered for
    /// it, which is the same disclosure one row at a time. Never a 403, which would answer the
    /// question by refusing it.
    /// <para>
    /// The reader refused here holds nothing at all, and the same reader given one explicit read
    /// is answered in the same test — otherwise a group that was shut to everybody would pass.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_event_a_caller_may_not_read_is_missing_rather_than_forbidden()
    {
        var evening = await CreateEventAsync("Committee night");

        var shut = new[]
        {
            await stranger.GetAsync(Group(evening)),
            await InviteAsync(stranger, evening, strangerCaver),
            await AnswerAsync(stranger, evening, strangerCaver, "yes"),
            await SelectAsync(stranger, evening, strangerCaver, true),
            await stranger.DeleteAsync(Person(evening, strangerCaver)),
        };

        foreach (var refusal in shut)
        {
            var payload = await refusal.Content.ReadAsStringAsync();
            refusal.StatusCode.ShouldBe(HttpStatusCode.NotFound, payload);
            payload.ShouldContain("event.not_found");
        }

        // The one explicit grant is the whole difference: the same account, the same routes.
        await GrantReadAsync(evening, strangerId);
        (await stranger.GetAsync(Group(evening))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AnswerAsync(stranger, evening, strangerCaver, "yes")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Reading is not writing. A reader may say what they themselves are doing and may not put
        // somebody else on the list, and that refusal is the forbidden the 404s above are not.
        var theirs = await AnswerAsync(stranger, evening, memberCaver, "yes");
        theirs.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await theirs.Content.ReadAsStringAsync()).ShouldContain("event_invitation.answer_forbidden");

        var adding = await InviteAsync(stranger, evening, memberCaver);
        adding.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await adding.Content.ReadAsStringAsync()).ShouldContain("event_invitation.invite_forbidden");
    }

    /// <summary>Nobody reads or writes an event's list without signing in first.</summary>
    [Fact]
    public async Task Nobody_reaches_the_list_without_an_account()
    {
        var evening = await CreateEventAsync("Signed in only");

        (await anonymous.GetAsync(Group(evening))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await InviteAsync(anonymous, evening, memberCaver)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AnswerAsync(anonymous, evening, memberCaver, "yes"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The shape checks, each answering with a code of its own so a client can tell them apart:
    /// a body that says nothing, a note longer than one, a person the directory has never heard
    /// of, and a pick of somebody who never said they were coming.
    /// </summary>
    [Fact]
    public async Task A_body_that_says_nothing_is_refused_with_the_reason_it_was_refused_for()
    {
        var evening = await CreateEventAsync("Shapes");

        var empty = await secretary.PutAsJsonAsync(Person(evening, memberCaver, "response"), new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var longNote = await AnswerAsync(secretary, evening, memberCaver, "yes", new string('x', 501));
        longNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var nobody = await InviteAsync(secretary, evening, Guid.NewGuid());
        nobody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await nobody.Content.ReadAsStringAsync()).ShouldContain("event_invitation.caver_unknown");

        var unheard = await SelectAsync(secretary, evening, memberCaver, true);
        unheard.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unheard.Content.ReadAsStringAsync()).ShouldContain("event_invitation.not_on_list");

        (await AnswerAsync(secretary, evening, memberCaver, "no")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var declined = await SelectAsync(secretary, evening, memberCaver, true);
        declined.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await declined.Content.ReadAsStringAsync()).ShouldContain("event_invitation.select_not_attending");
    }

    /// <summary>
    /// Every route onto these rows is addressed by the thing being answered about and the person
    /// answering, and none by the row's own key. A route addressed by invitation id would be a
    /// second door onto the same rows with its own idea of who may open it — and since one table
    /// now holds the answers about both trips and events, a door like that would open both at
    /// once. Asserted against the routes the application actually publishes rather than against
    /// the file, so that adding one is what fails rather than remembering not to.
    /// </summary>
    [Fact]
    public void No_route_reaches_an_answer_by_its_own_key()
    {
        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Where(p => p.Contains("/invitations", StringComparison.Ordinal))
            .ToList();

        // Both groups are here, so an empty list cannot be what makes this pass.
        patterns.ShouldContain(p => p.Contains("/events/", StringComparison.Ordinal));
        patterns.ShouldContain(p => p.Contains("/trip-logs/", StringComparison.Ordinal));

        foreach (var pattern in patterns)
        {
            var afterInvitations = pattern[(pattern.IndexOf("/invitations", StringComparison.Ordinal)
                + "/invitations".Length)..];

            // What may follow is a person, and the fixed words that act on that person's row.
            afterInvitations.Replace("{caverId:guid}", string.Empty, StringComparison.Ordinal)
                .ShouldNotContain("{", customMessage: pattern);
        }
    }

    /// <summary>When one person on the list was asked, as the stored row answers it.</summary>
    private async Task<string?> StampAsync(HttpClient client, Guid eventId, Guid caverId) =>
        (await ListBodyAsync(client, eventId)).GetProperty("invitations").EnumerateArray()
            .Single(x => x.GetProperty("caverId").GetGuid() == caverId)
            .GetProperty("invitedAt").GetString();

    private string Group(Guid eventId) => $"/api/v1/events/{eventId}/invitations/";

    private string Person(Guid eventId, Guid caverId, string? verb = null) =>
        $"/api/v1/events/{eventId}/invitations/{caverId}" + (verb is null ? string.Empty : "/" + verb);

    private Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid eventId, Guid caverId) =>
        client.PostAsJsonAsync(Group(eventId), new { caverId });

    private Task<HttpResponseMessage> AnswerAsync(
        HttpClient client, Guid eventId, Guid caverId, string response, string? note = null) =>
        client.PutAsJsonAsync(Person(eventId, caverId, "response"), new { response, note });

    private Task<HttpResponseMessage> SelectAsync(
        HttpClient client, Guid eventId, Guid caverId, bool selected) =>
        client.PutAsJsonAsync(Person(eventId, caverId, "selection"), new { selected });

    private async Task<JsonElement> ListBodyAsync(HttpClient client, Guid eventId)
    {
        var response = await client.GetAsync(Group(eventId));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<JsonElement> TripListBodyAsync(HttpClient client, Guid tripId)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/{tripId}/invitations/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static Task<List<(Guid Caver, int Place, bool Attending)>> PlacesAsync(JsonElement list) =>
        Task.FromResult(list.GetProperty("invitations").EnumerateArray()
            .Select(x => (
                x.GetProperty("caverId").GetGuid(),
                x.GetProperty("place").GetInt32(),
                x.GetProperty("attending").GetBoolean()))
            .ToList());

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<Guid> CaverOfAsync(SilexGisDbContext db, Guid userId) =>
        (await db.Cavers.FirstAsync(c => c.UserId == userId)).Id;

    /// <summary>
    /// An event written up through the door that leaves it private, so that everybody who can read
    /// it below can read it because a grant says so and for no other reason.
    /// </summary>
    private async Task<Guid> CreateEventAsync(
        string title, string kind = "clubMeeting", int? maxParticipants = null)
    {
        var response = await secretary.PostAsJsonAsync("/api/v1/events", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            kind,
            startDate = "2054-09-12",
            maxParticipants,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title, int? maxParticipants = null)
    {
        var response = await secretary.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}",
            tripDate = "2054-09-12",
            participants = Array.Empty<object>(),
            maxParticipants,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task GrantReadAsync(Guid eventId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Events,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            // Non-feature domains anchor object scope in ScopeId; ScopeFeatureId is reserved for
            // the feature-domain foreign key.
            ScopeId = eventId,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        secretary?.Dispose();
        member?.Dispose();
        stranger?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }
}
