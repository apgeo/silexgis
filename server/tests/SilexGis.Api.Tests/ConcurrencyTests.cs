// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Optimistic concurrency over the feature aggregate. The version token is the FEATURE
/// row — one ETag covers a cave's own columns, its subtype attributes and the derived
/// mirror an entrance change refreshes — so a stale editor of any part is refused. Single
/// GETs carry the ETag, writes honor If-Match with 412 on staleness, and a fresh ETag is
/// issued after every successful update.
/// </summary>
public sealed class ConcurrencyTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private long caveTypeId;
    private long entranceTypeId;

    public ConcurrencyTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cc-own-{suffix}@t.local");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cc-own-{suffix}@t.local");
    }

    /// <summary>
    /// Every versioned table names a table that exists. The allow-list is an enum beside a
    /// dictionary and the compiler checks neither against the other, so a table registered in
    /// one and forgotten in the other would first be noticed as a failure to serve an ETag on a
    /// live endpoint. Asking for a row that is not there answers "no version" from a real
    /// database, which is only possible if both halves and the schema agree.
    /// </summary>
    [Fact]
    public async Task Every_versioned_table_resolves_against_the_schema()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        foreach (var table in Enum.GetValues<VersionedTable>())
        {
            var version = await ConcurrencySql.VersionAsync(db, table, Guid.CreateVersion7(), default);
            version.ShouldBeNull($"{table} should resolve and report no row.");
        }
    }

    [Fact]
    public async Task Etag_and_if_match_guard_against_lost_updates()
    {
        var caveId = await CreateCaveAsync("Concurrent Cave v1");

        var get = await owner.GetAsync($"/api/v1/caves/{caveId}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = get.Headers.ETag!.Tag;
        etag.ShouldNotBeNullOrWhiteSpace();
        // The token is the feature row's version, not something the cave subtype owns.
        etag.ShouldBe(await FeatureETagAsync(caveId));

        // Update WITH the fresh ETag → accepted, and a new ETag is issued.
        var okResponse = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Concurrent Cave v2"), etag);
        okResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await okResponse.Content.ReadAsStringAsync());
        var newEtag = okResponse.Headers.ETag!.Tag;
        newEtag.ShouldNotBe(etag); // xmin advanced with the update

        // Replaying the OLD ETag now → 412 with the stable code; nothing is written.
        var staleResponse = await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Concurrent Cave v3 (lost)"), etag);
        staleResponse.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await staleResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.version_mismatch");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}"))
            .GetProperty("name").GetString().ShouldBe("Concurrent Cave v2");

        // A PUT without the header is refused with 428, so an edit of a loaded resource can
        // never silently overwrite.
        var noHeader = await owner.PutAsJsonAsync($"/api/v1/caves/{caveId}", CaveBody("Concurrent Cave v3"));
        noHeader.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await noHeader.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.if_match_required");

        // Stale delete → 412; delete with the current ETag → gone.
        var currentEtag = await FeatureETagAsync(caveId);
        currentEtag.ShouldBe(newEtag);
        (await DeleteWithIfMatchAsync($"/api/v1/caves/{caveId}", etag))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await DeleteWithIfMatchAsync($"/api/v1/caves/{caveId}", currentEtag))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // DELETE stays lenient — list/map deletes carry no loaded version — so no header deletes.
        var second = await CreateCaveAsync("Lenient Delete Cave");
        (await owner.DeleteAsync($"/api/v1/caves/{second}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Entrance_edits_version_on_the_feature_row_and_bump_the_cave_aggregate()
    {
        var caveId = await CreateCaveAsync("Aggregate Token Cave");
        var caveEtagBefore = await FeatureETagAsync(caveId);

        // Adding an entrance refreshes the cave's derived mirror (count + representative
        // point), so the cave's own token advances and a stale editor is refused.
        var create = await owner.PostAsJsonAsync(
            $"/api/v1/caves/{caveId}/entrances", EntranceBody("Main", 25.81, 45.91, surveyedAt: null));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var entranceId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var caveEtagAfter = await FeatureETagAsync(caveId);
        caveEtagAfter.ShouldNotBe(caveEtagBefore);
        (await owner.PutWithIfMatchAsync(
            $"/api/v1/caves/{caveId}", CaveBody("Aggregate Token Cave v2"), caveEtagBefore))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // An entrance is versioned on its own feature row, and a subtype-only edit still
        // advances that token — otherwise an attribute change would slip past a concurrent
        // editor unnoticed.
        var entranceEtag = await FeatureETagAsync(entranceId);
        var edit = await owner.PutWithIfMatchAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main", 25.81, 45.91, surveyedAt: new DateOnly(2026, 5, 17)),
            entranceEtag);
        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        (await edit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("surveyedAt").GetString().ShouldBe("2026-05-17");

        var entranceEtagAfter = await FeatureETagAsync(entranceId);
        entranceEtagAfter.ShouldNotBe(entranceEtag);

        (await owner.PutWithIfMatchAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main", 25.81, 45.91, surveyedAt: new DateOnly(2026, 6, 1)),
            entranceEtag))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // Entrance edits keep the lenient contract: no header is still last-write-wins.
        (await owner.PutAsJsonAsync(
            $"/api/v1/cave-entrances/{entranceId}",
            EntranceBody("Main entrance", 25.81, 45.91, surveyedAt: null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The row's current version as an ETag value — the token every feature kind shares.</summary>
    private async Task<string> FeatureETagAsync(Guid featureId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var version = await ConcurrencySql.VersionAsync(db, VersionedTable.Features, featureId, CancellationToken.None);
        version.ShouldNotBeNull();
        return $"\"{version}\"";
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", CaveBody(name));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> DeleteWithIfMatchAsync(string url, string ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await owner.SendAsync(request);
    }

    private object CaveBody(string name) => new
    {
        name,
        caveTypeId,
        visibility = "private",
        locationProtected = false,
        explorationStatus = "unknown",
        isShowCave = false,
    };

    private object EntranceBody(string? name, double lon, double lat, DateOnly? surveyedAt) => new
    {
        name,
        entranceTypeId,
        isMain = true,
        geom = new { type = "Point", coordinates = new[] { lon, lat } },
        altitude = (decimal?)null,
        description = (string?)null,
        positionQuality = "gps",
        surveyedAt,
    };

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        factory.Dispose();
    }
}
