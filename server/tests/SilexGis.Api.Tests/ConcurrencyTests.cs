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
/// Optimistic concurrency: single GETs carry an ETag, writes honor If-Match with 412 on
/// staleness, and a fresh ETag is issued after a successful update.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConcurrencyTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private long caveTypeId;

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
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"cc-own-{suffix}@t.local");
    }

    [Fact]
    public async Task Etag_and_if_match_guard_against_lost_updates()
    {
        // Create, then GET to obtain the current ETag.
        var create = await owner.PostAsJsonAsync("/api/v1/caves", CaveBody("Concurrent Cave v1"));
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var caveId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var get = await owner.GetAsync($"/api/v1/caves/{caveId}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = get.Headers.ETag!.Tag;
        etag.ShouldNotBeNullOrWhiteSpace();

        // Update WITH the fresh ETag → accepted, and a new ETag is issued.
        using var okUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/caves/{caveId}")
        {
            Content = JsonContent.Create(CaveBody("Concurrent Cave v2")),
        };
        okUpdate.Headers.TryAddWithoutValidation("If-Match", etag);
        var okResponse = await owner.SendAsync(okUpdate);
        okResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await okResponse.Content.ReadAsStringAsync());
        var newEtag = okResponse.Headers.ETag!.Tag;
        newEtag.ShouldNotBe(etag); // xmin advanced with the update

        // Replaying the OLD ETag now → 412 with the stable code; nothing is written.
        using var staleUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/caves/{caveId}")
        {
            Content = JsonContent.Create(CaveBody("Concurrent Cave v3 (lost)")),
        };
        staleUpdate.Headers.TryAddWithoutValidation("If-Match", etag);
        var staleResponse = await owner.SendAsync(staleUpdate);
        staleResponse.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await staleResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.version_mismatch");
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}"))
            .GetProperty("name").GetString().ShouldBe("Concurrent Cave v2");

        // Stale delete → 412; delete with the current ETag → gone.
        using var staleDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/caves/{caveId}");
        staleDelete.Headers.TryAddWithoutValidation("If-Match", etag);
        (await owner.SendAsync(staleDelete)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        using var freshDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/caves/{caveId}");
        freshDelete.Headers.TryAddWithoutValidation("If-Match", newEtag);
        (await owner.SendAsync(freshDelete)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The freeze makes If-Match mandatory on edits of loaded resources: a PUT without it
        // is refused with 428 and the stable code, so an edit can never silently overwrite.
        var create2 = await owner.PostAsJsonAsync("/api/v1/caves", CaveBody("Frozen Cave"));
        var caveId2 = (await create2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var noHeader = await owner.PutAsJsonAsync($"/api/v1/caves/{caveId2}", CaveBody("Frozen Cave v2"));
        noHeader.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await noHeader.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("concurrency.if_match_required");

        // DELETE stays lenient — list/map deletes carry no loaded version — so no header deletes.
        (await owner.DeleteAsync($"/api/v1/caves/{caveId2}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private object CaveBody(string name) => new
    {
        name,
        caveTypeId,
        visibility = "private",
        locationProtected = false,
        explorationStatus = "Unknown",
        isShowCave = false,
    };

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
