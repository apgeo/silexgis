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
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A feature set's own record: its name and slug stay unique across a rename as well as a
/// create, and the trail of a membership edit says what the set holds afterwards — including
/// the members the editing caller could not see and therefore kept.
/// </summary>
public sealed class FeatureSetTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private Guid viewerId;
    private string suffix = null!;

    public FeatureSetTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"fset-adm-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fset-edi-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fset-vie-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"fset-adm-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"fset-edi-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"fset-vie-{suffix}@t.local");
    }

    [Fact]
    public async Task A_rename_meets_the_same_name_rules_as_a_create_and_moves_the_slug_with_it()
    {
        var alpha = await CreateSetAsync($"Alpha {suffix}");
        var beta = await CreateSetAsync($"Beta {suffix}");

        // Onto another set's name: a coded refusal, not a unique violation.
        var taken = await admin.PutAsJsonAsync($"/api/v1/feature-sets/{beta}", new { name = $"Alpha {suffix}" });
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await taken.Content.ReadAsStringAsync()).ShouldContain("feature_set.name_taken");

        // Onto a name that slugifies like another set's.
        var sameSlug = await admin.PutAsJsonAsync($"/api/v1/feature-sets/{beta}", new { name = $"ALPHA  {suffix}!" });
        sameSlug.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await sameSlug.Content.ReadAsStringAsync()).ShouldContain("feature_set.name_taken");

        // A name with no letters or digits is refused exactly as on create.
        var symbols = await admin.PutAsJsonAsync($"/api/v1/feature-sets/{beta}", new { name = "!!!" });
        symbols.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await symbols.Content.ReadAsStringAsync()).ShouldContain("feature_set.name_invalid");

        // Its own name, re-described, is not a collision with itself.
        var same = await admin.PutAsJsonAsync($"/api/v1/feature-sets/{alpha}",
            new { name = $"Alpha {suffix}", description = "kept" });
        same.StatusCode.ShouldBe(HttpStatusCode.OK, await same.Content.ReadAsStringAsync());

        // Renamed away, the old name and its slug are free again.
        var renamed = await admin.PutAsJsonAsync($"/api/v1/feature-sets/{alpha}", new { name = $"Gamma {suffix}" });
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()
            .ShouldBe($"gamma-{suffix}");
        var reused = await admin.PostAsJsonAsync("/api/v1/feature-sets/", new { name = $"Alpha {suffix}" });
        reused.StatusCode.ShouldBe(HttpStatusCode.Created, await reused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_rename_needs_an_account_a_set_that_exists_and_the_right_to_write_sets()
    {
        var set = await CreateSetAsync($"Guarded {suffix}");

        using var anonymous = factory.CreateClient();
        (await anonymous.PutAsJsonAsync($"/api/v1/feature-sets/{set}", new { name = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{Guid.NewGuid()}", new { name = $"Nowhere {suffix}" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var empty = await admin.PutAsJsonAsync($"/api/v1/feature-sets/{set}", new { name = "" });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A viewer holds no right over sets; the administrator does, on the same set.
        (await viewer.PutAsJsonAsync($"/api/v1/feature-sets/{set}", new { name = $"Taken over {suffix}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{set}", new { name = $"Guarded still {suffix}" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_membership_trail_records_what_the_set_holds_afterwards_hidden_members_included()
    {
        var open = await CreateCaveAsync(editor, $"Trail Open {suffix}", "authenticated");
        var added = await CreateCaveAsync(editor, $"Trail Added {suffix}", "authenticated");
        var hidden = await CreateCaveAsync(editor, $"Trail Hidden {suffix}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var delegated = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = viewerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.FeatureSets,
            Actions = AccessAction.Read | AccessAction.Write,
            ScopeKind = AccessScopeKind.All,
        };
        db.AccessEntries.Add(delegated);
        await db.SaveChangesAsync();
        try
        {
            var set = await CreateSetAsync($"Trail {suffix}");
            (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{set}/members", new { featureIds = new[] { open, hidden } }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // The keeper can read the open caves and not the private one (the positive and the
            // negative case of the same visibility, side by side).
            var shown = (await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/feature-sets/{set}/members"))
                .EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ToList();
            shown.ShouldBe([open]);

            (await viewer.PutAsJsonAsync($"/api/v1/feature-sets/{set}/members", new { featureIds = new[] { open, added } }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var held = await db.FeatureSetMembers.AsNoTracking()
                .Where(m => m.FeatureSetId == set).Select(m => m.FeatureId).ToListAsync();
            held.ShouldBe([open, added, hidden], ignoreOrder: true);

            var changes = await db.Set<AuditEntry>().AsNoTracking()
                .Where(a => a.EntityType == nameof(FeatureSet) && a.EntityId == set.ToString())
                .OrderByDescending(a => a.Id)
                .Select(a => a.Changes)
                .FirstAsync();
            var members = JsonDocument.Parse(changes!).RootElement.GetProperty("Members");
            members.GetProperty("old").EnumerateArray().Select(e => e.GetGuid())
                .ShouldBe([open, hidden], ignoreOrder: true);
            members.GetProperty("new").EnumerateArray().Select(e => e.GetGuid())
                .ShouldBe(held, ignoreOrder: true);
        }
        finally
        {
            db.AccessEntries.Remove(delegated);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Every other route of the resource: an account is needed, a caller with no right over sets is
    /// refused beside one who has it on the same set, and an id that names no set is a coded miss.
    /// </summary>
    [Fact]
    public async Task Every_set_route_needs_an_account_the_right_over_sets_and_a_set_that_exists()
    {
        var set = await CreateSetAsync($"Routes {suffix}");
        var doomed = await CreateSetAsync($"Doomed {suffix}");
        var nowhere = Guid.NewGuid();
        var members = new { featureIds = Array.Empty<Guid>() };

        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.GetAsync("/api/v1/feature-sets/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync($"/api/v1/feature-sets/{set}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.GetAsync($"/api/v1/feature-sets/{set}/members")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.PostAsJsonAsync("/api/v1/feature-sets/", new { name = $"Anon {suffix}" }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.PutAsJsonAsync($"/api/v1/feature-sets/{set}/members", members))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await anonymous.DeleteAsync($"/api/v1/feature-sets/{set}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // A viewer holds no right over sets; the administrator does, route by route.
        (await viewer.GetAsync("/api/v1/feature-sets/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/feature-sets/")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/feature-sets/{set}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/v1/feature-sets/{set}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/feature-sets/{set}/members")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/v1/feature-sets/{set}/members")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refusedCreate = await viewer.PostAsJsonAsync("/api/v1/feature-sets/", new { name = $"Viewer's {suffix}" });
        refusedCreate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/v1/feature-sets/", new { name = $"Admin's {suffix}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await viewer.PutAsJsonAsync($"/api/v1/feature-sets/{set}/members", members))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{set}/members", members))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await viewer.DeleteAsync($"/api/v1/feature-sets/{doomed}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/api/v1/feature-sets/{doomed}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/api/v1/feature-sets/{doomed}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/feature-sets/{doomed}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // An id that names no set.
        foreach (var missing in new[]
                 {
                     await admin.GetAsync($"/api/v1/feature-sets/{nowhere}"),
                     await admin.GetAsync($"/api/v1/feature-sets/{nowhere}/members"),
                     await admin.PutAsJsonAsync($"/api/v1/feature-sets/{nowhere}/members", members),
                     await admin.DeleteAsync($"/api/v1/feature-sets/{nowhere}"),
                 })
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("feature_set.not_found");
        }

        // A name with no letters or digits is refused on create as on rename.
        var symbols = await admin.PostAsJsonAsync("/api/v1/feature-sets/", new { name = "!!!" });
        symbols.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await symbols.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("feature_set.name_invalid");
    }

    public static TheoryData<string> InvalidWrites => new()
    {
        """{"name":""}""",
        "{\"name\":\"" + new string('n', 101) + "\"}",
        "{\"name\":\"Long description\",\"description\":\"" + new string('d', 1001) + "\"}",
    };

    [Theory]
    [MemberData(nameof(InvalidWrites))]
    public async Task A_set_body_outside_the_rules_is_refused_on_create_and_rename(string body)
    {
        var set = await CreateSetAsync($"Validated {suffix} {Guid.NewGuid():N}"[..30]);
        foreach (var response in new[]
                 {
                     await admin.PostAsync("/api/v1/feature-sets/", Json(body)),
                     await admin.PutAsync($"/api/v1/feature-sets/{set}", Json(body)),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("validation.failed");
        }

        var nullMembers = await admin.PutAsync($"/api/v1/feature-sets/{set}/members", Json("""{"featureIds":null}"""));
        nullMembers.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nullMembers.Content.ReadAsStringAsync());
    }

    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    private async Task<Guid> CreateSetAsync(string name)
    {
        var created = await admin.PostAsJsonAsync("/api/v1/feature-sets/", new { name });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(HttpClient author, string name, string visibility = "private")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
