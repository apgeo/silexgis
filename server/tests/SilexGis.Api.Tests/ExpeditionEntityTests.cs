// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the expedition table itself guarantees, rather than what an endpoint does with it: a
/// stored end date always means "and it ran on to", and a working area is held in the one
/// reference system every geometry in this schema is held in.
/// </summary>
public sealed class ExpeditionEntityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private Guid ownerId;

    public ExpeditionEntityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xe-own-{suffix}@t.local");
    }

    [Fact]
    public async Task A_camp_that_ran_on_keeps_its_end_and_a_one_day_camp_has_none()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var fortnight = New("Two-week camp", new DateOnly(2026, 7, 14));
        fortnight.EndDate = DayRange.EndForStorage(fortnight.StartDate, new DateOnly(2026, 7, 28));
        var oneDay = New("Recce", new DateOnly(2026, 7, 14));
        oneDay.EndDate = DayRange.EndForStorage(oneDay.StartDate, new DateOnly(2026, 7, 14));
        db.Expeditions.AddRange(fortnight, oneDay);
        await db.SaveChangesAsync();

        var stored = await db.Expeditions.AsNoTracking()
            .Where(x => x.Id == fortnight.Id || x.Id == oneDay.Id)
            .ToDictionaryAsync(x => x.Id, x => x.EndDate);
        stored[fortnight.Id].ShouldBe(new DateOnly(2026, 7, 28));
        stored[oneDay.Id].ShouldBeNull();
    }

    [Fact]
    public async Task A_camp_ending_the_day_it_starts_is_refused_by_the_database()
    {
        // The rule that a stored end means "and it ran on to" is what every reader is written
        // against, so it is held where no writer can miss it. A row that got past the write
        // path's own normalisation would otherwise make one camp read as a range of itself and
        // nothing would notice.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var sameDay = New("Refused", new DateOnly(2026, 7, 14));
        sameDay.EndDate = new DateOnly(2026, 7, 14);
        db.Expeditions.Add(sameDay);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_camp_ending_before_it_starts_is_refused_by_the_database()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var backwards = New("Backwards", new DateOnly(2026, 7, 14));
        backwards.EndDate = new DateOnly(2026, 7, 1);
        db.Expeditions.Add(backwards);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_working_area_is_stored_and_read_back_in_the_schemas_own_reference_system()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var area = New("Massif camp", new DateOnly(2026, 8, 1));
        area.Geom = new Point(25.4455, 45.5301) { SRID = 4326 };
        db.Expeditions.Add(area);
        await db.SaveChangesAsync();

        var stored = await db.Expeditions.AsNoTracking().FirstAsync(x => x.Id == area.Id);
        stored.Geom.ShouldNotBeNull();
        stored.Geom!.SRID.ShouldBe(4326);
    }

    [Fact]
    public async Task A_trip_is_in_one_camp_and_the_table_is_what_says_so()
    {
        // "At most one camp" is an index, not a convention: a handler holds it only until the
        // second writer, and a bulk path or an import is exactly the second writer. The row it
        // would leave behind reads as a trip in two camps, and every roll-up over either would
        // then count it.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var first = New("First camp", new DateOnly(2026, 7, 14));
        var second = New("Second camp", new DateOnly(2026, 7, 14));
        var trip = new TripLog
        {
            Title = "A trip in one camp",
            TripDate = new DateOnly(2026, 7, 15),
            OwnerUserId = ownerId,
        };
        db.Expeditions.AddRange(first, second);
        db.TripLogs.Add(trip);
        db.ExpeditionTrips.Add(new ExpeditionTrip
        {
            ExpeditionId = first.Id,
            TripLogId = trip.Id,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        db.ExpeditionTrips.Add(new ExpeditionTrip
        {
            ExpeditionId = second.Id,
            TripLogId = trip.Id,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private Expedition New(string name, DateOnly start) =>
        new() { Name = name, StartDate = start, OwnerUserId = ownerId };

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
