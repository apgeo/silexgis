// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Expeditions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the camp-roster table itself guarantees, rather than what an endpoint does with it: a
/// stored end always means "and they stayed on to", nothing constrains one stay against another,
/// and a person is counted once however many roles they held.
/// </summary>
public sealed class ExpeditionRosterEntityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private Guid ownerId;
    private long memberRoleId;
    private long cookRoleId;

    public ExpeditionRosterEntityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xr-own-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        // By code, the way every seeded vocabulary is reached: the identity is an installation
        // detail and the code is what ships.
        memberRoleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == ExpeditionRosterRoleSeeds.MemberCode).Select(r => r.Id).SingleAsync();
        cookRoleId = await db.ExpeditionRosterRoles
            .Where(r => r.Code == "cook").Select(r => r.Id).SingleAsync();
    }

    [Fact]
    public async Task A_stay_that_ran_on_keeps_its_end_and_a_single_day_has_none()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Fortnight");
        var caver = NewCaver("Stayed all fortnight");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);

        var fortnight = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18));
        fortnight.ToDate = DayRange.EndForStorage(fortnight.FromDate, new DateOnly(2026, 8, 1));
        var oneDay = New(camp, caver, cookRoleId, new DateOnly(2026, 7, 20));
        oneDay.ToDate = DayRange.EndForStorage(oneDay.FromDate, new DateOnly(2026, 7, 20));
        db.ExpeditionRoster.AddRange(fortnight, oneDay);
        await db.SaveChangesAsync();

        var stored = await db.ExpeditionRoster.AsNoTracking()
            .Where(x => x.Id == fortnight.Id || x.Id == oneDay.Id)
            .ToDictionaryAsync(x => x.Id, x => x.ToDate);
        stored[fortnight.Id].ShouldBe(new DateOnly(2026, 8, 1));
        stored[oneDay.Id].ShouldBeNull();
    }

    [Fact]
    public async Task A_stay_with_no_end_is_somebody_who_is_still_there()
    {
        // An absent end is not missing data: the row says nothing ran on past the first day, so
        // while the camp is still going it reads as somebody who has not left. The column is
        // nullable for that reason and no writer is obliged to supply one.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Still going");
        var caver = NewCaver("Still there");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);

        var openEnded = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18));
        db.ExpeditionRoster.Add(openEnded);
        await db.SaveChangesAsync();

        var stored = await db.ExpeditionRoster.AsNoTracking().FirstAsync(x => x.Id == openEnded.Id);
        stored.ToDate.ShouldBeNull();
        stored.FromDate.ShouldBe(new DateOnly(2026, 7, 18));
    }

    [Fact]
    public async Task A_stay_ending_the_day_it_starts_is_refused_by_the_database()
    {
        // The rule that a stored end means "and they stayed on to" is what every reader of the
        // interval is written against, so it is held where no writer can miss it. A row that got
        // past the write path's own normalisation would otherwise make one day read as a range of
        // itself and nothing would notice.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Refuses a same-day end");
        var caver = NewCaver("Same-day end");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();

        var sameDay = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18));
        sameDay.ToDate = new DateOnly(2026, 7, 18);
        db.ExpeditionRoster.Add(sameDay);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_stay_ending_before_it_starts_is_refused_by_the_database()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Refuses a backwards stay");
        var caver = NewCaver("Backwards stay");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);
        await db.SaveChangesAsync();

        var backwards = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18));
        backwards.ToDate = new DateOnly(2026, 7, 10);
        db.ExpeditionRoster.Add(backwards);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task One_person_may_hold_two_roles_over_overlapping_days_and_is_still_one_person()
    {
        // Asserted rather than left unstated, because the table is defined as much by what it does
        // not constrain as by what it does. Nothing forbids two stays that overlap: somebody who
        // cooked for the first week and was on the roster for the whole fortnight is two rows, and
        // a uniqueness index copied from the trip's own people would have refused the second.
        // The same shape is why a count of people over a camp counts rows' cavers distinctly.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Two hats");
        var caver = NewCaver("Cooked and was there");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);

        var whole = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18));
        whole.ToDate = new DateOnly(2026, 8, 1);
        var cooking = New(camp, caver, cookRoleId, new DateOnly(2026, 7, 18));
        cooking.ToDate = new DateOnly(2026, 7, 25);
        db.ExpeditionRoster.AddRange(whole, cooking);
        await db.SaveChangesAsync();

        var rows = await db.ExpeditionRoster.AsNoTracking()
            .Where(x => x.ExpeditionId == camp.Id)
            .ToListAsync();
        rows.Count.ShouldBe(2);
        rows.Select(x => x.CaverId).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task One_person_may_leave_and_come_back_in_the_same_role()
    {
        // Two rows for one person in one role on one camp: an ordinary record of two stays, not a
        // duplicate. This is the case a unique index over (camp, role, person) would have refused.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Left and came back");
        var caver = NewCaver("Went home mid-camp");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);

        var first = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18));
        first.ToDate = new DateOnly(2026, 7, 22);
        var second = New(camp, caver, memberRoleId, new DateOnly(2026, 7, 28));
        second.ToDate = new DateOnly(2026, 8, 1);
        db.ExpeditionRoster.AddRange(first, second);
        await db.SaveChangesAsync();

        var rows = await db.ExpeditionRoster.AsNoTracking()
            .Where(x => x.ExpeditionId == camp.Id && x.RoleId == memberRoleId)
            .CountAsync();
        rows.ShouldBe(2);
    }

    [Fact]
    public async Task A_camp_that_goes_takes_its_roster_with_it_and_the_people_stay()
    {
        // The presence record belongs to the camp and means nothing without it, so it cascades.
        // The people do not: a caver is the club's own record and outlives every camp they were at.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var camp = NewCamp("Deleted camp");
        var caver = NewCaver("Was at the deleted camp");
        db.Expeditions.Add(camp);
        db.Cavers.Add(caver);
        db.ExpeditionRoster.Add(New(camp, caver, memberRoleId, new DateOnly(2026, 7, 18)));
        await db.SaveChangesAsync();

        db.Expeditions.Remove(camp);
        await db.SaveChangesAsync();

        (await db.ExpeditionRoster.AsNoTracking().AnyAsync(x => x.ExpeditionId == camp.Id)).ShouldBeFalse();
        (await db.Cavers.AsNoTracking().AnyAsync(c => c.Id == caver.Id)).ShouldBeTrue();
    }

    private static ExpeditionRosterEntry New(Expedition camp, Caver caver, long roleId, DateOnly from) =>
        new() { ExpeditionId = camp.Id, CaverId = caver.Id, RoleId = roleId, FromDate = from };

    private Expedition NewCamp(string name) =>
        new() { Name = name, StartDate = new DateOnly(2026, 7, 18), OwnerUserId = ownerId };

    private static Caver NewCaver(string what) =>
        new() { FullName = $"Roster {what} {Guid.NewGuid().ToString("N")[..6]}" };

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
