// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Putting a value back from the history, on the server: a large shape the history can only
/// summarise is kept in full beside the edit and comes back exactly; the restore is an ordinary
/// edit, so a caller without exact view gets the name back but not the geometry, and a field their
/// own history hides is not theirs to have back; older history without a kept shape says so; and
/// the route answers like every other route to callers who may not read or may not write.
/// </summary>
public sealed class HistoryRestoreTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor, owns everything seeded here — always exact
    private HttpClient editor = null!;   // Editor granted Read+Write on what is shared, but NOT ViewExactLocation
    private HttpClient outsider = null!; // Viewer, no grant
    private Guid editorId;
    private long caveTypeId;
    private long karstAreaTypeId;
    private long entranceTypeId;

    public HistoryRestoreTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rown-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"redit-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"rown-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"redit-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"rout-{suffix}@t.local");
    }

    [Fact]
    public async Task A_large_polygon_edit_keeps_both_shapes_and_the_restore_puts_the_old_one_back_exactly()
    {
        var before = Ring(1500, 26.30, 46.20, 0.05);
        var after = Ring(1500, 26.31, 46.21, 0.04);
        var areaId = await CreateAreaAsync(owner, before, protectedLocation: false);
        var (_, stored) = await FeatureAsync(areaId);
        stored.NumPoints.ShouldBe(1501);
        await UpdateAreaAsync(owner, areaId, "Edited area", after, protectedLocation: false);

        // A small outline is recorded in full and keeps nothing beside it.
        var smallId = await CreateAreaAsync(owner, Ring(5, 26.5, 46.5, 0.01), protectedLocation: false);
        await UpdateAreaAsync(owner, smallId, "Edited small area", Ring(5, 26.6, 46.6, 0.01), protectedLocation: false);

        var edit = await UpdateEntryAsync(areaId);
        var smallEdit = await UpdateEntryAsync(smallId);
        string? recordedOld;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var kept = await db.AuditGeometryValues.AsNoTracking().Where(v => v.AuditEntryId == edit).ToListAsync();
            kept.Count.ShouldBe(1);
            kept[0].Property.ShouldBe("Geom");
            kept[0].OldWkb.ShouldNotBeNull();
            kept[0].NewWkb.ShouldNotBeNull();
            (await db.AuditGeometryValues.CountAsync(v => v.AuditEntryId == smallEdit)).ShouldBe(0);

            var changes = JsonDocument.Parse((await db.AuditEntries.SingleAsync(a => a.Id == edit)).Changes!).RootElement;
            recordedOld = changes.GetProperty("Geom").GetProperty("old").GetProperty("sha256").GetString();
        }

        // The history payload is unchanged: still the summary, never the kept shape.
        var history = await owner.GetStringAsync($"/api/v1/history?entityType=feature&entityId={areaId}&pageSize=50");
        history.ShouldNotContain("POLYGON", Case.Sensitive);
        history.ShouldNotContain("Wkb");

        var (status, applied, skipped) = await RestoreAsync(owner, areaId, edit, ["Geom"]);
        status.ShouldBe(HttpStatusCode.OK);
        applied.ShouldBe(["Geom"]);
        skipped.ShouldBeEmpty();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var restored = (await db.Features.AsNoTracking().SingleAsync(f => f.Id == areaId)).Geom!;
            restored.EqualsExact(stored).ShouldBeTrue();
            AuditGeometry.Matches(restored, recordedOld).ShouldBeTrue();

            // The restore is a change of its own, and says so.
            var latest = await db.AuditEntries.AsNoTracking()
                .Where(a => a.EntityId == areaId.ToString())
                .OrderByDescending(a => a.Id)
                .FirstAsync();
            latest.Id.ShouldBeGreaterThan(edit);
            latest.Source.ShouldBe(ChangeSource.Restore);
            latest.ChangeSetId.ShouldNotBeNull();
            latest.ImportBatchId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_caller_without_exact_view_gets_the_name_back_but_not_the_geometry_until_granted()
    {
        var before = Ring(5, 25.40, 45.50, 0.02);
        var after = Ring(5, 25.45, 45.55, 0.02);
        var areaId = await CreateAreaAsync(owner, before, protectedLocation: true, name: "Original name");
        var (_, storedBefore) = await FeatureAsync(areaId);
        await GrantAsync(areaId, editorId, AccessAction.Read | AccessAction.Write);
        await UpdateAreaAsync(owner, areaId, "Changed name", after, protectedLocation: true);
        var (_, storedAfter) = await FeatureAsync(areaId);
        storedAfter.EqualsExact(storedBefore).ShouldBeFalse();
        var edit = await UpdateEntryAsync(areaId);

        // ---- no exact view: the name comes back, the geometry is refused and left as it is.
        var (status, applied, skipped) = await RestoreAsync(editor, areaId, edit, ["Name", "Geom"]);
        status.ShouldBe(HttpStatusCode.OK);
        applied.ShouldBe(["Name"]);
        skipped.ShouldBe([("Geom", "forbidden")]);

        var (name, geom) = await FeatureAsync(areaId);
        name.ShouldBe("Original name");
        geom.EqualsExact(storedAfter).ShouldBeTrue();

        // ---- the same caller with exact view: both come back.
        await GrantAsync(areaId, editorId, AccessAction.ViewExactLocation);
        (status, applied, skipped) = await RestoreAsync(editor, areaId, edit, fields: null);
        status.ShouldBe(HttpStatusCode.OK);
        applied.ShouldBe(["Name", "Geom"], ignoreOrder: true);
        skipped.ShouldBeEmpty();

        (name, geom) = await FeatureAsync(areaId);
        name.ShouldBe("Original name");
        geom.EqualsExact(storedBefore).ShouldBeTrue();
    }

    [Fact]
    public async Task A_field_the_callers_history_hides_is_reported_redacted_and_left_until_exact_view()
    {
        var caveId = await CreateCaveAsync(owner, protectedLocation: true, closestAddress: "Str. Veche 1", description: "Old note");
        await GrantAsync(caveId, editorId, AccessAction.Read | AccessAction.Write);
        var update = await owner.PutWithIfMatchAsync($"/api/v1/caves/{caveId}", new
        {
            name = "Restore cave",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            closestAddress = "Str. Noua 2",
            description = "New note",
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var edit = await UpdateEntryAsync(caveId);

        var (status, applied, skipped) = await RestoreAsync(editor, caveId, edit, ["Description", "ClosestAddress"]);
        status.ShouldBe(HttpStatusCode.OK);
        applied.ShouldBe(["Description"]);
        skipped.ShouldBe([("ClosestAddress", "redacted")]);
        (await CaveAddressAsync(caveId)).ShouldBe("Str. Noua 2");

        await GrantAsync(caveId, editorId, AccessAction.ViewExactLocation);
        (status, applied, skipped) = await RestoreAsync(editor, caveId, edit, ["ClosestAddress"]);
        status.ShouldBe(HttpStatusCode.OK);
        applied.ShouldBe(["ClosestAddress"]);
        skipped.ShouldBeEmpty();
        (await CaveAddressAsync(caveId)).ShouldBe("Str. Veche 1");
    }

    [Fact]
    public async Task Older_history_without_a_kept_shape_and_fields_a_restore_does_not_write_are_reported()
    {
        var areaId = await CreateAreaAsync(owner, Ring(5, 26.0, 46.0, 0.01), protectedLocation: false);

        // An edit recorded before full shapes were kept: a summary on both sides, nothing beside it.
        long older;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var summary = AuditGeometry.Summary(ToPolygon(Ring(1500, 26.0, 46.0, 0.05)));
            var entry = new AuditEntry
            {
                Action = AuditActions.Updated,
                EntityType = FeatureAudit.TypeName(FeatureKind.Generic),
                EntityId = areaId.ToString(),
                Changes = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["Geom"] = new Dictionary<string, object?> { ["old"] = summary, ["new"] = summary },
                    ["Visibility"] = new Dictionary<string, object?> { ["old"] = "private", ["new"] = "authenticated" },
                }),
            };
            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync();
            older = entry.Id;
        }

        var (status, applied, skipped) = await RestoreAsync(owner, areaId, older, ["Geom", "Visibility", "Description"]);
        status.ShouldBe(HttpStatusCode.OK);
        applied.ShouldBeEmpty();
        skipped.ShouldBe([("Geom", "no_pre_image"), ("Visibility", "not_restorable"), ("Description", "not_restorable")]);

        // A creation has no earlier value to put back.
        var created = await EntryAsync(areaId, AuditActions.Created);
        var response = await owner.PostAsJsonAsync("/api/v1/history/restore", Body(areaId, created, null));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("history.entry_not_restorable");
    }

    [Fact]
    public async Task The_route_asks_who_is_calling_what_they_may_read_and_what_they_may_change()
    {
        var shared = await CreateAreaAsync(owner, Ring(5, 26.2, 46.2, 0.01), protectedLocation: false);
        await UpdateAreaAsync(owner, shared, "Shared, edited", Ring(5, 26.25, 46.25, 0.01), protectedLocation: false);
        var sharedEdit = await UpdateEntryAsync(shared);

        var hidden = await CreateAreaAsync(owner, Ring(5, 26.3, 46.3, 0.01), protectedLocation: false, visibility: "private");
        await UpdateAreaAsync(owner, hidden, "Private, edited", Ring(5, 26.35, 46.35, 0.01), protectedLocation: false, visibility: "private");
        var hiddenEdit = await UpdateEntryAsync(hidden);

        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.PostAsJsonAsync("/api/v1/history/restore", Body(shared, sharedEdit, null)))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var invalid = await owner.PostAsJsonAsync("/api/v1/history/restore", new
        {
            entityType = "tripLog",
            entityId = shared,
            auditEntryId = 0,
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A private feature with no grant: the outsider is told nothing exists; its owner may restore.
        var unreadable = await outsider.PostAsJsonAsync("/api/v1/history/restore", Body(hidden, hiddenEdit, ["Name"]));
        unreadable.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(unreadable)).ShouldBe("history.entity_not_found");
        (await RestoreAsync(owner, hidden, hiddenEdit, ["Name"])).Applied.ShouldBe(["Name"]);

        // Readable but not writable: refused as a whole, nothing changed.
        (await outsider.PostAsJsonAsync("/api/v1/history/restore", Body(shared, sharedEdit, ["Name"])))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await FeatureAsync(shared)).Name.ShouldBe("Shared, edited");

        // A change of another feature is not this feature's to restore from.
        var foreign = await owner.PostAsJsonAsync("/api/v1/history/restore", Body(shared, hiddenEdit, ["Name"]));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(foreign)).ShouldBe("history.entry_not_found");
    }

    // ---- helpers

    /// <summary>
    /// Putting a promotion back gives the main role to the entrance the promotion took it from —
    /// recorded in the same change — rather than to the cave's fallback, its oldest entrance.
    /// </summary>
    [Fact]
    public async Task Restoring_a_promotion_gives_the_main_role_back_to_the_entrance_it_was_taken_from()
    {
        var cave = await CreateCaveAsync(owner, protectedLocation: false, "Str. Intrării 1", "Entrances");
        var oldest = await CreateEntranceAsync(cave, 25.31, 45.31, isMain: false);
        var held = await CreateEntranceAsync(cave, 25.32, 45.32, isMain: true);
        var promoted = await CreateEntranceAsync(cave, 25.33, 45.33, isMain: false);
        var put = await owner.PutAsJsonAsync($"/api/v1/cave-entrances/{promoted}", EntranceBody(25.33, 45.33, isMain: true));
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        (await MainRoleAsync(cave)).ShouldBe((promoted, 25.33));

        var (_, applied, _) = await RestoreAsync(owner, promoted, await UpdateEntryAsync(promoted), ["IsMain"]);
        applied.ShouldBe(["IsMain"]);

        (await MainRoleAsync(cave)).ShouldBe((held, 25.32));
        oldest.ShouldNotBe(held);
    }

    private object EntranceBody(double lon, double lat, bool isMain) => new
    {
        name = "Intrare",
        entranceTypeId,
        isMain,
        geom = new { type = "Point", coordinates = new[] { lon, lat } },
        altitude = 500m,
        positionQuality = "Gps",
    };

    private async Task<Guid> CreateEntranceAsync(Guid cave, double lon, double lat, bool isMain) =>
        await CreatedIdAsync(await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", EntranceBody(lon, lat, isMain)));

    /// <summary>The cave's main entrance and the longitude of the cave's own point.</summary>
    private async Task<(Guid? Main, double? CaveLon)> MainRoleAsync(Guid cave)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var mains = await db.CaveEntrances.AsNoTracking()
            .Where(e => e.CaveFeatureId == cave && e.IsMain && db.Features.Any(f => f.Id == e.Id))
            .Select(e => e.Id)
            .ToListAsync();
        mains.Count.ShouldBeLessThanOrEqualTo(1, "a cave has one main entrance at most");
        var point = (await db.Features.AsNoTracking().SingleAsync(f => f.Id == cave)).Geom as Point;
        return (mains.Count == 0 ? null : mains[0], point?.X);
    }

    private static object Body(Guid entityId, long auditEntryId, string[]? fields) =>
        new { entityType = "feature", entityId, auditEntryId, fields };

    private async Task<(HttpStatusCode Status, string[] Applied, (string Field, string Reason)[] Skipped)> RestoreAsync(
        HttpClient client, Guid entityId, long auditEntryId, string[]? fields)
    {
        var response = await client.PostAsJsonAsync("/api/v1/history/restore", Body(entityId, auditEntryId, fields));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var root = JsonDocument.Parse(body).RootElement;
        return (
            response.StatusCode,
            [.. root.GetProperty("applied").EnumerateArray().Select(a => a.GetString()!)],
            [.. root.GetProperty("skipped").EnumerateArray()
                .Select(s => (s.GetProperty("field").GetString()!, s.GetProperty("reason").GetString()!))]);
    }

    private async Task<long> UpdateEntryAsync(Guid featureId) => await EntryAsync(featureId, AuditActions.Updated);

    private async Task<long> EntryAsync(Guid featureId, string action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var key = featureId.ToString();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == key && a.Action == action)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Id)
            .FirstAsync();
    }

    private async Task<(string? Name, Geometry Geom)> FeatureAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId);
        return (feature.Name, feature.Geom!);
    }

    private async Task<string?> CaveAddressAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Caves.AsNoTracking().Where(c => c.Id == caveId).Select(c => c.ClosestAddress).SingleAsync();
    }

    /// <summary>A closed ring of <paramref name="points"/> vertices around a centre, written with full precision.</summary>
    private static double[][] Ring(int points, double lon, double lat, double radius)
    {
        var ring = Enumerable.Range(0, points)
            .Select(i => 2 * Math.PI * i / points)
            .Select(a => new[] { lon + (radius * Math.Cos(a)), lat + (radius * Math.Sin(a)) })
            .ToList();
        ring.Add(ring[0]);
        return [.. ring];
    }

    private static Polygon ToPolygon(double[][] ring) =>
        new(new LinearRing([.. ring.Select(c => new Coordinate(c[0], c[1]))])) { SRID = 4326 };

    private async Task<Guid> CreateAreaAsync(
        HttpClient client, double[][] ring, bool protectedLocation, string? name = null, string visibility = "authenticated")
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = name ?? $"Restore area {Guid.NewGuid():N}",
            featureTypeId = karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { ring } },
            locationProtected = protectedLocation,
            visibility,
        });
        return await CreatedIdAsync(response);
    }

    private async Task UpdateAreaAsync(
        HttpClient client, Guid id, string name, double[][] ring, bool protectedLocation, string visibility = "authenticated")
    {
        var response = await client.PutWithIfMatchAsync($"/api/v1/features/{id}", new
        {
            name,
            featureTypeId = karstAreaTypeId,
            geometry = new { type = "Polygon", coordinates = new[] { ring } },
            locationProtected = protectedLocation,
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(HttpClient client, bool protectedLocation, string closestAddress, string description)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = "Restore cave",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = protectedLocation,
            closestAddress,
            description,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(response);
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return root.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>ORs actions into one direct object-scope allow entry per subject per target.</summary>
    private async Task GrantAsync(Guid featureId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entry = await db.AccessEntries.FirstOrDefaultAsync(e =>
            e.SubjectKind == AccessSubjectKind.User && e.SubjectId == userId
            && e.Domain == AccessDomain.Features && e.ScopeKind == AccessScopeKind.Object
            && e.ScopeFeatureId == featureId && e.Effect == AccessEffect.Allow);
        if (entry is null)
        {
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = userId,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Features,
                Actions = actions,
                ScopeKind = AccessScopeKind.Object,
                ScopeFeatureId = featureId,
            });
        }
        else
        {
            entry.Actions |= actions;
        }

        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        editor.Dispose();
        outsider.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
