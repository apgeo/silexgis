// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests : IDisposable
{
    private readonly SilexGisApiFactory factory;

    public PersistenceTests(PostgresFixture postgres) => factory = new SilexGisApiFactory(postgres.ConnectionString);

    [Fact]
    public async Task Postgis_extension_is_installed_and_answers_spatial_sql()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var version = await db.Database
            .SqlQuery<string>($"SELECT postgis_full_version() AS \"Value\"")
            .SingleAsync();
        version.ShouldContain("POSTGIS=");

        var srid = await db.Database
            .SqlQuery<int>($"SELECT ST_SRID(ST_SetSRID(ST_MakePoint(25.58, 45.65), 4326)) AS \"Value\"")
            .SingleAsync();
        srid.ShouldBe(4326);
    }

    [Fact]
    public async Task Taxonomies_are_seeded_idempotently()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        (await db.CaveTypes.AnyAsync(x => x.Code == "cave")).ShouldBeTrue();
        (await db.EntranceTypes.AnyAsync(x => x.Code == "natural")).ShouldBeTrue();
        (await db.RockTypes.AnyAsync(x => x.Code == "limestone")).ShouldBeTrue();
        (await db.FeatureTypes.CountAsync()).ShouldBeGreaterThanOrEqualTo(19);

        // Re-running the seeder must not duplicate rows.
        var before = await db.FeatureTypes.CountAsync();
        await TaxonomySeeder.SeedAsync(db);
        (await db.FeatureTypes.CountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Insert_generates_uuid_v7_sets_timestamps_and_audits()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var team = new Team { Name = $"Test Team {Guid.NewGuid():N}", Slug = $"test-{Guid.NewGuid():N}" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        team.Id.ShouldNotBe(Guid.Empty);
        team.Id.ToString()[14].ShouldBe('7'); // uuid version nibble — must stay v7
        team.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
        team.UpdatedAt.ShouldBe(team.CreatedAt);

        var audit = await db.AuditEntries
            .Where(a => a.EntityType == nameof(Team) && a.EntityId == team.Id.ToString())
            .ToListAsync();
        audit.ShouldContain(a => a.Action == AuditActions.Created);

        team.Description = "updated";
        await db.SaveChangesAsync();
        team.UpdatedAt.ShouldBeGreaterThan(team.CreatedAt);

        var updateAudit = await db.AuditEntries
            .Where(a => a.EntityType == nameof(Team) && a.EntityId == team.Id.ToString() && a.Action == AuditActions.Updated)
            .SingleAsync();
        updateAudit.Changes.ShouldNotBeNull();
        updateAudit.Changes.ShouldContain("Description");
    }

    public void Dispose() => factory.Dispose();
}
