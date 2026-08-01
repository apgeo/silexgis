// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The integrity verifier's own checks. Every case here breaks state by writing straight
/// to the database — that is the point: the verifier exists to catch a write path that
/// bypassed the aggregate write service, so a test that went through the service could
/// never produce the state being checked for.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeatureIntegrityTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private Guid ownerId;
    private long caveTypeId;

    public FeatureIntegrityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fint-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task A_cave_left_without_a_default_centerline_is_reported()
    {
        var caveId = await SeedCaveWithCenterlinesAsync(count: 2);

        // The service's own output must be clean, or the check below proves nothing.
        (await VerifyAsync()).ShouldBeEmpty();

        // Drop the default without promoting a successor — what a delete path that
        // forgot the promotion would leave behind.
        var demoted = await SingleAsync(db => db.Centerlines
            .Where(c => c.CaveFeatureId == caveId && c.IsDefault).Select(c => c.Id));
        await ExecuteAsync(db => db.Centerlines
            .Where(c => c.Id == demoted)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false)));

        var problem = (await VerifyAsync()).ShouldHaveSingleItem();
        problem.Check.ShouldBe("default_centerline");
        problem.FeatureId.ShouldBe(caveId);
        problem.Detail.ShouldBe("2 centerline(s), 0 marked default");

        // The suite shares one database and other classes assert the verifier finds
        // nothing at all, so every case here puts back what it broke.
        await ExecuteAsync(db => db.Centerlines
            .Where(c => c.Id == demoted)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, true)));
    }

    [Fact]
    public async Task A_soft_deleted_centerline_does_not_count_towards_the_default_rule()
    {
        var caveId = await SeedCaveWithCenterlinesAsync(count: 2);

        // Delete the non-default one. One live centerline remains and it is the default,
        // so the rule still holds — the tombstoned row must not be counted as a second.
        var secondary = await SingleAsync(db => db.Centerlines
            .Where(c => c.CaveFeatureId == caveId && !c.IsDefault).Select(c => c.Id));
        await ExecuteAsync(db => db.Features
            .Where(f => f.Id == secondary)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.DeletedAt, DateTimeOffset.UtcNow)));

        (await VerifyAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_share_token_that_is_not_a_sha256_digest_is_reported()
    {
        var caveId = await SeedCaveWithCenterlinesAsync(count: 1);

        // A correctly minted share: base64url of a 32-byte digest.
        var good = new FeatureShare
        {
            FeatureId = caveId,
            TokenHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes("a token"))),
            CreatedBy = ownerId,
        };
        await ExecuteAsync(async db =>
        {
            db.FeatureShares.Add(good);
            await db.SaveChangesAsync();
        });
        (await VerifyAsync()).ShouldBeEmpty();

        // A hash the mint path could not have produced — here a truncated one, standing
        // in for any value that did not come out of the hash.
        await ExecuteAsync(db => db.FeatureShares
            .Where(s => s.Id == good.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenHash, "too-short")));

        var problem = (await VerifyAsync()).ShouldHaveSingleItem();
        problem.Check.ShouldBe("share_token_hash");
        problem.FeatureId.ShouldBe(good.Id);

        await ExecuteAsync(db => db.FeatureShares.Where(s => s.Id == good.Id).ExecuteDeleteAsync());
    }

    // ---- helpers ----

    /// <summary>
    /// A cave with <paramref name="count"/> centerlines, written through the aggregate
    /// service so every derived column starts out correct.
    /// </summary>
    private async Task<Guid> SeedCaveWithCenterlinesAsync(int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();

        var cave = await writer.CreateCaveAsync(
            new Feature
            {
                Name = $"Integrity cave {Guid.NewGuid():N}",
                OwnerUserId = ownerId,
                Visibility = Visibility.Public,
            },
            new Cave { CaveTypeId = caveTypeId },
            parents: []);
        await db.SaveChangesAsync();

        for (var i = 0; i < count; i++)
        {
            await writer.CreateCenterlineAsync(
                new Feature
                {
                    Name = $"Centerline {i}",
                    OwnerUserId = ownerId,
                    Geom = new MultiLineString([
                        new LineString([
                            new CoordinateZ(25.1 + (i * 0.001), 45.1, 900),
                            new CoordinateZ(25.2 + (i * 0.001), 45.2, 880),
                        ]),
                    ])
                    { SRID = 4326 },
                },
                new Centerline { CaveFeatureId = cave.Id, PathCount = 1 });
            await db.SaveChangesAsync();
        }

        return cave.Id;
    }

    private async Task<IReadOnlyList<IntegrityProblem>> VerifyAsync()
    {
        using var scope = factory.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>();
        return await verifier.VerifyAsync();
    }

    private async Task ExecuteAsync(Func<SilexGisDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private async Task<Guid> SingleAsync(Func<SilexGisDbContext, IQueryable<Guid>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()).SingleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
