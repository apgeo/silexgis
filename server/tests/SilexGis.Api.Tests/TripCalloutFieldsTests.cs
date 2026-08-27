// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a trip records when somebody arranges for their absence to be noticed: when they expect
/// to be back, when an alarm should go off if they are not, and where that alarm has got to.
/// <para>
/// The fields are real columns rather than entries in the trip's free-form bag because something
/// running with every browser closed selects on them, and a bag is not something a database can
/// look things up in. These tests hold that shape: they prove the columns exist on a database
/// built from the migrations, that they survive a round trip, and that a trip nobody arranged one
/// for reads as exactly that rather than as an absent answer.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripCalloutFieldsTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;

    private Guid ownerId;

    public TripCalloutFieldsTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"callout-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_trip_nobody_arranged_a_callout_for_says_so()
    {
        var id = await StoreTripAsync(trip => { });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == id);

        // Not null, and not a state anything has to interpret: "nobody arranged one" is a value.
        stored.CalloutState.ShouldBe(TripCalloutState.None);
        stored.ExpectedReturnAt.ShouldBeNull();
        stored.CalloutAlarmAt.ShouldBeNull();
        stored.PlanReminderSentAt.ShouldBeNull();
    }

    [Fact]
    public async Task An_arranged_callout_survives_being_written_down()
    {
        // The instants are the ones a pass compares against the clock, so they are stored to the
        // second in UTC and must come back saying the same thing — a callout that drifted by a
        // time zone on its way through the database is an alarm at the wrong hour.
        var expectedReturn = new DateTimeOffset(2026, 9, 3, 18, 30, 0, TimeSpan.Zero);
        var alarm = new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);

        var id = await StoreTripAsync(trip =>
        {
            trip.ExpectedReturnAt = expectedReturn;
            trip.CalloutAlarmAt = alarm;
            trip.CalloutState = TripCalloutState.Armed;
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.TripLogs.AsNoTracking().SingleAsync(t => t.Id == id);

        stored.ExpectedReturnAt.ShouldBe(expectedReturn);
        stored.CalloutAlarmAt.ShouldBe(alarm);
        stored.CalloutState.ShouldBe(TripCalloutState.Armed);

        // And the selection the pass makes is answerable by the database rather than in memory:
        // parties whose alarm has gone by and whose check is still live.
        var due = await db.TripLogs.AsNoTracking()
            .Where(t => t.CalloutState == TripCalloutState.Armed && t.CalloutAlarmAt <= alarm)
            .Select(t => t.Id)
            .ToListAsync();

        due.ShouldContain(id);
    }

    [Fact]
    public async Task Standing_an_alarm_down_keeps_the_record_of_what_was_armed()
    {
        var expectedReturn = new DateTimeOffset(2026, 9, 3, 18, 30, 0, TimeSpan.Zero);
        var id = await StoreTripAsync(trip =>
        {
            trip.ExpectedReturnAt = expectedReturn;
            trip.CalloutAlarmAt = expectedReturn.AddHours(2);
            trip.CalloutState = TripCalloutState.Armed;
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trip = await db.TripLogs.SingleAsync(t => t.Id == id);
            trip.CalloutState = TripCalloutState.StoodDown;
            await db.SaveChangesAsync();
        }

        await using var read = factory.Services.CreateAsyncScope();
        var stored = await read.ServiceProvider.GetRequiredService<SilexGisDbContext>()
            .TripLogs.AsNoTracking().SingleAsync(t => t.Id == id);

        // The alarm is disarmed, not deleted: what was arranged is still readable afterwards,
        // which is the point of writing it down in the first place.
        stored.CalloutState.ShouldBe(TripCalloutState.StoodDown);
        stored.ExpectedReturnAt.ShouldBe(expectedReturn);
        stored.CalloutAlarmAt.ShouldBe(expectedReturn.AddHours(2));
    }

    [Fact]
    public void An_interval_of_zero_is_no_schedule_at_all()
    {
        new TripCalloutOptions { SweepInterval = TimeSpan.Zero }.SweepIsScheduled.ShouldBeFalse();
        // Negative reads as off too, rather than as an interval so short it never stops.
        new TripCalloutOptions { SweepInterval = TimeSpan.FromMinutes(-1) }.SweepIsScheduled.ShouldBeFalse();
        // And the positive case beside it, so a predicate that answered "off" to everything
        // could not pass this.
        new TripCalloutOptions { SweepInterval = TimeSpan.FromMinutes(15) }.SweepIsScheduled.ShouldBeTrue();
    }

    [Fact]
    public void A_configured_interval_is_never_longer_than_the_page_assumes()
    {
        // The trip page reports a check the pass has not run for an hour as unchecked, in as many
        // words, and tells the reader to reach the party another way. The page cannot know what
        // this is set to, so an installation that lengthened the interval past that would show
        // that warning on every armed trip forever — and a warning that is always on is one
        // nobody reads, on the one feature where being ignored costs the most.
        new TripCalloutOptions { SweepInterval = TimeSpan.FromHours(2) }
            .EffectiveSweepInterval.ShouldBe(TripCalloutOptions.MaxSweepInterval);

        // Anything under the ceiling is honoured exactly, so this is a ceiling and not a policy
        // about how often the pass runs.
        new TripCalloutOptions { SweepInterval = TimeSpan.FromMinutes(1) }
            .EffectiveSweepInterval.ShouldBe(TimeSpan.FromMinutes(1));

        // And switched off stays switched off: the ceiling never turns a schedule back on.
        new TripCalloutOptions { SweepInterval = TimeSpan.Zero }.SweepIsScheduled.ShouldBeFalse();
    }

    [Fact]
    public void The_pass_is_switched_off_wherever_the_tests_run()
    {
        // Every test class in this suite shares one database. A pass left running under any of
        // them writes to trips the others own — moving an armed party to overdue and queueing an
        // alarm nobody asked for, in a class containing no bug at all. So the setting is asserted
        // on the running host rather than trusted: it is the whole of what keeps the suite honest
        // about what wrote a row.
        var options = factory.Services.GetRequiredService<IOptions<TripCalloutOptions>>().Value;

        options.SweepInterval.ShouldBe(TimeSpan.Zero);
        options.SweepIsScheduled.ShouldBeFalse();
    }

    [Fact]
    public void A_class_that_wants_the_pass_running_can_switch_it_back_on()
    {
        // The switch is an operator setting first and a test convenience second, so it has to
        // work in both directions — a class exercising the pass itself passes its own interval,
        // and the factory's own value must not win over it.
        using var wanted = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["TripCallout:SweepInterval"] = "00:05:00" });

        var options = wanted.Services.GetRequiredService<IOptions<TripCalloutOptions>>().Value;

        options.SweepInterval.ShouldBe(TimeSpan.FromMinutes(5));
        options.SweepIsScheduled.ShouldBeTrue();
    }

    private async Task<Guid> StoreTripAsync(Action<TripLog> arrange)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var trip = new TripLog
        {
            Title = $"Callout {Guid.NewGuid():N}",
            TripDate = new DateOnly(2026, 9, 3),
            OwnerUserId = ownerId,
            Visibility = Visibility.Private,
        };
        arrange(trip);

        db.TripLogs.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }
}
