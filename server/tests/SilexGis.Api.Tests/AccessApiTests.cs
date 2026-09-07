// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.Permissions;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The permission model's own API: rulesets and their trustees, feature sets, the rules
/// written directly onto one object, and the two answers a client needs — what may I do,
/// and why. Every write here moves authorization, so the guards are the point.
/// </summary>
public sealed class AccessApiTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private Guid editorId;
    private Guid viewerId;
    private string suffix = null!;

    public AccessApiTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"acc-api-adm-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"acc-api-edi-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"acc-api-vie-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"acc-api-adm-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"acc-api-edi-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"acc-api-vie-{suffix}@t.local");
    }

    // ---- the model's own surface is governed by the model ----

    [Fact]
    public async Task Only_the_permission_domain_opens_the_permission_surface()
    {
        // Administrators deliberately excludes this domain — running an installation and
        // rewriting its security model are different jobs — so an Editor is refused.
        (await editor.GetAsync("/api/v1/permission-groups/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/api/v1/permission-groups/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/permission-groups/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/permission-groups/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_catalog_offers_exactly_what_the_validator_accepts()
    {
        var response = await admin.GetAsync("/api/v1/permission-groups/catalog");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var catalog = await response.Content.ReadFromJsonAsync<JsonElement>();

        var domains = catalog.GetProperty("domains").EnumerateArray().ToList();
        domains.Count.ShouldBe(Enum.GetValues<AccessDomain>().Length);

        JsonElement DomainOf(string name) =>
            domains.First(d => d.GetProperty("name").GetString() == name);
        List<string> ScopesOf(JsonElement domain) =>
            [.. domain.GetProperty("scopes").EnumerateArray().Select(s => s.GetProperty("scopeKind").GetString()!)];

        // Only the feature world contains other things, so only it takes the collection
        // scopes; only the trio-carrying domains have an owner or a club binding to key on.
        var features = DomainOf("features");
        ScopesOf(features).ShouldContain("subtree");
        ScopesOf(features).ShouldContain("featureSet");
        features.GetProperty("supportsKindNarrowing").GetBoolean().ShouldBeTrue();

        var tags = DomainOf("tags");
        ScopesOf(tags).ShouldNotContain("subtree");
        ScopesOf(tags).ShouldNotContain("own");
        ScopesOf(tags).ShouldNotContain("cavingGroup");
        tags.GetProperty("supportsKindNarrowing").GetBoolean().ShouldBeFalse();

        // Nothing is created "into" a fixed set of existing rows, so the catalogue must
        // not offer it there — the same refusal the validator makes.
        var setScope = features.GetProperty("scopes").EnumerateArray()
            .First(s => s.GetProperty("scopeKind").GetString() == "featureSet");
        setScope.GetProperty("actions").EnumerateArray()
            .Select(a => a.GetString()).ShouldNotContain("create");

        // Documents are owned content: they carry the trio, so they take every scope that
        // keys on it, plus the one collection they belong to — the cabinet they are filed
        // in. They are not features, so the feature-world collections and kind narrowing
        // stay off the menu.
        var documents = DomainOf("documents");
        ScopesOf(documents).ShouldBe(
            ["all", "own", "cavingGroup", "cabinet", "object"], ignoreOrder: true);
        documents.GetProperty("supportsKindNarrowing").GetBoolean().ShouldBeFalse();

        // An expedition is owned content too, and the object scope is the reason it has a
        // domain of its own: sharing one camp with a partner club is one act on one object,
        // and only a rule scoped to that object says it. It belongs to no collection of any
        // kind, so all three collection scopes stay off its menu.
        var expeditions = DomainOf("expeditions");
        ScopesOf(expeditions).ShouldBe(["all", "own", "cavingGroup", "object"], ignoreOrder: true);
        expeditions.GetProperty("supportsKindNarrowing").GetBoolean().ShouldBeFalse();

        List<string> ActionsOf(JsonElement domain, string scopeKind) =>
            [.. domain.GetProperty("scopes").EnumerateArray()
                .First(s => s.GetProperty("scopeKind").GetString() == scopeKind)
                .GetProperty("actions").EnumerateArray().Select(a => a.GetString()!)];

        // Filing an existing document is a write on that document, so nothing is created
        // "into" a cabinet — the same refusal the validator makes for a feature set.
        ActionsOf(documents, "cabinet").ShouldNotContain("create");

        // Uploading a document is a Create right, offered exactly where a prospective
        // row can be evaluated: domain-wide and against a club binding, never against an
        // owner column or one existing document.
        ActionsOf(documents, "all").ShouldContain("create");
        ActionsOf(documents, "cavingGroup").ShouldContain("create");
        ActionsOf(documents, "own").ShouldNotContain("create");
        ActionsOf(documents, "object").ShouldNotContain("create");
    }

    [Fact]
    public async Task Rulesets_round_trip_and_refuse_rules_the_model_cannot_evaluate()
    {
        var groupId = await CreatePermissionGroupAsync($"Round Trip {suffix}");

        var ok = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new { effect = "allow", domain = "tripLogs", actions = "read, write", scopeKind = "all" },
            },
        });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var saved = await ok.Content.ReadFromJsonAsync<JsonElement>();
        saved.EnumerateArray().Count().ShouldBe(1);

        // Kind narrowing has no faithful flat form outside all/own, so it is refused
        // rather than quietly widened.
        var narrowed = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new
                {
                    effect = "allow", domain = "features", actions = "read", scopeKind = "subtree",
                    scopeId = Guid.NewGuid(), featureKind = "caveEntrance",
                },
            },
        });
        narrowed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await narrowed.Content.ReadAsStringAsync()).ShouldContain(AccessEntryRules.ScopeInvalidCode);

        // A rule anchored on something that does not exist would be a deny nobody could
        // ever find again.
        var dangling = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new { effect = "deny", domain = "features", actions = "read", scopeKind = "subtree", scopeId = Guid.NewGuid() },
            },
        });
        dangling.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await dangling.Content.ReadAsStringAsync()).ShouldContain("access_entry.anchor_not_found");

        // The refused writes changed nothing.
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/permission-groups/{groupId}/entries");
        current.EnumerateArray().Count().ShouldBe(1);
    }

    [Fact]
    public async Task The_protected_groups_keep_their_shape()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var fullAdmins = await db.PermissionGroups
            .FirstAsync(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug);

        // Renaming would move the anchor the resolver and every guard key on.
        var renamed = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{fullAdmins.Id}", new
        {
            name = "Renamed Administrators",
            description = (string?)null,
        });
        renamed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await renamed.Content.ReadAsStringAsync()).ShouldContain("permission_group.protected");

        (await admin.DeleteAsync($"/api/v1/permission-groups/{fullAdmins.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // And it holds no rules: membership is the grant, which is exactly what makes it
        // unreachable by a deny.
        var entries = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{fullAdmins.Id}/entries", new
        {
            entries = new[]
            {
                new { effect = "allow", domain = "features", actions = "read", scopeKind = "all" },
            },
        });
        entries.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A deny naming it would read as though it could reach them; it cannot.
        var other = await CreatePermissionGroupAsync($"Denier {suffix}");
        var deny = await admin.PutAsJsonAsync($"/api/v1/permission-groups/{other}/entries", new
        {
            entries = new[]
            {
                new
                {
                    effect = "deny", domain = "permissionGroups", actions = "read",
                    scopeKind = "object", scopeId = fullAdmins.Id,
                },
            },
        });
        deny.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await deny.Content.ReadAsStringAsync()).ShouldContain("permission_group.deny_full_administrators");
    }

    [Fact]
    public async Task Preview_answers_for_somebody_else()
    {
        var response = await admin.PostAsJsonAsync("/api/v1/permission-groups/preview", new
        {
            subjectKind = "user",
            subjectId = viewerId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var domains = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("domains");

        // A regular account holds the catalogue reads and nothing over content.
        domains.GetProperty("mapLayers").GetString()!.ShouldContain("read");
        domains.GetProperty("features").GetString().ShouldBe("none");

        var asEditor = await admin.PostAsJsonAsync("/api/v1/permission-groups/preview", new
        {
            subjectKind = "user",
            subjectId = editorId,
        });
        (await asEditor.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("domains").GetProperty("features").GetString()!.ShouldContain("write");

        // Somebody with no rights at all cannot ask what anybody holds.
        (await editor.PostAsJsonAsync("/api/v1/permission-groups/preview", new
        {
            subjectKind = "user",
            subjectId = editorId,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preview_explains_each_verdict_and_names_the_deciding_ruleset()
    {
        // A ruleset that denies the viewer trip-log reads: the preview must not only show
        // the bit missing but say which rule took it — that is the whole point of looking
        // before saving a deny.
        var groupId = await CreatePermissionGroupAsync($"Preview Deny {suffix}");
        (await admin.PostAsJsonAsync($"/api/v1/permission-groups/{groupId}/members", new
        {
            memberKind = "user",
            memberId = viewerId,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new { effect = "deny", domain = "tripLogs", actions = "read", scopeKind = "all" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await admin.PostAsJsonAsync("/api/v1/permission-groups/preview", new
        {
            subjectKind = "user",
            subjectId = viewerId,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        var explanation = preview.GetProperty("explanations").EnumerateArray().First(e =>
            e.GetProperty("domain").GetString() == "tripLogs"
            && e.GetProperty("action").GetString() == "read");
        explanation.GetProperty("allowed").GetBoolean().ShouldBeFalse();
        explanation.GetProperty("source").GetString().ShouldBe("entries");
        explanation.GetProperty("level").GetString().ShouldBe("global");
        // The admin looking may read permission groups, so the anchor is named, not redacted.
        explanation.GetProperty("ruleName").GetString().ShouldBe($"Preview Deny {suffix}");
        explanation.GetProperty("redacted").GetBoolean().ShouldBeFalse();

        // The catalogue reads every account holds explain themselves the same way.
        var mapLayers = preview.GetProperty("explanations").EnumerateArray().First(e =>
            e.GetProperty("domain").GetString() == "mapLayers"
            && e.GetProperty("action").GetString() == "read");
        mapLayers.GetProperty("allowed").GetBoolean().ShouldBeTrue();
        mapLayers.GetProperty("source").GetString().ShouldBe("entries");
    }

    [Fact]
    public async Task Capabilities_describe_the_caller_and_nobody_else()
    {
        var mine = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/me/capabilities");
        var domains = mine.GetProperty("domains");
        domains.GetProperty("cavingGroups").GetString()!.ShouldContain("create");
        domains.GetProperty("features").GetString().ShouldBe("none");
        // A saved view is the caller's own workspace state, so every account keeps it.
        domains.GetProperty("mapViews").GetString()!.ShouldContain("create");

        var theirs = await editor.GetFromJsonAsync<JsonElement>("/api/v1/me/capabilities");
        theirs.GetProperty("domains").GetProperty("features").GetString()!.ShouldContain("create");

        // Stated rather than left to be guessed from a domain right that happens to correlate.
        // A handful of decisions turn on the installation-wide role directly — publishing a
        // photograph to the open internet is one — and a client inferring it would offer that
        // control to the wrong people.
        mine.GetProperty("isFullAdmin").GetBoolean().ShouldBeFalse();
        theirs.GetProperty("isFullAdmin").GetBoolean().ShouldBeFalse();

        var groups = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/me/permission-groups");
        groups.EnumerateArray()
            .Select(g => g.GetProperty("slug").GetString())
            .ShouldContain(SeededPermissionGroups.AllUsersSlug);
    }

    // ---- feature sets ----

    [Fact]
    public async Task Feature_sets_carry_membership_and_refuse_to_vanish_under_a_rule()
    {
        var created = await admin.PostAsJsonAsync("/api/v1/feature-sets/", new
        {
            name = $"Sensitive {suffix}",
            description = "caves that are not an area",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var setId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var caveId = await CreateCaveAsync(editor, $"Set Member {suffix}");
        (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{setId}/members", new { featureIds = new[] { caveId } }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var members = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/feature-sets/{setId}/members");
        members.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ShouldContain(caveId);

        // Membership moves access, so it leaves a trail like a rule edit does.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.AuditEntries.AnyAsync(a =>
                a.EntityType == nameof(FeatureSet) && a.EntityId == setId.ToString()
                && a.Action == AuditActions.PermissionChanged)).ShouldBeTrue();
        }

        // While a rule points at it, deleting it would cancel that rule silently.
        var groupId = await CreatePermissionGroupAsync($"Set Rule {suffix}");
        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new { effect = "deny", domain = "features", actions = "read", scopeKind = "featureSet", scopeId = setId },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var blocked = await admin.DeleteAsync($"/api/v1/feature-sets/{setId}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).ShouldContain("feature_set.in_use");

        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new { entries = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/api/v1/feature-sets/{setId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Set_membership_speaks_only_of_features_the_caller_may_read()
    {
        // Set membership moves access, and the ids in a replace resolve through the
        // caller's own visibility: an unreadable feature is answered as nonexistent
        // (never an existence oracle), and an unreadable member already in the set
        // survives a round-trip edit that could not have listed it. The delegated set
        // keeper here is a regular account — somebody trusted with sets, not with
        // reading everything.
        var openCave = await CreateCaveAsync(editor, $"Set Open {suffix}", "authenticated");
        var hiddenCave = await CreateCaveAsync(editor, $"Set Hidden {suffix}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var delegated = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = viewerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.FeatureSets,
            Actions = AccessAction.Read | AccessAction.Create | AccessAction.Write,
            ScopeKind = AccessScopeKind.All,
        };
        db.AccessEntries.Add(delegated);
        await db.SaveChangesAsync();
        try
        {
            var created = await viewer.PostAsJsonAsync("/api/v1/feature-sets/", new
            {
                name = $"Visible Only {suffix}",
                description = (string?)null,
            });
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var setId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            // A private cave of somebody else's reads as nonexistent.
            var probed = await viewer.PutAsJsonAsync($"/api/v1/feature-sets/{setId}/members", new
            {
                featureIds = new[] { openCave, hiddenCave },
            });
            probed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await probed.Content.ReadAsStringAsync()).ShouldContain("feature_set.feature_not_found");

            (await viewer.PutAsJsonAsync($"/api/v1/feature-sets/{setId}/members", new
            {
                featureIds = new[] { openCave },
            })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // A full administrator adds the hidden member; the keeper's members view
            // stays filtered to what they may read.
            (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{setId}/members", new
            {
                featureIds = new[] { openCave, hiddenCave },
            })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var mine = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/feature-sets/{setId}/members");
            mine.EnumerateArray().Select(m => m.GetProperty("id").GetGuid())
                .ShouldBe([openCave]);

            // The round-trip edit the keeper CAN express never drops what they cannot
            // see — dropping a member can cancel a deny that names the set.
            (await viewer.PutAsJsonAsync($"/api/v1/feature-sets/{setId}/members", new
            {
                featureIds = new[] { openCave },
            })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var all = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/feature-sets/{setId}/members");
            all.EnumerateArray().Select(m => m.GetProperty("id").GetGuid())
                .ShouldBe([openCave, hiddenCave], ignoreOrder: true);

            (await admin.PutAsJsonAsync($"/api/v1/feature-sets/{setId}/members", new { featureIds = Array.Empty<Guid>() }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await admin.DeleteAsync($"/api/v1/feature-sets/{setId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            db.AccessEntries.Remove(delegated);
            await db.SaveChangesAsync();
        }
    }

    // ---- rules written straight onto an object ----

    [Fact]
    public async Task Object_rules_carry_deny_and_subtree_reach()
    {
        var caveId = await CreateCaveAsync(editor, $"Object Rules {suffix}");

        // A one-off grant now gets everything the model has, starting with a deny.
        var written = await editor.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read", scopeKind = "subtree" },
            },
        });
        written.StatusCode.ShouldBe(HttpStatusCode.OK, await written.Content.ReadAsStringAsync());

        var listed = await editor.GetFromJsonAsync<JsonElement>($"/api/v1/objects/feature/{caveId}/access");
        listed.EnumerateArray().Count().ShouldBe(1);
        listed[0].GetProperty("scopeKind").GetString().ShouldBe("subtree");
        listed[0].GetProperty("subjectName").GetString().ShouldNotBeNullOrEmpty();

        // The subtree reach is real: the grant descends to the cave's entrance, which
        // the grantee holds no rule of its own on.
        var entranceId = await AddEntranceAsync(editor, caveId);
        var reached = await viewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/objects/feature/{entranceId}/effective-access");
        reached.GetProperty("actions").GetString()!.ShouldContain("read");

        // Only features contain other things, so only they take a subtree rule.
        var trip = await CreateTripLogAsync(editor, $"Object Rules Trip {suffix}", caveId,
            await RosterHelper.CaverIdForAsync(factory, editorId));
        var refused = await editor.PutAsJsonAsync($"/api/v1/objects/tripLog/{trip}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read", scopeKind = "subtree" },
            },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Owning a row is a reason to hold every action on it, and sharing it is one of them —
    /// on a trip, a saved view and every other row that is not a feature, exactly as it
    /// already was on features. Both halves are here because only the pair proves anything:
    /// the owner may share what is theirs, and somebody holding nothing over the same row
    /// still may not. The strangers are Viewers on purpose — the seeded editing group reads
    /// and writes every content domain at the widest scope, so an editor authoring on their
    /// own row would pass on that rule alone and prove nothing about ownership.
    /// </summary>
    [Fact]
    public async Task An_owner_may_grant_on_their_own_object_and_somebody_holding_nothing_may_not()
    {
        var strangerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"acc-api-str-{suffix}@t.local");
        using var stranger = await AuthHelper.BearerClientAsync(factory, $"acc-api-str-{suffix}@t.local");

        // Written straight to storage: a Viewer holds no right to author a trip, and what
        // is under test is that owning the row is by itself enough to share it.
        Guid tripId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trip = new TripLog
            {
                Title = $"Owned Trip {suffix}",
                TripDate = new DateOnly(2026, 1, 1),
                OwnerUserId = viewerId,
                Visibility = Visibility.Private,
            };
            db.TripLogs.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        // Nobody who neither owns the trip nor holds a rule over it may write rules on it,
        // and they are told it does not exist rather than that they are not allowed.
        var strangerAttempt = await stranger.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = strangerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        strangerAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await strangerAttempt.Content.ReadAsStringAsync()).ShouldContain(ObjectAccessEndpoints.NotFoundCode);

        // The owner holds no rule at all over trips — only the row itself — and that is
        // enough to share it.
        var granted = await viewer.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = strangerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());

        // The grant is real, not merely accepted.
        var readBack = await stranger.GetFromJsonAsync<JsonElement>($"/api/v1/trip-logs/{tripId}");
        readBack.GetProperty("id").GetGuid().ShouldBe(tripId);

        // …and reading it is not managing it: the grantee still cannot write rules there.
        (await stranger.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The same is true of every other kind that anchors a rule on itself rather than
        // on a feature. A saved view is the one a caller with no editing rights can author
        // outright, so it is proved end to end.
        var created = await stranger.PostAsJsonAsync("/api/v1/map-views/", new
        {
            name = $"Owned View {suffix}",
            description = (string?)null,
            config = new { },
            isHome = false,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var viewId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await viewer.PutAsJsonAsync($"/api/v1/objects/mapView/{viewId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var sharedView = await stranger.PutAsJsonAsync($"/api/v1/objects/mapView/{viewId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        sharedView.StatusCode.ShouldBe(HttpStatusCode.OK, await sharedView.Content.ReadAsStringAsync());
        var visibleViews = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/map-views/");
        visibleViews.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ShouldContain(viewId);
    }

    /// <summary>
    /// The other half of reading a row's own facts at a rule's anchor: its audience counts
    /// too, and only for reading. Somebody who may administer a trip everybody signed in
    /// can already read may pass that reading on — it confers nothing the grantee did not
    /// have — but the same audience is no reason to hand out writing, which is why both
    /// halves are asserted together.
    /// </summary>
    [Fact]
    public async Task An_audience_that_admits_a_reader_lets_a_delegate_pass_reading_on_and_nothing_more()
    {
        var delegateId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"acc-api-del-{suffix}@t.local");
        using var delegated = await AuthHelper.BearerClientAsync(factory, $"acc-api-del-{suffix}@t.local");

        Guid tripId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var trip = new TripLog
            {
                Title = $"Open Trip {suffix}",
                TripDate = new DateOnly(2026, 1, 1),
                OwnerUserId = viewerId,
                Visibility = Visibility.Authenticated,
            };
            db.TripLogs.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        // The delegate is given the administering of the trip and nothing else — no rule of
        // theirs mentions reading it, so what they may pass on comes from the trip itself.
        (await viewer.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = delegateId, effect = "allow", actions = "managePermissions", scopeKind = "object" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var passedOn = await delegated.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = delegateId, effect = "allow", actions = "managePermissions", scopeKind = "object" },
                new { subjectKind = "user", subjectId = editorId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        passedOn.StatusCode.ShouldBe(HttpStatusCode.OK, await passedOn.Content.ReadAsStringAsync());

        // Writing is not something an audience confers, whoever it admits.
        var overreach = await delegated.PutAsJsonAsync($"/api/v1/objects/tripLog/{tripId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = editorId, effect = "allow", actions = "read, write", scopeKind = "object" },
            },
        });
        overreach.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await overreach.Content.ReadAsStringAsync()).ShouldContain(AccessEntryRules.ExceedsOwnRightsCode);
    }

    /// <summary>
    /// The same for a document, which has no per-object tab of its own — a rule anchored on
    /// one is written in a ruleset — so the write-time gate is driven where both surfaces
    /// meet it. Owning the document is the whole of the author's claim here, and somebody
    /// who owns nothing and holds nothing is refused at the same gate: without the document's
    /// own columns in the facts, the owner is refused too, and the refusal reads as a
    /// permission bug rather than as an unbuilt fact.
    /// </summary>
    [Fact]
    public async Task A_documents_own_owner_may_author_a_rule_on_it_and_somebody_holding_nothing_may_not()
    {
        var strangerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"acc-api-dstr-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();

        var document = new Document
        {
            Title = $"Owned Document {suffix}",
            OwnerUserId = viewerId,
            Visibility = Visibility.Private,
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var proposed = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = strangerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = document.Id,
            GrantedBy = viewerId,
        };

        // The owner holds no rule at all over documents — a Viewer holds none — and owning
        // the row is by itself enough to share the reading of it.
        var owner = await RosterHelper.AccessContextOfAsync(db, viewerId);
        (await AccessEntryMapping.RejectAsync(db, access, owner, proposed, CancellationToken.None))
            .ShouldBeNull();

        // Somebody who neither owns it nor holds a rule reaching it may not.
        var stranger = await RosterHelper.AccessContextOfAsync(db, strangerId);
        var refused = await AccessEntryMapping.RejectAsync(db, access, stranger, proposed, CancellationToken.None);
        refused.ShouldNotBeNull();
        refused.ProblemDetails.Extensions["code"].ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);
    }

    /// <summary>
    /// A feature is readable when its own audience or anything above it admits the caller,
    /// so what an author may pass on at a rule's anchor has to be judged with the same
    /// cascade. A private entrance inside a cave everybody signed in may see is such a row:
    /// its delegate may share the reading of it — which discloses nothing, the cave already
    /// does — and still may not share writing it.
    /// </summary>
    [Fact]
    public async Task An_ancestors_audience_counts_at_a_features_anchor_too()
    {
        var delegateId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"acc-api-anc-{suffix}@t.local");
        using var delegated = await AuthHelper.BearerClientAsync(factory, $"acc-api-anc-{suffix}@t.local");

        var caveId = await CreateCaveAsync(editor, $"Open Cave {suffix}", "authenticated");
        var entranceId = await AddEntranceAsync(editor, caveId);

        // The entrance itself is private — only the cave above it is open — and the
        // delegate is given the administering of the entrance and nothing else.
        (await editor.PutAsJsonAsync($"/api/v1/objects/feature/{entranceId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = delegateId, effect = "allow", actions = "managePermissions", scopeKind = "object" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var passedOn = await delegated.PutAsJsonAsync($"/api/v1/objects/feature/{entranceId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = delegateId, effect = "allow", actions = "managePermissions", scopeKind = "object" },
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read", scopeKind = "object" },
            },
        });
        passedOn.StatusCode.ShouldBe(HttpStatusCode.OK, await passedOn.Content.ReadAsStringAsync());

        var overreach = await delegated.PutAsJsonAsync($"/api/v1/objects/feature/{entranceId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read, write", scopeKind = "object" },
            },
        });
        overreach.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await overreach.Content.ReadAsStringAsync()).ShouldContain(AccessEntryRules.ExceedsOwnRightsCode);
    }

    [Fact]
    public async Task Effective_access_explains_itself_and_withholds_what_it_must()
    {
        var caveId = await CreateCaveAsync(editor, $"Explain {suffix}");

        var mine = await editor.GetFromJsonAsync<JsonElement>(
            $"/api/v1/objects/feature/{caveId}/effective-access?explain=true");
        mine.GetProperty("actions").GetString()!.ShouldContain("write");

        var read = mine.GetProperty("explain").EnumerateArray()
            .First(e => e.GetProperty("action").GetString() == "read");
        read.GetProperty("allowed").GetBoolean().ShouldBeTrue();
        read.GetProperty("source").GetString().ShouldBeOneOf("ownership", "entries", "visibility");

        // The rule that grants an Editor this cave lives in a ruleset they may not read,
        // so the outcome is stated and the rule's name is withheld — even though the
        // answer is in their favour. Redaction is about the policy, not the verdict.
        var write = mine.GetProperty("explain").EnumerateArray()
            .First(e => e.GetProperty("action").GetString() == "write");
        write.GetProperty("allowed").GetBoolean().ShouldBeTrue();
        if (write.GetProperty("source").GetString() == "entries")
        {
            write.GetProperty("ruleName").ValueKind.ShouldBe(JsonValueKind.Null);
            write.GetProperty("redacted").GetBoolean().ShouldBeTrue();
        }

        // Somebody who may read the permission model gets the name instead.
        var asAdmin = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/objects/feature/{caveId}/effective-access?explain=true");
        asAdmin.GetProperty("explain").EnumerateArray()
            .ShouldAllBe(e => !e.GetProperty("redacted").GetBoolean());

        // Without the flag it is just the answer, no reasoning.
        var plain = await editor.GetFromJsonAsync<JsonElement>(
            $"/api/v1/objects/feature/{caveId}/effective-access");
        plain.TryGetProperty("explain", out var absent).ShouldBeTrue();
        absent.ValueKind.ShouldBe(JsonValueKind.Null);

        // Somebody who cannot read it is told it does not exist, not why.
        (await viewer.GetAsync($"/api/v1/objects/feature/{caveId}/effective-access"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_explanation_never_names_a_ruleset_the_caller_cannot_read()
    {
        var caveId = await CreateCaveAsync(editor, $"Redacted {suffix}");

        // A ruleset the editor may not read grants them the cave; the explanation must
        // report that a rule decided without naming the policy that did it.
        var groupId = await CreatePermissionGroupAsync($"Hidden Rules {suffix}");
        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new { effect = "deny", domain = "features", actions = "share", scopeKind = "all" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/v1/permission-groups/{groupId}/members", new
        {
            memberKind = "user",
            memberId = editorId,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var explained = await editor.GetFromJsonAsync<JsonElement>(
            $"/api/v1/objects/feature/{caveId}/effective-access?explain=true");
        var share = explained.GetProperty("explain").EnumerateArray()
            .First(e => e.GetProperty("action").GetString() == "share");

        share.GetProperty("allowed").GetBoolean().ShouldBeFalse();
        share.GetProperty("source").GetString().ShouldBe("entries");
        share.GetProperty("level").GetString().ShouldBe("global");
        // The outcome is stated plainly; only the name of the rule is withheld.
        share.GetProperty("ruleName").ValueKind.ShouldBe(JsonValueKind.Null);
        share.GetProperty("redacted").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_escape_hatch_trustee_list_is_full_administrator_business_only()
    {
        // Full Administrators holds no entries, so the entry-based no-amplification
        // bound sees nothing to object to — yet a trustee row there IS everything at
        // once. A delegated PermissionGroups·Write on that very group must not be a
        // ladder into it, in either direction.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var groups = await db.PermissionGroups.AsNoTracking()
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug
                || g.Slug == SeededPermissionGroups.AllUsersSlug)
            .ToDictionaryAsync(g => g.Slug, g => g.Id);
        var fullAdminsId = groups[SeededPermissionGroups.FullAdministratorsSlug];

        var delegated = new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = editorId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.PermissionGroups,
            Actions = AccessAction.Read | AccessAction.Write,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = fullAdminsId,
        };
        db.AccessEntries.Add(delegated);
        await db.SaveChangesAsync();
        try
        {
            var selfAppointed = await editor.PostAsJsonAsync(
                $"/api/v1/permission-groups/{fullAdminsId}/members", new
                {
                    memberKind = "user",
                    memberId = editorId,
                });
            selfAppointed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await selfAppointed.Content.ReadAsStringAsync())
                .ShouldContain(AccessEntryRules.ExceedsOwnRightsCode);

            // Stripping an administrator is a security-model rewrite too.
            var adminMemberId = await db.PermissionGroupMembers.AsNoTracking()
                .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User)
                .Select(m => m.MemberId)
                .FirstAsync();
            (await editor.DeleteAsync(
                    $"/api/v1/permission-groups/{fullAdminsId}/members/user/{adminMemberId}"))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // All Users has no membership rows at all — every account is implicit.
            (await admin.PostAsJsonAsync(
                    $"/api/v1/permission-groups/{groups[SeededPermissionGroups.AllUsersSlug]}/members", new
                    {
                        memberKind = "user",
                        memberId = editorId,
                    }))
                .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
        finally
        {
            db.AccessEntries.Remove(delegated);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_trustee_may_not_be_handed_rights_the_granter_lacks()
    {
        // A ruleset that grants more than an Editor holds…
        var groupId = await CreatePermissionGroupAsync($"Escalator {suffix}");
        (await admin.PutAsJsonAsync($"/api/v1/permission-groups/{groupId}/entries", new
        {
            entries = new[]
            {
                new { effect = "allow", domain = "users", actions = "read, write", scopeKind = "all" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // …cannot be edited by that Editor at all, since the domain is closed to them.
        (await editor.PostAsJsonAsync($"/api/v1/permission-groups/{groupId}/members", new
        {
            memberKind = "user",
            memberId = editorId,
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And on the per-object surface, a manager cannot hand out exact location they
        // do not themselves hold.
        var caveId = await CreateCaveAsync(editor, $"No Amplify {suffix}");
        (await editor.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read, managePermissions", scopeKind = "object" },
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var exceeds = await viewer.PutAsJsonAsync($"/api/v1/objects/feature/{caveId}/access", new
        {
            entries = new[]
            {
                new { subjectKind = "user", subjectId = viewerId, effect = "allow", actions = "read, delete", scopeKind = "object" },
            },
        });
        exceeds.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await exceeds.Content.ReadAsStringAsync()).ShouldContain(AccessEntryRules.ExceedsOwnRightsCode);
    }

    // ---- helpers ----

    private async Task<Guid> CreatePermissionGroupAsync(string name)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/permission-groups/", new
        {
            name,
            description = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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

    private async Task<Guid> AddEntranceAsync(HttpClient author, Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();

        var response = await author.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.44721, 45.53127 } },
            positionQuality = "Gps",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTripLogAsync(
        HttpClient author, string title, Guid caveId, Guid caverId)
    {
        var response = await author.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-01-01",
            caveIds = new[] { caveId },
            participants = new[] { new { caverId, kind = "participant" } },
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
