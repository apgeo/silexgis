// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The events table and the domain that governs it: that the schema reaches a fresh database
/// with its days floating and its times zoneless, that an event is read exactly as far as its
/// audience says and no further, and that a right over every trip confers nothing here while an
/// entry naming one event does.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EventAccessDomainTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private Guid ownerId;
    private Guid outsiderId;
    private Guid clubmateId;
    private Guid cavingGroupId;

    public EventAccessDomainTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ev-own-{suffix}@t.local");

        // Viewers, deliberately. The seeded Editors group reads past visibility at the widest
        // scope by design, so an "an editor cannot see it" assertion would prove nothing about
        // the audience column — it would only prove the account had no editor rights.
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ev-out-{suffix}@t.local");
        clubmateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ev-club-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var group = new CavingGroup { Name = $"Event Club {suffix}", Slug = $"event-club-{suffix}" };
        db.CavingGroups.Add(group);
        await db.SaveChangesAsync();
        cavingGroupId = group.Id;
        await RosterHelper.AddMemberAsync(db, cavingGroupId, ownerId);
        await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
    }

    [Fact]
    public async Task The_schema_reaches_a_fresh_database_and_keeps_a_day_a_day_and_a_time_a_time()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var meeting = await AddEventAsync(db, ownerId, Visibility.Private, $"Club night {suffix}",
            new DateOnly(2026, 3, 14), kind: EventKind.ClubMeeting,
            startTime: new TimeOnly(19, 0), endTime: new TimeOnly(22, 30));

        var stored = await db.Events.AsNoTracking().SingleAsync(x => x.Id == meeting.Id);
        stored.Kind.ShouldBe(EventKind.ClubMeeting);
        stored.Visibility.ShouldBe(Visibility.Private);
        stored.State.ShouldBe(ActivityState.Draft);
        stored.OwnerUserId.ShouldBe(ownerId);
        stored.CreatedAt.ShouldNotBe(default);

        // The day comes back as the day it was written and the times as the times on a wall,
        // with nothing in between having read either as an instant in a zone. That is what lets
        // one grid draw an event beside a trip and a camp without asking which cell each is in
        // twice and getting two answers.
        stored.StartDate.ShouldBe(new DateOnly(2026, 3, 14));
        stored.EndDate.ShouldBeNull();
        stored.StartTime.ShouldBe(new TimeOnly(19, 0));
        stored.EndTime.ShouldBe(new TimeOnly(22, 30));

        var columns = db.Model.FindEntityType(typeof(Event))!;
        columns.GetProperty(nameof(Event.StartDate)).GetColumnType().ShouldBe("date");
        columns.GetProperty(nameof(Event.StartTime)).GetColumnType().ShouldBe("time without time zone");
    }

    [Fact]
    public async Task An_end_that_is_not_after_the_start_is_refused_by_the_database_itself()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // A stored end means "and it ran on to", so an end equal to the start would make a
        // single-day event read as a range of itself everywhere at once. The rule is held in
        // the schema and not only in the write path, so a writer that skips the normalisation
        // fails loudly rather than producing a row every reader has to second-guess.
        var day = new DateOnly(2026, 4, 4);
        db.Events.Add(new Event
        {
            Title = $"Same day {suffix}",
            OwnerUserId = ownerId,
            StartDate = day,
            EndDate = day,
        });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // The positive half, so the assertion above cannot pass against a table that refuses
        // everything: a genuine span is written without complaint.
        await using var second = factory.Services.CreateAsyncScope();
        var freshDb = second.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var weekend = await AddEventAsync(freshDb, ownerId, Visibility.Private,
            $"Training weekend {suffix}", day, kind: EventKind.Training, endDate: day.AddDays(2));
        (await freshDb.Events.AsNoTracking().SingleAsync(x => x.Id == weekend.Id))
            .EndDate.ShouldBe(day.AddDays(2));
    }

    [Fact]
    public async Task An_event_is_read_exactly_as_far_as_its_audience_says()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var mine = await AddEventAsync(db, ownerId, Visibility.Private, $"Private {suffix}",
            new DateOnly(2026, 5, 1));
        var clubs = await AddEventAsync(db, ownerId, Visibility.CavingGroup, $"Club {suffix}",
            new DateOnly(2026, 5, 2));
        clubs.CavingGroupId = cavingGroupId;
        await db.SaveChangesAsync();
        var everyones = await AddEventAsync(db, ownerId, Visibility.Authenticated, $"Open {suffix}",
            new DateOnly(2026, 5, 3));

        var owner = await RosterHelper.AccessContextOfAsync(db, ownerId);
        var clubmate = await RosterHelper.AccessContextOfAsync(db, clubmateId);
        var outsider = await RosterHelper.AccessContextOfAsync(db, outsiderId);

        // The positive half sits beside every negative one on purpose: a walk that returned
        // nothing at all would satisfy "the outsider cannot see it" and prove nothing.
        (await VisibleIdsAsync(db, owner)).ShouldBe([mine.Id, clubs.Id, everyones.Id], ignoreOrder: true);
        (await VisibleIdsAsync(db, clubmate)).ShouldBe([clubs.Id, everyones.Id], ignoreOrder: true);
        (await VisibleIdsAsync(db, outsider)).ShouldBe([everyones.Id]);
    }

    [Fact]
    public async Task A_grant_naming_one_event_reaches_it_and_a_right_over_every_trip_does_not()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var shut = await AddEventAsync(db, ownerId, Visibility.Private, $"Shut {suffix}",
            new DateOnly(2026, 6, 6));

        // Read over every trip in the installation, at the widest scope there is. This is the
        // only shape an event grant could have taken had events ridden the trip domain, because
        // an entry scoped to one object resolves what it is anchored to against the table its
        // domain names — an event id under the trips names nothing and is refused. So the
        // choice was between conferring everything and conferring nothing, and the domain of
        // its own is what makes the answer here "nothing".
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = outsiderId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = AccessActions.Everything,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();

        var withTripRights = await RosterHelper.AccessContextOfAsync(db, outsiderId);
        (await VisibleIdsAsync(db, withTripRights)).ShouldNotContain(shut.Id);

        // And the grant that is supposed to work does — the same subject, the same row, one
        // entry in the events domain naming that one event. That entry is the whole of what
        // "share this event with them" means, and without this half the assertion above would
        // pass just as well against a walk that admitted nobody to anything.
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = outsiderId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Events,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = shut.Id,
        });
        await db.SaveChangesAsync();

        var withEventGrant = await RosterHelper.AccessContextOfAsync(db, outsiderId);
        (await VisibleIdsAsync(db, withEventGrant)).ShouldContain(shut.Id);

        // Reading it is not writing it: ownership and the audience are separate answers, and
        // only the first admits a write.
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        var target = await db.Events.AsNoTracking().SingleAsync(x => x.Id == shut.Id);
        (await access.DecideAsync(withEventGrant, AccessAction.Read, target)).Allowed.ShouldBeTrue();
        (await access.DecideAsync(withEventGrant, AccessAction.Write, target)).Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task An_event_outlives_the_club_that_ran_it_and_not_the_account_that_wrote_it()
    {
        // The asymmetry every owned row carries: an account with content behind it is not
        // deleted out from under it, while a club that dissolves leaves its events standing.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var keys = db.Model.FindEntityType(typeof(Event))!.GetForeignKeys().ToList();
        keys.Single(fk => fk.Properties.Any(p => p.Name == nameof(Event.OwnerUserId)))
            .DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        keys.Single(fk => fk.Properties.Any(p => p.Name == nameof(Event.CavingGroupId)))
            .DeleteBehavior.ShouldBe(DeleteBehavior.SetNull);
    }

    private static async Task<Event> AddEventAsync(
        SilexGisDbContext db,
        Guid ownerUserId,
        Visibility visibility,
        string title,
        DateOnly startDate,
        EventKind kind = EventKind.ClubMeeting,
        DateOnly? endDate = null,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null)
    {
        var row = new Event
        {
            Title = title,
            OwnerUserId = ownerUserId,
            Visibility = visibility,
            Kind = kind,
            StartDate = startDate,
            EndDate = endDate,
            StartTime = startTime,
            EndTime = endTime,
        };
        db.Events.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// The events this caller reaches, narrowed to the ones this class made — the fixture shares
    /// one database with every other suite, so an unnarrowed count would be a count of the
    /// afternoon's other tests.
    /// </summary>
    private Task<List<Guid>> VisibleIdsAsync(SilexGisDbContext db, AccessContext ctx) =>
        db.Events.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Events)
            .Where(x => x.Title.EndsWith(suffix))
            .Select(x => x.Id)
            .ToListAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
