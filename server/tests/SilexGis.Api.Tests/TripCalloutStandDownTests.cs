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
/// Saying the party is out, and what a page is told about whether anything is actually watching.
/// <para>
/// Two rules are held here and they pull in opposite directions on purpose. Standing a check down
/// is open to the people the trip names or has asked and to nobody else — not to whoever may edit
/// the trip, because the person who knows the party is out is on the trip and the delay while they
/// find somebody with editing rights is the delay a callout exists to remove. And an armed check
/// says when it was last actually looked at, because a promise that something is watching, drawn
/// without saying when it last ran, reads as safety it is not entitled to.
/// </para>
/// <para>
/// Everyone refused here is refused for a reason constructed in the test rather than inherited:
/// the trips are private and each read is granted one entry at a time, and the person who may is
/// asserted beside the person who may not.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripCalloutStandDownTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient organiser = null!; // Editor; owns the plans and is on them
    private HttpClient mate = null!;      // Viewer; asked on the trip and given the read on it
    private HttpClient stranger = null!;  // Viewer; asked on the trip and given nothing at all
    private HttpClient editor = null!;    // Editor; may read and write every trip, on none of them

    private Guid organiserId;
    private Guid mateId;
    private Guid strangerId;

    private Guid mateCaver;
    private Guid strangerCaver;

    public TripCalloutStandDownTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        organiserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tcd-org-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tcd-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tcd-str-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tcd-ed-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            mateCaver = (await db.Cavers.FirstAsync(c => c.UserId == mateId)).Id;
            strangerCaver = (await db.Cavers.FirstAsync(c => c.UserId == strangerId)).Id;
        }

        organiser = await AuthHelper.BearerClientAsync(factory, $"tcd-org-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"tcd-mate-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"tcd-str-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"tcd-ed-{suffix}@t.local");
    }

    /// <summary>
    /// Somebody who was asked on the trip and holds nothing else can say the party is out, and the
    /// times that were arranged are still there afterwards. The record of what was set up is part
    /// of the trip — a search that was nearly called is worth reading later — so the state moves
    /// and nothing else does.
    /// </summary>
    [Fact]
    public async Task Somebody_on_the_trip_says_the_party_is_out_and_what_was_arranged_survives()
    {
        var trip = await ArmedTripAsync("Out by dark");
        var (expected, alarm) = await TimesOfAsync(trip);

        var response = await mate.PostAsync(Url(trip), content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("calloutState").GetString().ShouldBe("stoodDown");

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.StoodDown);
        var (expectedAfter, alarmAfter) = await TimesOfAsync(trip);
        expectedAfter.ShouldBe(expected);
        alarmAfter.ShouldBe(alarm);
    }

    /// <summary>
    /// A party surfacing late still surfaces: the check is stood down from the overdue state as
    /// readily as from the armed one, because the message the alarm already sent is the reason
    /// somebody is tapping this at all.
    /// </summary>
    [Fact]
    public async Task A_party_that_is_already_reported_overdue_can_still_report_itself_out()
    {
        var trip = await ArmedTripAsync("Late out");
        await MutateAsync(trip, row => row.CalloutState = TripCalloutState.Overdue);

        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.StoodDown);
    }

    /// <summary>
    /// A second tap answers the same as the first. Somebody unsure whether the message went
    /// through will send it again, and an error is the one answer that must not come back at the
    /// moment a party is telling the club it is safe.
    /// </summary>
    [Fact]
    public async Task Saying_it_twice_is_not_an_error()
    {
        var trip = await ArmedTripAsync("Twice told");

        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.StoodDown);
    }

    /// <summary>
    /// A trip nobody arranged a check for has nothing to stand down, and answering "done" would
    /// report a check that never existed as one somebody had dealt with.
    /// </summary>
    [Fact]
    public async Task A_trip_with_no_check_refuses_rather_than_pretending()
    {
        var trip = await ArmedTripAsync("Never armed");
        await MutateAsync(trip, row =>
        {
            row.CalloutState = TripCalloutState.None;
            row.CalloutAlarmAt = null;
            row.ExpectedReturnAt = null;
        });

        var response = await mate.PostAsync(Url(trip), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("trip_log.callout_not_armed");
    }

    /// <summary>
    /// The gate is being on the trip, and it is not the right to change the trip. An account that
    /// may read and write every trip in the installation but is on none of them is refused, in the
    /// same test as the account that is on the trip and holds no rights over it at all.
    /// </summary>
    [Fact]
    public async Task Being_able_to_edit_the_trip_is_not_being_on_it()
    {
        var trip = await ArmedTripAsync("Not the editor's to say");

        (await editor.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Armed);

        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.StoodDown);
    }

    /// <summary>
    /// Somebody who cannot read the trip is told it does not exist, whatever else is true of them
    /// — including being on it. The trip here is private and the refused account holds no entry
    /// over it; the account that was given one is asserted beside it, so the refusal is the grant
    /// and not the fixture.
    /// </summary>
    [Fact]
    public async Task A_trip_nobody_may_read_is_not_there_to_be_stood_down()
    {
        var trip = await ArmedTripAsync("Private plan");

        (await stranger.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Armed);

        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.StoodDown);
    }

    /// <summary>
    /// The two doors have different gates, and this is the pair that says so. Somebody on the
    /// trip who holds nothing over it may say the party is out but may not change the hours; the
    /// account that may change the trip may arrange the check but is not the one who says people
    /// are safe.
    /// </summary>
    [Fact]
    public async Task Arranging_the_check_is_the_writer_s_and_standing_it_down_is_not()
    {
        var trip = await ArmedTripAsync("Two doors");
        var due = DateTimeOffset.UtcNow.AddHours(8);

        var byTheMate = await mate.PostWithIfMatchAsync(
            $"/api/v1/trip-logs/{trip}/callout", new { expectedReturnAt = due, calloutAlarmAt = due });
        byTheMate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await SaveAsync(trip, due, due.AddHours(1));
        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Armed);

        (await editor.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Nobody at all is nobody on the trip.</summary>
    [Fact]
    public async Task A_caller_with_no_account_cannot_stand_a_check_down()
    {
        var trip = await ArmedTripAsync("Signed out");
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync(Url(trip), content: null);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Armed);
    }

    /// <summary>
    /// An ordinary edit to the trip does not put a stood-down check back on. This is the reading
    /// the whole arming rule turns on: a form saves the trip whole, so a note added the morning
    /// after a party got home would otherwise re-arm the alarm they had already answered.
    /// </summary>
    [Fact]
    public async Task Editing_the_trip_afterwards_does_not_re_arm_a_check_that_was_stood_down()
    {
        var trip = await ArmedTripAsync("Home and edited");
        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SaveTripAsync(trip, "Home and edited, with a note");

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.StoodDown);
        // And the arrangement is still on the record: an ordinary save of the trip touches none
        // of it, because it is not part of the trip's own write request at all.
        var (expected, alarm) = await TimesOfAsync(trip);
        expected.ShouldNotBeNull();
        alarm.ShouldNotBeNull();
    }

    /// <summary>
    /// A different hour is a different arrangement. A party that came home, said so, and went back
    /// in for the evening has arranged a second callout rather than repeated the first — so a new
    /// alarm time arms the check again, from whatever state it was in.
    /// </summary>
    [Fact]
    public async Task A_new_alarm_time_arms_the_check_again()
    {
        var trip = await ArmedTripAsync("Second trip of the day");
        (await mate.PostAsync(Url(trip), content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var (expected, alarm) = await TimesOfAsync(trip);
        await SaveAsync(trip, expected, alarm!.Value.AddHours(6));

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Armed);
    }

    /// <summary>
    /// Clearing the hour is how the whole arrangement is called off, and it is the one path open
    /// to somebody who may edit the trip but is not on it.
    /// </summary>
    [Fact]
    public async Task Clearing_the_alarm_time_calls_the_arrangement_off()
    {
        var trip = await ArmedTripAsync("Called off");

        await SaveAsync(trip, expectedReturn: null, alarm: null);

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.None);
        var (expected, alarm) = await TimesOfAsync(trip);
        expected.ShouldBeNull();
        alarm.ShouldBeNull();
    }

    /// <summary>An alarm before the hour the party said they would be out is refused.</summary>
    [Fact]
    public async Task An_alarm_cannot_go_off_before_the_party_is_even_due()
    {
        var trip = await ArmedTripAsync("Backwards");
        var due = DateTimeOffset.UtcNow.AddHours(6);

        var bad = await SaveResponseAsync(trip, due, due.AddHours(-1));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var good = await SaveResponseAsync(trip, due, due.AddHours(1));
        good.StatusCode.ShouldBe(HttpStatusCode.OK, await good.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A live check says when it was last looked at, and a pass that failed is not a look. The
    /// reading has to be able to say "nothing has checked this since" — a failed pass reported as
    /// "last checked at" would be exactly the reassurance the value exists to withhold, and an
    /// armed alarm nothing has run against is unchecked rather than quiet.
    /// </summary>
    [Fact]
    public async Task A_failed_pass_is_not_a_check_and_a_finished_one_is()
    {
        var trip = await ArmedTripAsync("Watched, or not");

        var before = await LastCheckedAsync(trip);

        // A pass that fell over later than any that finished. It must move nothing: it looked at
        // nothing, so the trip is no more checked than it was.
        await RecordSweepAsync(ProcessingJobStatus.Failed, DateTimeOffset.UtcNow.AddMinutes(20));
        (await LastCheckedAsync(trip)).ShouldBe(before);

        var finished = DateTimeOffset.UtcNow.AddMinutes(40);
        await RecordSweepAsync(ProcessingJobStatus.Succeeded, finished);
        var after = await LastCheckedAsync(trip);
        after.ShouldNotBeNull();
        after!.Value.ShouldBe(finished, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A trip nobody arranged a check for says nothing about when the pass ran, even while passes
    /// are running. The value is about a promise being kept, and there is no promise here — a date
    /// beside a trip with no callout would read as a check on a trip that has none.
    /// </summary>
    [Fact]
    public async Task A_trip_with_no_check_is_told_nothing_about_the_pass()
    {
        var trip = await ArmedTripAsync("No arrangement");
        await RecordSweepAsync(ProcessingJobStatus.Succeeded, DateTimeOffset.UtcNow);
        (await LastCheckedAsync(trip)).ShouldNotBeNull();

        await MutateAsync(trip, row =>
        {
            row.CalloutState = TripCalloutState.None;
            row.CalloutAlarmAt = null;
            row.ExpectedReturnAt = null;
        });

        (await LastCheckedAsync(trip)).ShouldBeNull();
    }

    // ---- fixtures ----

    private static string Url(Guid tripId) => $"/api/v1/trip-logs/{tripId}/callout/stand-down";

    /// <summary>
    /// A private plan the organiser owns, with the mate asked on it and given the read, and the
    /// stranger asked on it and given nothing — so "on the trip" and "may read the trip" are two
    /// separately constructed facts rather than one.
    /// </summary>
    private async Task<Guid> ArmedTripAsync(string title)
    {
        var trip = await CreatePlanAsync(title);
        await GrantTripReadAsync(trip, mateId);
        (await InviteAsync(trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var now = DateTimeOffset.UtcNow;
        await MutateAsync(trip, row =>
        {
            row.ExpectedReturnAt = now.AddHours(4);
            row.CalloutAlarmAt = now.AddHours(6);
            row.CalloutState = TripCalloutState.Armed;
        });
        return trip;
    }

    private async Task<Guid> CreatePlanAsync(string title)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-09-12",
            participants = Array.Empty<object>(),
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> InviteAsync(Guid tripId, Guid caverId) =>
        organiser.PostAsJsonAsync($"/api/v1/trip-logs/{tripId}/invitations/", new { caverId });

    private async Task GrantTripReadAsync(Guid tripId, Guid userId)
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

    private async Task SaveAsync(Guid tripId, DateTimeOffset? expectedReturn, DateTimeOffset? alarm)
    {
        var response = await SaveResponseAsync(tripId, expectedReturn, alarm);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Arranging the check through its own door, which is the only way in. It is deliberately not
    /// two fields on the trip's own write request: that request replaces the whole trip, so a
    /// surface which never drew them would send them absent and call off a live callout without
    /// anybody meaning to.
    /// </summary>
    private Task<HttpResponseMessage> SaveResponseAsync(
        Guid tripId, DateTimeOffset? expectedReturn, DateTimeOffset? alarm) =>
        organiser.PostWithIfMatchAsync($"/api/v1/trip-logs/{tripId}/callout", new
        {
            expectedReturnAt = expectedReturn,
            calloutAlarmAt = alarm,
        });

    /// <summary>
    /// An ordinary save of the trip, touching nothing about the callout — the case the separate
    /// door exists for.
    /// </summary>
    private async Task SaveTripAsync(Guid tripId, string title)
    {
        var response = await organiser.PutWithIfMatchAsync($"/api/v1/trip-logs/{tripId}", new
        {
            title,
            tripDate = "2026-09-12",
            participants = Array.Empty<object>(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// One job row of the sweep's kind, written as if a pass had run. The pass itself is exercised
    /// elsewhere; what is under test here is only what the reading makes of the row it leaves.
    /// </summary>
    private async Task RecordSweepAsync(ProcessingJobStatus status, DateTimeOffset completedAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.TripCalloutSweep,
            Status = status,
            StartedAt = completedAt.AddSeconds(-1),
            CompletedAt = completedAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task<DateTimeOffset?> LastCheckedAsync(Guid tripId)
    {
        var response = await mate.GetAsync($"/api/v1/trip-logs/{tripId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var value = JsonDocument.Parse(payload).RootElement.GetProperty("calloutLastCheckedAt");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDateTimeOffset();
    }

    private async Task<(DateTimeOffset? Expected, DateTimeOffset? Alarm)> TimesOfAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == tripId);
        return (row.ExpectedReturnAt, row.CalloutAlarmAt);
    }

    private async Task<TripCalloutState> StateOfAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == tripId)).CalloutState;
    }

    private async Task MutateAsync(Guid tripId, Action<TripLog> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.TripLogs.SingleAsync(t => t.Id == tripId);
        change(row);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        mate?.Dispose();
        stranger?.Dispose();
        editor?.Dispose();
        factory.Dispose();
    }
}
