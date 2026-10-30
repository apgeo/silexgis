// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A paged list sorted on a column that rows share must still come back in one order, or a row
/// is shown twice on one page and never on the next while somebody turns the pages. Each list
/// here is given rows whose sort column is identical, inserted in the opposite order to the key
/// the list falls back on, and walked a small page at a time: the pages put end to end must be
/// every row once, in the fallback order.
/// </summary>
public sealed class PagedListOrderTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const int Rows = 7;
    private const int PageSize = 2;

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private Guid adminId;

    public PagedListOrderTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async ValueTask InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"plo-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"plo-adm-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        admin.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Confirmations_with_the_same_moment_are_paged_newest_id_first_without_repeats()
    {
        var ids = Ascending();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var id in ids)
            {
                db.ImportBatches.Add(new ImportBatch { Id = id, ConfirmedByUserId = adminId });
            }

            await db.SaveChangesAsync();
            await db.ImportBatches.ExecuteUpdateAsync(s => s.SetProperty(b => b.CreatedAt, Moment));
        }

        (await WalkAsync("/api/v1/import-batches")).ShouldBe(ids.OrderByDescending(id => id));
    }

    [Fact]
    public async Task Vector_files_with_the_same_moment_are_paged_newest_id_first_without_repeats()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"Notes {Guid.NewGuid()}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", $"track-{suffix}.txt" } };
        var upload = await admin.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        upload.EnsureSuccessStatusCode();
        var fileId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var ids = Ascending();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var id in ids)
            {
                db.Geofiles.Add(new Geofile
                {
                    Id = id,
                    Name = $"Track {id:N}",
                    FileId = fileId,
                    Format = GeofileFormat.Gpx,
                    OwnerUserId = adminId,
                });
            }

            await db.SaveChangesAsync();
            await db.Geofiles.ExecuteUpdateAsync(s => s.SetProperty(g => g.UpdatedAt, Moment));
        }

        (await WalkAsync("/api/v1/geofiles")).ShouldBe(ids.OrderByDescending(id => id));
    }

    private static readonly DateTimeOffset Moment = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Ascending ids, in the order the rows are inserted: the order the database would hand ties
    /// back in if left to itself, and the reverse of the order the lists are meant to use.
    /// </summary>
    private static List<Guid> Ascending() =>
        [.. Enumerable.Range(1, Rows).Select(_ => Guid.CreateVersion7()).Order()];

    private async Task<List<Guid>> WalkAsync(string route)
    {
        var seen = new List<Guid>();
        for (var page = 1; page <= (Rows + PageSize - 1) / PageSize + 1; page++)
        {
            var response = await admin.GetAsync($"{route}?page={page}&pageSize={PageSize}");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("totalItems").GetInt32().ShouldBe(Rows);
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        }

        return seen;
    }
}
