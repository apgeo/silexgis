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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Entity history end-to-end: timeline (incl. child events via audit roots), the
/// protection-of-history redaction that mirrors live DTO masking, and the write-path guard
/// that stops non-exact editors from round-tripping obfuscated values over precise data.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HistoryTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;    // Editor, owns the cave — has exact location (owner)
    private HttpClient editor = null!;   // Editor granted Read+Write, but NOT ViewExactLocation
    private HttpClient outsider = null!; // Editor, unrelated
    private Guid editorId;
    private long caveTypeId;
    private long entranceTypeId;

    private const double ExactLon = 25.46110;
    private const double ExactLat = 45.53220;
    private const string SecretAddress = "Str. Secreta 5";

    public HistoryTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hown-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hedit-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"hout-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"hown-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"hedit-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"hout-{suffix}@t.local");
    }

    [Fact]
    public async Task Timeline_includes_children_and_redacts_protected_location()
    {
        var caveId = await CreateProtectedCaveAsync();
        var entranceId = await CreateEntranceAsync(caveId, ExactLon, ExactLat);
        await GrantAsync(caveId, editorId, ObjectPermission.Read | ObjectPermission.Write);

        // Owner edits the cave and moves the entrance — both must surface in the cave timeline.
        await UpdateCaveAsync(owner, caveId, description: "Owner note", closestAddress: SecretAddress);
        await UpdateEntranceAsync(owner, caveId, entranceId, ExactLon + 0.001, ExactLat + 0.001, "moved");

        // ---- auth & existence
        using (var anon = factory.CreateClient())
        {
            (await anon.GetAsync($"/api/v1/history?entityType=Cave&entityId={caveId}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await outsider.GetAsync($"/api/v1/history?entityType=Cave&entityId={Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound); // existence not disclosed

        // ---- owner (exact): timeline carries the entrance child event and exact values (WKT).
        var (ownerBody, ownerEvents) = await HistoryAsync(owner, "Cave", caveId);
        ownerEvents.ShouldContain(e => e.GetProperty("entityType").GetString() == "CaveEntrance");
        ownerBody.ShouldContain("POINT");   // entrance geometry WKT is visible
        ownerBody.ShouldContain("Secreta"); // cave address is visible

        // ---- editor (no exact): coordinate + address values are removed and named, no WKT leaks.
        var (editorBody, editorEvents) = await HistoryAsync(editor, "Cave", caveId);
        editorBody.ShouldNotContain("POINT");
        editorBody.ShouldNotContain("Secreta");

        var entranceUpdate = editorEvents.First(e =>
            e.GetProperty("entityType").GetString() == "CaveEntrance"
            && e.GetProperty("action").GetString() == "updated");
        Redacted(entranceUpdate).ShouldContain("Geom");
        entranceUpdate.GetProperty("changes").ValueKind.ShouldNotBe(JsonValueKind.Null);
        entranceUpdate.GetProperty("changes").TryGetProperty("Geom", out _).ShouldBeFalse();

        // The entrance's *created* snapshot must not leak the birth coordinates either.
        var entranceCreate = editorEvents.First(e =>
            e.GetProperty("entityType").GetString() == "CaveEntrance"
            && e.GetProperty("action").GetString() == "created");
        Redacted(entranceCreate).ShouldContain("Geom");

        // ---- grant flip: granting ViewExactLocation lifts the redaction retroactively.
        await GrantAsync(caveId, editorId, ObjectPermission.ViewExactLocation);
        var (editorBody2, _) = await HistoryAsync(editor, "Cave", caveId);
        editorBody2.ShouldContain("POINT");
        editorBody2.ShouldContain("Secreta");
    }

    [Fact]
    public async Task Write_path_guard_preserves_protected_fields_for_non_exact_editors()
    {
        var caveId = await CreateProtectedCaveAsync();
        var entranceId = await CreateEntranceAsync(caveId, ExactLon, ExactLat);
        await GrantAsync(caveId, editorId, ObjectPermission.Read | ObjectPermission.Write);

        // The editor only ever sees obfuscated values; a normal full-replace edit submits the
        // address as null (redacted) and the snapped coordinates — the server must ignore those
        // for the protected fields and keep the precise stored data.
        var snapped = await ReadEntranceGeomAsync(editor, caveId, entranceId);
        await UpdateCaveAsync(editor, caveId, description: "Editor note", closestAddress: null);
        await UpdateEntranceAsync(editor, caveId, entranceId, snapped[0], snapped[1], "editor moved");

        // Owner (exact) confirms the protected fields survived untouched, while the editor's
        // non-protected edits (descriptions) did apply.
        var cave = await GetJsonAsync(owner, $"/api/v1/caves/{caveId}");
        cave.GetProperty("closestAddress").GetString().ShouldBe(SecretAddress);
        cave.GetProperty("description").GetString().ShouldBe("Editor note");

        var entrance = await ReadEntranceAsync(owner, caveId, entranceId);
        entrance.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(ExactLon, 1e-9);
        entrance.GetProperty("description").GetString().ShouldBe("editor moved");

        // A privileged editor's coordinate change still applies.
        await GrantAsync(caveId, editorId, ObjectPermission.ViewExactLocation);
        await UpdateEntranceAsync(editor, caveId, entranceId, ExactLon + 0.002, ExactLat + 0.002, "exact move");
        var moved = await ReadEntranceAsync(owner, caveId, entranceId);
        moved.GetProperty("geom").GetProperty("coordinates")[0].GetDouble().ShouldBe(ExactLon + 0.002, 1e-9);
    }

    // ---- helpers

    private static string[] Redacted(JsonElement e) =>
        [.. e.GetProperty("redactedProperties").EnumerateArray().Select(x => x.GetString()!)];

    private async Task<(string Body, List<JsonElement> Events)> HistoryAsync(HttpClient client, string type, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/history?entityType={type}&entityId={id}&pageSize=200");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var items = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().ToList();
        return (body, items);
    }

    private async Task<Guid> CreateProtectedCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Protected {Guid.NewGuid():N}",
            caveTypeId,
            visibility = "Authenticated",
            locationProtected = true,
            closestAddress = SecretAddress,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateEntranceAsync(Guid caveId, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            altitude = 900m,
            positionQuality = "Gps",
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task UpdateCaveAsync(HttpClient client, Guid caveId, string description, string? closestAddress)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/caves/{caveId}", new
        {
            name = $"Protected {caveId:N}"[..20],
            caveTypeId,
            visibility = "Authenticated",
            locationProtected = true,
            closestAddress,
            description,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
    }

    private async Task UpdateEntranceAsync(HttpClient client, Guid caveId, Guid entranceId, double lon, double lat, string description)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/cave-entrances/{entranceId}", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            description,
            positionQuality = "Gps",
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
    }

    private async Task<double[]> ReadEntranceGeomAsync(HttpClient client, Guid caveId, Guid entranceId)
    {
        var entrance = await ReadEntranceAsync(client, caveId, entranceId);
        var coords = entrance.GetProperty("geom").GetProperty("coordinates");
        return [coords[0].GetDouble(), coords[1].GetDouble()];
    }

    private async Task<JsonElement> ReadEntranceAsync(HttpClient client, Guid caveId, Guid entranceId)
    {
        var list = await GetJsonAsync(client, $"/api/v1/caves/{caveId}/entrances");
        return list.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == entranceId).Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task GrantAsync(Guid caveId, Guid userId, ObjectPermission permissions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var acl = await db.ObjectAcls.FirstOrDefaultAsync(a =>
            a.EntityType == AttachedEntityType.Cave && a.EntityId == caveId
            && a.SubjectKind == AclSubjectKind.User && a.SubjectId == userId);
        if (acl is null)
        {
            db.ObjectAcls.Add(new ObjectAcl
            {
                EntityType = AttachedEntityType.Cave,
                EntityId = caveId,
                SubjectKind = AclSubjectKind.User,
                SubjectId = userId,
                Permissions = permissions,
            });
        }
        else
        {
            acl.Permissions |= permissions; // one row per subject (unique index); OR grants in
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        owner.Dispose();
        editor.Dispose();
        outsider.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
