// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The checklist the product ships: seeded once, owned by the administrator, readable by every
/// account, and put back whole when it is gone — but never line by line, because the list is the
/// administrator's to adapt.
/// </summary>
public sealed class ChecklistSeedTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private Guid adminId;
    private HttpClient reader = null!;

    public ChecklistSeedTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"cls-adm-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cls-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cls-read-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        reader.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task The_shipped_list_is_seeded_once_published_to_every_account_and_put_back_whole_when_lost()
    {
        await SeedTwiceAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var lists = await db.Checklists.Where(c => c.Title == ChecklistSeeds.DefaultTitle).ToListAsync();
            var list = lists.ShouldHaveSingleItem();
            list.OwnerUserId.ShouldBe(adminId);
            list.Visibility.ShouldBe(Visibility.Authenticated);
            (await db.ChecklistItems.CountAsync(i => i.ChecklistId == list.Id)).ShouldBe(ChecklistSeeds.DefaultItems.Count);
        }

        // Published: a plain reader with no grant of any kind sees it, lines and all.
        var listed = await reader.GetFromJsonAsync<JsonElement>("/api/v1/checklists/");
        var shipped = listed.EnumerateArray()
            .Single(x => x.GetProperty("title").GetString() == ChecklistSeeds.DefaultTitle);
        shipped.GetProperty("items").GetArrayLength().ShouldBe(ChecklistSeeds.DefaultItems.Count);

        // A line the administrator took off stays off: the seeder puts back the list, not its lines.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var listId = await db.Checklists.Where(c => c.Title == ChecklistSeeds.DefaultTitle).Select(c => c.Id).SingleAsync();
            await db.ChecklistItems.Where(i => i.ChecklistId == listId && i.SortOrder == 0).ExecuteDeleteAsync();
            await ChecklistSeeder.SeedAsync(db, adminId);
            (await db.ChecklistItems.CountAsync(i => i.ChecklistId == listId)).ShouldBe(ChecklistSeeds.DefaultItems.Count - 1);

            // The whole list gone is a loss, and the next start puts the shipped one back whole.
            await db.Checklists.Where(c => c.Id == listId).ExecuteDeleteAsync();
            await ChecklistSeeder.SeedAsync(db, adminId);
            var restored = await db.Checklists.Where(c => c.Title == ChecklistSeeds.DefaultTitle).SingleAsync();
            (await db.ChecklistItems.CountAsync(i => i.ChecklistId == restored.Id)).ShouldBe(ChecklistSeeds.DefaultItems.Count);
        }
    }

    private async Task SeedTwiceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await ChecklistSeeder.SeedAsync(db, adminId);
        await ChecklistSeeder.SeedAsync(db, adminId);
    }
}
