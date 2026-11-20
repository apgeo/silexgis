// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The map offers imported vector files and ready georeferenced maps as layers. It used to read one
/// page of a hundred of each and drop the unfinished ones itself, so an installation with more than
/// a hundred lost the oldest from the layer list for good. The lists now narrow to what can be drawn
/// when asked (<c>ready=true</c>), before the count and the page, so walking the pages yields every
/// drawable layer and nothing else.
/// </summary>
public sealed class ReadyLayerListTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    // More than the hundred the map used to read, and more than one page of the size it reads with.
    private const int Ready = 130;
    private const int Unfinished = 12;

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private Guid adminId;

    public ReadyLayerListTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async ValueTask InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rll-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rll-adm-{suffix}@t.local");
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

    // The facts of this class share one database, and another fact's rows are visible to this
    // administrator; so each exact assertion is taken against what was listed before its own seed.

    [Fact]
    public async Task Every_imported_vector_file_is_listed_when_only_ready_ones_are_asked_for()
    {
        var before = await WalkAsync(admin, "/api/v1/geofiles?ready=true", "importStatus", "imported");
        var totalBefore = await TotalAsync(admin, "/api/v1/geofiles");
        var unfinished = new[] { GeofileImportStatus.Uploaded, GeofileImportStatus.Importing, GeofileImportStatus.Failed };
        var imported = await SeedAsync((db, file) =>
        {
            var ids = new List<Guid>();
            for (var i = 0; i < Ready + Unfinished; i++)
            {
                var geofile = new Geofile
                {
                    Name = $"Track {i} {suffix}",
                    FileId = file.Id,
                    Format = GeofileFormat.Gpx,
                    OwnerUserId = adminId,
                    ImportStatus = i < Ready ? GeofileImportStatus.Imported : unfinished[i % unfinished.Length],
                };
                db.Geofiles.Add(geofile);
                if (i < Ready)
                {
                    ids.Add(geofile.Id);
                }
            }

            return ids;
        }, FileKind.Vector, "gpx");

        var walked = await WalkAsync(admin, "/api/v1/geofiles?ready=true", "importStatus", "imported");
        walked.ShouldBe(before.Concat(imported), ignoreOrder: true);

        // Without the narrowing the list is still every file, unfinished ones too, as the
        // administration pages need it.
        (await TotalAsync(admin, "/api/v1/geofiles")).ShouldBe(totalBefore + Ready + Unfinished);
    }

    [Fact]
    public async Task Every_ready_georeferenced_map_is_listed_when_only_ready_ones_are_asked_for()
    {
        var before = await WalkAsync(admin, "/api/v1/georeferenced-maps?ready=true", "status", "ready");
        var totalBefore = await TotalAsync(admin, "/api/v1/georeferenced-maps");
        var unfinished = new[] { RasterStatus.Uploaded, RasterStatus.Processing, RasterStatus.Failed, RasterStatus.Unplaced };
        var ready = await SeedAsync((db, file) =>
        {
            var ids = new List<Guid>();
            for (var i = 0; i < Ready + Unfinished; i++)
            {
                var map = new GeoreferencedMap
                {
                    Name = $"Sheet {i} {suffix}",
                    FileId = file.Id,
                    OwnerUserId = adminId,
                    Status = i < Ready ? RasterStatus.Ready : unfinished[i % unfinished.Length],
                };
                db.GeoreferencedMaps.Add(map);
                if (i < Ready)
                {
                    ids.Add(map.Id);
                }
            }

            return ids;
        }, FileKind.Raster, "tif");

        var walked = await WalkAsync(admin, "/api/v1/georeferenced-maps?ready=true", "status", "ready");
        walked.ShouldBe(before.Concat(ready), ignoreOrder: true);
        (await TotalAsync(admin, "/api/v1/georeferenced-maps")).ShouldBe(totalBefore + Ready + Unfinished);
    }

    [Fact]
    public async Task The_ready_narrowing_never_lists_a_file_the_caller_may_not_see()
    {
        // Viewers on purpose: the seeded Editors group reads past visibility everywhere.
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rll-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rll-out-{suffix}@t.local");
        using var owner = await AuthHelper.BearerClientAsync(factory, $"rll-own-{suffix}@t.local");
        using var outsider = await AuthHelper.BearerClientAsync(factory, $"rll-out-{suffix}@t.local");

        var (geofileId, mapId) = await SeedAsync((db, file) =>
        {
            var geofile = new Geofile
            {
                Name = $"Private track {suffix}",
                FileId = file.Id,
                Format = GeofileFormat.Gpx,
                OwnerUserId = ownerId,
                Visibility = Visibility.Private,
                ImportStatus = GeofileImportStatus.Imported,
            };
            var map = new GeoreferencedMap
            {
                Name = $"Private sheet {suffix}",
                FileId = file.Id,
                OwnerUserId = ownerId,
                Visibility = Visibility.Private,
                Status = RasterStatus.Ready,
            };
            db.Geofiles.Add(geofile);
            db.GeoreferencedMaps.Add(map);
            return (geofile.Id, map.Id);
        }, FileKind.Vector, "gpx", ownerId);

        (await WalkAsync(owner, "/api/v1/geofiles?ready=true", "importStatus", "imported")).ShouldContain(geofileId);
        (await WalkAsync(owner, "/api/v1/georeferenced-maps?ready=true", "status", "ready")).ShouldContain(mapId);
        (await WalkAsync(outsider, "/api/v1/geofiles?ready=true", "importStatus", "imported")).ShouldNotContain(geofileId);
        (await WalkAsync(outsider, "/api/v1/georeferenced-maps?ready=true", "status", "ready")).ShouldNotContain(mapId);
    }

    private async Task<T> SeedAsync<T>(
        Func<SilexGisDbContext, StoredFile, T> seed, FileKind kind, string extension, Guid? fileOwnerId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = ConformanceSeeds.File(db, fileOwnerId ?? adminId, kind, extension);
        var result = seed(db, file);
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Every row of a list, read the way the map reads it: the largest page the server serves, turned
    /// until the total is reached. Each row must carry the expected ready state.
    /// </summary>
    private static async Task<List<Guid>> WalkAsync(HttpClient client, string route, string statusMember, string readyValue)
    {
        var seen = new List<Guid>();
        // A small page as well as the largest, so the walk really turns pages at this row count.
        foreach (var size in new[] { 50, Paging.MaxPageSize })
        {
            seen.Clear();
            for (var page = 1; page < 100; page++)
            {
                var body = await client.GetFromJsonAsync<JsonElement>($"{route}&page={page}&pageSize={size}");
                var items = body.GetProperty("items").EnumerateArray().ToList();
                foreach (var item in items)
                {
                    item.GetProperty(statusMember).GetString().ShouldBe(readyValue);
                    seen.Add(item.GetProperty("id").GetGuid());
                }

                if (items.Count < size || seen.Count >= body.GetProperty("totalItems").GetInt32())
                {
                    break;
                }
            }

            seen.Distinct().Count().ShouldBe(seen.Count);
        }

        return seen;
    }

    private static async Task<int> TotalAsync(HttpClient client, string route)
    {
        var body = await client.GetFromJsonAsync<JsonElement>($"{route}?pageSize=1");
        return body.GetProperty("totalItems").GetInt32();
    }
}
