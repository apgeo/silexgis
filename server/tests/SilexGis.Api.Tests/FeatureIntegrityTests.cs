// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The integrity verifier's own checks. Every case here breaks state by writing straight
/// to the database — that is the point: the verifier exists to catch a write path that
/// bypassed the aggregate write service, so a test that went through the service could
/// never produce the state being checked for.
/// </summary>
public sealed class FeatureIntegrityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private Guid ownerId;
    private long caveTypeId;
    private long entranceTypeId;

    public FeatureIntegrityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fint-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// The scheduled pass is due when the last one was queued at least an interval ago, or none ever
    /// was — counted from the queue, not from when this process started, so an instance restarted
    /// more often than the interval is still checked, and a restart does not queue a second pass.
    /// </summary>
    [Fact]
    public async Task A_scheduled_pass_is_due_from_the_last_pass_not_from_start_up()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.FeatureIntegrityVerify).ExecuteDeleteAsync();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        FeatureIntegrityScheduler Started() => new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new FeatureIntegrityOptions { Interval = TimeSpan.FromHours(24) }),
            clock,
            NullLogger<FeatureIntegrityScheduler>.Instance);

        (await Started().QueueIfDueAsync(CancellationToken.None)).ShouldBeTrue("no pass was ever queued");
        await FinishPassesAsync(db);

        // A restart an hour later, and one a day less an hour after the pass: nothing is due.
        clock.Now = clock.Now.AddHours(1);
        (await Started().QueueIfDueAsync(CancellationToken.None)).ShouldBeFalse();
        clock.Now = clock.Now.AddHours(22);
        (await Started().QueueIfDueAsync(CancellationToken.None)).ShouldBeFalse();

        // A day after the last pass, whatever the process's age: due.
        clock.Now = clock.Now.AddHours(2);
        (await Started().QueueIfDueAsync(CancellationToken.None)).ShouldBeTrue();
        (await db.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.FeatureIntegrityVerify)).ShouldBe(2);
    }

    private static Task FinishPassesAsync(SilexGisDbContext db) =>
        db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.FeatureIntegrityVerify)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ProcessingJobStatus.Succeeded));

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

    /// <summary>
    /// A centerline sits under its own cave and nothing else. A second, non-primary edge — which
    /// the schema does not prevent — is reported, and only as that: the closure and the stored
    /// ancestor ids are brought into line with the extra edge first, so the one thing wrong is the
    /// parent set itself.
    /// </summary>
    [Fact]
    public async Task A_centerline_with_a_second_parent_breaks_the_fixed_parent_rule()
    {
        var caveId = await SeedCaveWithCenterlinesAsync(count: 1);
        var otherCaveId = await SeedCaveWithCenterlinesAsync(count: 0);
        var centerlineId = await SingleAsync(db => db.Centerlines
            .Where(c => c.CaveFeatureId == caveId).Select(c => c.Id));

        (await VerifyAsync()).ShouldBeEmpty();

        var ancestorIds = await ExecuteReturningAsync(db => db.Features
            .Where(f => f.Id == centerlineId).Select(f => f.AncestorIds).SingleAsync());
        await ExecuteAsync(async db =>
        {
            db.FeatureHierarchyEdges.Add(
                new FeatureHierarchyEdge { ParentId = otherCaveId, ChildId = centerlineId, IsPrimary = false });
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = centerlineId, AncestorId = otherCaveId });
            await db.SaveChangesAsync();
            await db.Features.Where(f => f.Id == centerlineId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.AncestorIds, ancestorIds.Append(otherCaveId).ToArray()));
        });

        try
        {
            var problem = (await VerifyAsync()).ShouldHaveSingleItem();
            problem.Check.ShouldBe("fixed_parent");
            problem.FeatureId.ShouldBe(centerlineId);
            problem.Detail.ShouldBe("2 containment edge(s), 1 besides the primary edge to its own cave");
        }
        finally
        {
            // Other classes assert the verifier finds nothing in the shared database.
            await ExecuteAsync(async db =>
            {
                await db.FeatureHierarchyEdges
                    .Where(e => e.ParentId == otherCaveId && e.ChildId == centerlineId).ExecuteDeleteAsync();
                await db.FeatureAncestors
                    .Where(a => a.FeatureId == centerlineId && a.AncestorId == otherCaveId).ExecuteDeleteAsync();
                await db.Features.Where(f => f.Id == centerlineId)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.AncestorIds, ancestorIds));
            });
        }

        (await VerifyAsync()).ShouldBeEmpty();
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

    /// <summary>
    /// The kinds that have no table of their own, so nothing exists for a pair to point at and
    /// the verifier has nothing to check them against. Every other value of the shared vocabulary
    /// is driven below: a member added to it later fails here until the verifier covers it or it
    /// is named on this list with its reason.
    /// </summary>
    private static readonly AttachedEntityType[] KindsWithoutATable =
    [
        // Reserved for a comment entity that does not exist yet.
        AttachedEntityType.Comment,
    ];

    public static TheoryData<AttachedEntityType> KindsWithATable()
    {
        var data = new TheoryData<AttachedEntityType>();
        foreach (var type in Enum.GetValues<AttachedEntityType>().Except(KindsWithoutATable))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KindsWithATable))]
    public async Task A_row_pointing_at_a_vanished_target_is_reported_for_every_kind_that_has_a_table(
        AttachedEntityType type)
    {
        // The polymorphic pair has no foreign key, so nothing but this check notices a row
        // whose target is gone. The net is a list of kinds, one line each, and a kind left off
        // it is silently unchecked rather than loudly wrong — which is why every kind that has
        // a table of its own is driven from the enumeration rather than read off the list.
        (await VerifyAsync()).ShouldBeEmpty();

        var tagId = await ExecuteReturningAsync(async db =>
        {
            var name = $"orphan-net-{Guid.NewGuid():N}"[..20];
            var tag = new Tag { Name = name, Slug = name };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            return tag.Id;
        });

        var vanished = Guid.CreateVersion7();
        await ExecuteAsync(async db =>
        {
            db.Taggings.Add(new Tagging { TagId = tagId, EntityType = type, EntityId = vanished });
            await db.SaveChangesAsync();
        });

        var problem = (await VerifyAsync()).ShouldHaveSingleItem();
        problem.Check.ShouldBe("tagging_orphan");
        problem.Detail.ShouldContain(vanished.ToString());
        problem.Detail.ShouldContain(type.ToString());

        // The suite shares one database and other classes assert the verifier finds nothing at
        // all, so this puts back what it broke.
        await ExecuteAsync(db => db.Taggings.Where(t => t.EntityId == vanished).ExecuteDeleteAsync());
        await ExecuteAsync(db => db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync());
        (await VerifyAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// Binning a document keeps its tags, attachments and link memberships so that putting it
    /// back is exact. Those rows point at a row that still exists, only hidden; the nightly pass
    /// must not report them as pointing at nothing, while a pair naming a document that really
    /// is gone is still reported.
    /// </summary>
    [Fact]
    public async Task A_binned_document_keeps_its_tags_without_being_reported_as_an_orphan()
    {
        (await VerifyAsync()).ShouldBeEmpty();

        var (documentId, tagId) = await ExecuteReturningAsync(async db =>
        {
            var name = $"binned-doc-{Guid.NewGuid():N}"[..20];
            var tag = new Tag { Name = name, Slug = name };
            var document = new Document { Title = "Binned integrity document", OwnerUserId = ownerId };
            db.Tags.Add(tag);
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            db.Taggings.Add(new Tagging { TagId = tag.Id, EntityType = AttachedEntityType.Document, EntityId = document.Id });
            await db.SaveChangesAsync();
            return (document.Id, tag.Id);
        });

        try
        {
            await ExecuteAsync(db => db.Documents
                .Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeletedAt, DateTimeOffset.UtcNow)));

            (await VerifyAsync()).ShouldBeEmpty();

            // The positive case in the same breath: a document that is really gone still shows.
            await ExecuteAsync(db => db.Documents.IgnoreQueryFilters()
                .Where(d => d.Id == documentId)
                .ExecuteDeleteAsync());
            var problem = (await VerifyAsync()).ShouldHaveSingleItem();
            problem.Check.ShouldBe("tagging_orphan");
            problem.Detail.ShouldContain(documentId.ToString());
        }
        finally
        {
            await ExecuteAsync(db => db.Taggings.Where(t => t.EntityId == documentId).ExecuteDeleteAsync());
            await ExecuteAsync(db => db.Documents.IgnoreQueryFilters().Where(d => d.Id == documentId).ExecuteDeleteAsync());
            await ExecuteAsync(db => db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync());
        }

        (await VerifyAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// A cave with live entrances has exactly one main one: its point is the cave's point. With
    /// none marked main the verifier reports that, and does not compare the cave's point with
    /// whichever entrance the database happened to return first.
    /// </summary>
    [Fact]
    public async Task A_cave_with_entrances_but_no_main_one_is_reported()
    {
        var caveId = await SeedCaveWithCenterlinesAsync(count: 1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
            for (var i = 0; i < 2; i++)
            {
                await writer.CreateEntranceAsync(
                    new Feature
                    {
                        Name = $"Integrity entrance {i}",
                        OwnerUserId = ownerId,
                        Geom = new Point(25.1 + (i * 0.01), 45.1) { SRID = 4326 },
                    },
                    new CaveEntrance { CaveFeatureId = caveId, EntranceTypeId = entranceTypeId });
                await db.SaveChangesAsync();
            }
        }

        (await VerifyAsync()).ShouldBeEmpty();

        var mainId = await SingleAsync(db => db.CaveEntrances
            .Where(e => e.CaveFeatureId == caveId && e.IsMain).Select(e => e.Id));
        try
        {
            await ExecuteAsync(db => db.CaveEntrances
                .Where(e => e.Id == mainId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsMain, false)));

            var problem = (await VerifyAsync()).ShouldHaveSingleItem();
            problem.Check.ShouldBe("main_entrance");
            problem.FeatureId.ShouldBe(caveId);
        }
        finally
        {
            await ExecuteAsync(db => db.CaveEntrances
                .Where(e => e.Id == mainId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsMain, true)));
        }

        (await VerifyAsync()).ShouldBeEmpty();
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

    private async Task<T> ExecuteReturningAsync<T>(Func<SilexGisDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private async Task<Guid> SingleAsync(Func<SilexGisDbContext, IQueryable<Guid>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>()).SingleAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
