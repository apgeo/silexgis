// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
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
[Collection(PostgresCollection.Name)]
public sealed class WorkAreaTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private long workAreaTypeId;

    public WorkAreaTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"wa-own-{suffix}@t.local");
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

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
    }
}
