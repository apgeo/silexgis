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
/// The mobile sync surface as it stands before any row moves: what a device is told it may
/// expect, and the lifecycle of the selection a caver keeps on it. The load-bearing
/// assertions here are that a sync set is reachable by nobody but its owner, that naming a
/// root is checked against the caller's own reads, and that the announced contract version
/// is a fixed statement about this build rather than something an installation may set.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SyncSetTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private HttpClient anonymous = null!;
    private Guid ownGroupId;
    private Guid foreignGroupId;
    private Guid readableFeatureId;
    private Guid privateFeatureId;

    public SyncSetTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ss-own-{suffix}@t.local");

        // A Viewer, deliberately: the seeded Editors group reads past visibility across the
        // whole feature domain, so an Editor outsider would read the private root and the
        // negative below would pass for the wrong reason.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ss-out-{suffix}@t.local");

        long karstAreaTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();

            var joined = new CavingGroup { Name = $"Sync In {suffix}", Slug = $"sync-in-{suffix}" };
            var foreign = new CavingGroup { Name = $"Sync Out {suffix}", Slug = $"sync-out-{suffix}" };
            db.CavingGroups.AddRange(joined, foreign);
            await db.SaveChangesAsync();
            await RosterHelper.AddMemberAsync(db, joined.Id, ownerId);
            ownGroupId = joined.Id;
            foreignGroupId = foreign.Id;
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"ss-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"ss-out-{suffix}@t.local");
        anonymous = factory.CreateClient();

        readableFeatureId = await CreateFeatureAsync(karstAreaTypeId, $"Sync readable {suffix}", "authenticated");
        privateFeatureId = await CreateFeatureAsync(karstAreaTypeId, $"Sync private {suffix}", "private");
    }

    [Fact]
    public async Task Capabilities_state_the_contract_version_and_the_limits_a_device_sizes_itself_to()
    {
        var response = await owner.GetAsync("/api/v1/sync/capabilities");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Fixed for the whole of this protocol generation. A device pins it, so a change here
        // is a change every phone in the field has to be rebuilt for.
        body.GetProperty("contractVersion").GetInt32().ShouldBe(1);
        body.GetProperty("pageSizeMax").GetInt32().ShouldBeGreaterThan(0);
        body.GetProperty("uploadRowsMax").GetInt32().ShouldBeGreaterThan(0);

        // Nothing moves rows yet, and the device is told so by name rather than by discovering
        // it at the first transfer.
        var features = body.GetProperty("features").EnumerateArray().Select(x => x.GetString()).ToList();
        features.ShouldNotContain("download");
        features.ShouldNotContain("upload");
    }

    [Fact]
    public async Task Every_sync_route_refuses_a_caller_with_no_token()
    {
        (await anonymous.GetAsync("/api/v1/sync/capabilities")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/sync/sets/")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/sync/sets/", Body("Phone"))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/sync/sets/{Guid.NewGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"/api/v1/sync/sets/{Guid.NewGuid()}", Body("Phone"))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/sync/sets/{Guid.NewGuid()}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_set_is_created_read_replaced_and_deleted_and_only_a_real_change_moves_the_revision()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/sync/sets/", Body(
            "Field phone", roots: [readableFeatureId], settings: new { pciStrategy = "ro-default", digits = 4 }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var set = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = set.GetProperty("id").GetGuid();
        set.GetProperty("revision").GetInt64().ShouldBe(1);
        set.GetProperty("uploadVisibility").GetString().ShouldBe("private");
        set.GetProperty("rootFeatureIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([readableFeatureId]);
        set.GetProperty("settings").GetProperty("pciStrategy").GetString().ShouldBe("ro-default");

        var listed = await owner.GetFromJsonAsync<JsonElement>("/api/v1/sync/sets/");
        listed.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldContain(id);

        var fetched = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sync/sets/{id}");
        fetched.GetProperty("name").GetString().ShouldBe("Field phone");

        // Reposting the same selection is not a change, so a device that resends what it
        // already holds is not told its copy has gone stale.
        var unchanged = await owner.PutAsJsonAsync($"/api/v1/sync/sets/{id}", Body(
            "Field phone", roots: [readableFeatureId], settings: new { pciStrategy = "ro-default", digits = 4 }));
        unchanged.StatusCode.ShouldBe(HttpStatusCode.OK, await unchanged.Content.ReadAsStringAsync());
        (await unchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt64().ShouldBe(1);

        // Dropping the root is a change, and the revision is what the device compares on.
        var changed = await owner.PutAsJsonAsync($"/api/v1/sync/sets/{id}", Body(
            "Field phone", roots: [], settings: new { pciStrategy = "ro-default", digits = 4 }));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var replaced = await changed.Content.ReadFromJsonAsync<JsonElement>();
        replaced.GetProperty("revision").GetInt64().ShouldBe(2);
        replaced.GetProperty("rootFeatureIds").EnumerateArray().ShouldBeEmpty();

        (await owner.DeleteAsync($"/api/v1/sync/sets/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/sync/sets/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_set_belongs_to_its_owner_alone_and_everyone_else_is_told_it_does_not_exist()
    {
        var id = await CreateSetAsync("Owner's phone");

        // The positive, in the same test: the owner reads exactly the set the outsider cannot.
        var mine = await owner.GetAsync($"/api/v1/sync/sets/{id}");
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);

        var theirs = await outsider.GetAsync($"/api/v1/sync/sets/{id}");
        theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await theirs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("sync.set_not_found");

        var theirList = await outsider.GetFromJsonAsync<JsonElement>("/api/v1/sync/sets/");
        theirList.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldNotContain(id);

        // Writing is refused the same way, so an outsider cannot learn a set exists by probing.
        (await outsider.PutAsJsonAsync($"/api/v1/sync/sets/{id}", Body("Stolen"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/v1/sync/sets/{id}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // …and none of that touched it.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sync/sets/{id}"))
            .GetProperty("name").GetString().ShouldBe("Owner's phone");
    }

    [Fact]
    public async Task A_root_the_caller_cannot_read_is_refused_exactly_like_one_that_is_not_there()
    {
        // The positive first: a feature the outsider genuinely reads is accepted.
        var allowed = await outsider.PostAsJsonAsync("/api/v1/sync/sets/",
            Body("Viewer phone", roots: [readableFeatureId]));
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());

        // The negative: a private feature owned by somebody else, which this Viewer holds no
        // grant over at all — and a UUID that names nothing, answered identically.
        foreach (var unreadable in new[] { privateFeatureId, Guid.NewGuid() })
        {
            var refused = await outsider.PostAsJsonAsync("/api/v1/sync/sets/",
                Body("Viewer phone", roots: [unreadable]));
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("sync.root_not_found");
        }
    }

    [Fact]
    public async Task A_group_binding_is_accepted_for_the_callers_own_group_and_refused_for_anybody_elses()
    {
        var mine = await owner.PostAsJsonAsync("/api/v1/sync/sets/", Body("Club phone", group: ownGroupId));
        mine.StatusCode.ShouldBe(HttpStatusCode.Created, await mine.Content.ReadAsStringAsync());
        (await mine.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cavingGroupId").GetGuid()
            .ShouldBe(ownGroupId);

        var theirs = await owner.PostAsJsonAsync("/api/v1/sync/sets/", Body("Club phone", group: foreignGroupId));
        theirs.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await theirs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("sync.caving_group_forbidden");

        var missing = await owner.PostAsJsonAsync("/api/v1/sync/sets/", Body("Club phone", group: Guid.NewGuid()));
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("sync.caving_group_not_found");
    }

    /// <summary>
    /// Club visibility with no club named is refused. It reads like a half-filled form, but this
    /// field is the audience every row the device later uploads is created with, so the pair
    /// "visible to a club" and "no club" would silently make each of those rows readable by
    /// nobody at all — not even the club — with nothing anywhere saying so.
    /// </summary>
    [Fact]
    public async Task Club_visibility_needs_a_club_to_name()
    {
        var missingClub = await owner.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = "Club phone",
            cavingGroupId = (Guid?)null,
            uploadVisibility = "cavingGroup",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        missingClub.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await missingClub.Content.ReadAsStringAsync());
        (await missingClub.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("validation.failed");

        // The positive: the same request naming a club the caller is in is what the field is for.
        var named = await owner.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = "Club phone",
            cavingGroupId = ownGroupId,
            uploadVisibility = "cavingGroup",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        named.StatusCode.ShouldBe(HttpStatusCode.Created, await named.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A write that states no settings document is refused rather than read as an empty one. A
    /// write here replaces the whole set, so "absent" could only mean "clear it", and a caller
    /// that simply did not echo the field back would wipe the code-generation settings the
    /// device needs — then hand it the new revision it would have to notice the loss by.
    /// </summary>
    [Fact]
    public async Task A_write_that_states_no_settings_document_is_refused_and_leaves_the_stored_one_alone()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/sync/sets/", Body(
            "Settings phone", settings: new { pciStrategy = "ro-default", digits = 4 }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var silent = await owner.PutAsJsonAsync($"/api/v1/sync/sets/{id}", new
        {
            name = "Settings phone",
            cavingGroupId = (Guid?)null,
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
        });
        silent.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await silent.Content.ReadAsStringAsync());
        (await silent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("validation.failed");

        // Nothing was cleared and nothing moved, so the device's copy is still the current one.
        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sync/sets/{id}");
        after.GetProperty("settings").GetProperty("pciStrategy").GetString().ShouldBe("ro-default");
        after.GetProperty("revision").GetInt64().ShouldBe(1);
    }

    /// <summary>
    /// A sync set leaves no audit entry. The audit trail is read on a right over the audit
    /// domain and not on the row an entry describes, so an audited create would hand this set's
    /// name, its club binding and its whole settings document to every account holding that
    /// right — undoing by a side door the rule that its owner is its only reader.
    /// </summary>
    [Fact]
    public async Task The_lifecycle_of_a_set_writes_nothing_to_the_audit_trail()
    {
        var id = await CreateSetAsync("Audited phone");
        (await owner.PutAsJsonAsync($"/api/v1/sync/sets/{id}", Body("Audited phone renamed"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/sync/sets/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.AuditEntries.AsNoTracking()
            .CountAsync(a => a.EntityType != null && a.EntityType.Contains("SyncSet"))).ShouldBe(0);

        // Read as a total as well, so the assertion above cannot be satisfied by an audit trail
        // that stopped recording anything at all: this class creates features, which are audited.
        (await AuditCountAsync()).ShouldBeGreaterThan(0);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AuditEntries.AsNoTracking().CountAsync();
    }

    [Fact]
    public async Task Malformed_requests_are_refused_before_anything_is_written()
    {
        foreach (var body in new object[]
        {
            Body(string.Empty),
            Body(new string('x', 201)),
            new { name = "Phone", uploadVisibility = "private", rootFeatureIds = new[] { readableFeatureId, readableFeatureId }, settings = new { } },
            new { name = "Phone", uploadVisibility = "private", rootFeatureIds = Array.Empty<Guid>(), settings = 7 },
        })
        {
            var response = await owner.PostAsJsonAsync("/api/v1/sync/sets/", body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(body));
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("validation.failed");
        }

        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/sync/sets/"))
            .EnumerateArray().Select(x => x.GetProperty("name").GetString()).ShouldNotContain("Phone");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    private static object Body(
        string name,
        Guid? group = null,
        IReadOnlyList<Guid>? roots = null,
        object? settings = null) => new
        {
            name,
            cavingGroupId = group,
            uploadVisibility = "private",
            rootFeatureIds = roots ?? [],
            settings = settings ?? new { },
        };

    private async Task<Guid> CreateSetAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/sync/sets/", Body(name));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateFeatureAsync(long featureTypeId, string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.50, 45.60 }, new[] { 25.52, 45.60 },
                        new[] { 25.52, 45.62 }, new[] { 25.50, 45.62 },
                        new[] { 25.50, 45.60 },
                    },
                },
            },
            visibility,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
