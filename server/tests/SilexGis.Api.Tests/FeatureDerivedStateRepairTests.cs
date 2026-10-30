// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The operator's count and repair of stored hierarchy state, and the count of uploaded
/// centrelines whose stored length is not their passage's. Every defect here is written straight
/// to the database, because it reproduces rows an earlier faulty version stored: a test that went
/// through the write service could never produce them.
/// </summary>
public sealed class FeatureDerivedStateRepairTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private Guid ownerId;
    private long caveTypeId;

    public FeatureDerivedStateRepairTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fdsr-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// The shape the faulty hierarchy write left behind: a feature under a protected cave whose
    /// stored ancestry lost the cave, so its flag says unprotected and every read that trusts the
    /// flag shows its exact position. Beside it a feature carrying a closure row too many and a
    /// protection flag it should not have, so both directions of the closure repair are seen, and an
    /// untouched pair that must not be rewritten.
    /// </summary>
    [Fact]
    public async Task A_stale_ancestry_is_counted_left_alone_by_a_dry_run_and_repaired_by_apply()
    {
        var (guardedCave, guardedLine) = await SeedCaveWithCenterlineAsync(lengthM: null);
        var (openCave, openLine) = await SeedCaveWithCenterlineAsync(lengthM: null);
        await SeedCaveWithCenterlineAsync(lengthM: null);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FeatureWriteService>()
                .SetLocationProtectedAsync(guardedCave, true);
            await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().SaveChangesAsync();
        }

        // The write service's own output must count as clean, or the figures below prove nothing.
        var clean = await CountAsync();
        clean.FeaturesWrong.ShouldBe(0);
        (await VerifyAsync()).ShouldBeEmpty();
        (await ProtectedEffectiveAsync(guardedLine)).ShouldBeTrue();

        var guardedAncestors = await ExecuteReturningAsync(db => db.Features
            .Where(f => f.Id == guardedLine).Select(f => f.AncestorIds).SingleAsync());
        await ExecuteAsync(async db =>
        {
            await db.Features.Where(f => f.Id == guardedLine).ExecuteUpdateAsync(s => s
                .SetProperty(f => f.AncestorIds, new[] { guardedLine })
                .SetProperty(f => f.IsProtectedEffective, false));
            await db.FeatureAncestors
                .Where(a => a.FeatureId == guardedLine && a.AncestorId == guardedCave).ExecuteDeleteAsync();

            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = openLine, AncestorId = guardedCave });
            await db.SaveChangesAsync();
            await db.Features.Where(f => f.Id == openLine)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsProtectedEffective, true));
        });

        try
        {
            var broken = await CountAsync();
            broken.FeaturesExamined.ShouldBe(clean.FeaturesExamined);
            broken.FeaturesWrong.ShouldBe(2);
            broken.AncestorIdsWrong.ShouldBe(1);
            broken.ClosureWrong.ShouldBe(2);
            broken.ProtectedEffectiveWrong.ShouldBe(2);
            broken.ShownAsUnprotected.ShouldBe(1);

            // A dry run does the work inside its transaction, says it would reach zero, and leaves
            // every row as it found it.
            var dryRun = await RepairAsync(apply: false);
            dryRun.Applied.ShouldBeFalse();
            dryRun.Before.ShouldBe(broken);
            dryRun.FeatureRowsUpdated.ShouldBe(2);
            dryRun.ClosureRowsAdded.ShouldBe(1);
            dryRun.ClosureRowsRemoved.ShouldBe(1);
            dryRun.After.FeaturesWrong.ShouldBe(0);
            (await CountAsync()).ShouldBe(broken);
            (await ProtectedEffectiveAsync(guardedLine)).ShouldBeFalse();

            // Applied, it rewrites exactly the two rows the count named and nothing beside them.
            var applied = await RepairAsync(apply: true);
            applied.Applied.ShouldBeTrue();
            applied.FeatureRowsUpdated.ShouldBe(2);
            applied.ClosureRowsAdded.ShouldBe(1);
            applied.ClosureRowsRemoved.ShouldBe(1);
            applied.After.FeaturesWrong.ShouldBe(0);

            (await CountAsync()).FeaturesWrong.ShouldBe(0);
            (await VerifyAsync()).ShouldBeEmpty();
            (await ProtectedEffectiveAsync(guardedLine)).ShouldBeTrue();
            (await ProtectedEffectiveAsync(openLine)).ShouldBeFalse();
            (await ProtectedEffectiveAsync(openCave)).ShouldBeFalse();
            (await ExecuteReturningAsync(db => db.Features
                    .Where(f => f.Id == guardedLine).Select(f => f.AncestorIds).SingleAsync()))
                .Order().ShouldBe(guardedAncestors.Order());
        }
        finally
        {
            // Put back by hand whatever a failed repair left, so the class's database stays sound
            // for the case after this one.
            await ExecuteAsync(async db =>
            {
                await db.Features.Where(f => f.Id == guardedLine).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.AncestorIds, guardedAncestors)
                    .SetProperty(f => f.IsProtectedEffective, true));
                if (!await db.FeatureAncestors.AnyAsync(a => a.FeatureId == guardedLine && a.AncestorId == guardedCave))
                {
                    db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = guardedLine, AncestorId = guardedCave });
                    await db.SaveChangesAsync();
                }

                await db.FeatureAncestors
                    .Where(a => a.FeatureId == openLine && a.AncestorId == guardedCave).ExecuteDeleteAsync();
                await db.Features.Where(f => f.Id == openLine)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsProtectedEffective, false));
            });
        }
    }

    [Fact]
    public async Task Applying_a_repair_on_sound_state_changes_nothing()
    {
        await SeedCaveWithCenterlineAsync(lengthM: null);

        var result = await RepairAsync(apply: true);

        result.Before.FeaturesWrong.ShouldBe(0);
        result.FeatureRowsUpdated.ShouldBe(0);
        result.ClosureRowsAdded.ShouldBe(0);
        result.ClosureRowsRemoved.ShouldBe(0);
        result.After.FeaturesWrong.ShouldBe(0);
    }

    /// <summary>
    /// An upload measured over every wall shot stored a length many times the passage's. The count
    /// finds it by measuring the passage again the way an upload now does; a figure that agrees, a
    /// centreline with no figure and one read out of a survey model (always measured over the
    /// traverse) are not counted as inflated.
    /// </summary>
    [Fact]
    public async Task An_uploaded_centreline_stored_longer_than_its_passage_is_counted()
    {
        var before = await LengthCountAsync();

        var geometry = Line(0);
        var passage = await ExecuteReturningAsync(db =>
            CenterlineSql.GeodesicLengthMetersAsync(db, CenterlineSkeleton.Build3D(geometry), CancellationToken.None));
        passage.ShouldBeGreaterThan(1m);

        await SeedCaveWithCenterlineAsync(lengthM: passage * 15m, offset: 1);
        await SeedCaveWithCenterlineAsync(lengthM: passage, offset: 2);
        await SeedCaveWithCenterlineAsync(lengthM: null, offset: 3);
        await SeedCaveWithCenterlineAsync(lengthM: passage * 15m, offset: 4, source: CenterlineSource.Extracted);

        var after = await LengthCountAsync();
        (after.Inflated - before.Inflated).ShouldBe(1);
        (after.Agreeing - before.Agreeing).ShouldBe(1);
        (after.NotMeasurable - before.NotMeasurable).ShouldBe(1);
        (after.Shorter - before.Shorter).ShouldBe(0);
        (after.Examined - before.Examined).ShouldBe(2);
    }

    // ---------- helpers ----------

    private static MultiLineString Line(int offset) =>
        new([
            new LineString([
                new CoordinateZ(25.1 + (offset * 0.01), 45.1, 900),
                new CoordinateZ(25.1005 + (offset * 0.01), 45.1004, 890),
                new CoordinateZ(25.101 + (offset * 0.01), 45.1006, 885),
            ]),
        ])
        { SRID = 4326 };

    private async Task<(Guid CaveId, Guid CenterlineId)> SeedCaveWithCenterlineAsync(
        decimal? lengthM, int offset = 0, CenterlineSource source = CenterlineSource.Uploaded)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();

        var cave = await writer.CreateCaveAsync(
            new Feature
            {
                Name = $"Repair cave {Guid.NewGuid():N}",
                OwnerUserId = ownerId,
                Visibility = Visibility.Public,
            },
            new Cave { CaveTypeId = caveTypeId },
            parents: []);
        await db.SaveChangesAsync();

        var line = new Feature { Name = "Centerline", OwnerUserId = ownerId, Geom = Line(offset) };
        await writer.CreateCenterlineAsync(
            line,
            new Centerline { CaveFeatureId = cave.Id, PathCount = 1, LengthM = lengthM, Source = source });
        await db.SaveChangesAsync();
        return (cave.Id, line.Id);
    }

    private async Task<DerivedStateCount> CountAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FeatureDerivedStateRepair>().CountAsync();
    }

    private async Task<DerivedStateRepairResult> RepairAsync(bool apply)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FeatureDerivedStateRepair>().RepairAsync(apply);
    }

    private async Task<CenterlineLengthCount> LengthCountAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CenterlineLengthAudit>().CountAsync();
    }

    private async Task<IReadOnlyList<IntegrityProblem>> VerifyAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync();
    }

    private Task<bool> ProtectedEffectiveAsync(Guid featureId) =>
        ExecuteReturningAsync(db => db.Features.IgnoreQueryFilters()
            .Where(f => f.Id == featureId).Select(f => f.IsProtectedEffective).SingleAsync());

    private async Task ExecuteAsync(Func<SilexGisDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private async Task<T> ExecuteReturningAsync<T>(Func<SilexGisDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
