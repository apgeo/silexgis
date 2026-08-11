// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
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
/// Resource links over HTTP: n-ary associations with identity, a permalink short code,
/// anchors into parts of targets, and a relation vocabulary. The suite pins the two load-
/// bearing security facts — a link never grants access (an unreadable target's member
/// appears with no display data, no anchor payload, no route), and authoring floors are
/// the target worlds' own rules (Read to add a member, feature Create to mint a GPS
/// point) — plus the write-path laws: anchor kinds per target type, payload shapes, the
/// single-main marker of directed relations, the one-member floor, and the seeded
/// vocabulary's immutability.
///
/// Every negative asserts its matching positive in the same test, so a fixture that
/// quietly stopped working cannot pass for a passing refusal.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ResLinkApiTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — authors most links here
    private HttpClient editor2 = null!; // Editor, not the creator — reads everything, edits nothing of others'
    private HttpClient viewer = null!;  // Viewer — sees only what visibility opens, cannot create features
    private HttpClient admin = null!;   // Admin — Full Administrators member
    private HttpClient keeper = null!;  // Manager — roster keeper, writes caver entries
    private Guid ownerId;
    private Guid viewerId;
    private long caveTypeId;
    private long genericTypeId;
    private long entranceTypeId;

    public ResLinkApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-reslinks-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rl-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"rl-ed2-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"rl-view-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"rl-adm-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"rl-keep-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            genericTypeId = await db.FeatureTypes.Where(t => t.Code == "generic").Select(t => t.Id).SingleAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"rl-own-{suffix}@t.local");
        editor2 = await AuthHelper.BearerClientAsync(factory, $"rl-ed2-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"rl-view-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"rl-adm-{suffix}@t.local");
        keeper = await AuthHelper.BearerClientAsync(factory, $"rl-keep-{suffix}@t.local");
    }

    // ---- lifecycle and the short code ------------------------------------------------

    [Fact]
    public async Task A_link_lives_by_id_and_by_short_code_and_dies_with_both()
    {
        var caveA = await CreateCaveAsync(owner, "Link Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Link Cave B", "authenticated");
        var relatedTo = await RelationIdAsync("related-to");

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = relatedTo,
            description = "Two chambers of one system, probably.",
            members = new[] { Member("feature", caveA), Member("feature", caveB, sortOrder: 1) },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = await ReadJsonAsync(created);
        var id = dto.GetProperty("id").GetGuid();
        var shortCode = dto.GetProperty("shortCode").GetString()!;
        shortCode.Length.ShouldBe(8);
        shortCode.All(char.IsAsciiLetterOrDigit).ShouldBeTrue(shortCode);
        dto.GetProperty("members").GetArrayLength().ShouldBe(2);
        dto.GetProperty("relationType").GetProperty("code").GetString().ShouldBe("related-to");
        dto.GetProperty("relationType").GetProperty("seeded").GetBoolean().ShouldBeTrue();

        // The same row answers under both addresses, told apart by shape alone.
        (await ReadJsonAsync(await owner.GetAsync($"/api/v1/reslinks/{id}")))
            .GetProperty("shortCode").GetString().ShouldBe(shortCode);
        (await ReadJsonAsync(await owner.GetAsync($"/api/v1/reslinks/{shortCode}")))
            .GetProperty("id").GetGuid().ShouldBe(id);

        // Neither shape: unresolvable. Valid shape, no row: same answer.
        var oddShape = await owner.GetAsync("/api/v1/reslinks/not-a-code");
        oddShape.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(oddShape)).ShouldBe("reslink.code.unresolved");
        var missingId = await owner.GetAsync($"/api/v1/reslinks/{Guid.NewGuid()}");
        missingId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(missingId)).ShouldBe("reslink.not_found");

        var patched = await owner.PatchAsJsonAsync($"/api/v1/reslinks/{id}", new
        {
            description = "Confirmed by the 2019 dye trace.",
            relationTypeId = (long?)null,
            mainMemberId = (Guid?)null,
        });
        patched.StatusCode.ShouldBe(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());
        var patchedDto = await ReadJsonAsync(patched);
        patchedDto.GetProperty("description").GetString().ShouldBe("Confirmed by the 2019 dye trace.");
        patchedDto.GetProperty("relationType").ValueKind.ShouldBe(JsonValueKind.Null);

        (await owner.DeleteAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/reslinks/{shortCode}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Every_reslink_endpoint_requires_a_signed_in_caller()
    {
        using var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={id}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/reslinks/targets/search?type=feature&q=x"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/reslinks", new { members = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PatchAsJsonAsync($"/api/v1/reslinks/{id}", new { description = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/reslinks/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/reslinks/{id}/members", Member("feature", id)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PatchAsJsonAsync($"/api/v1/reslinks/{id}/members/{id}", new { isMain = false, sortOrder = 0 }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/reslinks/{id}/members/{id}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/reslinks/relation-types"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/reslinks/relation-types", new { code = "x", name = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PatchAsJsonAsync("/api/v1/reslinks/relation-types/1", new { code = "x", name = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync("/api/v1/reslinks/relation-types/1"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- member validity -------------------------------------------------------------

    [Fact]
    public async Task Only_linkable_target_types_join_and_only_they_are_queried()
    {
        var caveId = await CreateCaveAsync(owner, "Vocabulary Cave", "authenticated");

        // Files are addressed by anchor on their document, raster maps have no resolver
        // yet, comments do not exist yet, and nonsense is nonsense — all refused alike.
        foreach (var refusedType in new[] { "storedFile", "georeferencedMap", "comment", "bogus" })
        {
            var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
            {
                relationTypeId = (long?)null,
                description = (string?)null,
                members = new[] { Member("feature", caveId), Member(refusedType, Guid.NewGuid(), sortOrder: 1) },
            });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusedType);
            (await ReadCodeAsync(response)).ShouldBe("reslink.member.type_not_linkable", refusedType);

            var forTarget = await owner.GetAsync(
                $"/api/v1/reslinks/for-target?type={refusedType}&id={Guid.NewGuid()}");
            forTarget.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusedType);
            (await ReadCodeAsync(forTarget)).ShouldBe("reslink.entity_type_unknown", refusedType);

            var search = await owner.GetAsync($"/api/v1/reslinks/targets/search?type={refusedType}&q=x");
            search.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refusedType);
            (await ReadCodeAsync(search)).ShouldBe("reslink.entity_type_unknown", refusedType);
        }

        // The refusals above were about the type: the same request against a linkable
        // one goes through.
        (await CreateLinkAsync(owner, Member("feature", caveId))).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Anchor_kinds_and_payloads_answer_to_the_targets_type()
    {
        var caveId = await CreateCaveAsync(owner, "Anchor Cave", "authenticated");
        var cabinetId = await CreateCabinetAsync($"Anchors {Guid.NewGuid():N}"[..24]);
        var (documentId, _) = await UploadDocumentAsync("anchored.txt");

        // A part-anchor into something that has no addressable parts.
        await ShouldRefuseMemberAsync(
            Member("feature", caveId, anchorKind: "page", anchor: new { page = 2 }),
            "reslink.member.invalid_anchor_kind");
        await ShouldRefuseMemberAsync(
            Member("cabinet", cabinetId, anchorKind: "textRange", anchor: new { start = 0, end = 3, quote = "abc" }),
            "reslink.member.invalid_anchor_kind");

        // Kind admitted, payload malformed — each a different law of its shape.
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "page", anchor: new { page = 0 }),
            "reslink.member.invalid_anchor");
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "pageRange", anchor: new { fromPage = 3, toPage = 2 }),
            "reslink.member.invalid_anchor");
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "textRange", anchor: new { start = 0, end = 8 }),
            "reslink.member.invalid_anchor");
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "timeRange", anchor: new { start = 4.0, end = 4.0 }),
            "reslink.member.invalid_anchor");

        // A whole-resource anchor carries no payload — one with a payload is claiming to
        // be something it is not.
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "whole", anchor: new { page = 1 }),
            "reslink.member.invalid_anchor");

        // The kinds themselves were fine: the same shapes, well formed, are accepted.
        var linkId = await CreateLinkAsync(
            owner,
            Member("document", documentId, anchorKind: "page", anchor: new { page = 3 }),
            Member("feature", caveId, sortOrder: 1));
        linkId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task The_same_whole_target_joins_a_link_once_but_parts_may_repeat()
    {
        var caveId = await CreateCaveAsync(owner, "Duplicate Cave", "authenticated");
        var (documentId, _) = await UploadDocumentAsync("parts.txt");

        var duplicated = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[] { Member("feature", caveId), Member("feature", caveId, sortOrder: 1) },
        });
        duplicated.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(duplicated)).ShouldBe("reslink.member.duplicate_whole");

        var linkId = await CreateLinkAsync(owner, Member("feature", caveId));
        var again = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", caveId));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(again)).ShouldBe("reslink.member.duplicate_whole");

        // Only the whole-target duplicate is a restatement: the same document may sit in
        // one link as a whole and again through a part of itself.
        var wholeAndPart = await CreateLinkAsync(
            owner,
            Member("document", documentId),
            Member("document", documentId, sortOrder: 1, anchorKind: "page", anchor: new { page = 2 }));
        wholeAndPart.ShouldNotBe(Guid.Empty);
    }

    // ---- the main marker -------------------------------------------------------------

    [Fact]
    public async Task The_main_marker_exists_exactly_where_the_relation_directs()
    {
        var caveA = await CreateCaveAsync(owner, "Main Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Main Cave B", "authenticated");
        var contains = await RelationIdAsync("contains");
        var relatedTo = await RelationIdAsync("related-to");

        // No distinguished side to read from: undirected and untyped links refuse the marker.
        await ShouldRefuseCreateAsync(relatedTo, "reslink.main.not_allowed_for_relation",
            Member("feature", caveA, isMain: true), Member("feature", caveB, sortOrder: 1));
        await ShouldRefuseCreateAsync(null, "reslink.main.not_allowed_for_relation",
            Member("feature", caveA, isMain: true), Member("feature", caveB, sortOrder: 1));

        // A one-member directed link has nothing to read towards.
        await ShouldRefuseCreateAsync(contains, "reslink.main.not_allowed_for_relation",
            Member("feature", caveA, isMain: true));

        // Two or more members under a directed relation: exactly one main, not none, not two.
        await ShouldRefuseCreateAsync(contains, "reslink.main.required",
            Member("feature", caveA), Member("feature", caveB, sortOrder: 1));
        await ShouldRefuseCreateAsync(contains, "reslink.main.not_single",
            Member("feature", caveA, isMain: true), Member("feature", caveB, isMain: true, sortOrder: 1));

        var accepted = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = contains,
            description = (string?)null,
            members = new[] { Member("feature", caveA, isMain: true), Member("feature", caveB, sortOrder: 1) },
        });
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_main_marker_moves_by_explicit_hand_and_clears_when_meaningless()
    {
        var caveA = await CreateCaveAsync(owner, "Marker Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Marker Cave B", "authenticated");
        var caveC = await CreateCaveAsync(owner, "Marker Cave C", "authenticated");
        var contains = await RelationIdAsync("contains");
        var relatedTo = await RelationIdAsync("related-to");

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = contains,
            description = (string?)null,
            members = new[] { Member("feature", caveA, isMain: true), Member("feature", caveB, sortOrder: 1) },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var linkId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var memberA = await MemberIdOfAsync(linkId, caveA);
        var memberB = await MemberIdOfAsync(linkId, caveB);

        // Promoting hands the marker over in the same act — there is never a moment with
        // two mains or none.
        (await owner.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}/members/{memberB}",
            new { isMain = true, sortOrder = 1, note = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MainTargetIdsAsync(linkId)).ShouldBe([caveB]);

        // An undirected relation has no side to distinguish: switching clears the marker.
        (await owner.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}", new
        {
            description = (string?)null,
            relationTypeId = relatedTo,
            mainMemberId = (Guid?)null,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MainTargetIdsAsync(linkId)).ShouldBeEmpty();

        // Going back to a directed relation needs a main named in the same act — the
        // markerless link cannot become directed on its own.
        var missingMain = await owner.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}", new
        {
            description = (string?)null,
            relationTypeId = contains,
            mainMemberId = (Guid?)null,
        });
        missingMain.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(missingMain)).ShouldBe("reslink.main.required");

        (await owner.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}", new
        {
            description = (string?)null,
            relationTypeId = contains,
            mainMemberId = (Guid?)memberA,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MainTargetIdsAsync(linkId)).ShouldBe([caveA]);

        // Adding a member as main demotes the incumbent in the same transaction.
        (await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members",
            Member("feature", caveC, isMain: true, sortOrder: 2)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await MainTargetIdsAsync(linkId)).ShouldBe([caveC]);
    }

    [Fact]
    public async Task The_last_member_stays_and_the_main_of_a_directed_link_needs_a_successor()
    {
        var caveA = await CreateCaveAsync(owner, "Floor Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Floor Cave B", "authenticated");
        var caveC = await CreateCaveAsync(owner, "Floor Cave C", "authenticated");
        var contains = await RelationIdAsync("contains");

        // A one-member link: the member is not removable, the link is.
        var single = await CreateLinkAsync(owner, Member("feature", caveA));
        var lastMember = await MemberIdOfAsync(single, caveA);
        var refused = await owner.DeleteAsync($"/api/v1/reslinks/{single}/members/{lastMember}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(refused)).ShouldBe("reslink.member.last");
        (await owner.DeleteAsync($"/api/v1/reslinks/{single}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = contains,
            description = (string?)null,
            members = new[]
            {
                Member("feature", caveA, isMain: true),
                Member("feature", caveB, sortOrder: 1),
                Member("feature", caveC, sortOrder: 2),
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var linkId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        // Removing the main while two others would remain leaves a directed link with no
        // side to read from — promote first.
        var mainGone = await owner.DeleteAsync(
            $"/api/v1/reslinks/{linkId}/members/{await MemberIdOfAsync(linkId, caveA)}");
        mainGone.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(mainGone)).ShouldBe("reslink.main.required");

        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{await MemberIdOfAsync(linkId, caveB)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MainTargetIdsAsync(linkId)).ShouldBe([caveA]);

        // Dropping to one member makes the marker meaningless, so it comes off with the
        // removal that got the link there.
        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{await MemberIdOfAsync(linkId, caveC)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var remaining = (await GetLinkAsync(linkId)).GetProperty("members");
        remaining.GetArrayLength().ShouldBe(1);
        remaining[0].GetProperty("isMain").GetBoolean().ShouldBeFalse();
    }

    // ---- who may edit ----------------------------------------------------------------

    [Fact]
    public async Task Editing_a_link_belongs_to_its_creator_and_to_administrators()
    {
        var caveA = await CreateCaveAsync(owner, "Owned Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Owned Cave B", "authenticated");
        var linkId = await CreateLinkAsync(owner, Member("feature", caveA), Member("feature", caveB, sortOrder: 1));
        var memberId = await MemberIdOfAsync(linkId, caveB);

        // Another editor reads the link like anyone signed in — and edits nothing of it.
        (await editor2.GetAsync($"/api/v1/reslinks/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor2.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}",
            new { description = "not mine", relationTypeId = (long?)null, mainMemberId = (Guid?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor2.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", Member("feature", caveA, sortOrder: 2)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor2.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}/members/{memberId}",
            new { isMain = false, sortOrder = 9, note = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor2.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{memberId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor2.DeleteAsync($"/api/v1/reslinks/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A full administrator holds what the stranger did not; the same acts go through.
        (await admin.PatchAsJsonAsync($"/api/v1/reslinks/{linkId}",
            new { description = "curated", relationTypeId = (long?)null, mainMemberId = (Guid?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---- visibility ------------------------------------------------------------------

    [Fact]
    public async Task An_unreadable_target_travels_as_a_bare_member_and_gates_its_own_panel()
    {
        var openCave = await CreateCaveAsync(owner, "Open Panel Cave", "authenticated");
        var hidden = await CreateGenericFeatureAsync(owner, "Hidden Spring", "private");
        var linkId = await CreateLinkAsync(
            owner, Member("feature", openCave), Member("feature", hidden, sortOrder: 1));
        var second = await CreateLinkAsync(owner, Member("feature", openCave));
        second.ShouldNotBe(Guid.Empty);

        // The owner sees both members whole.
        var mine = (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray().ToList();
        mine.Count.ShouldBe(2);
        mine.ShouldAllBe(m => m.GetProperty("display").ValueKind == JsonValueKind.Object);

        // The viewer sees both members — the association is not a secret — but nothing of
        // the target they may not read: no title, no route, no anchor.
        var theirs = (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray().ToList();
        theirs.Count.ShouldBe(2);
        var visible = theirs.Single(m => m.GetProperty("targetId").GetGuid() == openCave);
        visible.GetProperty("display").GetProperty("title").GetString().ShouldNotBeNullOrEmpty();
        var withheld = theirs.Single(m => m.GetProperty("targetId").GetGuid() == hidden);
        withheld.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        withheld.GetProperty("anchor").ValueKind.ShouldBe(JsonValueKind.Null);

        // The panel on a readable target lists every incident link; its total is the badge.
        var panel = await viewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/for-target?type=feature&id={openCave}");
        panel.GetProperty("totalItems").GetInt32().ShouldBe(2);

        // The panel on the unreadable target answers as if the target were not there —
        // while its owner gets the listing.
        var gated = await viewer.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={hidden}");
        gated.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(gated)).ShouldBe("reslink.target_not_found");
        var owned = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/for-target?type=feature&id={hidden}");
        owned.GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_document_members_anchor_is_withheld_with_its_target()
    {
        var (documentId, fileId) = await UploadDocumentAsync("cited.txt");
        var caveId = await CreateCaveAsync(owner, "Citing Cave", "authenticated");

        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[]
            {
                Member("feature", caveId),
                Member("document", documentId, sortOrder: 1, anchorKind: "textRange",
                    anchor: new { start = 0, end = 8, quote = "contents" }, anchorFileId: fileId),
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var linkId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        // The uploader reads the anchor as authored, measured against the pinned file.
        var mine = (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetType").GetString() == "document");
        mine.GetProperty("anchor").GetProperty("quote").GetString().ShouldBe("contents");
        mine.GetProperty("anchorFileId").GetGuid().ShouldBe(fileId);
        mine.GetProperty("anchorState").GetString().ShouldBe("exact");

        // The document is private: for the viewer the member stays, everything of the
        // target goes — the payload too, because a payload quotes what it anchors to.
        var theirs = (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetType").GetString() == "document");
        theirs.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("anchor").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("anchorFileId").ValueKind.ShouldBe(JsonValueKind.Null);

        // The document's own panel obeys the same gate as the document.
        (await viewer.GetAsync($"/api/v1/reslinks/for-target?type=document&id={documentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var panel = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/for-target?type=document&id={documentId}");
        panel.GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_measured_against_pin_names_a_file_of_the_target_document_or_nothing()
    {
        var (documentId, fileId) = await UploadDocumentAsync("pinned.txt");
        var (_, foreignFileId) = await UploadDocumentAsync("other.txt");
        var textRange = new { start = 0, end = 8, quote = "contents" };

        // A pin on a whole-resource member asserts a provenance the anchor does not have.
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorFileId: fileId),
            "reslink.member.anchor_file_invalid");

        // A pin naming another document's file is not what this anchor was measured against.
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "textRange", anchor: textRange,
                anchorFileId: foreignFileId),
            "reslink.member.anchor_file_invalid");

        // An image region is measured in pixels of a specific file: without the pin the
        // coordinates have no space to live in, and a later version would silently
        // reinterpret them — so the pin is required, not just permitted.
        var region = new { shape = "rect", x = 10, y = 10, w = 40, h = 20 };
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "imageRegion", anchor: region),
            "reslink.member.anchor_pin_required");

        // The right pin on the right shapes goes through.
        var linkId = await CreateLinkAsync(
            owner,
            Member("document", documentId, anchorKind: "textRange", anchor: textRange, anchorFileId: fileId),
            Member("document", documentId, sortOrder: 1, anchorKind: "imageRegion", anchor: region,
                anchorFileId: fileId));
        linkId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_pin_probe_on_an_unreadable_document_refuses_like_a_missing_target()
    {
        var (documentId, fileId) = await UploadDocumentAsync("probed.txt");
        var (_, foreignFileId) = await UploadDocumentAsync("elsewhere.txt");
        var textRange = new { start = 0, end = 8, quote = "contents" };

        // The documents are private to the owner. Whether the named file belongs to the
        // named document is a fact about the document, so for a caller without read the
        // refusal must have one shape whichever way that fact falls — otherwise the
        // create endpoint is an oracle for file-to-document association.
        foreach (var pin in new[] { fileId, foreignFileId })
        {
            var probed = await viewer.PostAsJsonAsync("/api/v1/reslinks", new
            {
                relationTypeId = (long?)null,
                description = (string?)null,
                members = new[]
                {
                    Member("document", documentId, anchorKind: "textRange", anchor: textRange,
                        anchorFileId: pin),
                },
            });
            probed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadCodeAsync(probed)).ShouldBe("reslink.member.target_not_found");
        }

        // The same probe through member-add refuses identically.
        var viewerCave = await CreateCaveAsync(owner, "Probe Cave", "authenticated");
        var viewerLink = await CreateLinkAsync(viewer, Member("feature", viewerCave));
        var added = await viewer.PostAsJsonAsync($"/api/v1/reslinks/{viewerLink}/members",
            Member("document", documentId, sortOrder: 1, anchorKind: "textRange", anchor: textRange,
                anchorFileId: fileId));
        added.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(added)).ShouldBe("reslink.member.target_not_found");

        // The distinction the viewer never saw exists for a reader: right pin creates,
        // foreign pin is the pin refusal — so the floor, not the check, did the hiding.
        (await CreateLinkAsync(owner,
            Member("document", documentId, anchorKind: "textRange", anchor: textRange, anchorFileId: fileId)))
            .ShouldNotBe(Guid.Empty);
        await ShouldRefuseMemberAsync(
            Member("document", documentId, anchorKind: "textRange", anchor: textRange,
                anchorFileId: foreignFileId),
            "reslink.member.anchor_file_invalid");
    }

    [Fact]
    public async Task A_superseded_pin_degrades_for_readers_and_says_nothing_to_others()
    {
        var (documentId, v1FileId) = await UploadDocumentAsync("versioned.txt");
        var caveId = await CreateCaveAsync(owner, "Version Cave", "authenticated");
        var linkId = await CreateLinkAsync(
            owner,
            Member("feature", caveId),
            Member("document", documentId, sortOrder: 1, anchorKind: "textRange",
                anchor: new { start = 0, end = 8, quote = "contents" }, anchorFileId: v1FileId));

        // Fresh pin: the anchor addresses what the document currently serves.
        (await DocumentMemberAsync(owner, linkId)).GetProperty("anchorState").GetString().ShouldBe("exact");

        // A new version supersedes the pinned file: the anchor still describes v1
        // exactly, so against current content it is degraded — never silently re-aimed.
        var v2FileId = await UploadVersionAsync(v1FileId, "versioned.txt", "reworded entirely"u8.ToArray());
        var stale = await DocumentMemberAsync(owner, linkId);
        stale.GetProperty("anchorState").GetString().ShouldBe("degraded");
        stale.GetProperty("anchorFileId").GetGuid().ShouldBe(v1FileId);

        // A member measured against the current file stays exact amid the churn.
        (await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members",
            Member("document", documentId, sortOrder: 2, anchorKind: "textRange",
                anchor: new { start = 0, end = 8, quote = "reworded" }, anchorFileId: v2FileId)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("anchorFileId").ValueKind == JsonValueKind.String
                && m.GetProperty("anchorFileId").GetGuid() == v2FileId)
            .GetProperty("anchorState").GetString().ShouldBe("exact");

        // The viewer may not read the document, so nothing of the anchor travels — the
        // state included: that the document was re-versioned is a fact about it.
        var theirs = await DocumentMemberAsync(viewer, linkId);
        theirs.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("anchorFileId").ValueKind.ShouldBe(JsonValueKind.Null);
        theirs.GetProperty("anchorState").GetString().ShouldBe("exact");
    }

    [Fact]
    public async Task Creating_a_link_takes_read_on_every_member_target()
    {
        var openCave = await CreateCaveAsync(owner, "Readable Cave", "authenticated");
        var hidden = await CreateGenericFeatureAsync(owner, "Unreadable Spring", "private");

        // A missing target and an unreadable one refuse identically — the link surface is
        // never an oracle for what exists.
        var refused = await viewer.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[] { Member("feature", openCave), Member("feature", hidden, sortOrder: 1) },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("reslink.member.target_not_found");

        // The viewer authors happily about what they can see.
        var viewerLink = await CreateLinkAsync(viewer, Member("feature", openCave));

        // The same floor holds when adding to an existing link.
        var late = await viewer.PostAsJsonAsync(
            $"/api/v1/reslinks/{viewerLink}/members", Member("feature", hidden, sortOrder: 1));
        late.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(late)).ShouldBe("reslink.member.target_not_found");
    }

    [Fact]
    public async Task Target_search_serves_only_what_the_caller_may_read()
    {
        var token = $"Srch{Guid.NewGuid():N}"[..12];
        var openId = await CreateGenericFeatureAsync(owner, $"{token} open", "authenticated");
        var secretId = await CreateGenericFeatureAsync(owner, $"{token} secret", "private");

        var forViewer = await viewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/targets/search?type=feature&q={token}");
        var viewerIds = forViewer.EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).ToList();
        viewerIds.ShouldBe([openId]);
        viewerIds.ShouldNotContain(secretId);

        // The owner finds both, so the thinning above was visibility, not the query.
        var forOwner = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/targets/search?type=feature&q={token}");
        forOwner.EnumerateArray().Select(h => h.GetProperty("id").GetGuid())
            .ShouldBe([openId, secretId], ignoreOrder: true);

        // No query, no feed — the picker never dumps a domain.
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/targets/search?type=feature"))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Trip_log_map_view_and_geofile_targets_answer_under_their_own_visibility()
    {
        var token = $"Rlv{Guid.NewGuid():N}"[..12];
        var caveId = await CreateCaveAsync(owner, "Resolver Cave", "authenticated");

        var openTrip = await CreateTripLogAsync($"{token} surveyed", caveId, "authenticated");
        var hiddenTrip = await CreateTripLogAsync($"{token} scouted", caveId, "private");
        var openView = await CreateMapViewAsync($"{token} overview", "authenticated");
        var hiddenView = await CreateMapViewAsync($"{token} drafts", "private");
        var openGeofile = await UploadGeofileAsync($"{token} track.geojson", "authenticated");
        var hiddenGeofile = await UploadGeofileAsync($"{token} secret.geojson", null);

        var linkId = await CreateLinkAsync(
            owner,
            Member("tripLog", openTrip),
            Member("tripLog", hiddenTrip, sortOrder: 1),
            Member("mapView", openView, sortOrder: 2),
            Member("mapView", hiddenView, sortOrder: 3),
            Member("geofile", openGeofile, sortOrder: 4),
            Member("geofile", hiddenGeofile, sortOrder: 5));

        // Every member answers under its own world's visibility filter: the open ones
        // display, the private ones stay as bare members with nothing of the target.
        var members = (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("targetId").GetGuid());
        members.Count.ShouldBe(6);
        foreach (var openId in new[] { openTrip, openView, openGeofile })
        {
            members[openId].GetProperty("display").GetProperty("title").GetString()!
                .ShouldContain(token, customMessage: openId.ToString());
        }

        foreach (var hiddenId in new[] { hiddenTrip, hiddenView, hiddenGeofile })
        {
            members[hiddenId].GetProperty("display").ValueKind
                .ShouldBe(JsonValueKind.Null, hiddenId.ToString());
        }

        // The picker feed thins the same way — and the owner finding both proves the
        // thinning was visibility, not the query.
        foreach (var type in new[] { "tripLog", "mapView", "geofile" })
        {
            (await SearchIdsAsync(viewer, type, token)).Count.ShouldBe(1, type);
            (await SearchIdsAsync(owner, type, token)).Count.ShouldBe(2, type);
        }
    }

    [Fact]
    public async Task Survey_model_targets_follow_cave_visibility_and_the_exact_location_cut()
    {
        var token = $"Rlm{Guid.NewGuid():N}"[..12];
        var openCave = await CreateCaveAsync(owner, "Open Model Cave", "authenticated");
        var protectedCave = await CreateProtectedCaveAsync(owner, "Guarded Model Cave");
        var privateCave = await CreateCaveAsync(owner, "Private Model Cave", "private");

        var openModel = await CreateSurveyModelAsync(openCave, $"{token} open.lox");
        var protectedModel = await CreateSurveyModelAsync(protectedCave, $"{token} guarded.lox");
        var privateModel = await CreateSurveyModelAsync(privateCave, $"{token} private.lox");

        var linkId = await CreateLinkAsync(
            owner,
            Member("surveyModel", openModel),
            Member("surveyModel", protectedModel, sortOrder: 1),
            Member("surveyModel", privateModel, sortOrder: 2));

        // A survey file is absolute georeferenced coordinates, so a readable cave is not
        // enough: without exact-location view the model is withheld entirely.
        var members = (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("targetId").GetGuid());
        members[openModel].GetProperty("display").GetProperty("title").GetString()!.ShouldContain(token);
        members[protectedModel].GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        members[privateModel].GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        (await SearchIdsAsync(viewer, "surveyModel", token)).ShouldBe([openModel]);
        (await SearchIdsAsync(owner, "surveyModel", token))
            .ShouldBe([openModel, protectedModel, privateModel], ignoreOrder: true);

        // Exact view granted on the protected cave opens exactly that model.
        await GrantExactViewAsync(protectedCave, viewerId);
        (await SearchIdsAsync(viewer, "surveyModel", token))
            .ShouldBe([openModel, protectedModel], ignoreOrder: true);
        (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetId").GetGuid() == protectedModel)
            .GetProperty("display").GetProperty("title").GetString()!.ShouldContain(token);
    }

    [Fact]
    public async Task Caver_search_is_gated_like_the_roster_it_enumerates()
    {
        var token = $"Rlc{Guid.NewGuid():N}"[..12];
        var caverId = await CreateCaverAsync($"{token} Ionescu");
        var linkId = await CreateLinkAsync(owner, Member("caver", caverId));

        // On a default install every account holds the seeded roster read.
        (await SearchIdsAsync(viewer, "caver", token)).ShouldBe([caverId]);

        // The seeded entry is removable policy, not a constant: an installation that
        // revokes it must not find its roster enumerable through the picker feed —
        // while a caller who holds the read through another group still searches.
        var revoked = await RemoveAllUsersCaversReadAsync();
        try
        {
            (await SearchIdsAsync(viewer, "caver", token)).ShouldBeEmpty();
            (await SearchIdsAsync(keeper, "caver", token)).ShouldBe([caverId]);

            // Labels shown in the context of a readable link follow the established
            // roster convention (names are readable in context) and stay.
            (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
                .GetProperty("members")[0].GetProperty("display").GetProperty("title").GetString()
                .ShouldStartWith(token);
        }
        finally
        {
            await RestoreAllUsersCaversReadAsync(revoked);
        }

        (await SearchIdsAsync(viewer, "caver", token)).ShouldBe([caverId]);
    }

    [Fact]
    public async Task Caving_group_and_cabinet_targets_resolve_with_their_own_gates()
    {
        var token = $"Rlg{Guid.NewGuid():N}"[..12];
        var groupId = await CreateCavingGroupAsync($"{token} Club");
        var shelfId = await CreateCabinetAsync($"{token} Shelf");
        var yearId = await CreateCabinetAsync("1987", parentId: shelfId);

        var linkId = await CreateLinkAsync(
            owner, Member("cavingGroup", groupId), Member("cabinet", yearId, sortOrder: 1));

        // Directory-level facts resolve for any signed-in caller; the cabinet subtitle
        // is its ancestry, so two "1987" shelves stay distinguishable.
        var members = (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("targetId").GetGuid());
        members[groupId].GetProperty("display").GetProperty("title").GetString()!.ShouldContain(token);
        var cabinetDisplay = members[yearId].GetProperty("display");
        cabinetDisplay.GetProperty("title").GetString().ShouldBe("1987");
        cabinetDisplay.GetProperty("subtitle").GetString()!.ShouldContain(token);
        (await SearchIdsAsync(viewer, "cavingGroup", token)).ShouldBe([groupId]);
        (await SearchIdsAsync(viewer, "cabinet", "1987"))
            .ShouldContain(yearId);

        // A deny naming the group closes it for that caller — display and search alike —
        // while everyone else still sees the directory fact.
        await DenyCavingGroupReadAsync(groupId, viewerId);
        (await SearchIdsAsync(viewer, "cavingGroup", token)).ShouldBeEmpty();
        (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetId").GetGuid() == groupId)
            .GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        (await SearchIdsAsync(owner, "cavingGroup", token)).ShouldBe([groupId]);
    }

    // ---- the GPS-point convenience ---------------------------------------------------

    [Fact]
    public async Task A_new_gps_point_is_born_for_signed_in_readers_owned_by_the_caller_and_linked_in_one_act()
    {
        var caveId = await CreateCaveAsync(owner, "Spring Cave", "authenticated");
        var linkId = await CreateLinkAsync(owner, Member("feature", caveId));
        var pointName = $"Karst spring {Guid.NewGuid():N}"[..24];

        var added = await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", new
        {
            targetType = (string?)null,
            targetId = (Guid?)null,
            newGeoPoint = new { lon = 25.5, lat = 45.5, z = 820.0, name = pointName, visibility = (string?)null },
            isMain = false,
            sortOrder = 1,
            note = (string?)null,
            anchorKind = "whole",
            anchor = (object?)null,
            anchorFileId = (Guid?)null,
        });
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var member = await ReadJsonAsync(added);
        member.GetProperty("targetType").GetString().ShouldBe("feature");
        member.GetProperty("display").GetProperty("title").GetString().ShouldBe(pointName);
        var pointId = member.GetProperty("targetId").GetGuid();

        // The creator belongs to no club here, so the point opens to signed-in callers:
        // a viewer with no rule anywhere reads the feature itself.
        (await viewer.GetAsync($"/api/v1/features/{pointId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // And it is an ordinary caller-owned feature — owner trio and coordinates intact.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var feature = await db.Features.AsNoTracking().SingleAsync(f => f.Id == pointId);
            feature.OwnerUserId.ShouldBe(ownerId);
            feature.Visibility.ShouldBe(Visibility.Authenticated);
            feature.CavingGroupId.ShouldBeNull();
            var point = feature.Geom.ShouldBeOfType<Point>();
            point.X.ShouldBe(25.5, 1e-9);
            point.Y.ShouldBe(45.5, 1e-9);
            point.Coordinate.Z.ShouldBe(820.0, 1e-9);
        }

        // The caller narrows the default when they mean to.
        var privatePoint = await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", new
        {
            targetType = (string?)null,
            targetId = (Guid?)null,
            newGeoPoint = new { lon = 25.6, lat = 45.6, z = (double?)null, name = "Quiet spring", visibility = "private" },
            isMain = false,
            sortOrder = 2,
            note = (string?)null,
            anchorKind = "whole",
            anchor = (object?)null,
            anchorFileId = (Guid?)null,
        });
        privatePoint.StatusCode.ShouldBe(HttpStatusCode.Created, await privatePoint.Content.ReadAsStringAsync());
        var privateId = (await ReadJsonAsync(privatePoint)).GetProperty("targetId").GetGuid();
        (await viewer.GetAsync($"/api/v1/features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/features/{privateId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_new_gps_point_takes_the_creators_club_when_they_have_exactly_one()
    {
        var caveId = await CreateCaveAsync(owner, "Club Cave", "authenticated");
        var linkId = await CreateLinkAsync(owner, Member("feature", caveId));

        // Founding a club enrolls the founder in it, so the creator now belongs to one.
        var clubId = await CreateCavingGroupAsync($"Point Club {Guid.NewGuid():N}"[..24]);

        // Stating no visibility hands the point to the creator's club, named on the row —
        // a club band with no club would admit nobody at all.
        var pointId = await AddGeoPointAsync(owner, linkId, 25.51, 45.51, sortOrder: 1, visibility: null);
        await AssertPointAudienceAsync(pointId, Visibility.CavingGroup, clubId);

        // …and the club is who reads it: an outsider with no rule anywhere does not, the
        // same account does the moment it joins.
        (await viewer.GetAsync($"/api/v1/features/{pointId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await RosterHelper.AddMemberAsync(
                scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(), clubId, viewerId);
        }

        (await viewer.GetAsync($"/api/v1/features/{pointId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Asking for the club band explicitly names the same club — the switch on the form
        // cannot produce a point nobody can read.
        var askedId = await AddGeoPointAsync(owner, linkId, 25.52, 45.52, sortOrder: 2, visibility: "cavingGroup");
        await AssertPointAudienceAsync(askedId, Visibility.CavingGroup, clubId);

        // A second membership leaves no single club to mean, so the default widens to
        // every signed-in caller rather than picking one of them.
        _ = await CreateCavingGroupAsync($"Other Club {Guid.NewGuid():N}"[..24]);
        var widerId = await AddGeoPointAsync(owner, linkId, 25.53, 45.53, sortOrder: 3, visibility: null);
        await AssertPointAudienceAsync(widerId, Visibility.Authenticated, null);

        // The explicit field still wins over either default.
        var privateId = await AddGeoPointAsync(owner, linkId, 25.54, 45.54, sortOrder: 4, visibility: "private");
        await AssertPointAudienceAsync(privateId, Visibility.Private, null);
    }

    [Fact]
    public async Task The_form_is_told_the_audience_a_new_point_will_actually_get()
    {
        // A form has to name the audience before the point exists, and the fact it needs —
        // the caller's own roster memberships — is published nowhere else, so it is asked
        // for here and must answer exactly what the write then applies.
        var beforeAny = await owner.GetAsync("/api/v1/reslinks/point-default");
        beforeAny.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await ReadJsonAsync(beforeAny);
        before.GetProperty("visibility").GetString().ShouldBe("authenticated");
        before.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);
        // Nothing to name: the notice says "everyone signed in" rather than inventing a group.
        before.GetProperty("cavingGroupName").ValueKind.ShouldBe(JsonValueKind.Null);

        var clubName = $"Notice Club {Guid.NewGuid():N}"[..24];
        var clubId = await CreateCavingGroupAsync(clubName);

        var afterOne = await ReadJsonAsync(await owner.GetAsync("/api/v1/reslinks/point-default"));
        afterOne.GetProperty("visibility").GetString().ShouldBe("cavingGroup");
        afterOne.GetProperty("cavingGroupId").GetGuid().ShouldBe(clubId);
        afterOne.GetProperty("cavingGroupName").GetString().ShouldBe(clubName);

        // What it promised is what the point gets — the same rule answers both.
        var caveId = await CreateCaveAsync(owner, "Notice Cave", "authenticated");
        var linkId = await CreateLinkAsync(owner, Member("feature", caveId));
        var pointId = await AddGeoPointAsync(owner, linkId, 25.57, 45.57, sortOrder: 1, visibility: null);
        await AssertPointAudienceAsync(pointId, Visibility.CavingGroup, clubId);

        // A second membership leaves no single group to mean, and the notice widens with it.
        _ = await CreateCavingGroupAsync($"Second Club {Guid.NewGuid():N}"[..24]);
        var afterTwo = await ReadJsonAsync(await owner.GetAsync("/api/v1/reslinks/point-default"));
        afterTwo.GetProperty("visibility").GetString().ShouldBe("authenticated");
        afterTwo.GetProperty("cavingGroupId").ValueKind.ShouldBe(JsonValueKind.Null);
        var widened = await AddGeoPointAsync(owner, linkId, 25.58, 45.58, sortOrder: 2, visibility: null);
        await AssertPointAudienceAsync(widened, Visibility.Authenticated, null);

        // It reports the caller's own membership and nothing else, so it takes an account.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/reslinks/point-default")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Minting_a_gps_point_is_an_ordinary_feature_creation_with_its_rules()
    {
        var caveId = await CreateCaveAsync(owner, "Gatekept Cave", "authenticated");

        // The viewer authors their own link about a readable cave…
        var linkId = await CreateLinkAsync(viewer, Member("feature", caveId));

        // …but holds no right to create features, and the link surface adds none.
        var refused = await viewer.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", new
        {
            targetType = (string?)null,
            targetId = (Guid?)null,
            newGeoPoint = new { lon = 25.5, lat = 45.5, z = (double?)null, name = "Bypass", visibility = (string?)null },
            isMain = false,
            sortOrder = 1,
            note = (string?)null,
            anchorKind = "whole",
            anchor = (object?)null,
            anchorFileId = (Guid?)null,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("access.create_forbidden");
        (await ReadJsonAsync(await viewer.GetAsync($"/api/v1/reslinks/{linkId}")))
            .GetProperty("members").GetArrayLength().ShouldBe(1);

        // Shape rules: a coordinate off the planet, a double target, a part-anchor on a
        // point that has no parts — all stopped in validation.
        foreach (var invalid in new object[]
        {
            new
            {
                targetType = (string?)null, targetId = (Guid?)null,
                newGeoPoint = new { lon = 999.0, lat = 45.5 },
                isMain = false, sortOrder = 0, anchorKind = "whole",
            },
            new
            {
                targetType = (string?)"feature", targetId = (Guid?)caveId,
                newGeoPoint = new { lon = 25.5, lat = 45.5 },
                isMain = false, sortOrder = 0, anchorKind = "whole",
            },
            new
            {
                targetType = (string?)null, targetId = (Guid?)null,
                newGeoPoint = new { lon = 25.5, lat = 45.5 },
                isMain = false, sortOrder = 0, anchorKind = "page",
            },
        })
        {
            var response = await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", invalid);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, invalid.ToString());
            (await ReadCodeAsync(response)).ShouldBe("validation.failed");
        }
    }

    // ---- the relation vocabulary -----------------------------------------------------

    [Fact]
    public async Task The_relation_vocabulary_ships_seeded_and_grows_by_admin_hand()
    {
        // Any signed-in caller reads the vocabulary — pickers and link rendering need it.
        var listed = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        var byCode = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);
        foreach (var (code, directed) in new[]
        {
            ("same-object", false), ("related-to", false), ("contains", true),
            ("documents", true), ("derived-from", true), ("adjacent-to", false),
            ("duplicate-of", true), ("needs-clarification", false),
        })
        {
            byCode.ContainsKey(code).ShouldBeTrue(code);
            var row = byCode[code];
            row.GetProperty("seeded").GetBoolean().ShouldBeTrue(code);
            row.GetProperty("directed").GetBoolean().ShouldBe(directed, code);
            (row.GetProperty("inverseName").ValueKind == JsonValueKind.String).ShouldBe(directed, code);
        }

        // Managing it is a full-administrator act; an editor is refused.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            code = $"custom-{suffix}",
            name = "Explored with",
            description = (string?)null,
            sortOrder = 500,
            directed = false,
            inverseName = (string?)null,
        };
        (await owner.PostAsJsonAsync("/api/v1/reslinks/relation-types", request))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var created = await admin.PostAsJsonAsync("/api/v1/reslinks/relation-types", request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var custom = await ReadJsonAsync(created);
        custom.GetProperty("seeded").GetBoolean().ShouldBeFalse();
        var customId = custom.GetProperty("id").GetInt64();

        // A custom row is installation-local: renamed, re-coded, flipped at will while
        // nothing references it.
        var updated = await admin.PatchAsJsonAsync($"/api/v1/reslinks/relation-types/{customId}", new
        {
            code = $"custom2-{suffix}",
            name = "Surveyed with",
            description = "Same expedition.",
            sortOrder = 510,
            directed = true,
            inverseName = "Surveyed during",
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        // Codes are a namespace: a second row cannot take a live one.
        var taken = await admin.PostAsJsonAsync("/api/v1/reslinks/relation-types", request with
        {
            code = "related-to",
        });
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(taken)).ShouldBe("reslink.relation.code_taken");

        // An undirected relation has no inverse reading.
        var inverseOnUndirected = await admin.PostAsJsonAsync("/api/v1/reslinks/relation-types", request with
        {
            code = $"custom3-{suffix}",
            inverseName = "Backwards",
        });
        inverseOnUndirected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(inverseOnUndirected)).ShouldBe("validation.failed");

        (await admin.DeleteAsync($"/api/v1/reslinks/relation-types/{customId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Seeded_relation_rows_keep_their_code_and_directedness_and_never_leave()
    {
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        var seeded = listed.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "contains");
        var id = seeded.GetProperty("id").GetInt64();
        var body = new
        {
            code = seeded.GetProperty("code").GetString(),
            name = seeded.GetProperty("name").GetString(),
            description = seeded.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString()
                : null,
            sortOrder = seeded.GetProperty("sortOrder").GetInt32(),
            directed = seeded.GetProperty("directed").GetBoolean(),
            inverseName = seeded.GetProperty("inverseName").GetString(),
        };

        // Seeded codes are the exchange vocabulary — clients translate labels by them.
        var recoded = await admin.PatchAsJsonAsync(
            $"/api/v1/reslinks/relation-types/{id}", body with { code = "holds" });
        recoded.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(recoded)).ShouldBe("reslink.relation.seeded_immutable");

        // Directedness decides how every link under the row validates its main member.
        var flipped = await admin.PatchAsJsonAsync(
            $"/api/v1/reslinks/relation-types/{id}", body with { directed = false, inverseName = (string?)null });
        flipped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(flipped)).ShouldBe("reslink.relation.seeded_immutable");

        var deleted = await admin.DeleteAsync($"/api/v1/reslinks/relation-types/{id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(deleted)).ShouldBe("reslink.relation.seeded_immutable");

        // The refusals were about code and directedness: the identical write with both
        // unchanged is an ordinary allowed edit.
        (await admin.PatchAsJsonAsync($"/api/v1/reslinks/relation-types/{id}", body))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task What_a_trip_did_to_what_it_names_ships_as_ten_directed_roles()
    {
        // The roles are ordinary rows of the shared vocabulary, so any signed-in caller reads
        // them the same way they read the rest — no trip-specific surface, no second list.
        var listed = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        var byCode = listed.EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);

        string[] roles =
        [
            "trip-work-area", "trip-objective", "trip-visited", "trip-surveyed", "trip-discovered",
            "trip-dug", "trip-photographed", "trip-searched-not-found", "trip-lead",
            "trip-follows-on-from",
        ];

        foreach (var code in roles)
        {
            byCode.ContainsKey(code).ShouldBeTrue(code);
            var row = byCode[code];
            row.GetProperty("seeded").GetBoolean().ShouldBeTrue(code);

            // Every role is directed with the trip as the main member: "Trip surveyed Cave" and
            // "Cave was surveyed on Trip" are the two readings a role-filtered field needs, and
            // an undirected role would have neither the moment two trips shared one link.
            row.GetProperty("directed").GetBoolean().ShouldBeTrue(code);
            row.GetProperty("inverseName").ValueKind.ShouldBe(JsonValueKind.String, code);
            row.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();

            // Directedness is what the write path checks to require exactly one main member, so
            // a role that lost it would let a link claim two trips did the thing — hence every
            // role is undeletable, like every other shipped code.
            var deleted = await admin.DeleteAsync(
                $"/api/v1/reslinks/relation-types/{row.GetProperty("id").GetInt64()}");
            deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict, code);
            (await ReadCodeAsync(deleted)).ShouldBe("reslink.relation.seeded_immutable", code);
        }

        // Managing the vocabulary at all is a full-administrator act — an editor is refused
        // before the row is even looked at.
        var visited = byCode["trip-visited"];
        var visitedId = visited.GetProperty("id").GetInt64();
        (await owner.DeleteAsync($"/api/v1/reslinks/relation-types/{visitedId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var body = new
        {
            code = visited.GetProperty("code").GetString(),
            name = visited.GetProperty("name").GetString(),
            description = (string?)null,
            sortOrder = visited.GetProperty("sortOrder").GetInt32(),
            directed = visited.GetProperty("directed").GetBoolean(),
            inverseName = visited.GetProperty("inverseName").GetString(),
        };

        var recoded = await admin.PatchAsJsonAsync(
            $"/api/v1/reslinks/relation-types/{visitedId}", body with { code = "visited" });
        recoded.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(recoded)).ShouldBe("reslink.relation.seeded_immutable");

        var flipped = await admin.PatchAsJsonAsync(
            $"/api/v1/reslinks/relation-types/{visitedId}",
            body with { directed = false, inverseName = (string?)null });
        flipped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(flipped)).ShouldBe("reslink.relation.seeded_immutable");

        // The positive twin of all three refusals: wording and ordering are an installation's
        // own, so the identical write with code and directedness untouched goes through.
        (await admin.PatchAsJsonAsync($"/api/v1/reslinks/relation-types/{visitedId}", body))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A caller who is not signed in reads none of it.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/reslinks/relation-types"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_relation_type_in_use_keeps_its_directedness_and_its_life()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/v1/reslinks/relation-types", new
        {
            code = $"in-use-{suffix}",
            name = "Feeds",
            description = (string?)null,
            sortOrder = 520,
            directed = true,
            inverseName = "Fed by",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var relationId = (await ReadJsonAsync(created)).GetProperty("id").GetInt64();

        var caveA = await CreateCaveAsync(owner, "Relation Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Relation Cave B", "authenticated");
        var link = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = relationId,
            description = (string?)null,
            members = new[] { Member("feature", caveA, isMain: true), Member("feature", caveB, sortOrder: 1) },
        });
        link.StatusCode.ShouldBe(HttpStatusCode.Created, await link.Content.ReadAsStringAsync());
        var linkId = (await ReadJsonAsync(link)).GetProperty("id").GetGuid();

        // Flipping directedness would invalidate the link's main marker in place.
        var flipped = await admin.PatchAsJsonAsync($"/api/v1/reslinks/relation-types/{relationId}", new
        {
            code = $"in-use-{suffix}",
            name = "Feeds",
            description = (string?)null,
            sortOrder = 520,
            directed = false,
            inverseName = (string?)null,
        });
        flipped.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(flipped)).ShouldBe("reslink.relation.in_use");

        var deleted = await admin.DeleteAsync($"/api/v1/reslinks/relation-types/{relationId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(deleted)).ShouldBe("reslink.relation.in_use");

        // Retype the link away and the same row goes — the refusal was the reference.
        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/v1/reslinks/relation-types/{relationId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---- the panel answers for one relation --------------------------------------------

    [Fact]
    public async Task The_panel_answers_for_one_relation_and_refuses_a_code_no_relation_carries()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var caveId = await CreateCaveAsync(owner, "Role Cave", "authenticated");
        var tripId = await CreateTripLogAsync($"Roles {suffix}", caveId, "authenticated");

        // Three links between the same two things, differing only in what the trip did:
        // exactly the case a filtered field has to tell apart.
        var visitedLink = await CreateTypedLinkAsync(
            owner,
            await RelationIdAsync("trip-visited"),
            Member("tripLog", tripId, isMain: true),
            Member("feature", caveId, sortOrder: 1));
        var dugLink = await CreateTypedLinkAsync(
            owner,
            await RelationIdAsync("trip-dug"),
            Member("tripLog", tripId, isMain: true),
            Member("feature", caveId, sortOrder: 1));
        var untypedLink = await CreateLinkAsync(
            owner, Member("tripLog", tripId), Member("feature", caveId, sortOrder: 1));

        var everything = new[] { visitedLink, dugLink, untypedLink };

        // Unasked, the panel is every link incident to the cave, whatever it means…
        (await PanelLinkIdsAsync(owner, "feature", caveId)).ShouldBe(everything, ignoreOrder: true);

        // …and asked for one relation it is that relation alone — not the sibling role,
        // not the link that names no relation at all.
        (await PanelLinkIdsAsync(owner, "feature", caveId, "trip-visited"))
            .ShouldBe(new[] { visitedLink });
        (await PanelLinkIdsAsync(owner, "feature", caveId, "trip-dug")).ShouldBe(new[] { dugLink });

        // The same question from the trip's side reads back the other way round and
        // narrows identically.
        (await PanelLinkIdsAsync(owner, "tripLog", tripId, "trip-visited"))
            .ShouldBe(new[] { visitedLink });

        // A relation nobody used here is a question with an answer, and the answer is
        // none; an empty filter is no filter at all.
        (await PanelLinkIdsAsync(owner, "feature", caveId, "trip-photographed")).ShouldBeEmpty();
        (await PanelLinkIdsAsync(owner, "feature", caveId, string.Empty))
            .ShouldBe(everything, ignoreOrder: true);
        (await PanelLinkIdsAsync(owner, "feature", caveId, "   "))
            .ShouldBe(everything, ignoreOrder: true);

        // A code no relation type carries is refused rather than answered with an empty
        // page: a mistyped role must not read as a role nobody used.
        var mistyped = await owner.GetAsync(
            $"/api/v1/reslinks/for-target?type=feature&id={caveId}&relation=trip-abseiled");
        mistyped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(mistyped)).ShouldBe("reslink.relation.not_found");

        // The target's own rules still gate the panel first: a caller who may not read the
        // target learns that and nothing else, whatever they asked about the relation.
        var privateCave = await CreateCaveAsync(owner, "Role Cave Hidden", "private");
        var gated = await viewer.GetAsync(
            $"/api/v1/reslinks/for-target?type=feature&id={privateCave}&relation=trip-abseiled");
        gated.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(gated)).ShouldBe("reslink.target_not_found");

        // …and the owner of that cave reads its (empty) panel with the same filter, so the
        // refusal above was the caller's rights and not the route.
        (await PanelLinkIdsAsync(owner, "feature", privateCave, "trip-visited")).ShouldBeEmpty();

        // Asked with a filter, an anonymous caller is still refused before any of it is
        // resolved — the parameter opens no door of its own.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/reslinks/for-target?type=feature&id={caveId}&relation=trip-visited"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- target deletion cleans the membership ----------------------------------------

    [Fact]
    public async Task Deleting_a_target_through_its_own_slice_removes_the_members_that_named_it()
    {
        var caveId = await CreateCaveAsync(owner, "Cleanup Cave", "authenticated");
        var tripId = await CreateTripLogAsync($"Cleanup trip {Guid.NewGuid():N}"[..30], caveId, "authenticated");
        var viewId = await CreateMapViewAsync($"Cleanup view {Guid.NewGuid():N}"[..30], "authenticated");
        var cabinetId = await CreateCabinetAsync($"Cleanup {Guid.NewGuid():N}"[..24]);
        var groupId = await CreateCavingGroupAsync($"Cleanup club {Guid.NewGuid():N}"[..24]);
        var geofileId = await UploadGeofileAsync("cleanup.geojson", "authenticated");
        var modelId = await CreateSurveyModelAsync(caveId, "cleanup.lox");
        var caverId = await CreateCaverAsync($"Cleanup Caver {Guid.NewGuid():N}"[..24]);

        var linkId = await CreateLinkAsync(
            owner,
            Member("feature", caveId),
            Member("tripLog", tripId, sortOrder: 1),
            Member("mapView", viewId, sortOrder: 2),
            Member("cabinet", cabinetId, sortOrder: 3),
            Member("cavingGroup", groupId, sortOrder: 4),
            Member("geofile", geofileId, sortOrder: 5),
            Member("surveyModel", modelId, sortOrder: 6),
            Member("caver", caverId, sortOrder: 7));
        (await GetLinkAsync(linkId)).GetProperty("members").GetArrayLength().ShouldBe(8);

        // Each world's own delete flow takes its members with it — a dead target must
        // not linger in links as a permanent restricted-looking member.
        var deletions = new (HttpClient Client, string Route, Guid TargetId)[]
        {
            (owner, $"/api/v1/trip-logs/{tripId}", tripId),
            (owner, $"/api/v1/map-views/{viewId}", viewId),
            (owner, $"/api/v1/cabinets/{cabinetId}", cabinetId),
            (admin, $"/api/v1/caving-groups/{groupId}", groupId),
            (owner, $"/api/v1/geofiles/{geofileId}", geofileId),
            (owner, $"/api/v1/survey-models/{modelId}", modelId),
            (admin, $"/api/v1/cavers/{caverId}", caverId),
        };
        var expected = 8;
        foreach (var (client, route, targetId) in deletions)
        {
            (await client.DeleteAsync(route)).StatusCode.ShouldBe(HttpStatusCode.NoContent, route);
            var remaining = (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray().ToList();
            remaining.Count.ShouldBe(--expected, route);
            remaining.ShouldAllBe(m => m.GetProperty("targetId").GetGuid() != targetId);
        }

        // The link survives its shrinkage; only the feature member remains.
        (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray()
            .Single().GetProperty("targetType").GetString().ShouldBe("feature");
    }

    [Fact]
    public async Task Merging_cavers_folds_their_memberships_like_their_trips()
    {
        var survivor = await CreateCaverAsync($"Survivor {Guid.NewGuid():N}"[..24]);
        var duplicate = await CreateCaverAsync($"Duplicate {Guid.NewGuid():N}"[..24]);
        var alsoThere = await CreateCaverAsync($"AlsoThere {Guid.NewGuid():N}"[..24]);
        var caveId = await CreateCaveAsync(owner, "Merge Cave", "authenticated");

        // Link A names only the duplicate; link B already holds the survivor as well.
        var repointed = await CreateLinkAsync(
            owner, Member("feature", caveId), Member("caver", duplicate, sortOrder: 1));
        var collapsed = await CreateLinkAsync(
            owner, Member("caver", survivor), Member("caver", alsoThere, sortOrder: 1));

        (await keeper.PostAsJsonAsync($"/api/v1/cavers/{survivor}/merge",
            new { sourceCaverId = duplicate })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await keeper.PostAsJsonAsync($"/api/v1/cavers/{survivor}/merge",
            new { sourceCaverId = alsoThere })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // What named the duplicate now names the survivor…
        (await GetLinkAsync(repointed)).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetType").GetString() == "caver")
            .GetProperty("targetId").GetGuid().ShouldBe(survivor);

        // …except where the survivor already sat in the link: the duplicate row goes
        // rather than colliding with one-whole-member-per-target.
        var survivors = (await GetLinkAsync(collapsed)).GetProperty("members").EnumerateArray().ToList();
        survivors.Count.ShouldBe(1);
        survivors[0].GetProperty("targetId").GetGuid().ShouldBe(survivor);
    }

    // ---- the schema backstops ---------------------------------------------------------

    [Fact]
    public async Task The_database_itself_holds_the_member_shape_constraints()
    {
        var caveA = await CreateCaveAsync(owner, "Backstop Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Backstop Cave B", "authenticated");
        var caveC = await CreateCaveAsync(owner, "Backstop Cave C", "authenticated");
        var (documentId, _) = await UploadDocumentAsync("backstop.txt");
        var linkId = await CreateLinkAsync(owner, Member("feature", caveA));

        // Written straight to the database, which is the point: the invariants must not
        // depend on every future write path remembering them.

        // Exactly one target shape.
        await ShouldRejectDirectInsertAsync(new ResLinkMember
        {
            ResLinkId = linkId,
            FeatureId = caveB,
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
        }, "ck_res_link_members_one_target");

        // Payload exactly when the anchor is a part.
        await ShouldRejectDirectInsertAsync(new ResLinkMember
        {
            ResLinkId = linkId,
            FeatureId = caveB,
            Anchor = "{}",
        }, "ck_res_link_members_anchor_payload");
        await ShouldRejectDirectInsertAsync(new ResLinkMember
        {
            ResLinkId = linkId,
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
            AnchorKind = AnchorKind.Page,
        }, "ck_res_link_members_anchor_payload");

        // A second main. The first insert proves the fixture; the second proves the
        // partial unique index's filter actually bites.
        await DirectInsertAsync(new ResLinkMember { ResLinkId = linkId, FeatureId = caveB, IsMain = true });
        await ShouldRejectDirectInsertAsync(
            new ResLinkMember { ResLinkId = linkId, FeatureId = caveC, IsMain = true },
            "ix_res_link_members_main");

        // A second whole member for the same target, on both target shapes — while a
        // part on the same target passes, proving the filter cuts on anchor kind.
        await ShouldRejectDirectInsertAsync(
            new ResLinkMember { ResLinkId = linkId, FeatureId = caveA },
            "ix_res_link_members_whole_feature");
        await DirectInsertAsync(new ResLinkMember
        {
            ResLinkId = linkId,
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
        });
        await ShouldRejectDirectInsertAsync(new ResLinkMember
        {
            ResLinkId = linkId,
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
        }, "ix_res_link_members_whole_entity");
        await DirectInsertAsync(new ResLinkMember
        {
            ResLinkId = linkId,
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
            AnchorKind = AnchorKind.Page,
            Anchor = """{"page": 2}""",
        });

        // The short-code unique index, by the exact name the create retry keys on.
        var shortCode = (await GetLinkAsync(linkId)).GetProperty("shortCode").GetString()!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.ResLinks.Add(new ResLink { ShortCode = shortCode });
        var collision = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        collision.InnerException.ShouldBeOfType<PostgresException>()
            .ConstraintName.ShouldBe("ix_res_links_short_code");
    }

    // ---- concurrent writers -----------------------------------------------------------

    [Fact]
    public async Task Concurrent_removals_cannot_empty_a_link()
    {
        var caveA = await CreateCaveAsync(owner, "Race Cave A", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Race Cave B", "authenticated");
        var linkId = await CreateLinkAsync(
            owner, Member("feature", caveA), Member("feature", caveB, sortOrder: 1));
        var memberA = await MemberIdOfAsync(linkId, caveA);
        var memberB = await MemberIdOfAsync(linkId, caveB);

        // Both removals pass the floor against the same two-member snapshot unless the
        // writers are serialized; the loser must re-read and refuse, never leave zero.
        var responses = await Task.WhenAll(
            owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{memberA}"),
            owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{memberB}"));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        var refused = responses.Single(r => r.StatusCode != HttpStatusCode.NoContent);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(refused)).ShouldBe("reslink.member.last");
        (await GetLinkAsync(linkId)).GetProperty("members").GetArrayLength().ShouldBe(1);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_whole_target_leave_one_member()
    {
        var caveA = await CreateCaveAsync(owner, "Race Cave C", "authenticated");
        var caveB = await CreateCaveAsync(owner, "Race Cave D", "authenticated");
        var linkId = await CreateLinkAsync(owner, Member("feature", caveA));

        // The duplicate-whole rule is checked against a snapshot too: the racing loser
        // is refused the way a sequential duplicate is, not surfaced as a server error.
        var responses = await Task.WhenAll(
            owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", Member("feature", caveB, sortOrder: 1)),
            owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", Member("feature", caveB, sortOrder: 1)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var refused = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(refused)).ShouldBe("reslink.member.duplicate_whole");
        (await GetLinkAsync(linkId)).GetProperty("members").GetArrayLength().ShouldBe(2);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ---- bounds -----------------------------------------------------------------------

    [Fact]
    public async Task Anchors_and_membership_stay_bounded()
    {
        var (documentId, _) = await UploadDocumentAsync("bounded.txt");

        // A payload is stored and echoed verbatim to every reader; size is validated
        // like any other user-authored field.
        var oversized = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[]
            {
                Member("document", documentId, anchorKind: "page",
                    anchor: new { page = 1, note = new string('x', ResLinkRules.MaxAnchorLength) }),
            },
        });
        oversized.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(oversized)).ShouldBe("validation.failed");

        // One over the member ceiling at create is refused whole…
        var pageMembers = Enumerable.Range(1, ResLinkRules.MaxMembers + 1)
            .Select(i => Member("document", documentId, sortOrder: i, anchorKind: "page", anchor: new { page = i }))
            .ToArray();
        var tooMany = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = pageMembers,
        });
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(tooMany)).ShouldBe("validation.failed");

        // …a link at the ceiling exists, and the next member is refused with its own code.
        var linkId = await CreateLinkAsync(owner, pageMembers[..ResLinkRules.MaxMembers]);
        var overflow = await owner.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members",
            Member("document", documentId, sortOrder: 999, anchorKind: "page", anchor: new { page = 999 }));
        overflow.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(overflow)).ShouldBe("reslink.member.limit_reached");
    }

    // ---- what a trip did to what it names ---------------------------------------------

    /// <summary>
    /// The ten roles a trip plays towards the things it names are ordinary relation types
    /// on this one link mechanism, so they are written through this one write path: a link
    /// per role, the trip as its main member, targets added and removed one at a time. No
    /// second write law exists for them, and this test is the statement that none is
    /// needed — every role is authored, read back and asked for by name here.
    /// </summary>
    [Fact]
    public async Task Every_trip_role_links_its_targets_with_the_trip_as_the_main_member()
    {
        var cave = await CreateCaveAsync(owner, "Role Cave", "authenticated");
        var tripId = await CreateTripLogAsync("Role trip", cave, "authenticated");

        var links = new Dictionary<string, Guid>();
        foreach (var role in TripRoleCodes)
        {
            var relationId = await RelationIdAsync(role);
            links[role] = await CreateTypedLinkAsync(
                owner, relationId,
                Member("tripLog", tripId, isMain: true),
                Member("feature", cave, sortOrder: 1));

            var link = await GetLinkAsync(links[role]);
            link.GetProperty("relationType").GetProperty("code").GetString().ShouldBe(role);
            link.GetProperty("relationType").GetProperty("directed").GetBoolean().ShouldBeTrue(role);
            // Directed with the trip as main is what makes the two readings distinguishable:
            // the trip's page reads outwards, the cave's page reads the inverse back.
            link.GetProperty("relationType").GetProperty("inverseName").GetString().ShouldNotBeNull();
            var main = link.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("isMain").GetBoolean());
            main.GetProperty("targetType").GetString().ShouldBe("tripLog");
            main.GetProperty("targetId").GetGuid().ShouldBe(tripId);
        }

        // Asked for one role, either side answers with that role's link alone; asked for
        // nothing, with all ten. This is what a filtered field on a page is made of.
        foreach (var (role, linkId) in links)
        {
            (await PanelLinkIdsAsync(owner, "tripLog", tripId, role)).ShouldBe([linkId]);
            (await PanelLinkIdsAsync(owner, "feature", cave, role)).ShouldBe([linkId]);
        }

        (await PanelLinkIdsAsync(owner, "tripLog", tripId)).Count.ShouldBe(TripRoleCodes.Length);
    }

    /// <summary>
    /// A role's membership is edited one member at a time through the generic member
    /// routes — the marker handover included. Adding a member marked main demotes the
    /// sitting one first, in its own statement, because the single-main index is checked
    /// per statement; removing the main while two or more would remain is refused rather
    /// than leaving a directed link with no main.
    /// </summary>
    [Fact]
    public async Task A_role_grows_and_shrinks_one_member_at_a_time_and_the_marker_moves_with_it()
    {
        var cave = await CreateCaveAsync(owner, "Surveyed Cave", "authenticated");
        var otherCave = await CreateCaveAsync(owner, "Surveyed Cave Too", "authenticated");
        var entrance = await CreateEntranceAsync(cave);
        var tripId = await CreateTripLogAsync("Survey trip", cave, "authenticated");
        var surveyed = await RelationIdAsync("trip-surveyed");

        var linkId = await CreateTypedLinkAsync(
            owner, surveyed,
            Member("tripLog", tripId, isMain: true),
            Member("feature", cave, sortOrder: 1));

        // Growing: two more targets, neither of them a cave — a trip surveys entrances and
        // surface features too, and the role does not inherit the trip's cave-only list.
        foreach (var (target, sort) in new[] { (entrance, 2), (otherCave, 3) })
        {
            var added = await owner.PostAsJsonAsync(
                $"/api/v1/reslinks/{linkId}/members", Member("feature", target, sortOrder: sort));
            added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        }

        (await GetLinkAsync(linkId)).GetProperty("members").GetArrayLength().ShouldBe(4);
        (await MainTargetIdsAsync(linkId)).ShouldBe([tripId]);

        // The main cannot simply leave while the link still needs one…
        var tripMember = await MemberIdOfAsync(linkId, tripId);
        var orphaned = await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{tripMember}");
        orphaned.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(orphaned)).ShouldBe("reslink.main.required");

        // …the marker is handed over instead, and the handover is a single act.
        var caveMember = await MemberIdOfAsync(linkId, cave);
        var promoted = await owner.PatchAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members/{caveMember}",
            new { isMain = true, sortOrder = 1, note = (string?)null });
        promoted.StatusCode.ShouldBe(HttpStatusCode.OK, await promoted.Content.ReadAsStringAsync());
        (await MainTargetIdsAsync(linkId)).ShouldBe([cave]);

        // Adding a member as main hands it over the same way rather than colliding.
        var thirdCave = await CreateCaveAsync(owner, "Surveyed Cave Three", "authenticated");
        var addedAsMain = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", thirdCave, isMain: true, sortOrder: 4));
        addedAsMain.StatusCode.ShouldBe(HttpStatusCode.Created, await addedAsMain.Content.ReadAsStringAsync());
        (await MainTargetIdsAsync(linkId)).ShouldBe([thirdCave]);

        // Shrinking: members come off one at a time, the main last of all — and the marker
        // comes off with the removal that takes the link below two members, where it means
        // nothing.
        foreach (var target in new[] { cave, entrance, otherCave, thirdCave })
        {
            var memberId = await MemberIdOfAsync(linkId, target);
            (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{memberId}"))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await GetLinkAsync(linkId)).GetProperty("members").GetArrayLength().ShouldBe(1);
        (await MainTargetIdsAsync(linkId)).ShouldBeEmpty();

        // Emptying a role is deleting its link, never removing members down to none.
        var lastMember = await MemberIdOfAsync(linkId, tripId);
        var last = await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{lastMember}");
        last.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(last)).ShouldBe("reslink.member.last");
        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The marker laws a role write hits on its first request: a role link holding only
    /// the trip has nothing to be main of, and one holding two or more needs exactly one.
    /// Stated here because the obvious authoring order — create the link with the trip as
    /// main, then add targets — is refused, and a client that tried it would read the
    /// refusal as the role being unavailable. The first target to arrive at a trip-only
    /// role hands the marker to the trip, so an ordinary add never reverses the role.
    /// </summary>
    [Fact]
    public async Task A_role_carries_its_main_marker_only_once_it_has_something_to_be_main_of()
    {
        var cave = await CreateCaveAsync(owner, "Marker Cave", "authenticated");
        var otherCave = await CreateCaveAsync(owner, "Marker Cave Too", "authenticated");
        var tripId = await CreateTripLogAsync("Marker trip", cave, "authenticated");
        var visited = await RelationIdAsync("trip-visited");

        await ShouldRefuseCreateAsync(visited, "reslink.main.not_allowed_for_relation",
            Member("tripLog", tripId, isMain: true));
        await ShouldRefuseCreateAsync(visited, "reslink.main.required",
            Member("tripLog", tripId), Member("feature", cave, sortOrder: 1));
        await ShouldRefuseCreateAsync(visited, "reslink.main.not_single",
            Member("tripLog", tripId, isMain: true), Member("feature", cave, isMain: true, sortOrder: 1));

        // A role opened with the trip alone is legal without the marker: below two members
        // it would mean nothing. The first target to arrive brings the link to two, and the
        // marker goes to the member that was already there — the trip — so an ordinary add
        // states "the trip visited the cave" and not its reverse.
        var linkId = await CreateTypedLinkAsync(owner, visited, Member("tripLog", tripId));
        (await MainTargetIdsAsync(linkId)).ShouldBeEmpty();
        var added = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", cave, isMain: false, sortOrder: 1));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        (await MainTargetIdsAsync(linkId)).ShouldBe([tripId]);

        // The same whole target twice in one role is refused: a role names a thing once.
        var again = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", cave, sortOrder: 2));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(again)).ShouldBe("reslink.member.duplicate_whole");

        // A target nobody may read refuses exactly like one that does not exist.
        var missing = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", Guid.NewGuid(), sortOrder: 3));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(missing)).ShouldBe("reslink.member.target_not_found");

        // And the positive alongside it, so the refusals above cannot be a broken fixture.
        var real = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", otherCave, sortOrder: 3));
        real.StatusCode.ShouldBe(HttpStatusCode.Created, await real.Content.ReadAsStringAsync());
        (await MainTargetIdsAsync(linkId)).ShouldBe([tripId]);
    }

    /// <summary>
    /// The everyday correction — the wrong cave was named, take it off and put the right
    /// one on — leaves the role reading in the same direction it started in. Removing the
    /// last target takes the marker off, because a one-member link may not carry one; the
    /// replacement puts it back on the trip. The reverse would be a silent inversion:
    /// "this cave surveyed that trip", rendered as such on both pages, and for a role
    /// joining two trips it would invert an actual claim about which came first.
    /// </summary>
    [Fact]
    public async Task A_role_emptied_and_refilled_still_reads_from_the_trip()
    {
        var cave = await CreateCaveAsync(owner, "Refill Cave", "authenticated");
        var replacement = await CreateCaveAsync(owner, "Refill Cave Too", "authenticated");
        var tripId = await CreateTripLogAsync("Refill trip", cave, "authenticated");
        var surveyed = await RelationIdAsync("trip-surveyed");

        var linkId = await CreateTypedLinkAsync(
            owner, surveyed,
            Member("tripLog", tripId, isMain: true),
            Member("feature", cave, sortOrder: 1));

        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{await MemberIdOfAsync(linkId, cave)}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MainTargetIdsAsync(linkId)).ShouldBeEmpty();

        var refilled = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", replacement, sortOrder: 1));
        refilled.StatusCode.ShouldBe(HttpStatusCode.Created, await refilled.Content.ReadAsStringAsync());
        (await MainTargetIdsAsync(linkId)).ShouldBe([tripId]);

        // An arriving member that claims the marker still takes it — the handover is an
        // explicit act, and only the silent case defaults to the member already there.
        var claimed = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", cave, isMain: true, sortOrder: 2));
        claimed.StatusCode.ShouldBe(HttpStatusCode.Created, await claimed.Content.ReadAsStringAsync());
        (await MainTargetIdsAsync(linkId)).ShouldBe([cave]);
    }

    /// <summary>
    /// A role is not one link — it is every link of that relation the trip is in. A link
    /// answers to whoever authored it, so a second person recording the same role on the
    /// same trip writes a second link rather than amending the first, and nothing refuses
    /// that. The panel asked for the role therefore returns both, and what the role names
    /// is the union of their targets: a reader that assumed a single link per role would
    /// silently hide the second author's caves.
    /// </summary>
    [Fact]
    public async Task A_role_recorded_by_two_authors_is_two_links_the_panel_lists_together()
    {
        var mine = await CreateCaveAsync(owner, "Union Cave", "authenticated");
        var theirs = await CreateCaveAsync(owner, "Union Cave Too", "authenticated");
        var tripId = await CreateTripLogAsync("Union trip", mine, "authenticated");
        var surveyed = await RelationIdAsync("trip-surveyed");

        var authored = await CreateTypedLinkAsync(
            owner, surveyed,
            Member("tripLog", tripId, isMain: true),
            Member("feature", mine, sortOrder: 1));

        // The second editor reads the role whole and may still not amend it…
        (await editor2.PostAsJsonAsync(
                $"/api/v1/reslinks/{authored}/members", Member("feature", theirs, sortOrder: 2)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // …so what they can record is a second link of the same role, which is accepted.
        var second = await CreateTypedLinkAsync(
            editor2, surveyed,
            Member("tripLog", tripId, isMain: true),
            Member("feature", theirs, sortOrder: 1));
        second.ShouldNotBe(authored);

        var listed = await PanelLinkIdsAsync(owner, "tripLog", tripId, "trip-surveyed");
        listed.Count.ShouldBe(2);
        listed.ShouldContain(authored);
        listed.ShouldContain(second);

        var named = new List<Guid>();
        foreach (var linkId in listed)
        {
            named.AddRange((await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray()
                .Select(m => m.GetProperty("targetId").GetGuid())
                .Where(id => id != tripId));
        }

        named.OrderBy(id => id).ToList().ShouldBe(new[] { mine, theirs }.OrderBy(id => id).ToList());
    }

    /// <summary>
    /// Who may amend a role: nobody unsigned, and among signed-in callers the link's
    /// author and full administrators — the ordinary law of this mechanism, which trip
    /// roles inherit rather than replace. A second editor of the same trip is refused,
    /// which is a real limit on how a trip's roles are curated and is stated here rather
    /// than discovered: their route is a second link of the same role, whose targets join
    /// the first link's when the role is read.
    /// </summary>
    [Fact]
    public async Task Role_membership_answers_to_whoever_authored_the_link_and_to_administrators()
    {
        var cave = await CreateCaveAsync(owner, "Authored Cave", "authenticated");
        var tripId = await CreateTripLogAsync("Authored trip", cave, "authenticated");
        var dug = await RelationIdAsync("trip-dug");
        var linkId = await CreateTypedLinkAsync(
            owner, dug, Member("tripLog", tripId, isMain: true), Member("feature", cave, sortOrder: 1));

        var target = await CreateCaveAsync(owner, "Authored Cave Too", "authenticated");
        using (var anonymous = factory.CreateClient())
        {
            (await anonymous.PostAsJsonAsync(
                    $"/api/v1/reslinks/{linkId}/members", Member("feature", target, sortOrder: 2)))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Another editor reads the link whole and may still not amend it.
        (await editor2.GetAsync($"/api/v1/reslinks/{linkId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor2.PostAsJsonAsync(
                $"/api/v1/reslinks/{linkId}/members", Member("feature", target, sortOrder: 2)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A viewer neither authored it nor administers anything.
        (await viewer.PostAsJsonAsync(
                $"/api/v1/reslinks/{linkId}/members", Member("feature", target, sortOrder: 2)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The author may…
        var byAuthor = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", target, sortOrder: 2));
        byAuthor.StatusCode.ShouldBe(HttpStatusCode.Created, await byAuthor.Content.ReadAsStringAsync());

        // …and so may a full administrator, over a link they did not author.
        var another = await CreateCaveAsync(owner, "Authored Cave Three", "authenticated");
        var byAdmin = await admin.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members", Member("feature", another, sortOrder: 3));
        byAdmin.StatusCode.ShouldBe(HttpStatusCode.Created, await byAdmin.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// What a role can actually name. The trip's own cave list is caves and nothing else —
    /// its write path says so — but a trip visits entrances, springs, digs and surface
    /// features, and the role mechanism admits every feature kind plus the other worlds a
    /// trip sensibly points at. Both halves are asserted together so the difference is
    /// visible rather than assumed.
    /// </summary>
    [Fact]
    public async Task A_role_names_any_feature_a_trip_reaches_where_the_trips_cave_list_names_only_caves()
    {
        var cave = await CreateCaveAsync(owner, "Reach Cave", "authenticated");
        var entrance = await CreateEntranceAsync(cave);
        var spring = await CreateGenericFeatureAsync(owner, "Reach Spring", "authenticated");
        var tripId = await CreateTripLogAsync("Reach trip", cave, "authenticated");
        var groupId = await CreateCavingGroupAsync($"Reach club {Guid.NewGuid():N}"[..40]);
        var (documentId, _) = await UploadDocumentAsync("reach.txt");
        var earlierTrip = await CreateTripLogAsync("Earlier trip", cave, "authenticated");
        var visited = await RelationIdAsync("trip-visited");

        // The trip's cave list refuses the entrance — that predicate is the trip write
        // path's, and the role must not inherit it.
        var asCaveList = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = "Entrance as a cave",
            tripDate = "2026-05-02",
            caveIds = new[] { entrance },
            participants = Array.Empty<object>(),
            visibility = "authenticated",
        });
        asCaveList.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(asCaveList)).ShouldBe("trip_log.cave_not_found");

        // The role names all three feature kinds, and a document and a club besides.
        var linkId = await CreateTypedLinkAsync(
            owner, visited,
            Member("tripLog", tripId, isMain: true),
            Member("feature", cave, sortOrder: 1),
            Member("feature", entrance, sortOrder: 2),
            Member("feature", spring, sortOrder: 3),
            Member("document", documentId, sortOrder: 4),
            Member("cavingGroup", groupId, sortOrder: 5));

        var members = (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray().ToList();
        members.Count.ShouldBe(6);
        members.Select(m => m.GetProperty("targetId").GetGuid())
            .ShouldBe([tripId, cave, entrance, spring, documentId, groupId], ignoreOrder: true);

        // A second trip in the same role is admitted too — which is exactly why the roles
        // are directed: without a main member, "which trip did this" has no answer here.
        var follows = await RelationIdAsync("trip-follows-on-from");
        var chained = await CreateTypedLinkAsync(
            owner, follows,
            Member("tripLog", tripId, isMain: true),
            Member("tripLog", earlierTrip, sortOrder: 1));
        (await PanelLinkIdsAsync(owner, "tripLog", earlierTrip, "trip-follows-on-from")).ShouldBe([chained]);
    }

    /// <summary>
    /// Every target type a role can name resolves through a registered resolver. The
    /// directory throws when asked for a linkable type nobody registered — a failed
    /// request rather than a refused one — so the coverage is asserted directly against
    /// the rule that decides which types are linkable, and again through the route that
    /// reaches a resolver from outside.
    /// </summary>
    [Fact]
    public async Task Every_target_type_a_role_can_name_answers_through_a_registered_resolver()
    {
        var linkable = Enum.GetValues<AttachedEntityType>().Where(ResLinkRules.IsLinkableType).ToList();
        linkable.ShouldContain(AttachedEntityType.TripLog);

        using (var scope = factory.Services.CreateScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<ResLinkTargetDirectory>();
            Should.NotThrow(() => directory.Of(null));
            foreach (var type in linkable)
            {
                Should.NotThrow(() => directory.Of(type), type.ToString());
            }
        }

        // …and from outside, where an unregistered resolver would surface as a failure
        // rather than an answer.
        foreach (var name in linkable
            .Select(t => JsonNamingPolicy.CamelCase.ConvertName(t.ToString()))
            .Append(ResLinkTargets.FeatureName))
        {
            var response = await owner.GetAsync($"/api/v1/reslinks/targets/search?type={name}&q=a");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, name);
        }

        // The rule and the wire vocabulary agree about what is linkable, in both
        // directions: a type the rule refuses is not parseable either.
        foreach (var refused in Enum.GetValues<AttachedEntityType>().Where(t => !ResLinkRules.IsLinkableType(t)))
        {
            ResLinkTargets.TryParse(JsonNamingPolicy.CamelCase.ConvertName(refused.ToString()), out _)
                .ShouldBeFalse(refused.ToString());
        }
    }

    /// <summary>
    /// The real ceiling of a role: a link holds a hundred members and the trip is one of
    /// them, so a role carries ninety-nine targets. Fine for a trip; worth knowing before
    /// anything rolls several trips up into one link.
    /// </summary>
    [Fact]
    public async Task A_role_carries_the_trip_and_ninety_nine_targets_and_refuses_the_hundredth()
    {
        var cave = await CreateCaveAsync(owner, "Ceiling Cave", "authenticated");
        var tripId = await CreateTripLogAsync("Ceiling trip", cave, "authenticated");
        var photographed = await RelationIdAsync("trip-photographed");

        var targets = await SeedGenericFeaturesAsync(ResLinkRules.MaxMembers);
        var members = new List<object> { Member("tripLog", tripId, isMain: true) };
        members.AddRange(targets[..(ResLinkRules.MaxMembers - 1)]
            .Select((id, i) => Member("feature", id, sortOrder: i + 1)));
        members.Count.ShouldBe(ResLinkRules.MaxMembers);

        var linkId = await CreateTypedLinkAsync(owner, photographed, [.. members]);
        (await GetLinkAsync(linkId)).GetProperty("members").GetArrayLength()
            .ShouldBe(ResLinkRules.MaxMembers);

        var overflow = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members",
            Member("feature", targets[^1], sortOrder: ResLinkRules.MaxMembers));
        overflow.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(overflow)).ShouldBe("reslink.member.limit_reached");

        // One removed, one admitted: the ceiling is a count, not a closed link.
        var freed = await MemberIdOfAsync(linkId, targets[0]);
        (await owner.DeleteAsync($"/api/v1/reslinks/{linkId}/members/{freed}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var admitted = await owner.PostAsJsonAsync(
            $"/api/v1/reslinks/{linkId}/members",
            Member("feature", targets[^1], sortOrder: ResLinkRules.MaxMembers));
        admitted.StatusCode.ShouldBe(HttpStatusCode.Created, await admitted.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A role link discloses nothing its members' own rules withhold: a private feature in
    /// a role travels as a bare member, named nowhere in the answer, while its owner reads
    /// the same link whole. The outsider is a viewer with no grant of any kind — an editor
    /// reads past visibility by design and would prove nothing.
    /// </summary>
    [Fact]
    public async Task A_role_names_a_private_target_to_nobody_who_could_not_already_read_it()
    {
        var cave = await CreateCaveAsync(owner, "Shared Cave", "authenticated");
        var secretName = $"Private dig {Guid.NewGuid():N}"[..40];
        var secret = await CreateGenericFeatureAsync(owner, secretName, "private");
        var tripId = await CreateTripLogAsync("Digging trip", cave, "authenticated");
        var dug = await RelationIdAsync("trip-dug");
        var linkId = await CreateTypedLinkAsync(
            owner, dug,
            Member("tripLog", tripId, isMain: true),
            Member("feature", cave, sortOrder: 1),
            Member("feature", secret, sortOrder: 2));

        var byOwner = await GetLinkAsync(linkId);
        byOwner.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetId").GetGuid() == secret)
            .GetProperty("display").GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();

        var response = await viewer.GetAsync($"/api/v1/reslinks/{linkId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(secretName);
        var seen = JsonDocument.Parse(raw).RootElement.GetProperty("members").EnumerateArray().ToList();
        seen.Count.ShouldBe(3);
        var bare = seen.Single(m => m.GetProperty("targetId").GetGuid() == secret);
        bare.GetProperty("display").ValueKind.ShouldBe(JsonValueKind.Null);
        // The readable sibling proves the viewer's read is working at all.
        seen.Single(m => m.GetProperty("targetId").GetGuid() == cave)
            .GetProperty("display").GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- helpers ---------------------------------------------------------------------

    /// <summary>The roles a trip plays towards the things it names.</summary>
    private static readonly string[] TripRoleCodes =
    [
        "trip-work-area", "trip-objective", "trip-visited", "trip-surveyed", "trip-discovered",
        "trip-dug", "trip-photographed", "trip-searched-not-found", "trip-lead", "trip-follows-on-from",
    ];

    /// <summary>A member payload in the create/add wire shape.</summary>
    private static object Member(
        string targetType,
        Guid targetId,
        bool isMain = false,
        int sortOrder = 0,
        string anchorKind = "whole",
        object? anchor = null,
        Guid? anchorFileId = null) => new
    {
        targetType,
        targetId,
        isMain,
        sortOrder,
        note = (string?)null,
        anchorKind,
        anchor,
        anchorFileId,
    };

    /// <summary>Creates an untyped link from members and returns its id.</summary>
    private Task<Guid> CreateLinkAsync(HttpClient client, params object[] members) =>
        CreateTypedLinkAsync(client, null, members);

    /// <summary>Creates a link of one relation type and returns its id. A directed
    /// relation wants exactly one member marked main once the link has two, so callers
    /// pass one.</summary>
    private async Task<Guid> CreateTypedLinkAsync(
        HttpClient client, long? relationTypeId, params object[] members)
    {
        var response = await client.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId,
            description = (string?)null,
            members,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// The link ids one panel lists, optionally asked for a single relation, with the
    /// reported total asserted against the rows actually returned — the total doubles as
    /// the badge count, so the two agreeing is part of every answer.
    /// </summary>
    private static async Task<List<Guid>> PanelLinkIdsAsync(
        HttpClient client, string targetType, Guid targetId, string? relation = null)
    {
        var route = $"/api/v1/reslinks/for-target?type={targetType}&id={targetId}"
            + (relation is null ? string.Empty : $"&relation={Uri.EscapeDataString(relation)}");
        var response = await client.GetAsync(route);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var panel = JsonDocument.Parse(payload).RootElement;
        var ids = panel.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();
        panel.GetProperty("totalItems").GetInt32().ShouldBe(ids.Count, route);
        return ids;
    }

    /// <summary>Asserts that creating an untyped link with this single member is refused
    /// with the given code.</summary>
    private async Task ShouldRefuseMemberAsync(object member, string expectedCode)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = (string?)null,
            members = new[] { member },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await ReadCodeAsync(response)).ShouldBe(expectedCode);
    }

    /// <summary>Asserts that creating a link under this relation with these members is
    /// refused with the given code.</summary>
    private async Task ShouldRefuseCreateAsync(long? relationTypeId, string expectedCode, params object[] members)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId,
            description = (string?)null,
            members,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await ReadCodeAsync(response)).ShouldBe(expectedCode);
    }

    private async Task<JsonElement> GetLinkAsync(Guid linkId)
    {
        var response = await owner.GetAsync($"/api/v1/reslinks/{linkId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    /// <summary>The member id targeting the given feature, from the creator's read.</summary>
    private async Task<Guid> MemberIdOfAsync(Guid linkId, Guid featureId) =>
        (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetId").GetGuid() == featureId)
            .GetProperty("id").GetGuid();

    /// <summary>Target ids of the members currently marked main.</summary>
    private async Task<List<Guid>> MainTargetIdsAsync(Guid linkId) =>
        [.. (await GetLinkAsync(linkId)).GetProperty("members").EnumerateArray()
            .Where(m => m.GetProperty("isMain").GetBoolean())
            .Select(m => m.GetProperty("targetId").GetGuid())];

    /// <summary>The id of a seeded relation type, by code.</summary>
    private async Task<long> RelationIdAsync(string code)
    {
        var listed = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reslinks/relation-types");
        return listed.EnumerateArray()
            .Single(r => r.GetProperty("code").GetString() == code)
            .GetProperty("id").GetInt64();
    }

    private async Task<Guid> CreateCaveAsync(HttpClient client, string name, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility,
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateGenericFeatureAsync(HttpClient client, string name, string visibility)
    {
        var response = await client.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {Guid.NewGuid():N}"[..40],
            featureTypeId = genericTypeId,
            geometry = new { type = "Point", coordinates = new[] { 25.81, 45.81 } },
            locationProtected = false,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>An entrance of a cave, returning the entrance's own feature id.</summary>
    private async Task<Guid> CreateEntranceAsync(Guid caveId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.82, 45.82 } },
            positionQuality = "Gps",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Publicly readable generic features, written straight to the database because the
    /// test that needs a hundred of them is about the membership ceiling and not about the
    /// feature write path — which the rest of the suite exercises through its own route.
    /// </summary>
    private async Task<List<Guid>> SeedGenericFeaturesAsync(int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var features = Enumerable.Range(0, count).Select(i => new Feature
        {
            Kind = FeatureKind.Generic,
            FeatureTypeId = genericTypeId,
            Name = $"Ceiling target {i} {Guid.NewGuid():N}"[..40],
            Geom = new Point(25.83, 45.83) { SRID = 4326 },
            OwnerUserId = ownerId,
            Visibility = Visibility.Public,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
        foreach (var feature in features)
        {
            feature.AncestorIds = [feature.Id];
        }

        db.Features.AddRange(features);

        // Containment is stored twice: the flattened array above, and one closure row per
        // ancestor — including the depth-0 row naming the feature itself, which a root with
        // no containment edges has as its only one. The database-wide integrity check
        // compares both against the edges, so seeding only the array would leave these rows
        // diverging for the rest of the run and fail every later check, not just this test.
        db.FeatureAncestors.AddRange(features.Select(
            f => new FeatureAncestor { FeatureId = f.Id, AncestorId = f.Id }));
        await db.SaveChangesAsync();
        return [.. features.Select(f => f.Id)];
    }

    private async Task<Guid> CreateCabinetAsync(string name, Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/cabinets", new
        {
            name,
            description = (string?)null,
            parentId,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A location-protected, otherwise readable cave — visible without exact view.</summary>
    private async Task<Guid> CreateProtectedCaveAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = true,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripLogAsync(string title, Guid caveId, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-05-01",
            caveIds = new[] { caveId },
            participants = Array.Empty<object>(),
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateMapViewAsync(string name, string visibility)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/map-views/", new
        {
            name,
            description = (string?)null,
            config = new { },
            isHome = false,
            cavingGroupId = (Guid?)null,
            visibility,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads an empty GeoJSON as the owner; a null visibility keeps the
    /// upload default (private), anything else is set right after.</summary>
    private async Task<Guid> UploadGeofileAsync(string fileName, string? visibility)
    {
        var content = new ByteArrayContent("""{"type":"FeatureCollection","features":[]}"""u8.ToArray());
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync("/api/v1/geofiles", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var geofile = JsonDocument.Parse(payload).RootElement;
        var id = geofile.GetProperty("id").GetGuid();
        if (visibility is not null)
        {
            var updated = await owner.PutAsJsonAsync($"/api/v1/geofiles/{id}", new
            {
                name = geofile.GetProperty("name").GetString(),
                description = (string?)null,
                style = (object?)null,
                cavingGroupId = (Guid?)null,
                visibility,
            });
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        }

        return id;
    }

    private async Task<Guid> CreateSurveyModelAsync(Guid caveId, string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes($"LOX-{Guid.NewGuid():N}"));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaverAsync(string fullName)
    {
        var response = await keeper.PostAsJsonAsync("/api/v1/cavers/", new { fullName });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Mints a GPS point as a new member and returns the feature's id.</summary>
    private static async Task<Guid> AddGeoPointAsync(
        HttpClient client, Guid linkId, double lon, double lat, int sortOrder, string? visibility)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/reslinks/{linkId}/members", new
        {
            targetType = (string?)null,
            targetId = (Guid?)null,
            newGeoPoint = new { lon, lat, z = (double?)null, name = $"Point {sortOrder}", visibility },
            isMain = false,
            sortOrder,
            note = (string?)null,
            anchorKind = "whole",
            anchor = (object?)null,
            anchorFileId = (Guid?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("targetId").GetGuid();
    }

    /// <summary>The audience a minted point actually carries, read off the row.</summary>
    private async Task AssertPointAudienceAsync(Guid featureId, Visibility expected, Guid? expectedCavingGroupId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var feature = await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId);
        feature.OwnerUserId.ShouldBe(ownerId);
        feature.Visibility.ShouldBe(expected);
        feature.CavingGroupId.ShouldBe(expectedCavingGroupId);
    }

    private async Task<Guid> CreateCavingGroupAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name,
            description = (string?)null,
            website = (string?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Grants a user Read + ViewExactLocation on one feature.</summary>
    private async Task GrantExactViewAsync(Guid featureId, Guid userId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = userId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    /// <summary>Removes the seeded everyone-reads-the-roster entry, returning it for
    /// restoration — the shared fixture database must leave the test as it entered.</summary>
    private async Task<AccessEntry> RemoveAllUsersCaversReadAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var allUsersId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.AllUsersSlug)
            .Select(g => g.Id)
            .SingleAsync();
        var entry = await db.AccessEntries
            .SingleAsync(e => e.PermissionGroupId == allUsersId && e.Domain == AccessDomain.Cavers);
        db.AccessEntries.Remove(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private async Task RestoreAllUsersCaversReadAsync(AccessEntry removed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            PermissionGroupId = removed.PermissionGroupId,
            Effect = removed.Effect,
            Domain = removed.Domain,
            Actions = removed.Actions,
            ScopeKind = removed.ScopeKind,
        });
        await db.SaveChangesAsync();
    }

    private async Task DenyCavingGroupReadAsync(Guid groupId, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = AccessDomain.CavingGroups,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = groupId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<Guid>> SearchIdsAsync(HttpClient client, string type, string query)
    {
        var hits = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/reslinks/targets/search?type={type}&q={Uri.EscapeDataString(query)}");
        return [.. hits.EnumerateArray().Select(h => h.GetProperty("id").GetGuid())];
    }

    /// <summary>The document member at one sort position, as one caller sees it.</summary>
    private async Task<JsonElement> DocumentMemberAsync(HttpClient client, Guid linkId, int sortOrder = 1)
    {
        var response = await client.GetAsync($"/api/v1/reslinks/{linkId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("targetType").GetString() == "document"
                && m.GetProperty("sortOrder").GetInt32() == sortOrder);
    }

    private async Task<Guid> UploadVersionAsync(Guid fileId, string fileName, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await owner.PostAsync($"/api/v1/files/{fileId}/versions", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Inserts one member row straight into the database — the schema backstops
    /// exist for writers the endpoints' own checks never see.</summary>
    private async Task DirectInsertAsync(ResLinkMember member)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.ResLinkMembers.Add(member);
        await db.SaveChangesAsync();
    }

    private async Task ShouldRejectDirectInsertAsync(ResLinkMember member, string constraintName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.ResLinkMembers.Add(member);
        var rejected = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        rejected.InnerException.ShouldBeOfType<PostgresException>()
            .ConstraintName.ShouldBe(constraintName);
    }

    /// <summary>Uploads a small text file as the owner and returns (documentId, fileId).
    /// The document is born private — exactly what the visibility tests need.</summary>
    private async Task<(Guid DocumentId, Guid FileId)> UploadDocumentAsync(string fileName)
    {
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"contents of {fileName}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var fileId = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return (version.DocumentId, fileId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        editor2?.Dispose();
        viewer?.Dispose();
        admin?.Dispose();
        keeper?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
