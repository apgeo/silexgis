// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The cabinet tree against a real database: that the materialized path is stamped on
/// insert and rewritten for a whole subtree on re-parent, that the indexed subtree match
/// really is a database operation (it is the one thing the pure rules cannot prove), and
/// that sibling-name uniqueness is the database's answer rather than a convention.
/// </summary>
public class CabinetTreeTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory = new(postgres.ConnectionString);

    public void Dispose()
    {
        factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_cabinets_path_names_its_ancestors_and_the_subtree_match_runs_in_the_database()
    {
        using var scope = Scope();
        var (db, cabinets) = Services(scope);
        var tag = Guid.NewGuid().ToString("N")[..8];

        var archive = await cabinets.CreateAsync($"archive-{tag}", null, null);
        var bulletins = await cabinets.CreateAsync("bulletins", null, archive.Id);
        var year = await cabinets.CreateAsync("1987", null, bulletins.Id);
        var surveys = await cabinets.CreateAsync($"surveys-{tag}", null, null);
        await db.SaveChangesAsync();

        // Read the paths back out of storage rather than off the tracked instances.
        db.ChangeTracker.Clear();
        (await PathAsync(db, year.Id)).ShouldBe($"{archive.Id}.{bulletins.Id}.{year.Id}");
        CabinetHierarchyRules.IdsOf(await PathAsync(db, year.Id))
            .ShouldBe(new[] { archive.Id, bulletins.Id, year.Id });

        // The prefix match is translated to the database, not evaluated in memory: the
        // subtree comes back, and the unrelated root — which genuinely has no ancestor in
        // common — does not.
        var subtree = await cabinets.SubtreeAsync(await PathAsync(db, archive.Id));
        subtree.Select(c => c.Id).ShouldBe(new[] { archive.Id, bulletins.Id, year.Id }, ignoreOrder: true);
        subtree.ShouldNotContain(c => c.Id == surveys.Id);
    }

    [Fact]
    public async Task Re_parenting_rewrites_the_paths_of_everything_below_the_moved_cabinet()
    {
        using var scope = Scope();
        var (db, cabinets) = Services(scope);
        var tag = Guid.NewGuid().ToString("N")[..8];

        var archive = await cabinets.CreateAsync($"archive-{tag}", null, null);
        var bulletins = await cabinets.CreateAsync("bulletins", null, archive.Id);
        var year = await cabinets.CreateAsync("1987", null, bulletins.Id);
        var surveys = await cabinets.CreateAsync($"surveys-{tag}", null, null);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var moved = await db.Cabinets.SingleAsync(c => c.Id == bulletins.Id);
        await cabinets.MoveAsync(moved, surveys.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        moved.ParentId.ShouldBe(surveys.Id);
        (await PathAsync(db, bulletins.Id)).ShouldBe($"{surveys.Id}.{bulletins.Id}");
        (await PathAsync(db, year.Id)).ShouldBe($"{surveys.Id}.{bulletins.Id}.{year.Id}",
            "a descendant's path names its ancestors, so it moves with them");
        (await PathAsync(db, archive.Id)).ShouldBe(archive.Id.ToString(),
            "the cabinet left behind keeps its own path");

        // Promoting a cabinet to a root is the same operation with no new parent.
        var promoted = await db.Cabinets.SingleAsync(c => c.Id == bulletins.Id);
        await cabinets.MoveAsync(promoted, null);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await PathAsync(db, year.Id)).ShouldBe($"{bulletins.Id}.{year.Id}");
    }

    [Fact]
    public async Task A_cabinet_cannot_be_moved_into_its_own_subtree()
    {
        using var scope = Scope();
        var (db, cabinets) = Services(scope);
        var tag = Guid.NewGuid().ToString("N")[..8];

        var archive = await cabinets.CreateAsync($"archive-{tag}", null, null);
        var bulletins = await cabinets.CreateAsync("bulletins", null, archive.Id);
        var elsewhere = await cabinets.CreateAsync($"elsewhere-{tag}", null, null);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var moved = await db.Cabinets.SingleAsync(c => c.Id == archive.Id);
        var refusal = await Should.ThrowAsync<CabinetWriteException>(() => cabinets.MoveAsync(moved, bulletins.Id));
        refusal.Code.ShouldBe(CabinetHierarchyRules.CycleCode);

        // The same cabinet moves fine somewhere that is not below it.
        await cabinets.MoveAsync(moved, elsewhere.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await PathAsync(db, bulletins.Id)).ShouldBe($"{elsewhere.Id}.{archive.Id}.{bulletins.Id}");
    }

    [Fact]
    public async Task Nesting_stops_at_the_depth_cap()
    {
        using var scope = Scope();
        var (db, cabinets) = Services(scope);
        var tag = Guid.NewGuid().ToString("N")[..8];

        Guid? parentId = null;
        for (var level = 1; level <= CabinetHierarchyRules.MaxDepth; level++)
        {
            // The cap is reached level by level, so every level up to it is accepted.
            var cabinet = await cabinets.CreateAsync($"level-{level}-{tag}", null, parentId);
            parentId = cabinet.Id;
        }

        await db.SaveChangesAsync();

        var refusal = await Should.ThrowAsync<CabinetWriteException>(
            () => cabinets.CreateAsync($"too-deep-{tag}", null, parentId));
        refusal.Code.ShouldBe(CabinetHierarchyRules.TooDeepCode);
    }

    [Fact]
    public async Task A_name_is_unique_among_siblings_and_among_roots_but_not_across_the_tree()
    {
        using var scope = Scope();
        var (db, cabinets) = Services(scope);
        var tag = Guid.NewGuid().ToString("N")[..8];

        var first = await cabinets.CreateAsync($"archive-{tag}", null, null);
        var second = await cabinets.CreateAsync($"other-{tag}", null, null);
        await cabinets.CreateAsync("1987", null, first.Id);
        // The same name under a different parent is a different shelf, and is allowed.
        await cabinets.CreateAsync("1987", null, second.Id);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await cabinets.CreateAsync("1987", null, first.Id);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // Two roots may not share a name either — the database, not the caller, says so.
        await cabinets.CreateAsync($"archive-{tag}", null, null);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private IServiceScope Scope() => factory.Services.CreateScope();

    private static (SilexGisDbContext Db, CabinetWriteService Cabinets) Services(IServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
         scope.ServiceProvider.GetRequiredService<CabinetWriteService>());

    private static async Task<string> PathAsync(SilexGisDbContext db, Guid cabinetId)
    {
        var paths = await db.Cabinets.AsNoTracking()
            .Where(c => c.Id == cabinetId)
            .Select(c => EF.Property<LTree>(c, CabinetWriteService.PathProperty))
            .ToListAsync();

        return paths.Single();
    }
}
