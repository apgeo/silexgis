// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A download raced against a write that is stamped early and commits late, for real: one
/// transaction holds an edit uncommitted while a later edit commits and a phone pages, and the
/// phone pages again once the first edit lands.
/// </summary>
/// <remarks>
/// Every stream orders on a change time the application stamps as it writes, and a write's stamp
/// is taken before its transaction commits — an upload batch writes item by item and commits at
/// the end, an import commits after all its saves. Paged one after the other, every way of cutting
/// a page passes. The case that matters is the overlap: an uncommitted row stamped T1, a committed
/// row stamped T2 after it, and a page read in between. A page that hands the phone a position at
/// T2 leaves the T1 row behind that position for good once it commits; only a test that holds the
/// first write open across the read can tell a stream that never skips from one that merely looks
/// as if it doesn't.
/// </remarks>
public sealed class SyncLateCommitTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string connectionString;
    private HttpClient phone = null!;
    private string marker = null!;

    public SyncLateCommitTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString);
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"late-{marker}@t.local");
        phone = await AuthHelper.BearerClientAsync(factory, $"late-{marker}@t.local");
    }

    [Fact]
    public async Task A_write_that_commits_after_a_later_stamped_one_still_reaches_a_phone_that_paged_in_between()
    {
        var slowId = Guid.CreateVersion7();
        var quickId = Guid.CreateVersion7();
        var creator = await CreateSetAsync([]);
        var created = await UploadAsync(creator, Cave(slowId, $"Slow {marker}"), Cave(quickId, $"Quick {marker}"));
        created.GetProperty("written").GetInt32().ShouldBe(2);
        var set = await CreateSetAsync([slowId, quickId]);

        // The phone is level with the server before anything else happens.
        var level = await DownloadAsync(set, null);
        Features(level).ShouldBe([slowId, quickId], ignoreOrder: true);
        var cursor = level.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNull();

        // A slow writer stamps its edit of one cave and keeps its transaction open — the window an
        // upload adopting photos, or an import working through a file, keeps open for as long as
        // it runs.
        await using var slowScope = factory.Services.CreateAsyncScope();
        var slowDb = slowScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await using var slow = await slowDb.Database.BeginTransactionAsync();
        var slowRow = await slowDb.Features.SingleAsync(f => f.Id == slowId);
        slowRow.Name = $"Slow renamed {marker}";
        await slowDb.SaveChangesAsync();
        var slowStamp = slowRow.UpdatedAt;

        // A quick writer edits the other cave after it and commits at once, so the committed rows
        // now include a change stamped later than one that is not visible yet.
        await using (var quickScope = factory.Services.CreateAsyncScope())
        {
            var quickDb = quickScope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var quickRow = await quickDb.Features.SingleAsync(f => f.Id == quickId);
            quickRow.Name = $"Quick renamed {marker}";
            await quickDb.SaveChangesAsync();
            quickRow.UpdatedAt.ShouldBeGreaterThan(slowStamp, "the committed edit must be the later-stamped one");
        }

        // The phone pages while the slow edit is still in flight...
        var during = await DownloadAsync(set, cursor);
        var seen = Features(during).ToList();
        cursor = during.GetProperty("nextCursor").GetString() ?? cursor;

        // ...the slow edit lands...
        await slow.CommitAsync();

        // ...and the phone pages again from the position it was handed. Between the two pages it
        // must have been given both edits: the one that committed late is not left behind a
        // position the earlier page moved past.
        for (var page = 0; page < 5; page++)
        {
            var after = await DownloadAsync(set, cursor);
            seen.AddRange(Features(after));
            cursor = after.GetProperty("nextCursor").GetString() ?? cursor;
            if (!after.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        seen.ShouldContain(slowId, "the edit that committed after a later-stamped one was never delivered");
        seen.ShouldContain(quickId);

        // Caught up now: asking again from the last position hands over nothing more.
        Features(await DownloadAsync(set, cursor)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_installation_settle_interval_holds_back_a_change_made_within_it()
    {
        // An installation whose clocks may disagree by up to a minute: nothing stamped within the
        // last minute is handed over yet, and anything older is.
        using var settled = new SilexGisApiFactory(
            connectionString,
            new Dictionary<string, string?> { ["Sync:DownloadSettleMilliseconds"] = "60000" });
        using var settledPhone = await AuthHelper.BearerClientAsync(settled, $"late-{marker}@t.local");

        var editedId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        var creator = await CreateSetAsync([]);
        (await UploadAsync(creator, Cave(editedId, $"Edited {marker}"), Cave(otherId, $"Other {marker}")))
            .GetProperty("written").GetInt32().ShouldBe(2);
        await StampAsync([editedId, otherId], DateTimeOffset.UtcNow.AddMinutes(-5));
        var set = await CreateSetAsync([editedId, otherId]);

        // Settled long ago: handed over.
        var level = await DownloadAsync(settledPhone, set, null);
        Features(level).ShouldBe([editedId, otherId], ignoreOrder: true);
        var cursor = level.GetProperty("nextCursor").GetString();
        cursor.ShouldNotBeNull();

        // Edited just now and committed: inside the interval, so not handed over yet.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.Features.SingleAsync(f => f.Id == editedId);
            row.Name = $"Edited again {marker}";
            await db.SaveChangesAsync();
        }

        Features(await DownloadAsync(settledPhone, set, cursor))
            .ShouldNotContain(editedId, "a change made within the settle interval was handed over");

        // The same edit once it is older than the interval: handed over from the same position.
        await StampAsync([editedId], DateTimeOffset.UtcNow.AddMinutes(-2));
        Features(await DownloadAsync(settledPhone, set, cursor)).ShouldBe([editedId]);
    }

    private async Task StampAsync(Guid[] ids, DateTimeOffset at)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Features.Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(f => f.UpdatedAt, at));
    }

    private static IEnumerable<Guid> Features(JsonElement page) =>
        page.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetGuid());

    private Task<JsonElement> DownloadAsync(Guid set, string? cursor) => DownloadAsync(phone, set, cursor);

    private static async Task<JsonElement> DownloadAsync(HttpClient client, Guid set, string? cursor)
    {
        var url = $"/api/v1/sync/sets/{set}/download"
            + (cursor is null ? string.Empty : $"?cursor={Uri.EscapeDataString(cursor)}");
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> UploadAsync(Guid set, params object[] rows)
    {
        using var response = await phone.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Cave(Guid id, string name) => new
    {
        id,
        kind = "cave",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private async Task<Guid> CreateSetAsync(Guid[] roots)
    {
        using var response = await phone.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            cavingGroupId = (Guid?)null,
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public void Dispose()
    {
        phone?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
