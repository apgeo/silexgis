// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The containment hierarchy endpoints: parent edges (DAG rules — cycle rejection,
/// exactly one primary, duplicates), children listing under visibility filtering,
/// the protection guard on re-parenting, and the subtree behavior of soft deletes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeatureHierarchyTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor
    private HttpClient outsider = null!; // Editor, unrelated
    private Guid outsiderId;
    private long karstAreaTypeId;
    private long sinkholeTypeId;
    private long stalactiteTypeId;
    private long caveTypeId;

    public FeatureHierarchyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fhown-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fhout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
            sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync();
            stalactiteTypeId = await db.FeatureTypes.Where(t => t.Code == "stalactite").Select(t => t.Id).SingleAsync();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"fhown-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"fhout-{suffix}@t.local");
    }

    [Fact]
    public async Task Parent_edges_follow_dag_rules_and_children_are_visibility_filtered()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var areaA = await CreateAreaAsync(owner, $"Area A {marker}");
        var areaB = await CreateAreaAsync(owner, $"Area B {marker}");
        var child = await CreateFeatureAsync(owner, new
        {
            kind = "generic",
            name = $"Child {marker}",
            featureTypeId = sinkholeTypeId,
            geometry = Point(25.51, 45.61),
            visibility = "authenticated",
            parents = new[] { new { parentId = areaA, isPrimary = true } },
        });

        // Creating with a parent already produced the edge and the breadcrumb.
        var detail = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{child}"))!;
        detail["feature"]!["parents"]!.AsArray().Single()!["id"]!.GetValue<Guid>().ShouldBe(areaA);

        var parents = (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{child}/parents"))!;
        parents.Single()!["id"]!.GetValue<Guid>().ShouldBe(areaA);
        parents.Single()!["isPrimary"]!.GetValue<bool>().ShouldBeTrue();

        // A kind marked parent-required cannot be created floating.
        var floating = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Floating stalactite {marker}",
            featureTypeId = stalactiteTypeId,
            geometry = Point(25.52, 45.62),
            visibility = "private",
        });
        floating.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await floating.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>()
            .ShouldBe("feature.parent_required");

        // Exactly one primary edge, no duplicates.
        await PutParentsExpectingAsync(owner, child, "feature.primary_parent",
            new { parentId = areaA, isPrimary = true }, new { parentId = areaB, isPrimary = true });
        await PutParentsExpectingAsync(owner, child, "feature.primary_parent",
            new { parentId = areaA, isPrimary = false }, new { parentId = areaB, isPrimary = false });
        await PutParentsExpectingAsync(owner, child, "feature.parent_duplicate",
            new { parentId = areaA, isPrimary = true }, new { parentId = areaA, isPrimary = false });

        // A valid secondary membership; the primary edge sorts first in the view.
        var multi = await owner.PutAsJsonAsync($"/api/v1/features/{child}/parents", new
        {
            parents = new[]
            {
                new { parentId = areaB, isPrimary = false },
                new { parentId = areaA, isPrimary = true },
            },
        });
        multi.StatusCode.ShouldBe(HttpStatusCode.OK, await multi.Content.ReadAsStringAsync());
        var multiView = (await multi.Content.ReadFromJsonAsync<JsonArray>())!;
        multiView.Count.ShouldBe(2);
        multiView[0]!["id"]!.GetValue<Guid>().ShouldBe(areaA);
        multiView[0]!["isPrimary"]!.GetValue<bool>().ShouldBeTrue();

        // Cycles are rejected: the area cannot descend from its own child, nor from itself.
        await PutParentsExpectingAsync(owner, areaA, "feature.hierarchy_cycle",
            new { parentId = child, isPrimary = true });
        await PutParentsExpectingAsync(owner, areaA, "feature.hierarchy_cycle",
            new { parentId = areaA, isPrimary = true });

        // A parent that does not exist — or is not readable — is the same named miss.
        await PutParentsExpectingAsync(owner, child, "feature.parent_not_found",
            new { parentId = Guid.NewGuid(), isPrimary = true });
        var outsiderPrivate = await CreateFeatureAsync(outsider, new
        {
            kind = "generic",
            name = $"Outsider private {marker}",
            featureTypeId = sinkholeTypeId,
            geometry = Point(25.53, 45.63),
            visibility = "private",
        });
        await PutParentsExpectingAsync(owner, child, "feature.parent_not_found",
            new { parentId = outsiderPrivate, isPrimary = true });

        // Children listing is visibility-filtered per caller.
        var privateChild = await CreateFeatureAsync(owner, new
        {
            kind = "generic",
            name = $"Private child {marker}",
            featureTypeId = sinkholeTypeId,
            geometry = Point(25.54, 45.64),
            visibility = "private",
            parents = new[] { new { parentId = areaA, isPrimary = true } },
        });

        var ownerChildren = (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features/{areaA}/children"))!
            ["items"]!.AsArray().Select(c => c!["id"]!.GetValue<Guid>()).ToList();
        ownerChildren.ShouldContain(child);
        ownerChildren.ShouldContain(privateChild);

        var outsiderChildren = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/features/{areaA}/children"))!
            ["items"]!.AsArray().Select(c => c!["id"]!.GetValue<Guid>()).ToList();
        outsiderChildren.ShouldContain(child);
        outsiderChildren.ShouldNotContain(privateChild);

        // Hierarchy reads on an unreadable feature do not disclose it.
        (await outsider.GetAsync($"/api/v1/features/{privateChild}/parents"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reparenting_into_or_out_of_a_protected_subtree_requires_exact_view()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];

        // A protected cave with a generic child inside its protection subtree.
        var caveResponse = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Guard Cave {marker}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        caveResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await caveResponse.Content.ReadAsStringAsync());
        var caveId = (await caveResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();

        var lon = 25.7001;
        var lat = 45.8002;
        var inside = await CreateFeatureAsync(owner, new
        {
            kind = "generic",
            name = $"Inside {marker}",
            featureTypeId = sinkholeTypeId,
            geometry = Point(lon, lat),
            visibility = "authenticated",
            parents = new[] { new { parentId = caveId, isPrimary = true } },
        });

        // A Write grant does NOT carry exact view: the grantee reads the row snapped.
        await GrantAsync(owner, inside, outsiderId, "Read,Write");
        var granteeView = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/features/{inside}"))!;
        granteeView["feature"]!["approximateLocation"]!.GetValue<bool>().ShouldBeTrue();
        granteeView["feature"]!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldNotBe(lon);

        // Moving the feature OUT of the protected subtree would publish its exact
        // coordinates — refused without exact view, and the edge must survive.
        (await outsider.PutAsJsonAsync($"/api/v1/features/{inside}/parents", new { parents = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<JsonArray>($"/api/v1/features/{inside}/parents"))!
            .Single()!["id"]!.GetValue<Guid>().ShouldBe(caveId);

        // Moving one's own feature IN under a protected root is refused the same way.
        var outsiderOwn = await CreateFeatureAsync(outsider, new
        {
            kind = "generic",
            name = $"Outsider own {marker}",
            featureTypeId = sinkholeTypeId,
            geometry = Point(25.71, 45.81),
            visibility = "authenticated",
        });
        (await outsider.PutAsJsonAsync($"/api/v1/features/{outsiderOwn}/parents", new
        {
            parents = new[] { new { parentId = caveId, isPrimary = true } },
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner holds exact view and may detach; protection stops cascading with the edge.
        var detach = await owner.PutAsJsonAsync(
            $"/api/v1/features/{inside}/parents", new { parents = Array.Empty<object>() });
        detach.StatusCode.ShouldBe(HttpStatusCode.OK, await detach.Content.ReadAsStringAsync());
        (await detach.Content.ReadFromJsonAsync<JsonArray>())!.Count.ShouldBe(0);

        var afterDetach = (await outsider.GetFromJsonAsync<JsonObject>($"/api/v1/features/{inside}"))!;
        afterDetach["feature"]!["approximateLocation"]!.GetValue<bool>().ShouldBeFalse();
        afterDetach["feature"]!["geometry"]!["coordinates"]![0]!.GetValue<double>().ShouldBe(lon);
    }

    [Fact]
    public async Task Deleting_a_feature_soft_deletes_its_subtree_and_leaves_audit_rows()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var area = await CreateAreaAsync(owner, $"Doomed Area {marker}");
        var child = await CreateFeatureAsync(owner, new
        {
            kind = "generic",
            name = $"Doomed Child {marker}",
            featureTypeId = sinkholeTypeId,
            geometry = Point(25.55, 45.65),
            visibility = "authenticated",
            parents = new[] { new { parentId = area, isPrimary = true } },
        });

        // Deletion needs the Delete permission — readability alone is not enough.
        (await outsider.DeleteAsync($"/api/v1/features/{area}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await owner.DeleteAsync($"/api/v1/features/{area}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The whole subtree is gone from every read path — detail, list, children.
        (await owner.GetAsync($"/api/v1/features/{area}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/features/{child}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<JsonObject>($"/api/v1/features?search=Doomed Child {marker}"))!
            ["items"]!.AsArray().Count.ShouldBe(0);
        (await owner.GetAsync($"/api/v1/features/{area}/children")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Soft deletes are forensics: each subtree row got a kind-typed deletion audit row.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        foreach (var id in new[] { area, child })
        {
            (await db.AuditEntries.AnyAsync(a =>
                a.EntityId == id.ToString() && a.Action == "deleted" && a.EntityType == "Feature:Generic"))
                .ShouldBeTrue($"missing deletion audit row for {id}");
        }

        // After the API workout (edges, re-parents, protection flips, subtree deletes)
        // the derived state the write service maintains must re-derive cleanly.
        var verifier = scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>();
        var problems = await verifier.VerifyAsync();
        problems.ShouldBeEmpty(string.Join("; ", problems.Select(p => $"{p.Check}:{p.FeatureId} {p.Detail}")));
    }

    private static object Point(double lon, double lat) =>
        new { type = "Point", coordinates = new[] { lon, lat } };

    private async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        // Karst areas accept polygon geometry (the kind's accepted class set).
        var polygon = new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { 25.50, 45.60 }, new[] { 25.60, 45.60 },
                    new[] { 25.60, 45.70 }, new[] { 25.50, 45.70 },
                    new[] { 25.50, 45.60 },
                },
            },
        };
        return await CreateFeatureAsync(client, new
        {
            kind = "generic",
            name,
            featureTypeId = karstAreaTypeId,
            geometry = polygon,
            visibility = "authenticated",
        });
    }

    private static async Task<Guid> CreateFeatureAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private static async Task PutParentsExpectingAsync(
        HttpClient client, Guid featureId, string code, params object[] parents)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/features/{featureId}/parents", new { parents });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>().ShouldBe(code);
    }

    private static async Task GrantAsync(HttpClient granter, Guid featureId, Guid subjectId, string permissions)
    {
        var response = await granter.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/acl", new
        {
            entries = new[] { new { subjectKind = "user", subjectId, permissions } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public async Task DisposeAsync()
    {
        owner.Dispose();
        outsider.Dispose();
        await Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
