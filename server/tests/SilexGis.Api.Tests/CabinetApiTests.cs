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
/// The filing tree over HTTP: cabinets, what is filed in them, and the three things the
/// permission model has to be able to say about them — that filing alone grants nothing,
/// that a rule denying a shelf beats a rule allowing the archive above it, and that filing
/// takes rights on both the document and the cabinet so it can never widen anything.
///
/// Every negative builds the state it claims rather than relying on a default: the refused
/// caller is a Viewer, who holds nothing over documents beyond the built-ins, or a caller
/// facing an explicit deny. Each asserts the matching positive in the same test, so a
/// fixture that quietly stopped working cannot pass for a passing security assertion.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CabinetApiTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — uploads documents, administers the tree
    private HttpClient reader = null!;  // Viewer — nothing over documents until granted
    private HttpClient founder = null!; // Editor — founds the clubs, so nobody else is in one
    private HttpClient anonymous = null!;
    private Guid ownerId;
    private Guid readerId;

    public CabinetApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-cabinets-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cab-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"cab-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cab-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"cab-read-{suffix}@t.local");

        // Creating a caving group enrolls its creator, so clubs are founded by somebody who
        // takes no part in the assertions — otherwise "is not a member" would never hold.
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cab-found-{suffix}@t.local");
        founder = await AuthHelper.BearerClientAsync(factory, $"cab-found-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        founder?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Reading_the_tree_needs_a_signed_in_caller()
    {
        (await anonymous.GetAsync("/api/v1/cabinets")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The tree itself is global structure with no read gate of its own, so the Viewer —
        // who holds nothing at all over documents — still sees it once signed in.
        (await reader.GetAsync("/api/v1/cabinets")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Creating_a_cabinet_takes_the_right_to_write_documents_there()
    {
        var refused = await reader.PostAsJsonAsync("/api/v1/cabinets", NewCabinet("Reader archive"));
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Same request, a caller who does hold document write: the refusal above was the
        // rule, not a broken fixture.
        var allowed = await owner.PostAsJsonAsync("/api/v1/cabinets", NewCabinet("Club archive"));
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_cabinet_is_named_once_among_its_siblings_and_the_name_is_required()
    {
        var blank = await owner.PostAsJsonAsync("/api/v1/cabinets", NewCabinet(" "));
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(blank)).ShouldBe("validation.failed");

        var name = $"Bulletins {Guid.NewGuid():N}"[..24];
        var parent = await CreateCabinetAsync($"Archive {Guid.NewGuid():N}"[..24]);
        (await owner.PostAsJsonAsync("/api/v1/cabinets", NewCabinet(name, parent)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await owner.PostAsJsonAsync("/api/v1/cabinets", NewCabinet(name, parent));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(duplicate)).ShouldBe("cabinet.name_taken");

        // The same name one shelf over is a different cabinet, which is the whole point of
        // naming being local: "1987" sits under many archives.
        var elsewhere = await CreateCabinetAsync($"Other {Guid.NewGuid():N}"[..24]);
        (await owner.PostAsJsonAsync("/api/v1/cabinets", NewCabinet(name, elsewhere)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Filing_a_document_puts_it_on_the_shelf_and_a_subtree_listing_finds_it_below()
    {
        var archive = await CreateCabinetAsync($"Archive {Guid.NewGuid():N}"[..24]);
        var shelf = await CreateCabinetAsync("1987", archive);
        var documentId = await UploadDocumentAsync("survey.txt");

        (await owner.PutAsync($"/api/v1/cabinets/{shelf}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Filing what is already filed states the same fact, so it answers the same way.
        (await owner.PutAsync($"/api/v1/cabinets/{shelf}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListedIdsAsync(owner, shelf)).ShouldContain(documentId);
        (await ListedIdsAsync(owner, archive)).ShouldNotContain(documentId);
        (await ListedIdsAsync(owner, archive, includeSubtree: true)).ShouldContain(documentId);

        // Filing reads back from the document side too — the shelf it was put on, not the
        // archive above it — because that is what the control on the document panel edits.
        (await FiledInAsync(documentId)).ShouldBe([shelf]);

        (await owner.DeleteAsync($"/api/v1/cabinets/{shelf}/documents/{documentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListedIdsAsync(owner, shelf)).ShouldNotContain(documentId);
        (await FiledInAsync(documentId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Membership_grants_nothing_until_a_rule_names_the_cabinet()
    {
        var archive = await CreateCabinetAsync($"Grantable {Guid.NewGuid():N}"[..24]);
        var documentId = await UploadDocumentAsync("filed.txt");
        (await owner.PutAsync($"/api/v1/cabinets/{archive}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Filed, and the Viewer holds nothing that names it — so the filing has moved no
        // access at all: neither the listing nor the document itself answers.
        (await ListedIdsAsync(reader, archive)).ShouldNotContain(documentId);
        (await reader.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Cabinet, archive);

        // Same filing, same caller, one rule later.
        (await ListedIdsAsync(reader, archive)).ShouldContain(documentId);
        (await reader.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deny_on_a_shelf_beats_an_allow_on_the_archive_above_it()
    {
        var archive = await CreateCabinetAsync($"Whole {Guid.NewGuid():N}"[..24]);
        var restricted = await CreateCabinetAsync("Restricted", archive);

        var open = await UploadDocumentAsync("open.txt");
        var secret = await UploadDocumentAsync("secret.txt");
        (await owner.PutAsync($"/api/v1/cabinets/{archive}/documents/{open}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.PutAsync($"/api/v1/cabinets/{restricted}/documents/{secret}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Cabinet, archive);
        await GrantAsync(readerId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Cabinet, restricted);

        // Both rules decide at the same level, and a deny among them is final — so the
        // allow reaches everything in the archive except what the deny names.
        var listed = await ListedIdsAsync(reader, archive, includeSubtree: true);
        listed.ShouldContain(open);
        listed.ShouldNotContain(secret);
        (await reader.GetAsync($"/api/v1/documents/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/documents/{secret}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_shelf_label_counts_what_its_listing_shows_for_a_caller_scoped_to_a_cabinet()
    {
        var archive = await CreateCabinetAsync($"Counted {Guid.NewGuid():N}"[..24]);
        var restricted = await CreateCabinetAsync("Restricted", archive);

        var open = await UploadDocumentAsync("counted-open.txt");
        var alsoOpen = await UploadDocumentAsync("counted-open-2.txt");
        var secret = await UploadDocumentAsync("counted-secret.txt");
        foreach (var (cabinet, document) in new[] { (archive, open), (archive, alsoOpen), (restricted, secret) })
        {
            (await owner.PutAsync($"/api/v1/cabinets/{cabinet}/documents/{document}", null))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // A cabinet-scoped rule is the only kind that makes the read walk ask about filings at
        // all, so it is the only caller for whom the count and the listing are computed the
        // hard way. A Viewer holding nothing, or an Editor who reads past everything, would
        // exercise neither.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Cabinet, archive);
        await GrantAsync(readerId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Cabinet, restricted);

        // The positive: the allow reaches both documents on the archive, and the number drawn
        // on the shelf says two because that is what opening the shelf shows.
        (await ListedIdsAsync(reader, archive)).Count.ShouldBe(2);
        (await TreeCountAsync(reader, archive)).ShouldBe(2);
        (await CabinetCountAsync(reader, archive)).ShouldBe(2);

        // The negative, built explicitly rather than inherited from a default: the deny names
        // the shelf, so its one document is neither listed nor counted. A count taken over
        // everything filed would have read one here and stated exactly what was withheld.
        (await ListedIdsAsync(reader, restricted)).ShouldBeEmpty();
        (await TreeCountAsync(reader, restricted)).ShouldBe(0);
        (await CabinetCountAsync(reader, restricted)).ShouldBe(0);

        // And the fixture really does hold that document, so the zero above is the rule and
        // not an empty shelf.
        (await ListedIdsAsync(owner, restricted)).ShouldContain(secret);
        (await TreeCountAsync(owner, restricted)).ShouldBe(1);
    }

    [Fact]
    public async Task Filing_takes_rights_on_the_document_and_on_the_cabinet_alike()
    {
        var archive = await CreateCabinetAsync($"Guarded {Guid.NewGuid():N}"[..24]);
        var documentId = await UploadDocumentAsync("moving.txt");

        // Write on the document alone: the caller can edit it but holds nothing where they
        // are trying to put it, so filing would be filing under somebody else's rules.
        await GrantAsync(
            readerId, AccessEffect.Allow, AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Object, documentId);
        var refused = await reader.PutAsync($"/api/v1/cabinets/{archive}/documents/{documentId}", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await GrantAsync(
            readerId, AccessEffect.Allow, AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Cabinet, archive);
        (await reader.PutAsync($"/api/v1/cabinets/{archive}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Filing_a_document_the_caller_cannot_read_answers_as_if_it_were_not_there()
    {
        var archive = await CreateCabinetAsync($"Probe {Guid.NewGuid():N}"[..24]);
        var documentId = await UploadDocumentAsync("private.txt");

        // The Viewer holds the cabinet but nothing over the document, so the document is
        // answered as nonexistent rather than as forbidden — filing is never an oracle.
        await GrantAsync(
            readerId, AccessEffect.Allow, AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Cabinet, archive);
        var refused = await reader.PutAsync($"/api/v1/cabinets/{archive}/documents/{documentId}", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("document.not_found");

        // The owner of the same document, at the same cabinet, files it — the refusal was
        // about the document, not about the request.
        (await owner.PutAsync($"/api/v1/cabinets/{archive}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_cabinet_a_rule_points_at_or_that_still_holds_something_is_not_deleted()
    {
        var archive = await CreateCabinetAsync($"Kept {Guid.NewGuid():N}"[..24]);
        var shelf = await CreateCabinetAsync("Inner", archive);

        var withChild = await owner.DeleteAsync($"/api/v1/cabinets/{archive}");
        withChild.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(withChild)).ShouldBe("cabinet.not_empty");

        var documentId = await UploadDocumentAsync("held.txt");
        (await owner.PutAsync($"/api/v1/cabinets/{shelf}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withDocument = await owner.DeleteAsync($"/api/v1/cabinets/{shelf}");
        withDocument.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(withDocument)).ShouldBe("cabinet.not_empty");

        (await owner.DeleteAsync($"/api/v1/cabinets/{shelf}/documents/{documentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await GrantAsync(readerId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Cabinet, shelf);
        var anchored = await owner.DeleteAsync($"/api/v1/cabinets/{shelf}");
        anchored.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(anchored)).ShouldBe("cabinet.in_use");

        // Emptied and unanchored, the same cabinet goes.
        await RemoveEntriesAsync(shelf);
        (await owner.DeleteAsync($"/api/v1/cabinets/{shelf}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Moving_a_cabinet_carries_its_shelves_and_refuses_to_enter_its_own_subtree()
    {
        var archive = await CreateCabinetAsync($"Moving {Guid.NewGuid():N}"[..24]);
        var shelf = await CreateCabinetAsync("Shelf", archive);
        var leaf = await CreateCabinetAsync("Leaf", shelf);
        var elsewhere = await CreateCabinetAsync($"Elsewhere {Guid.NewGuid():N}"[..24]);

        var cycle = await owner.PutAsJsonAsync($"/api/v1/cabinets/{archive}", NewCabinet("Moving", leaf));
        cycle.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(cycle)).ShouldBe("cabinet.cycle");

        (await owner.PutAsJsonAsync($"/api/v1/cabinets/{shelf}", NewCabinet("Shelf", elsewhere)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The whole subtree came along: the leaf's ancestry now names its new archive, which
        // is what a rule scoped to that archive matches on.
        var tree = await owner.GetFromJsonAsync<JsonElement>("/api/v1/cabinets");
        var moved = tree.EnumerateArray().First(c => c.GetProperty("id").GetGuid() == leaf);
        var ancestors = moved.GetProperty("ancestorIds").EnumerateArray().Select(a => a.GetGuid()).ToList();
        ancestors.ShouldBe([elsewhere, shelf, leaf]);
    }

    // ---- Authoring a cabinet-scoped rule ---------------------------------------------

    /// <summary>
    /// The no-amplification bound judges the author at the cabinet the rule names — and a
    /// cabinet means its whole ancestry, exactly as it does everywhere else. Without that,
    /// a deny naming an archive would be invisible one shelf down and the author could hand
    /// out, on a shelf, a right the same archive denies them.
    /// </summary>
    [Fact]
    public async Task A_deny_on_an_archive_stops_its_holder_authoring_a_rule_on_a_shelf_inside_it()
    {
        var archive = await CreateCabinetAsync($"Denied {Guid.NewGuid():N}"[..24]);
        var shelf = await CreateCabinetAsync("Bulletins", archive);
        var unrelated = await CreateCabinetAsync($"Open {Guid.NewGuid():N}"[..24]);
        var ruleset = await CreateRulesetAsync();

        // The author may read documents everywhere except inside that archive, and may
        // write this one ruleset.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All);
        await GrantAsync(readerId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Cabinet, archive);
        await GrantAsync(
            readerId, AccessEffect.Allow, AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Object, ruleset, AccessDomain.PermissionGroups);

        var exceeds = await WriteRuleAsync(reader, ruleset, shelf);
        exceeds.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await exceeds.Content.ReadAsStringAsync());
        (await ReadCodeAsync(exceeds)).ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);

        // The identical request on a cabinet the deny does not reach goes through, so the
        // refusal above was the deny rather than a fixture that grants nothing.
        (await WriteRuleAsync(reader, ruleset, unrelated))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The same fact in the other direction: an archive is a delegable unit.</summary>
    [Fact]
    public async Task Whoever_holds_an_archive_may_author_a_rule_on_a_shelf_inside_it_and_nowhere_else()
    {
        var archive = await CreateCabinetAsync($"Held {Guid.NewGuid():N}"[..24]);
        var shelf = await CreateCabinetAsync("1987", archive);
        var elsewhere = await CreateCabinetAsync($"Foreign {Guid.NewGuid():N}"[..24]);
        var ruleset = await CreateRulesetAsync();

        // Read over documents only inside that archive — nothing domain-wide.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Cabinet, archive);
        await GrantAsync(
            readerId, AccessEffect.Allow, AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Object, ruleset, AccessDomain.PermissionGroups);

        var inside = await WriteRuleAsync(reader, ruleset, shelf);
        inside.StatusCode.ShouldBe(HttpStatusCode.OK, await inside.Content.ReadAsStringAsync());

        var outside = await WriteRuleAsync(reader, ruleset, elsewhere);
        outside.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(outside)).ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);
    }

    /// <summary>
    /// Naming the document instead of the shelf is not a way around the shelf's rule: a
    /// document carries where it is filed into the bound as well.
    /// </summary>
    [Fact]
    public async Task A_deny_on_an_archive_reaches_a_rule_written_against_a_document_filed_in_it()
    {
        var archive = await CreateCabinetAsync($"Sealed {Guid.NewGuid():N}"[..24]);
        var shelf = await CreateCabinetAsync("Inner", archive);
        var filed = await UploadDocumentAsync("filed-away.txt");
        var loose = await UploadDocumentAsync("on-no-shelf.txt");
        (await owner.PutAsync($"/api/v1/cabinets/{shelf}/documents/{filed}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var ruleset = await CreateRulesetAsync();

        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All);
        await GrantAsync(readerId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Cabinet, archive);
        await GrantAsync(
            readerId, AccessEffect.Allow, AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Object, ruleset, AccessDomain.PermissionGroups);

        var exceeds = await WriteRuleAsync(reader, ruleset, filed, AccessScopeKind.Object);
        exceeds.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await exceeds.Content.ReadAsStringAsync());
        (await ReadCodeAsync(exceeds)).ShouldBe(AccessEntryRules.ExceedsOwnRightsCode);

        // A document on no shelf at all is untouched by the archive's deny.
        (await WriteRuleAsync(reader, ruleset, loose, AccessScopeKind.Object))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- The document's own audience and club binding -------------------------------

    [Fact]
    public async Task Raising_a_documents_visibility_is_a_write_on_it_and_opens_it_to_everyone_signed_in()
    {
        var documentId = await UploadDocumentAsync("audience.txt");

        // Uploaded private: the Viewer, who holds nothing over documents, cannot reach it.
        (await reader.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A Viewer cannot widen it either — that edit is an ordinary document write.
        var refused = await reader.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = "Taken over",
            documentTypeId = (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var raised = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = "Open bulletin",
            documentTypeId = (long?)null,
            metadata = (object?)null,
            visibility = "authenticated",
            cavingGroupId = (Guid?)null,
        });
        raised.StatusCode.ShouldBe(HttpStatusCode.OK, await raised.Content.ReadAsStringAsync());
        (await ReadJsonAsync(raised)).GetProperty("visibility").GetString().ShouldBe("authenticated");

        (await reader.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Binding_a_document_to_a_club_takes_more_than_write_on_it()
    {
        var cavingGroupId = await CreateCavingGroupAsync();
        var documentId = await UploadDocumentAsync("club.txt");

        // The owner may write the document and is refused anyway: binding hands the club's
        // members whatever their ruleset grants over its content, so it needs the club.
        var refused = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = "Club bulletin",
            documentTypeId = (long?)null,
            metadata = (object?)null,
            visibility = "private",
            cavingGroupId,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("access.caving_group_binding_forbidden");

        await AddToCavingGroupAsync(cavingGroupId, ownerId);

        var bound = await owner.PutAsJsonAsync($"/api/v1/documents/{documentId}", new
        {
            title = "Club bulletin",
            documentTypeId = (long?)null,
            metadata = (object?)null,
            visibility = "cavingGroup",
            cavingGroupId,
        });
        bound.StatusCode.ShouldBe(HttpStatusCode.OK, await bound.Content.ReadAsStringAsync());
        (await ReadJsonAsync(bound)).GetProperty("cavingGroupId").GetGuid().ShouldBe(cavingGroupId);
    }

    // ---- The club's starter ruleset covers its documents ----------------------------

    [Fact]
    public async Task A_club_member_can_upload_and_read_the_clubs_documents()
    {
        var cavingGroupId = await CreateCavingGroupAsync();

        // Before joining, the Viewer holds no right to add a document anywhere — including
        // one named for this club.
        var refused = await UploadAsync(reader, "before.txt", cavingGroupId);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await AddToCavingGroupAsync(cavingGroupId, readerId);

        var accepted = await UploadAsync(reader, "after.txt", cavingGroupId);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());

        // And the club's own rules reach it: a second member reads what the first uploaded.
        var documentId = await DocumentIdOfAsync((await ReadJsonAsync(accepted)).GetProperty("id").GetGuid());
        await AddToCavingGroupAsync(cavingGroupId, ownerId);
        (await owner.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static object NewCabinet(string name, Guid? parentId = null) =>
        new { name, description = (string?)null, parentId };

    private async Task<Guid> CreateCabinetAsync(string name, Guid? parentId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/cabinets", NewCabinet(name, parentId));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ListedIdsAsync(HttpClient client, Guid cabinetId, bool includeSubtree = false)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/cabinets/{cabinetId}/documents?includeSubtree={includeSubtree.ToString().ToLowerInvariant()}");
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
    }

    /// <summary>The count this cabinet carries in the whole-tree response.</summary>
    private static async Task<int> TreeCountAsync(HttpClient client, Guid cabinetId)
    {
        var tree = await client.GetFromJsonAsync<JsonElement>("/api/v1/cabinets");
        return tree.EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == cabinetId)
            .GetProperty("documentCount")
            .GetInt32();
    }

    /// <summary>The count the same cabinet carries when fetched on its own.</summary>
    private static async Task<int> CabinetCountAsync(HttpClient client, Guid cabinetId)
    {
        var cabinet = await client.GetFromJsonAsync<JsonElement>($"/api/v1/cabinets/{cabinetId}");
        return cabinet.GetProperty("documentCount").GetInt32();
    }

    private async Task<List<Guid>> FiledInAsync(Guid documentId)
    {
        var response = await owner.GetAsync($"/api/v1/documents/{documentId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await ReadJsonAsync(response)).GetProperty("cabinetIds").EnumerateArray().Select(c => c.GetGuid())];
    }

    private async Task<Guid> UploadDocumentAsync(string fileName)
    {
        var response = await UploadAsync(owner, fileName, cavingGroupId: null);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return await DocumentIdOfAsync(JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string fileName, Guid? cavingGroupId)
    {
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"contents of {fileName}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var url = cavingGroupId is { } id ? $"/api/v1/files/?cavingGroupId={id}&allowDuplicate=true" : "/api/v1/files/?allowDuplicate=true";
        return await client.PostAsync(url, form);
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private async Task<Guid> CreateCavingGroupAsync()
    {
        var response = await founder.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Club {Guid.NewGuid():N}"[..24],
            description = (string?)null,
            website = (string?)null,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AddToCavingGroupAsync(Guid cavingGroupId, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await RosterHelper.AddMemberAsync(db, cavingGroupId, userId);
    }

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring
    /// surface refuses rules handing out more than the author holds, which is exactly what
    /// a fixture needs to do.
    /// </summary>
    private async Task GrantAsync(
        Guid userId,
        AccessEffect effect,
        AccessAction actions,
        AccessScopeKind scopeKind,
        Guid? scopeId = null,
        AccessDomain domain = AccessDomain.Documents)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = domain,
            Actions = actions,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>An empty ruleset with no trustees, written straight into storage — the
    /// authoring surface under test is the one that fills it, not the one that makes it.</summary>
    private async Task<Guid> CreateRulesetAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var group = new PermissionGroup { Name = $"Cabinet rules {suffix}", Slug = $"cabinet-rules-{suffix}" };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    /// <summary>
    /// Authors one allow-read rule over documents through the real ruleset editor, which is
    /// where the no-amplification bound is applied.
    /// </summary>
    private static Task<HttpResponseMessage> WriteRuleAsync(
        HttpClient author, Guid rulesetId, Guid anchorId, AccessScopeKind scopeKind = AccessScopeKind.Cabinet) =>
        author.PutAsJsonAsync($"/api/v1/permission-groups/{rulesetId}/entries", new
        {
            entries = new[]
            {
                new
                {
                    effect = "allow",
                    domain = "documents",
                    actions = "read",
                    scopeKind = scopeKind == AccessScopeKind.Cabinet ? "cabinet" : "object",
                    scopeId = anchorId,
                },
            },
        });

    private async Task RemoveEntriesAsync(Guid cabinetId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.RemoveRange(
            await db.AccessEntries.Where(e => e.ScopeKind == AccessScopeKind.Cabinet && e.ScopeId == cabinetId)
                .ToListAsync());
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
