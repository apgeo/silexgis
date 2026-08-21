// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The pass that notices a party is overdue: who it tells, what it refuses to say, and what stops
/// it saying it twice.
/// <para>
/// Every plan here is created private and opened to exactly the people a test needs, one explicit
/// grant at a time. The group every ordinary account is put in reads past visibility at the widest
/// scope, so an account that "cannot see" a trip while holding that membership proves nothing —
/// everybody refused here is a plain reader holding nothing, and the person who is told is
/// asserted in the same test as the person who is not.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripCalloutSweepTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    private HttpClient organiser = null!; // Editor; owns the plans
    private HttpClient mate = null!;      // Viewer; on the trip and given the read on it
    private HttpClient stranger = null!;  // Viewer; on the trip and given nothing at all
    private HttpClient keeper = null!;    // Editor; owns the caves and goes on no trip

    private Guid organiserId;
    private Guid mateId;
    private Guid strangerId;

    private Guid mateCaver;
    private Guid strangerCaver;

    private long caveTypeId;

    public TripCalloutSweepTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        organiserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tcs-org-{suffix}@t.local");
        mateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tcs-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tcs-str-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tcs-keep-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            mateCaver = (await db.Cavers.FirstAsync(c => c.UserId == mateId)).Id;
            strangerCaver = (await db.Cavers.FirstAsync(c => c.UserId == strangerId)).Id;
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        organiser = await AuthHelper.BearerClientAsync(factory, $"tcs-org-{suffix}@t.local");
        mate = await AuthHelper.BearerClientAsync(factory, $"tcs-mate-{suffix}@t.local");
        stranger = await AuthHelper.BearerClientAsync(factory, $"tcs-str-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"tcs-keep-{suffix}@t.local");
    }

    /// <summary>
    /// The whole of the check's idempotence is on the trip: the row leaves the armed state in the
    /// same save that queues the message about it, so a pass that runs again — after a restart, or
    /// simply on its next tick — finds nothing to do rather than sending a second alarm. The queue
    /// itself cannot answer "was one already sent about this", so nothing else could.
    /// </summary>
    [Fact]
    public async Task An_overdue_party_is_reported_once_however_often_the_pass_runs()
    {
        var trip = await ArmedTripAsync("Past the hour", TimeSpan.FromHours(1));

        await SweepAsync();

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Overdue);
        (await AlarmsAboutAsync(mateId, trip)).Count.ShouldBe(1);

        await SweepAsync();
        await SweepAsync();

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Overdue);
        (await AlarmsAboutAsync(mateId, trip)).Count.ShouldBe(1);
    }

    /// <summary>An alarm whose time has not come is not raised early.</summary>
    [Fact]
    public async Task A_party_still_within_its_time_is_left_alone()
    {
        var early = await ArmedTripAsync("Still underground", TimeSpan.FromHours(-4));
        var late = await ArmedTripAsync("Late out", TimeSpan.FromMinutes(5));

        await SweepAsync();

        (await StateOfAsync(early)).ShouldBe(TripCalloutState.Armed);
        (await AlarmsAboutAsync(mateId, early)).ShouldBeEmpty();

        (await StateOfAsync(late)).ShouldBe(TripCalloutState.Overdue);
        (await AlarmsAboutAsync(mateId, late)).Count.ShouldBe(1);
    }

    /// <summary>
    /// A trip called off had nobody on it to be late, and a trip put back has moved — the hour the
    /// alarm was armed against belongs to a date that is no longer true, so raising it would report
    /// a party overdue from a trip that has not started. Both keep the check armed rather than
    /// having it quietly cleared: only a person stands an alarm down.
    /// </summary>
    [Fact]
    public async Task A_trip_called_off_or_put_back_raises_nothing()
    {
        var cancelled = await ArmedTripAsync("Called off", TimeSpan.FromHours(2));
        var delayed = await ArmedTripAsync("Put back", TimeSpan.FromHours(2));
        var going = await ArmedTripAsync("Still going ahead", TimeSpan.FromHours(2));

        await SetStateAsync(cancelled, ActivityState.Cancelled);
        await SetStateAsync(delayed, ActivityState.Delayed);

        await SweepAsync();

        (await StateOfAsync(cancelled)).ShouldBe(TripCalloutState.Armed);
        (await AlarmsAboutAsync(mateId, cancelled)).ShouldBeEmpty();
        (await StateOfAsync(delayed)).ShouldBe(TripCalloutState.Armed);
        (await AlarmsAboutAsync(mateId, delayed)).ShouldBeEmpty();

        // The same arrangement on a trip that is still going ahead does raise, so the silence
        // above is the rule and not a pass that raises nothing at all.
        (await StateOfAsync(going)).ShouldBe(TripCalloutState.Overdue);
        (await AlarmsAboutAsync(mateId, going)).Count.ShouldBe(1);
    }

    /// <summary>
    /// Somebody said the party was out. The alarm is disarmed rather than deleted, and the pass
    /// leaves it alone from then on — including after the hour it was armed for has gone by.
    /// </summary>
    [Fact]
    public async Task An_alarm_somebody_stood_down_is_never_raised()
    {
        var acknowledged = await ArmedTripAsync("Reported safe", TimeSpan.FromHours(3));
        var untouched = await ArmedTripAsync("Nobody said anything", TimeSpan.FromHours(3));

        await SetCalloutStateAsync(acknowledged, TripCalloutState.StoodDown);

        await SweepAsync();

        (await StateOfAsync(acknowledged)).ShouldBe(TripCalloutState.StoodDown);
        (await AlarmsAboutAsync(mateId, acknowledged)).ShouldBeEmpty();

        (await StateOfAsync(untouched)).ShouldBe(TripCalloutState.Overdue);
        (await AlarmsAboutAsync(mateId, untouched)).Count.ShouldBe(1);
    }

    /// <summary>
    /// Being named on a trip is not being given it. Each recipient's own right to read the trip is
    /// decided freshly, from their own access — there is no actor here whose reach could be
    /// borrowed, and being on the list is exactly what must not stand in for a grant.
    /// </summary>
    [Fact]
    public async Task Nobody_is_told_about_a_trip_they_could_not_open()
    {
        var trip = await ArmedTripAsync("Shut out and overdue", TimeSpan.FromHours(1));

        // Both are Viewers, both are on the trip's list, and they differ in exactly one thing: an
        // entry saying the reader may read this trip.
        (await mate.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stranger.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SweepAsync();

        (await AlarmsAboutAsync(mateId, trip)).Count.ShouldBe(1);
        (await AlarmsAboutAsync(strangerId, trip)).ShouldBeEmpty();
    }

    /// <summary>
    /// The alarm names no cave, and this is the message where the temptation is strongest — an
    /// overdue party feels like the one thing that ought to say where to look. It still does not:
    /// the message reaches everybody the trip names, and where a trip is going is readable by fewer
    /// people than that. Whoever runs a search reads the trip, where the answer is kept for them
    /// under the rules that decide who may have it.
    /// </summary>
    [Fact]
    public async Task No_alarm_says_where_the_party_went()
    {
        var caveName = $"Peștera Ascunsă {Guid.NewGuid():N}"[..30];
        var caveId = await CreateCaveAsync(caveName);
        var trip = await ArmedTripAsync("Overdue in a cave nobody may open", TimeSpan.FromHours(1), caveId);

        // The recipient can read the trip and genuinely cannot read the cave it is about, which is
        // the shape the alarm has to survive: everything it says goes to somebody who is not
        // allowed to know where.
        (await mate.GetAsync($"/api/v1/trip-logs/{trip}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mate.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SweepAsync();

        var alarms = await AlarmsAboutAsync(mateId, trip);
        alarms.Count.ShouldBe(1, "the alarm itself must reach them, or this test proves nothing");

        var body = alarms[0].Placeholders;
        body.ShouldNotContain(caveName, Case.Insensitive);
        body.ShouldNotContain(caveId.ToString(), Case.Insensitive);
    }

    /// <summary>
    /// A pass that falls over half way through leaves neither an alarm nobody was told about nor a
    /// trip moved out of the state that is the only reason a later pass would look at it again.
    /// </summary>
    /// <remarks>
    /// The saving at the end is not decoration and is the whole point of the test. The handler does
    /// not own the context it writes through — whoever runs the job resolves both from one scope,
    /// and after a handler throws it records the failure on the job row and saves. If the pass left
    /// its work tracked, that save would write it: trips moved to overdue whose messages were never
    /// queued, which no later pass selects, which is a party nobody is ever told about. So this
    /// makes exactly that save and then asserts the trip is untouched.
    /// </remarks>
    [Fact]
    public async Task A_pass_that_fails_writes_nothing_even_when_its_caller_saves_afterwards()
    {
        var trip = await ArmedTripAsync("Interrupted pass", TimeSpan.FromHours(1));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = new TripCalloutSweepHandler(
            db,
            new RefusingAccess(),
            Options.Create(new TripCalloutOptions { ReminderLead = TimeSpan.Zero }),
            NullLogger<TripCalloutSweepHandler>.Instance);

        var job = new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep };
        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(job, CancellationToken.None));

        // What the caller does next, on this very context.
        job.Status = ProcessingJobStatus.Failed;
        job.Error = "deliberate";
        job.CompletedAt = DateTimeOffset.UtcNow;
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        (await StateOfAsync(trip)).ShouldBe(TripCalloutState.Armed);
        (await AlarmsAboutAsync(mateId, trip)).ShouldBeEmpty();
    }

    /// <summary>
    /// Somebody taps <i>the party is out</i> while a pass is already running. The pass must not put
    /// the trip back to overdue behind them and mail everybody about a party sitting in the pub.
    /// </summary>
    /// <remarks>
    /// The stand-down is driven from inside the pass, between the query that found the trip and the
    /// moment the pass writes to it — which is where the window actually is, and it is widest at
    /// the alarm hour, when stand-downs cluster. Two trips are needed because the hook has to fire
    /// while the pass is busy with the other one.
    /// </remarks>
    [Fact]
    public async Task An_alarm_stood_down_while_the_pass_is_running_is_not_raised_behind_it()
    {
        var first = await ArmedTripAsync("Pass reaches this one first", TimeSpan.FromHours(3));
        var second = await ArmedTripAsync("Stood down mid-pass", TimeSpan.FromHours(2));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var real = scope.ServiceProvider.GetRequiredService<IAccessService>();
        var handler = new TripCalloutSweepHandler(
            db,
            new StandsSomethingDownOnce(real, () => SetCalloutStateAsync(second, TripCalloutState.StoodDown)),
            Options.Create(new TripCalloutOptions { ReminderLead = TimeSpan.Zero }),
            NullLogger<TripCalloutSweepHandler>.Instance);

        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep }, CancellationToken.None);

        // The trip whose alarm was genuinely due is reported, so the pass did run.
        (await StateOfAsync(first)).ShouldBe(TripCalloutState.Overdue);
        (await AlarmsAboutAsync(mateId, first)).Count.ShouldBe(1);

        // The one somebody spoke for keeps what they said, and nobody is mailed about it.
        (await StateOfAsync(second)).ShouldBe(TripCalloutState.StoodDown);
        (await AlarmsAboutAsync(mateId, second)).ShouldBeEmpty();
    }

    /// <summary>
    /// A trip that has been put back still carries the date it was going to happen on, and nobody
    /// is going on that date. Reminding people of it is the same mistake as an alarm quoting an
    /// hour that has moved — the reminder rides this pass rather than the queue precisely so it
    /// can be withheld.
    /// </summary>
    [Fact]
    public async Task A_trip_that_was_put_back_is_not_reminded_about_on_the_date_it_no_longer_has()
    {
        using var reminding = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["TripCallout:ReminderLead"] = "2.00:00:00" });

        var trip = await CreatePlanAsync("Put back", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
        await ProposeAsync(trip);
        await GrantTripReadAsync(trip, mateId);
        (await InviteAsync(trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await SetStateAsync(trip, ActivityState.Delayed);

        await using (var scope = reminding.Services.CreateAsyncScope())
        {
            await HandlerIn(scope).ExecuteAsync(
                new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep }, CancellationToken.None);
        }

        (await NoticesAboutAsync(mateId, MessageTemplateCatalog.NotifyTripPlanReminder, trip))
            .ShouldBeEmpty();

        // And the stamp is not spent either, so the reminder is still owed once a real date is
        // settled and the trip is planned again.
        (await ReminderStampOfAsync(trip)).ShouldBeNull();
    }

    /// <summary>
    /// A check armed on a trip that was put back or called off is one no pass will ever look at.
    /// Reporting a pass that ran and deliberately skipped it as <i>last checked a minute ago</i>
    /// is the one reading this value exists to prevent, so the answer is withheld instead.
    /// </summary>
    [Fact]
    public async Task An_armed_check_no_pass_watches_is_never_reported_as_recently_checked()
    {
        var trip = await ArmedTripAsync("Armed, then put back", TimeSpan.FromHours(-4));
        await RecordSweptAsync();

        // Still watched while the trip is going ahead: the negative below is a change, not the
        // value this endpoint always gives.
        (await CalloutCheckedAtAsync(trip)).ShouldNotBeNull();

        await SetStateAsync(trip, ActivityState.Delayed);

        (await CalloutCheckedAtAsync(trip)).ShouldBeNull();
    }

    /// <summary>
    /// A tick queues exactly one pass, and a tick that finds one already waiting queues none.
    /// </summary>
    /// <remarks>
    /// Asked of the scheduler directly rather than by starting a live schedule, and inside a
    /// transaction that is rolled back: a pass committed here would be claimed by the worker and
    /// would read every trip in the database this suite shares. The switched-off case is asserted
    /// beside it, so neither half is a negative standing on its own.
    /// </remarks>
    [Fact]
    public async Task A_tick_queues_one_pass_and_a_tick_beside_a_pending_one_queues_none()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var uncommitted = await db.Database.BeginTransactionAsync();

        var scheduler = new TripCalloutScheduler(
            new OneScopeOnly(scope),
            Options.Create(new TripCalloutOptions()),
            NullLogger<TripCalloutScheduler>.Instance);

        var before = await QueuedPassesAsync(db);
        await scheduler.QueuePassAsync(CancellationToken.None);
        var afterFirst = await QueuedPassesAsync(db);
        await scheduler.QueuePassAsync(CancellationToken.None);
        var afterSecond = await QueuedPassesAsync(db);

        (afterFirst - before).ShouldBe(1);
        afterSecond.ShouldBe(afterFirst);

        await uncommitted.RollbackAsync();
        scheduler.Dispose();
    }


    /// <summary>
    /// The reminder rides the same pass rather than being written onto the queue when the trip is
    /// arranged, and the stamp it leaves on the trip is the whole of what makes it one message
    /// rather than one per pass.
    /// </summary>
    [Fact]
    public async Task A_trip_that_is_nearly_here_is_mentioned_once()
    {
        // Opened deliberately: the run-up window is shut wherever these tests run, because a pass
        // reads every trip in the database this suite shares.
        using var reminding = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["TripCallout:ReminderLead"] = "2.00:00:00" });

        var trip = await CreatePlanAsync("Nearly here", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
        await ProposeAsync(trip);
        await GrantTripReadAsync(trip, mateId);
        (await InviteAsync(trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await using (var scope = reminding.Services.CreateAsyncScope())
        {
            await HandlerIn(scope).ExecuteAsync(
                new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep }, CancellationToken.None);
            await HandlerIn(scope).ExecuteAsync(
                new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep }, CancellationToken.None);
        }

        var reminders = await NoticesAboutAsync(mateId, MessageTemplateCatalog.NotifyTripPlanReminder, trip);
        reminders.Count.ShouldBe(1);
        reminders[0].Placeholders.ShouldNotContain("cave", Case.Insensitive);
    }

    /// <summary>
    /// A zero interval is a real operator setting and it is what keeps this suite honest: every
    /// class shares one database, so a schedule left running under one of them would move another
    /// class's armed trip to overdue and queue an alarm nobody asked for.
    /// </summary>
    [Fact]
    public async Task A_zero_interval_queues_no_pass_at_all()
    {
        var before = await LastSweepJobIdAsync();

        var scheduler = new TripCalloutScheduler(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TripCalloutOptions { SweepInterval = TimeSpan.Zero }),
            NullLogger<TripCalloutScheduler>.Instance);

        await scheduler.StartAsync(CancellationToken.None);

        // It returned rather than settling into a loop, which is the whole of "switched off" — a
        // schedule that merely ticked slowly would still be running here. The positive half is the
        // interval predicate itself, asserted where the setting lives; starting a live schedule
        // inside this suite is the one thing the setting exists to prevent.
        (scheduler.ExecuteTask is not null).ShouldBeTrue();
        await scheduler.ExecuteTask!;
        scheduler.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();

        await scheduler.StopAsync(CancellationToken.None);
        scheduler.Dispose();

        (await LastSweepJobIdAsync()).ShouldBe(before);
    }

    // ---- fixture ----

    private static IProcessingJobHandler HandlerIn(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.TripCalloutSweep);

    private async Task SweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await HandlerIn(scope).ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.TripCalloutSweep }, CancellationToken.None);
    }

    /// <summary>
    /// A plan somebody is on, readable by the mate and not by the stranger, with its alarm set
    /// <paramref name="ago"/> in the past — or, for a negative figure, that far ahead.
    /// </summary>
    private async Task<Guid> ArmedTripAsync(string title, TimeSpan ago, params Guid[] caveIds)
    {
        var trip = await CreatePlanAsync(title, new DateOnly(2026, 9, 12), caveIds);
        await ProposeAsync(trip);
        await GrantTripReadAsync(trip, mateId);
        (await InviteAsync(trip, mateCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(trip, strangerCaver)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var now = DateTimeOffset.UtcNow;
        await MutateAsync(trip, row =>
        {
            row.ExpectedReturnAt = now - ago - TimeSpan.FromHours(1);
            row.CalloutAlarmAt = now - ago;
            row.CalloutState = TripCalloutState.Armed;
        });
        return trip;
    }

    private async Task<Guid> CreatePlanAsync(string title, DateOnly date, params Guid[] caveIds)
    {
        var response = await organiser.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"{title} {Guid.NewGuid():N}"[..40],
            tripDate = date.ToString("yyyy-MM-dd"),
            caveIds,
            participants = Array.Empty<object>(),
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task ProposeAsync(Guid tripId)
    {
        var response = await organiser.PostWithIfMatchAsync(
            $"/api/v1/trip-logs/{tripId}/state", new { state = "proposed" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> InviteAsync(Guid tripId, Guid caverId) =>
        organiser.PostAsJsonAsync($"/api/v1/trip-logs/{tripId}/invitations/", new { caverId });

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await keeper.PostAsJsonAsync("/api/v1/caves", new
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
        var caveId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        // The organiser has to be able to name it onto a trip; nobody else is given anything.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = organiserId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Features,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeFeatureId = caveId,
        });
        await db.SaveChangesAsync();
        return caveId;
    }

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

    private Task SetStateAsync(Guid tripId, ActivityState state) =>
        MutateAsync(tripId, row => row.State = state);

    private Task SetCalloutStateAsync(Guid tripId, TripCalloutState state) =>
        MutateAsync(tripId, row => row.CalloutState = state);

    private async Task MutateAsync(Guid tripId, Action<TripLog> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.TripLogs.SingleAsync(t => t.Id == tripId);
        change(row);
        await db.SaveChangesAsync();
    }

    private async Task<TripCalloutState> StateOfAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == tripId)).CalloutState;
    }

    private Task<List<NotificationOutboxEntry>> AlarmsAboutAsync(Guid userId, Guid tripId) =>
        NoticesAboutAsync(userId, MessageTemplateCatalog.NotifyTripCalloutOverdue, tripId);

    /// <summary>
    /// One person's messages of one kind about one trip. Narrowed in memory on the stored
    /// placeholders, which are jsonb and have no text-matching operator.
    /// </summary>
    private async Task<List<NotificationOutboxEntry>> NoticesAboutAsync(
        Guid userId, string templateKey, Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var sent = await db.NotificationOutbox.AsNoTracking()
            .Where(x => x.UserId == userId && x.TemplateKey == templateKey)
            .OrderBy(x => x.Id)
            .ToListAsync();
        return [.. sent.Where(x => x.Placeholders.Contains(tripId.ToString(), StringComparison.Ordinal))];
    }

    private async Task<long> LastSweepJobIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TripCalloutSweep)
            .Select(j => j.Id)
            .OrderByDescending(id => id)
            .FirstOrDefaultAsync();
    }

    private static Task<int> QueuedPassesAsync(SilexGisDbContext db) =>
        db.ProcessingJobs
            .Where(j => j.Kind == ProcessingJobKinds.TripCalloutSweep
                && (j.Status == ProcessingJobStatus.Queued || j.Status == ProcessingJobStatus.Running))
            .CountAsync();

    private async Task<DateTimeOffset?> ReminderStampOfAsync(Guid tripId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == tripId)).PlanReminderSentAt;
    }

    /// <summary>A pass that completed, which is the only kind the page counts as a check.</summary>
    private async Task RecordSweptAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.TripCalloutSweep,
            Status = ProcessingJobStatus.Succeeded,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>What the trip tells its reader about when its check was last looked at.</summary>
    private async Task<string?> CalloutCheckedAtAsync(Guid tripId)
    {
        var response = await organiser.GetAsync($"/api/v1/trip-logs/{tripId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var root = JsonDocument.Parse(payload).RootElement;
        var checkedAt = root.GetProperty("calloutLastCheckedAt");
        return checkedAt.ValueKind == JsonValueKind.Null ? null : checkedAt.GetString();
    }

    /// <summary>
    /// An access service that refuses to answer at all, so that a pass fails in the middle of
    /// deciding who may be told rather than at a point of its own choosing.
    /// </summary>
    private sealed class RefusingAccess : IAccessService
    {
        private static InvalidOperationException Refuse() => new("deliberate failure mid-pass");

        public Task<AccessDecision> DecideAsync(
            AccessContext? ctx, AccessAction action, IProtectedEntity entity, CancellationToken ct = default) =>
            throw Refuse();

        public Task<AccessAction> EffectiveAsync(
            AccessContext? ctx, IProtectedEntity entity, CancellationToken ct = default) =>
            throw Refuse();

        public Task<AccessTargetFacts> FactsOfAsync(IProtectedEntity entity, CancellationToken ct = default) =>
            throw Refuse();

        public Task<IReadOnlyDictionary<Guid, AccessTargetFacts>> FactsOfManyAsync(
            IReadOnlyCollection<IProtectedEntity> entities, CancellationToken ct = default) =>
            throw Refuse();

        public Task<HashSet<Guid>> ViewExactLocationRootIdsAsync(
            AccessContext? ctx, IReadOnlyCollection<Guid> protectionRootIds, CancellationToken ct = default) =>
            throw Refuse();
    }

    /// <summary>
    /// The real access rule, with a single hook that fires the first time the pass asks it
    /// anything — which is while the pass is busy with a trip, and so is the moment a person
    /// tapping <i>the party is out</i> would land in.
    /// </summary>
    private sealed class StandsSomethingDownOnce(IAccessService inner, Func<Task> once) : IAccessService
    {
        private int fired;

        private async Task InterruptAsync()
        {
            if (Interlocked.Exchange(ref fired, 1) == 0)
            {
                await once();
            }
        }

        public async Task<AccessDecision> DecideAsync(
            AccessContext? ctx, AccessAction action, IProtectedEntity entity, CancellationToken ct = default)
        {
            await InterruptAsync();
            return await inner.DecideAsync(ctx, action, entity, ct);
        }

        public async Task<AccessAction> EffectiveAsync(
            AccessContext? ctx, IProtectedEntity entity, CancellationToken ct = default)
        {
            await InterruptAsync();
            return await inner.EffectiveAsync(ctx, entity, ct);
        }

        public Task<AccessTargetFacts> FactsOfAsync(IProtectedEntity entity, CancellationToken ct = default) =>
            inner.FactsOfAsync(entity, ct);

        public Task<IReadOnlyDictionary<Guid, AccessTargetFacts>> FactsOfManyAsync(
            IReadOnlyCollection<IProtectedEntity> entities, CancellationToken ct = default) =>
            inner.FactsOfManyAsync(entities, ct);

        public Task<HashSet<Guid>> ViewExactLocationRootIdsAsync(
            AccessContext? ctx, IReadOnlyCollection<Guid> protectionRootIds, CancellationToken ct = default) =>
            inner.ViewExactLocationRootIdsAsync(ctx, protectionRootIds, ct);
    }

    /// <summary>
    /// Hands out one scope the test already owns, so that what the scheduler writes goes through
    /// the very transaction this test is going to roll back.
    /// </summary>
    private sealed class OneScopeOnly(IServiceScope scope) : IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => scope.ServiceProvider;

        public IServiceScope CreateScope() => this;

        // The scheduler disposes what it is handed; the test owns this one.
        public void Dispose()
        {
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        organiser?.Dispose();
        mate?.Dispose();
        stranger?.Dispose();
        keeper?.Dispose();
        factory.Dispose();
    }
}
