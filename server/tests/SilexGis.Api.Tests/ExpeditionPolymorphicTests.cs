// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.ResLinks;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// A camp as one of the things the shared machinery already handles: tagged, given an album,
/// joined into a relation, and shared per object. Each of those surfaces keeps its own list of
/// which entity kinds it accepts, so an entity kind added to the shared vocabulary reaches none
/// of them by itself — and two of the lists answer a kind they do not know by throwing, which a
/// caller receives as a fault rather than a refusal. Every one of them is driven here rather
/// than read, because an omission is invisible until somebody hits it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionPolymorphicTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient owner = null!;

    // A plain reader throughout: the seeded Editors group holds every content domain at the
    // widest scope, so an Editor who "cannot see" a camp proves nothing about visibility.
    private HttpClient outsider = null!;
    private Guid outsiderId;

    public ExpeditionPolymorphicTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-xpoly-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xp-own-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xp-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xp-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xp-out-{suffix}@t.local");
    }

    // ---- the attachment and tagging allow-set ------------------------------------------

    [Fact]
    public async Task A_camp_takes_tags_and_only_from_somebody_who_may_write_it()
    {
        var camp = await CreateCampAsync("Tagged camp");
        var tagName = $"camp-{Guid.NewGuid():N}"[..16];

        var tagged = await owner.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName,
            entityType = "expedition",
            entityId = camp,
        });
        tagged.StatusCode.ShouldBe(HttpStatusCode.Created, await tagged.Content.ReadAsStringAsync());

        var listed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/taggings/?entityType=expedition&entityId={camp}");
        listed.GetArrayLength().ShouldBe(1);
        listed[0].GetProperty("tag").GetProperty("name").GetString().ShouldBe(tagName);

        // The negative half, with the unreadable state built rather than assumed: this reader
        // holds nothing on this private camp, so it neither sees the tag nor may add one.
        var deniedList = await outsider.GetAsync($"/api/v1/taggings/?entityType=expedition&entityId={camp}");
        deniedList.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var deniedWrite = await outsider.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName = "not-yours",
            entityType = "expedition",
            entityId = camp,
        });
        deniedWrite.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_camps_files_are_listed_to_a_reader_of_the_camp_and_to_nobody_else()
    {
        var camp = await CreateCampAsync("Camp with files");

        // The listing is what proves the target parses and the read arm answers: an accepted
        // target with nothing on it is an empty list, an unreadable one is "not found", and an
        // entity kind the attachment surface does not accept never gets that far.
        var mine = await owner.GetAsync($"/api/v1/attachments/?entityType=expedition&entityId={camp}");
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());
        JsonDocument.Parse(await mine.Content.ReadAsStringAsync()).RootElement.GetArrayLength().ShouldBe(0);

        (await outsider.GetAsync($"/api/v1/attachments/?entityType=expedition&entityId={camp}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var unknownKind = await owner.GetAsync($"/api/v1/attachments/?entityType=album&entityId={camp}");
        unknownKind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The arm that actually hands over bytes. A stored file is reachable when something it hangs
    /// on is readable, and the camp is now one of those things — so a caller holding nothing but a
    /// rule on the camp gets the file, and a caller holding nothing at all does not. An empty
    /// listing on a camp with no attachments never enters that arm at all, which is why the file
    /// is real here and the grant is the only thing that moves between the two halves.
    /// </summary>
    [Fact]
    public async Task A_file_attached_to_a_camp_is_handed_over_on_the_camps_own_grant_and_not_otherwise()
    {
        var camp = await CreateCampAsync("Camp holding a file");
        var fileId = await UploadAsync("camp-notes.txt");
        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "expedition",
            entityId = camp,
            role = "other",
            sortOrder = 0,
        });
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());

        // The owner reaches it through the camp: one attachment, and the bytes behind it.
        var listed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=expedition&entityId={camp}");
        listed.GetArrayLength().ShouldBe(1);
        (await owner.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The unreadable state, built rather than assumed: this reader holds nothing on the camp
        // and the file hangs on nothing else, so the file is not theirs to have.
        (await outsider.GetAsync($"/api/v1/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // …and the grant on the camp alone is what turns that round. A build that had lost the
        // camp arm of the reach would leave this refused; one that had widened it to every camp
        // would have served it before the grant.
        await GrantAsync(camp, outsiderId, AccessAction.Read);
        var seen = await outsider.GetAsync($"/api/v1/files/{fileId}");
        seen.StatusCode.ShouldBe(HttpStatusCode.OK, await seen.Content.ReadAsStringAsync());
        var theirs = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments/?entityType=expedition&entityId={camp}");
        theirs.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task An_album_may_be_about_a_camp()
    {
        // Albums reuse the attachment surface's parser for their subject, so the camp becoming
        // an accepted attachment target is what makes "the photographs of this camp" expressible
        // at all. Nothing else was added for it, which is exactly why it is pinned here.
        var camp = await CreateCampAsync("Camp with an album");

        var created = await owner.PostAsJsonAsync("/api/v1/albums", new
        {
            title = "Camp pictures",
            description = (string?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            subjectEntityType = "expedition",
            subjectEntityId = camp,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        dto.GetProperty("subjectEntityType").GetString().ShouldBe("expedition");
        dto.GetProperty("subjectEntityId").GetGuid().ShouldBe(camp);

        // A subject kind the parser does not accept is refused with the shared code rather than
        // stored — the album surface has no allow-set of its own to disagree with.
        var refused = await owner.PostAsJsonAsync("/api/v1/albums", new
        {
            title = "About a comment",
            description = (string?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            subjectEntityType = "comment",
            subjectEntityId = camp,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("attachment.entity_type_unknown");
    }

    // ---- the linkable-type rule and its registered resolver -----------------------------

    [Fact]
    public async Task A_camp_joins_a_relation_and_is_displayed_by_its_own_resolver()
    {
        var camp = await CreateCampAsync("Linked camp");
        var trip = await CreateTripAsync("A trip the camp relates to");
        var relatedTo = await RelationIdAsync("related-to");

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = relatedTo,
            description = "The camp and one of the trips written up under it.",
            members = new[]
            {
                new { targetType = "expedition", targetId = camp, sortOrder = 0 },
                new { targetType = "tripLog", targetId = trip, sortOrder = 1 },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        // Declaring a kind linkable without registering a resolver for it is not caught at
        // startup — the directory is built per request from whatever was registered, so the
        // divergence surfaces as a failed request rather than a refused one. Reading the link
        // back is what asks the directory for the camp's resolver.
        var dto = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        var member = dto.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetType").GetString() == "expedition");
        member.GetProperty("display").GetProperty("title").GetString().ShouldBe("Linked camp");

        // Named *and* navigable, because the client carries this address. The pair is what is
        // being asserted: a resolver naming a route the application does not have would send the
        // reader to the router's error screen, so this expectation is what stops the route being
        // taken out of the client without the resolver following it.
        member.GetProperty("display").GetProperty("route").GetString().ShouldBe($"/expeditions/{camp}");
    }

    [Fact]
    public async Task The_picker_finds_a_camp_the_caller_may_read_and_not_one_they_may_not()
    {
        var mine = await CreateCampAsync("Picker camp Zorlentu");
        var hidden = await CreateCampAsync("Picker camp Zorlentu hidden");

        var found = await SearchTargetsAsync(owner, "Zorlentu");
        found.ShouldContain(mine);
        found.ShouldContain(hidden);

        // The unreadable state is built explicitly: this reader holds nothing on either camp,
        // then is granted read on one of them, and the feed moves accordingly. A search that
        // returned both would be the picker used to probe for camps nobody may see.
        (await SearchTargetsAsync(outsider, "Zorlentu")).ShouldBeEmpty();
        await GrantAsync(mine, outsiderId, AccessAction.Read);
        (await SearchTargetsAsync(outsider, "Zorlentu")).ShouldBe([mine]);
    }

    // Every linkable kind answering through a registered resolver — the divergence that would
    // otherwise reach a caller as a fault — is asserted once, over the rule itself, beside the
    // link surface's own tests. A second copy here would be a second place to keep it true.

    /// <summary>
    /// What a camp takes with it. The rows a camp carries as a polymorphic target — attachments,
    /// taggings, its place in a relation — point at it by kind and id with no foreign key to
    /// follow, so nothing removes them unless the delete path does, and left behind they are
    /// precisely what the integrity check calls an orphan. Driven by deleting a camp that carries
    /// one of each, rather than by constructing an orphan directly, because the delete path is the
    /// thing under test.
    /// </summary>
    [Fact]
    public async Task Deleting_a_camp_takes_the_rows_that_pointed_at_it()
    {
        var camp = await CreateCampAsync("Camp about to go");
        var trip = await CreateTripAsync("A trip the camp related to");
        var fileId = await UploadAsync("doomed.txt");

        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "expedition",
            entityId = camp,
            role = "other",
            sortOrder = 0,
        });
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());
        var tagged = await owner.PostAsJsonAsync("/api/v1/taggings/", new
        {
            tagName = $"gone-{Guid.NewGuid():N}"[..16],
            entityType = "expedition",
            entityId = camp,
        });
        tagged.StatusCode.ShouldBe(HttpStatusCode.Created, await tagged.Content.ReadAsStringAsync());
        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = await RelationIdAsync("related-to"),
            description = "The camp and a trip.",
            members = new[]
            {
                new { targetType = "expedition", targetId = camp, sortOrder = 0 },
                new { targetType = "tripLog", targetId = trip, sortOrder = 1 },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        // Fixture proof: the relation really exists on the trip's side before the camp goes, so
        // an empty answer afterwards is the delete path and not a link that never formed.
        (await LinkCountAsync("tripLog", trip)).ShouldBe(1);

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{camp}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        // A relation with one end left is a thing no surface offers and no later edit would be
        // accepted for, so it goes with the camp rather than hanging off the trip.
        (await LinkCountAsync("tripLog", trip)).ShouldBe(0);

        // Asked of the rows themselves, because the surfaces that list them stop at the camp:
        // once the camp is gone nothing asks about it again, and a row still pointing at it is
        // invisible until the integrity check finds it.
        (await RowsPointingAtAsync(camp)).ShouldBe((0, 0, 0));
    }

    /// <summary>
    /// A directed relation reads from its distinguished member, and a link left with two or more
    /// members and no main is a state the link rules refuse outright — so it would render on every
    /// surviving member's panel while every edit of it, including one appointing a new main, was
    /// rejected. Deleting the camp that carried the marker therefore takes the whole relation,
    /// however much of it is left, which the survivor count alone would not do.
    /// </summary>
    [Fact]
    public async Task Deleting_the_camp_a_directed_relation_reads_from_takes_the_relation()
    {
        var camp = await CreateCampAsync("Camp at the head");
        var first = await CreateTripAsync("First leg");
        var second = await CreateTripAsync("Second leg");

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = await RelationIdAsync("contains"),
            description = "The camp and the trips it contains.",
            members = new[]
            {
                new { targetType = "expedition", targetId = camp, isMain = true, sortOrder = 0 },
                new { targetType = "tripLog", targetId = first, isMain = false, sortOrder = 1 },
                new { targetType = "tripLog", targetId = second, isMain = false, sortOrder = 2 },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{camp}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        // Two members would survive a count-based sweep on their own; what condemns the link is
        // that the member carrying the marker was the one removed.
        (await LinkCountAsync("tripLog", first)).ShouldBe(0);
        (await LinkCountAsync("tripLog", second)).ShouldBe(0);
        (await RowsPointingAtAsync(camp)).ShouldBe((0, 0, 0));
    }

    // ---- the object-permissions surface -------------------------------------------------

    [Fact]
    public async Task A_camp_is_shared_on_its_own_page_and_the_route_names_it()
    {
        var camp = await CreateCampAsync("Shared camp");

        // Before the grant: nothing. The route answers "not found" rather than "forbidden",
        // because a caller who may not read the camp learns nothing from asking about it.
        (await outsider.GetAsync($"/api/v1/objects/expedition/{camp}/access"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var replaced = await owner.PutAsJsonAsync($"/api/v1/objects/expedition/{camp}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());

        var rules = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/objects/expedition/{camp}/access");
        rules.GetArrayLength().ShouldBe(1);
        rules[0].GetProperty("subjectId").GetGuid().ShouldBe(outsiderId);

        // The grant is real: the reader may now read the camp and says so through the same route.
        var effective = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/v1/objects/expedition/{camp}/effective-access");
        (effective.GetProperty("actions").GetString() ?? string.Empty).ShouldContain("Read");
        (await outsider.GetAsync($"/api/v1/expeditions/{camp}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Route names are case-insensitive, as they are for every other kind, because a client
        // reads the camelCase name out of a payload and puts it back into a URL.
        (await owner.GetAsync($"/api/v1/objects/EXPEDITION/{camp}/access"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The person told about the grant is given somewhere to go, and it is the camp's own
        // page. A notification carrying an address this application does not answer is worse than
        // one carrying none: it reads as a working link and lands on the router's error screen.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var placeholders = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == outsiderId && n.Category == NotificationCategory.PermissionGranted)
            .OrderByDescending(n => n.Id)
            .Select(n => n.Placeholders)
            .FirstAsync();
        JsonDocument.Parse(placeholders).RootElement
            .GetProperty("url").GetString().ShouldBe($"/expeditions/{camp}");
    }

    [Fact]
    public async Task A_rule_on_a_camp_reaches_only_that_camp()
    {
        var shared = await CreateCampAsync("The one shared");
        var other = await CreateCampAsync("The one not shared");
        await GrantAsync(shared, outsiderId, AccessAction.Read);

        (await outsider.GetAsync($"/api/v1/expeditions/{shared}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/expeditions/{other}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Only features contain other objects, so a camp takes no subtree rule — the refusal is
        // the validator's, not an accident of the domain having no hierarchy to walk.
        var subtree = await owner.PutAsJsonAsync($"/api/v1/objects/expedition/{shared}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "subtree",
                },
            },
        });
        subtree.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- helpers -------------------------------------------------------------------------

    private async Task<Guid> CreateCampAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name,
            description = (string?)null,
            startDate = "2026-07-01",
            endDate = (string?)null,
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            visibility = "private",
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadAsync(string fileName)
    {
        var content = new ByteArrayContent("a camp's own paperwork"u8.ToArray());
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>How many relations a given target is a member of, asked the way a panel asks.</summary>
    private async Task<int> LinkCountAsync(string targetType, Guid targetId)
    {
        var response = await owner.GetAsync($"/api/v1/reslinks/for-target?type={targetType}&id={targetId}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("totalItems").GetInt32();
    }

    /// <summary>
    /// How many attachment, tagging and relation-membership rows still name the given camp —
    /// the three kinds that point at it by kind and id with no foreign key to follow, which is
    /// exactly why nothing removes them unless the delete path does. Counted over this camp
    /// rather than asked of the integrity verifier, whose answer is installation-wide.
    /// </summary>
    private async Task<(int Attachments, int Taggings, int LinkMembers)> RowsPointingAtAsync(Guid campId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (
            await db.Attachments.AsNoTracking().CountAsync(
                a => a.EntityType == AttachedEntityType.Expedition && a.EntityId == campId),
            await db.Taggings.AsNoTracking().CountAsync(
                t => t.EntityType == AttachedEntityType.Expedition && t.EntityId == campId),
            await db.ResLinkMembers.AsNoTracking().CountAsync(
                m => m.EntityType == AttachedEntityType.Expedition && m.EntityId == campId));
    }

    private async Task<List<Guid>> SearchTargetsAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/reslinks/targets/search?type=expedition&q={query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())];
    }

    private async Task<long> RelationIdAsync(string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkRelationTypes.AsNoTracking()
            .Where(t => t.Code == code)
            .Select(t => t.Id)
            .FirstAsync();
    }

    /// <summary>A rule on one camp, written straight to the table — the shape the route writes.</summary>
    private async Task GrantAsync(Guid campId, Guid userId, AccessAction actions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Expeditions,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = campId,
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
