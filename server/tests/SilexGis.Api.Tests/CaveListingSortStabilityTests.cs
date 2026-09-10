// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The caves listing has to be walkable: a client that asks for every page must be handed every
/// cave exactly once.
/// </summary>
/// <remarks>
/// <para>
/// None of the sortable columns is unique. Region and depth tie as a matter of course — a whole
/// massif shares one region, and round depths repeat — and even the updatedAt fallback ties after
/// an import, which stamps every row it wrote inside one transaction. PostgreSQL is under no
/// obligation to return tied rows in the same order twice, and it does not: each page is its own
/// query, so a row can land on page two the first time and page one the second. The client that
/// walked those pages sees it twice and never sees whatever it displaced.
/// </para>
/// <para>
/// Making that provable takes one deliberate step, and skipping it produces a test that passes
/// either way. Ids here are <see cref="Guid.CreateVersion7"/> — time-ordered — so ascending id
/// order is also insertion order, and a sequential scan of freshly written rows hands them back
/// in very nearly that order by accident. An ascending arm asserted against a fresh table
/// therefore passes with no tiebreaker at all, and only the descending arms fail. So the seeder
/// rewrites half the feature tuples afterwards with a raw update that changes no value: in
/// PostgreSQL that writes a new tuple version at the end of the heap, which moves those rows
/// physically past the ones written after them. Scan order and id order then disagree in both
/// directions, and every arm discriminates.
/// </para>
/// <para>
/// Asserting the exact sequence is what pins the rule: with the tiebreaker the answer is the one
/// total order, and without it the answer is heap order, which is now a different permutation.
/// The cheaper "every row appears once" assertion is kept beside it because it is the property a
/// paging client actually depends on.
/// </para>
/// <para>
/// Measured, so that nobody later assumes more than this class delivers: with the tiebreakers
/// removed from <c>ApplySort</c>, four of the six arms fail — <c>region</c>, <c>-region</c>,
/// <c>-depth</c> and <c>-updatedAt</c>. The other two pass on this data by coincidence, because
/// heap order still happens to agree with ascending id order for them. All six assertions are
/// kept, because each pins the contract for its own arm; only four of them are proven to catch
/// a regression, and re-deriving that is the way to check this class still bites.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CaveListingSortStabilityTests : IAsyncLifetime, IDisposable
{
    private const int TiedCaveCount = 12;

    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];
    private HttpClient editor = null!;
    private Guid editorId;
    private long caveTypeId;

    public CaveListingSortStabilityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"clss-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"clss-{tag}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    /// <summary>
    /// Twelve caves that agree on region and on depth, sorted by each in turn, come back in one
    /// total order — the tied column first, then the identifier — rather than in whatever order
    /// the scan happened to produce.
    /// </summary>
    [Theory]
    [InlineData("region")]
    [InlineData("-region")]
    [InlineData("depth")]
    [InlineData("-depth")]
    [InlineData("updatedAt")]
    [InlineData("-updatedAt")]
    public async Task A_sort_over_a_tied_column_is_a_total_order(string sort)
    {
        var seeded = await SeedTiedCavesAsync();

        var returned = IdsOf(await ListAsync(sort: sort, pageSize: TiedCaveCount));

        // Every arm ties on its primary key here, so the identifier alone decides the sequence,
        // in the same direction the caller asked for.
        var expected = sort.StartsWith('-')
            ? seeded.OrderByDescending(id => id).ToArray()
            : seeded.OrderBy(id => id).ToArray();

        returned.ShouldBe(expected);
    }

    /// <summary>
    /// The property a paging client depends on: walking every page of a tied sort yields every
    /// cave exactly once, with nothing duplicated and nothing skipped.
    /// </summary>
    [Fact]
    public async Task Paging_a_tied_sort_yields_every_cave_exactly_once()
    {
        var seeded = await SeedTiedCavesAsync();
        const int pageSize = 3;

        var seen = new List<Guid>();
        for (var page = 1; page <= TiedCaveCount / pageSize; page++)
        {
            seen.AddRange(IdsOf(await ListAsync(sort: "region", pageSize: pageSize, page: page)));
        }

        seen.Count.ShouldBe(TiedCaveCount, "a page-walk returned a different number of rows than exist");
        seen.ShouldBeUnique();
        seen.ShouldBe(seeded, ignoreOrder: true);
    }

    /// <summary>
    /// Twelve caves sharing one region and one depth, written inside one loop so their updatedAt
    /// stamps collide the way an import's do, and then shuffled in the heap so that scan order
    /// and identifier order disagree.
    /// </summary>
    private async Task<Guid[]> SeedTiedCavesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();

        var ids = new List<Guid>();
        for (var n = 0; n < TiedCaveCount; n++)
        {
            var feature = await writer.CreateCaveAsync(
                new Feature { Name = $"Pestera {n:00} {tag}", OwnerUserId = editorId, Visibility = Visibility.Public },
                new Cave { CaveTypeId = caveTypeId, Region = $"Masiv {tag}", Depth = 40m },
                parents: []);
            ids.Add(feature.Id);
        }

        await db.SaveChangesAsync();
        ids.Sort();

        // Rewrite the earlier half in place. `SET name = name` changes nothing a reader can see,
        // but PostgreSQL still writes a new tuple version at the end of the heap, so those rows
        // now sit physically after rows with larger ids. Without it a sequential scan returns
        // time-ordered ids in ascending order anyway and the ascending arms prove nothing.
        // Raw SQL on purpose: going through the change tracker would restamp updated_at and
        // break the tie this class exists to create.
        var moved = ids.Take(TiedCaveCount / 2).ToArray();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE features SET name = name WHERE id = ANY({0})", moved);

        return [.. ids];
    }

    private async Task<JsonElement> ListAsync(string sort, int pageSize, int page = 1)
    {
        var response = await editor.GetAsync(
            $"/api/v1/caves?search={tag}&sort={sort}&pageSize={pageSize}&page={page}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid[] IdsOf(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        factory.Dispose();
    }
}
