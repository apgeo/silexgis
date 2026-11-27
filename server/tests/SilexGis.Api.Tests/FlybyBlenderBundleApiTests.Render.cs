// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Flybys;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A flyby asked to be rendered on the server: the bundle is made by the download's code for the
/// account asking, and then holds nothing under location protection, because a render node is
/// another machine; what it left out for that is counted, said in the bundle's notes and
/// recorded. And who may ask, and when the asking is refused.
/// </summary>
public sealed partial class FlybyBlenderBundleApiTests
{
    private const string RenderRoute = "/api/v1/flybys/blender-renders";

    [Fact]
    public async Task A_render_on_the_server_leaves_out_the_protected_caves_and_places_its_asker_may_see_and_says_so()
    {
        var spot = Spot.Somewhere();
        var open = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var closed = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, locationProtected: true);
        var openPlace = await CreateFeatureAsync(Point(spot.East(0.001)));
        var closedPlace = await CreateFeatureAsync(Point(spot.East(0.003)), locationProtected: true);
        var bundleRequest = Request(
            spot,
            [Cave(open, walls: true), Cave(closed, walls: true)],
            markers: [MarkerLayer("features", "#722ed1", [Marker(openPlace, 900), Marker(closedPlace, 900)])]);

        // The positive half first: their owner downloads both caves and both places.
        var download = await owner.PostAsJsonAsync(Route, bundleRequest);
        Header(download, FlybyBlenderBundleEndpoints.WallsHeader).ShouldBe(2);
        Header(download, FlybyBlenderBundleEndpoints.MarkersHeader).ShouldBe(2);

        var started = await owner.PostAsJsonAsync(RenderRoute, new { bundle = bundleRequest, plan = "draft", container = "mp4" });
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync());
        var body = await started.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("walls").GetInt32().ShouldBe(1);
        body.GetProperty("notDrawn").GetInt32().ShouldBe(0);
        body.GetProperty("protectedLeftOut").GetInt32().ShouldBe(1);
        body.GetProperty("markers").GetInt32().ShouldBe(1);
        body.GetProperty("markersNotDrawn").GetInt32().ShouldBe(0);
        body.GetProperty("protectedMarkersLeftOut").GetInt32().ShouldBe(1);
        var renderId = body.GetProperty("render").GetProperty("id").GetGuid();
        body.GetProperty("render").GetProperty("status").GetString().ShouldBe("queued");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var render = await db.FlybyRenders.AsNoTracking().SingleAsync(r => r.Id == renderId);
        render.RequestedBy.ShouldBe(ownerId);
        render.Plan.ShouldBe("draft");
        render.ScratchBytes.ShouldBeGreaterThan(render.BundleSize);

        // What the node will be given: the open cave alone, and the notes saying why.
        var stored = StoredBundle(render.BundlePath!);
        stored.Meshes.ShouldBe(["cave-01-lines.ply", "cave-01-walls.ply"]);
        var readme = Encoding.UTF8.GetString(stored.Entries["README.txt"]);
        readme.ShouldContain("RENDERED ON THE SERVER");
        readme.ShouldContain("Left out for that: 1 cave and 1 entrance or feature.");
        readme.ShouldContain("1 cave under location protection");

        // Recorded as made for a render, with what it left out and the SHA-256 a node's download
        // of it will be recorded with.
        var made = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.UserId == ownerId && a.EntityType == FlybyBlenderBundleEndpoints.AuditEntityType)
            .OrderByDescending(a => a.Id)
            .FirstAsync();
        var changes = JsonDocument.Parse(made.Changes!).RootElement;
        changes.GetProperty("format").GetString().ShouldBe("blender-render");
        changes.GetProperty("wallsCaveIds").EnumerateArray().Select(e => e.GetString()).ShouldBe([open.ToString()]);
        changes.GetProperty("protectedLeftOutCount").GetInt32().ShouldBe(1);
        changes.GetProperty("protectedMarkersLeftOutCount").GetInt32().ShouldBe(1);
        changes.GetProperty("sha256").GetString().ShouldBe(render.BundleSha256);
    }

    [Fact]
    public async Task A_render_on_the_server_is_refused_while_one_is_going_when_storage_is_full_and_when_only_protected_caves_are_asked_for()
    {
        var spot = Spot.Somewhere();
        var open = await CreateSurveyedCaveAsync(spot, heightM: 900);
        var closed = await CreateSurveyedCaveAsync(spot.East(0.01), heightM: 900, locationProtected: true);
        object Asked(params object[] caves) => new { bundle = Request(spot, caves), plan = "full" };

        // Only protected caves: there is nothing a node may be given.
        var onlyProtected = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(closed, walls: true)));
        onlyProtected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(onlyProtected)).ShouldBe(FlybyBlenderBundleEndpoints.OnlyProtectedCode);

        var first = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("render").GetProperty("id").GetGuid();

        // One at a time: refused before a bundle is made, so no record of one is left.
        var records = await RenderRecordsAsync();
        var second = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(second)).ShouldBe(FlybyRenderEndpoints.AlreadyActiveCode);
        (await RenderRecordsAsync()).ShouldBe(records);

        // Stopped, the turn is free again; with the space for renders taken, it is refused for that.
        (await owner.PostAsync($"{RenderRoute}/{firstId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        Guid filler;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = new FlybyRender
            {
                RequestedBy = readerId,
                Name = "An invented placeholder",
                Plan = "full",
                Container = "mp4",
                BundlePath = "2026/11/an-invented-placeholder.zip",
                BundleSize = 60_000_000_000,
                BundleSha256 = new string('0', 64),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.FlybyRenders.Add(row);
            await db.SaveChangesAsync();
            filler = row.Id;
        }

        try
        {
            var full = await owner.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)));
            full.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeAsync(full)).ShouldBe(FlybyRenderEndpoints.StorageFullCode);
            (await RenderRecordsAsync()).ShouldBe(records);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.FlybyRenders.Where(r => r.Id == filler).ExecuteDeleteAsync();
        }

        // Nobody signed in may ask, and a personal access key may not either: a render is asked
        // for from the application, not from a field device.
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var key = await AuthHelper.CreateAccessKeyAsync(owner);
        using var device = AuthHelper.AccessKeyClient(factory, key);
        (await device.PostAsJsonAsync(RenderRoute, Asked(Cave(open, walls: true)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<int> RenderRecordsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.UserId == ownerId && a.EntityType == FlybyBlenderBundleEndpoints.AuditEntityType)
            .Select(a => a.Changes)
            .ToListAsync();
        return rows.Count(changes => changes is not null
            && JsonDocument.Parse(changes).RootElement.GetProperty("format").GetString() == "blender-render");
    }

    private Bundle StoredBundle(string storagePath)
    {
        using var scope = factory.Services.CreateScope();
        var path = scope.ServiceProvider.GetRequiredService<IFileStore>().GetAbsolutePath(storagePath);
        using var archive = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var held = new MemoryStream();
            using (var input = entry.Open())
            {
                input.CopyTo(held);
            }

            entries[entry.FullName[(entry.FullName.IndexOf('/') + 1)..]] = held.ToArray();
        }

        return new Bundle(entries, JsonDocument.Parse(entries["scene-plan.json"]).RootElement.Clone());
    }
}
