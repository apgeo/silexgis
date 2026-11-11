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
using SilexGis.Domain.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Work areas: the stretches of country a club works, read as one tree.
///
/// The PostGIS container is shared by the whole collection, so every assertion here names its own
/// rows by a per-run marker and never counts the whole answer — another class's seeded areas would
/// otherwise race these.
/// </summary>
public sealed class WorkAreaTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private Guid ownerId;
    private long workAreaTypeId;

    public WorkAreaTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"wa-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"wa-out-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            workAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == FeatureTypeSeeds.WorkArea)
                .Select(t => t.Id)
                .SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"wa-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"wa-out-{suffix}@t.local");
    }

    [Fact]
    public async Task Listing_requires_authentication()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/work-areas/")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_kind_is_seeded_so_the_board_is_not_permanently_empty()
    {
        // The endpoint resolves the kind by code and answers empty when it finds nothing. That is
        // the right behaviour and an indistinguishable one, so the row's existence is asserted
        // here rather than left to be inferred from a board that happens to have rows on it.
        workAreaTypeId.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_area_carries_its_name_description_and_shape()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateAreaAsync($"Massif {marker}", "authenticated", description: "Where we work.");

        var area = (await AreasAsync(owner)).Single(x => x.GetProperty("id").GetGuid() == id);

        area.GetProperty("name").GetString().ShouldBe($"Massif {marker}");
        area.GetProperty("description").GetString().ShouldBe("Where we work.");
        area.GetProperty("geometry").GetProperty("type").GetString().ShouldBe("Polygon");
        // Nothing above it, so nothing to link to.
        area.GetProperty("parentId").ValueKind.ShouldBe(JsonValueKind.Null);
        area.GetProperty("childCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_sub_area_states_the_area_it_sits_inside_and_is_counted_by_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var parent = await CreateAreaAsync($"Massif {marker}", "authenticated");
        var child = await CreateAreaAsync($"Valley {marker}", "authenticated", parentId: parent);

        var areas = await AreasAsync(owner);
        var parentRow = areas.Single(x => x.GetProperty("id").GetGuid() == parent);
        var childRow = areas.Single(x => x.GetProperty("id").GetGuid() == child);

        // The level below is reached through the containment hierarchy every feature already has,
        // not through a second parent column of this feature's own.
        childRow.GetProperty("parentId").GetGuid().ShouldBe(parent);
        parentRow.GetProperty("childCount").GetInt32().ShouldBe(1);
        // A sub-area is a work area in its own right, which is what lets a reader open it and be
        // offered the level beneath it by exactly the same rule.
        childRow.GetProperty("childCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task An_area_the_caller_may_not_read_is_not_listed()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var priv = await CreateAreaAsync($"Private {marker}", "private");
        var shared = await CreateAreaAsync($"Shared {marker}", "authenticated");

        var seen = (await AreasAsync(outsider)).Select(x => x.GetProperty("id").GetGuid()).ToList();

        seen.ShouldContain(shared);
        seen.ShouldNotContain(priv);
    }

    [Fact]
    public async Task A_parent_the_caller_may_not_read_is_not_named_by_its_child()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var parent = await CreateAreaAsync($"Hidden massif {marker}", "private");
        var child = await CreateAreaAsync($"Open valley {marker}", "authenticated", parentId: parent);

        var childRow = (await AreasAsync(outsider)).Single(x => x.GetProperty("id").GetGuid() == child);

        // The name would be absent but the link would still say something is there — which tells a
        // reader that an area they may not see exists, and roughly where.
        childRow.GetProperty("parentId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_protected_area_is_left_out_rather_than_drawn_snapped()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var open = await CreateAreaAsync($"Open {marker}", "authenticated");
        var guarded = await CreateAreaAsync($"Guarded {marker}", "authenticated", locationProtected: true);

        var seen = (await AreasAsync(outsider)).Select(x => x.GetProperty("id").GetGuid()).ToList();

        seen.ShouldContain(open);
        // A shape snapped to the protection grid is a boundary of the wrong size in the wrong
        // place, and it would read as the club's actual working area.
        seen.ShouldNotContain(guarded);
    }

    [Fact]
    public async Task An_area_nobody_has_outlined_yet_is_still_listed()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateAreaAsync($"Unmapped {marker}", "authenticated", withGeometry: false);

        var area = (await AreasAsync(owner)).Single(x => x.GetProperty("id").GetGuid() == id);

        // The board still names it; the overview leaves it off the map rather than inventing a
        // boundary for it.
        area.GetProperty("geometry").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_feature_that_is_not_a_work_area_is_not_listed()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        long genericTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            genericTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Not an area {marker}",
            featureTypeId = genericTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.44, 45.53 } },
            visibility = "authenticated",
        });
        var other = await CreatedIdAsync(response);

        (await AreasAsync(owner)).Select(x => x.GetProperty("id").GetGuid()).ShouldNotContain(other);
    }

    [Fact]
    public async Task A_deleted_area_is_no_longer_listed()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var kept = await CreateAreaAsync($"Kept {marker}", "authenticated");
        var gone = await CreateAreaAsync($"Gone {marker}", "authenticated");
        (await AreasAsync(owner)).Select(x => x.GetProperty("id").GetGuid()).ShouldContain(gone);

        var deleted = await owner.DeleteAsync($"/api/v1/features/{gone}");
        deleted.IsSuccessStatusCode.ShouldBeTrue(await deleted.Content.ReadAsStringAsync());

        var seen = (await AreasAsync(owner)).Select(x => x.GetProperty("id").GetGuid()).ToList();
        seen.ShouldContain(kept);
        seen.ShouldNotContain(gone);
        using var scope = factory.Services.CreateScope();
        // Soft-deleted, not removed: the row is still there, and only the listing's filter keeps it off.
        (await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().Features.IgnoreQueryFilters()
            .AsNoTracking().CountAsync(f => f.Id == gone && f.DeletedAt != null)).ShouldBe(1);
    }

    [Fact]
    public async Task Past_the_cap_the_answer_says_it_is_cut_and_a_child_whose_parent_was_cut_names_no_parent()
    {
        // Named to sort ahead of every other area on the shared database, so the first rows of the
        // answer are this test's: a child, then the fillers, with the parent sorting last of all.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var parent = await CreateAreaAsync($"zzzz {marker} parent", "authenticated");
        var child = await CreateAreaAsync($"0000 {marker} a child", "authenticated", parentId: parent);
        const int cap = 500;
        var fillers = new List<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            for (var i = 0; i < cap; i++)
            {
                var id = Guid.NewGuid();
                fillers.Add(id);
                db.Features.Add(new Feature
                {
                    Id = id,
                    Name = $"0000 {marker} b{i:000}",
                    Kind = FeatureKind.Generic,
                    FeatureTypeId = workAreaTypeId,
                    OwnerUserId = ownerId,
                    Visibility = Visibility.Authenticated,
                    AncestorIds = [id],
                });
                db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = id });
            }

            await db.SaveChangesAsync();
        }

        try
        {
            await ShouldBeCutAsync(child, parent, fillers, cap);
        }
        finally
        {
            // The class's tests share one database, and five hundred areas sorting first would
            // push every later test's areas past the cap.
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().Features
                .Where(f => f.Id == child || f.Id == parent || fillers.Contains(f.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.DeletedAt, DateTimeOffset.UtcNow));
        }
    }

    private async Task ShouldBeCutAsync(Guid child, Guid parent, List<Guid> fillers, int cap)
    {
        var (items, truncated) = await AnswerAsync(owner);

        truncated.ShouldBeTrue();
        items.Count.ShouldBe(cap);
        var ids = items.Select(x => x.GetProperty("id").GetGuid()).ToList();
        // The first rows by name, and the same rows asked again: a cut that moves between calls
        // would hand a board a different half each time.
        ids[0].ShouldBe(child);
        ids.Skip(1).ShouldBe(fillers.Take(cap - 1));
        (await AnswerAsync(owner)).Items.Select(x => x.GetProperty("id").GetGuid()).ShouldBe(ids);
        ids.ShouldNotContain(parent);
        // Its parent is past the cut, so the child names no parent rather than one the answer
        // does not hold.
        items[0].GetProperty("parentId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private async Task<Guid> CreateAreaAsync(
        string name,
        string visibility,
        string? description = null,
        Guid? parentId = null,
        bool locationProtected = false,
        bool withGeometry = true)
    {
        object? geometry = withGeometry
            ? new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.40, 45.50 }, new[] { 25.50, 45.50 },
                        new[] { 25.50, 45.60 }, new[] { 25.40, 45.60 },
                        new[] { 25.40, 45.50 },
                    },
                },
            }
            : null;

        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = workAreaTypeId,
            geometry,
            description,
            visibility,
            locationProtected,
            parents = parentId is null
                ? Array.Empty<object>()
                : [new { parentId = parentId.Value, isPrimary = true }],
        });
        return await CreatedIdAsync(response);
    }

    private static async Task<List<JsonElement>> AreasAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/work-areas/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.GetProperty("items").EnumerateArray()];
    }

    private static async Task<(List<JsonElement> Items, bool Truncated)> AnswerAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/work-areas/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var root = JsonDocument.Parse(payload).RootElement;
        return ([.. root.GetProperty("items").EnumerateArray()], root.GetProperty("truncated").GetBoolean());
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
    }
}
