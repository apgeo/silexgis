// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A caller who may not be shown where a cave is may not decide where it is either.
/// </summary>
/// <remarks>
/// <para>
/// The main entrance is the cave's own representative point: creating it, or promoting an existing
/// entrance into it, writes the cave's location. Both routes used to be open to any caller holding
/// Write, including one for whom the location is obfuscated on every read — so a protected cave
/// could be moved anywhere by somebody who was never allowed to know where it was. The sync channel
/// refuses the same thing and has since it shipped; these are the web twins of that rule.
/// </para>
/// <para>
/// The second test is the one worth having. The create guard alone leaves a detour, because adding
/// a <em>further</em> entrance is deliberately still open — those coordinates are the caller's own
/// and echoing them back discloses nothing. Add one at a position of your choosing, then promote
/// it, and two permitted operations have composed into the one thing neither is allowed to be.
/// Promotion writes no coordinate of its own, so a guard placed on the coordinate write does not
/// see it; the guard has to sit on the promotion.
/// </para>
/// <para>
/// <b>Not yet run.</b> This class needs a PostGIS database, and the machine-wide gate is held by
/// another effort. It compiles and is written against the shape its neighbours use, but no verdict
/// exists for it and none should be claimed until one does.
/// </para>
/// </remarks>
public sealed class EntrancePlacementGuardTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const double SomewhereLon = 25.44721;
    private const double SomewhereLat = 45.53127;
    private const double ElsewhereLon = 22.10000;
    private const double ElsewhereLat = 46.90000;

    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient editor = null!;
    private Guid editorId;
    private long caveTypeId;
    private long entranceTypeId;

    public EntrancePlacementGuardTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"epg-own-{tag}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"epg-ed-{tag}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"epg-own-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"epg-ed-{tag}@t.local");
    }

    /// <summary>
    /// Creating the main entrance of a protected cave is refused for a caller without exact view,
    /// and allowed once the grant is there — so the refusal is the protection rather than the
    /// route being broken.
    /// </summary>
    [Fact]
    public async Task The_main_entrance_of_a_withheld_cave_cannot_be_placed_without_exact_view()
    {
        var caveId = await CreateProtectedCaveAsync();
        await GrantAsync(caveId, AccessAction.Read | AccessAction.Write);

        var refused = await PostEntranceAsync(editor, caveId, isMain: true, SomewhereLon, SomewhereLat);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(refused)).ShouldBe("entrance.location_forbidden");

        // The first entrance becomes the main one whether or not it was offered as such, so the
        // same refusal has to hold when nothing was claimed about it.
        var refusedImplicitly =
            await PostEntranceAsync(editor, caveId, isMain: false, SomewhereLon, SomewhereLat);
        refusedImplicitly.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // With exact view the same request is accepted. Without this half, a route that had simply
        // stopped accepting entrances would pass the assertions above.
        await GrantAsync(caveId, AccessAction.Read | AccessAction.Write | AccessAction.ViewExactLocation);
        var allowed = await PostEntranceAsync(editor, caveId, isMain: true, SomewhereLon, SomewhereLat);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// The detour: a further entrance may be added by a caller without exact view, and promoting it
    /// is refused — because promotion is what moves the cave onto it.
    /// </summary>
    [Fact]
    public async Task A_further_entrance_may_be_added_but_not_promoted_without_exact_view()
    {
        var caveId = await CreateProtectedCaveAsync();

        // The owner places the real main entrance, so the cave has a position of its own.
        var placed = await PostEntranceAsync(owner, caveId, isMain: true, SomewhereLon, SomewhereLat);
        placed.StatusCode.ShouldBe(HttpStatusCode.Created);

        await GrantAsync(caveId, AccessAction.Read | AccessAction.Write);

        // Adding a further entrance stays open: these coordinates are the caller's own.
        var added = await PostEntranceAsync(editor, caveId, isMain: false, ElsewhereLon, ElsewhereLat);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var addedId = (await JsonOf(added)).GetProperty("id").GetGuid();

        // Promoting it is not, because that is the write the cave's own point would take.
        var promotion = await editor.PutAsJsonAsync(
            $"/api/v1/cave-entrances/{addedId}",
            new
            {
                name = $"Intrare {tag}",
                entranceTypeId,
                isMain = true,
                geom = Point(ElsewhereLon, ElsewhereLat),
                positionQuality = "Unknown",
            });

        promotion.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(promotion)).ShouldBe("entrance.location_forbidden");

        // And the cave has not moved: the guard refused before anything was written, rather than
        // refusing after the mirror had already been pointed somewhere else.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cave = await db.Features.AsNoTracking().FirstAsync(f => f.Id == caveId);
        cave.Geom.ShouldNotBeNull();
        cave.Geom!.Coordinate.X.ShouldBe(SomewhereLon, 1e-9);
    }

    private async Task<Guid> CreateProtectedCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Pestera Pazita {tag}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PostEntranceAsync(
        HttpClient client, Guid caveId, bool isMain, double lon, double lat) =>
        client.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = $"Intrare {tag}",
            entranceTypeId,
            isMain,
            geom = Point(lon, lat),
            positionQuality = "Unknown",
        });

    private static object Point(double lon, double lat) =>
        new { type = "Point", coordinates = new[] { lon, lat } };

    private async Task GrantAsync(Guid caveId, AccessAction actions)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = editorId,
                    effect = "allow",
                    actions = actions.ToString().Replace(" ", string.Empty),
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await JsonOf(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        editor?.Dispose();
        factory.Dispose();
    }
}
