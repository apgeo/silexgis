// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// One upload, three destinations: a shelf, an object, or nowhere in particular — and the
/// record of which drop it arrived in.
///
/// <para>
/// The security assertions here are all the same shape and it is worth naming: a destination
/// is not a way round the rule that governs it. Filing into a shelf takes the right to write
/// documents at that shelf, attaching to a cave takes write on the cave, and neither is
/// granted by being the person who happens to be uploading. Every refusal is paired with the
/// caller who is allowed, so a fixture that quietly stopped granting anything cannot pass as a
/// passing security assertion.
/// </para>
/// </summary>
public sealed class UploadDestinationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string connectionString;
    private string ownerEmail = null!;

    private HttpClient owner = null!;   // Editor — uploads, administers the tree
    private HttpClient member = null!;  // an ordinary member: adds documents, edits their own
    private Guid ownerId;
    private Guid memberId;

    public UploadDestinationTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-updest-{Guid.NewGuid():N}");
        connectionString = postgres.ConnectionString;
        factory = new SilexGisApiFactory(connectionString, Settings());
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"dest-own-{suffix}@t.local";
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, ownerEmail);
        owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);

        // An ordinary member rather than a second Editor, because an Editor holds read and
        // write over every document in the installation — against which "may not write this
        // one" could never be asserted. This is the realistic shape: may add documents, may
        // edit their own, holds nothing over anybody else's, administers no shelf.
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dest-mem-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"dest-mem-{suffix}@t.local");
        await GrantAsync(memberId, AccessDomain.Documents, AccessAction.Create, AccessScopeKind.All);
        await GrantAsync(
            memberId,
            AccessDomain.Documents,
            AccessAction.Read | AccessAction.Write,
            AccessScopeKind.Own);
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["Files:Root"] = filesRoot,
        ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
    };

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        member?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task An_upload_can_name_a_cabinet_and_lands_filed_there()
    {
        var shelf = await CreateCabinetAsync($"Archive {Guid.NewGuid():N}"[..24]);

        var fileId = await UploadAsync(owner, "survey.txt", cabinetId: shelf);

        (await FiledInAsync(await DocumentIdOfAsync(fileId))).ShouldBe([shelf]);
    }

    [Fact]
    public async Task Filing_at_upload_takes_the_same_right_as_filing_afterwards()
    {
        var shelf = await CreateCabinetAsync($"Archive {Guid.NewGuid():N}"[..24]);

        // An ordinary member may not write documents at this shelf, so naming it is refused —
        // uploading is not a way to put a document under rules you do not hold.
        var refused = await PostUploadAsync(member, "survey.txt", $"?cabinetId={shelf}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());

        // The same caller uploading nowhere in particular is fine: what was refused is the
        // destination, not the upload.
        (await PostUploadAsync(member, "survey.txt", string.Empty))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_upload_naming_nothing_lands_in_the_inbox_and_leaves_it_once_filed()
    {
        var fileId = await UploadAsync(owner, "unfiled.txt");
        var documentId = await DocumentIdOfAsync(fileId);

        (await UnfiledIdsAsync(owner)).ShouldContain(documentId);

        var shelf = await CreateCabinetAsync($"Archive {Guid.NewGuid():N}"[..24]);
        (await owner.PutAsync($"/api/v1/cabinets/{shelf}/documents/{documentId}", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Filing is an addition and leaving the inbox is automatic, which is the whole reason
        // "unfiled" is a query rather than a shelf of its own.
        (await UnfiledIdsAsync(owner)).ShouldNotContain(documentId);
    }

    [Fact]
    public async Task The_inbox_shows_nothing_of_somebody_elses()
    {
        var documentId = await DocumentIdOfAsync(await UploadAsync(owner, "private.txt"));

        // An unfiled document has no shelf for a rule to reach it through and starts private,
        // so the inbox is the uploader's own without needing a rule that says so.
        (await UnfiledIdsAsync(member)).ShouldNotContain(documentId);
        (await UnfiledIdsAsync(owner)).ShouldContain(documentId);
    }

    [Fact]
    public async Task A_dropped_folder_becomes_the_matching_subtree()
    {
        var root = await CreateCabinetAsync($"Club {Guid.NewGuid():N}"[..24]);

        var fileId = await UploadAsync(
            owner, "march.pdf", cabinetId: root, relativePath: "1987/bulletins/march.pdf");

        var filed = (await FiledInAsync(await DocumentIdOfAsync(fileId))).Single();
        var tree = await TreeAsync(owner);

        var bulletins = tree.Single(c => c.Id == filed);
        bulletins.Name.ShouldBe("bulletins");
        tree.Single(c => c.Id == bulletins.ParentId).Name.ShouldBe("1987");

        // A second file in the same folder joins the shelf rather than making another one:
        // dropping a folder twice adds to it, which is what somebody re-running a partial
        // import expects.
        var second = await UploadAsync(
            owner, "april.pdf", cabinetId: root, relativePath: "1987/bulletins/april.pdf");
        (await FiledInAsync(await DocumentIdOfAsync(second))).ShouldBe([filed]);
    }

    [Fact]
    public async Task A_folder_path_that_names_somewhere_else_is_refused_rather_than_rewritten()
    {
        var root = await CreateCabinetAsync($"Club {Guid.NewGuid():N}"[..24]);

        var refused = await PostUploadAsync(
            owner, "passwd", $"?cabinetId={root}&relativePath={Uri.EscapeDataString("../../etc/passwd")}");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe("upload.path_refused");

        // The ordinary shape of the same request is accepted, so the refusal above is the rule
        // rather than the parameter being rejected outright.
        (await PostUploadAsync(
            owner, "march.pdf", $"?cabinetId={root}&relativePath={Uri.EscapeDataString("1987/march.pdf")}"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_shelfs_defaults_fill_in_what_the_upload_did_not_say()
    {
        var tagId = await CreateTagAsync($"scan-{Guid.NewGuid():N}"[..12]);
        var shelf = await CreateCabinetAsync(
            $"Archive {Guid.NewGuid():N}"[..24],
            defaults: new { defaultVisibility = "public", defaultTagIds = new[] { tagId } });

        var fileId = await UploadAsync(owner, "survey.txt", cabinetId: shelf);
        var documentId = await DocumentIdOfAsync(fileId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId);
        document.Visibility.ShouldBe(Visibility.Public);

        // Tags go on the file, where every other tag in this application sits — so a new
        // revision carries them forward through the same path that moves any other tagging.
        var tagged = await db.Taggings.AsNoTracking().AnyAsync(
            t => t.TagId == tagId && t.EntityType == AttachedEntityType.StoredFile && t.EntityId == fileId);
        tagged.ShouldBeTrue();
    }

    [Fact]
    public async Task A_deeper_shelf_overrides_its_archives_visibility_and_inherits_the_rest()
    {
        var tagId = await CreateTagAsync($"club-{Guid.NewGuid():N}"[..12]);
        var archive = await CreateCabinetAsync(
            $"Archive {Guid.NewGuid():N}"[..24],
            defaults: new { defaultVisibility = "public", defaultTagIds = new[] { tagId } });
        var inner = await CreateCabinetAsync(
            "Private surveys", archive, defaults: new { defaultVisibility = "private" });

        var documentId = await DocumentIdOfAsync(await UploadAsync(owner, "secret.txt", cabinetId: inner));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId);

        // Its own answer for what it has an opinion about...
        document.Visibility.ShouldBe(Visibility.Private);

        // ...and the archive's for what it does not. Tags gather rather than being replaced.
        var fileId = await CurrentFileOfAsync(documentId);
        (await db.Taggings.AsNoTracking().AnyAsync(
            t => t.TagId == tagId && t.EntityId == fileId)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_shelf_states_what_it_expects_and_marks_what_does_not_answer_it()
    {
        var shelf = await CreateCabinetAsync(
            $"Archive {Guid.NewGuid():N}"[..24],
            defaults: new { requiredMetadataKeys = new[] { "author", "year" } });

        // The upload succeeds despite answering neither key. A shelf that refused would break
        // dropping four hundred scans in and filing them later — halfway through, for a field
        // nobody can fill in until they have opened the file.
        var documentId = await DocumentIdOfAsync(await UploadAsync(owner, "survey.txt", cabinetId: shelf));

        var listed = await ListingAsync(owner, shelf);
        var row = listed.Single(i => i.GetProperty("id").GetGuid() == documentId);
        Keys(row, "missingMetadataKeys").ShouldBe(["author", "year"]);

        // Answering one leaves the other outstanding, which is what makes the mark a checklist
        // rather than a flag.
        await SetMetadataAsync(documentId, """{"author":"S. Brașov"}""");
        row = (await ListingAsync(owner, shelf)).Single(i => i.GetProperty("id").GetGuid() == documentId);
        Keys(row, "missingMetadataKeys").ShouldBe(["year"]);
    }

    [Fact]
    public async Task Uploading_onto_an_object_files_and_attaches_in_one_request()
    {
        var caveId = await CreateCaveAsync();

        var fileId = await UploadAsync(
            owner, "entrance.txt", attachEntityType: "feature", attachEntityId: caveId);

        var attachments = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/attachments?entityType=feature&entityId={caveId}");
        attachments.EnumerateArray()
            .Select(a => a.GetProperty("fileId").GetGuid())
            .ShouldContain(fileId);
    }

    [Fact]
    public async Task Attaching_at_upload_takes_write_on_the_object_it_is_attached_to()
    {
        var caveId = await CreateCaveAsync();

        // Attaching hands the document to everyone who can read the cave, so it takes write on
        // the cave. An ordinary member holds none.
        var refused = await PostUploadAsync(
            member, "entrance.txt", $"?attachEntityType=feature&attachEntityId={caveId}");
        refused.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest);

        // The owner, who does hold write on it, is allowed — the refusal was the rule.
        (await PostUploadAsync(
            owner, "entrance.txt", $"?attachEntityType=feature&attachEntityId={caveId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// An upload hung on an object without naming a role takes the role its kind gives it there
    /// — a file that is not a picture is a document — rather than "other" for everything.
    /// </summary>
    [Fact]
    public async Task An_attachment_made_at_upload_without_a_role_takes_the_role_its_kind_gives_it()
    {
        var caveId = await CreateCaveAsync();
        var fileId = await UploadAsync(owner, "notes.txt", attachEntityType: "feature", attachEntityId: caveId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Attachments.AsNoTracking().Where(a => a.FileId == fileId && a.FeatureId == caveId)
            .Select(a => a.Role).SingleAsync()).ShouldBe(AttachmentRole.Document);
    }

    /// <summary>
    /// A folder drop mirrors its folders as shelves, and a file whose mirrored shelf the
    /// uploader may not file into is refused. The shelf made for it must not be created anyway
    /// by the save that writes the drop's report line on the same unit of work.
    /// </summary>
    [Fact]
    public async Task A_folder_drop_refused_filing_leaves_no_shelf_behind_it()
    {
        var shelf = await CreateCabinetAsync($"Drop {Guid.NewGuid():N}"[..24]);
        var restricted = await CreateCabinetAsync("Restricted", shelf);
        await GrantAsync(memberId, AccessDomain.Documents, AccessAction.Write, AccessScopeKind.Cabinet, shelf);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = memberId,
                Domain = AccessDomain.Documents,
                Actions = AccessAction.Write,
                Effect = AccessEffect.Deny,
                ScopeKind = AccessScopeKind.Cabinet,
                ScopeId = restricted,
            });
            await db.SaveChangesAsync();
        }

        var opened = await member.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Folder drop" });
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var batch = (await ReadJsonAsync(opened)).GetProperty("id").GetGuid();

        var refused = await PostUploadAsync(
            member, "x.txt",
            $"?cabinetId={shelf}&relativePath={Uri.EscapeDataString("Restricted/new/x.txt")}&batchId={batch}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await ShelvesNamedAsync("new", restricted)).ShouldBe(0);

        // The same member, the same drop, a folder beside the denied one: the shelf is made and
        // the file filed on it — the refusal above was the deny, not a broken drop.
        var open = await CreateCabinetAsync("Open", shelf);
        await UploadAsync(member, "y.txt", cabinetId: shelf, relativePath: "Open/new/y.txt", batchId: batch);
        (await ShelvesNamedAsync("new", open)).ShouldBe(1);
    }

    private async Task<int> ShelvesNamedAsync(string name, Guid parentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Cabinets.AsNoTracking().CountAsync(c => c.ParentId == parentId && c.Name == name);
    }

    [Fact]
    public async Task Everything_in_one_drop_carries_the_drops_reference_and_its_tag()
    {
        var tagName = $"batch-{Guid.NewGuid():N}"[..14];
        var batch = await OpenBatchAsync(label: "Bulletin scans", tagName: tagName);

        var first = await DocumentIdOfAsync(await UploadAsync(owner, "one.txt", batchId: batch));
        var second = await DocumentIdOfAsync(await UploadAsync(owner, "two.txt", batchId: batch));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The id is the accounting reference and is on every document the drop created.
        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.UploadBatchId == batch)
            .Select(d => d.Id)
            .ToListAsync();
        documents.ShouldBe([first, second], ignoreOrder: true);

        // The tag is the browsing half, minted from the name the uploader typed.
        var tagId = await db.UploadBatches.AsNoTracking()
            .Where(b => b.Id == batch).Select(b => b.TagId).FirstAsync();
        tagId.ShouldNotBeNull();
        (await db.Tags.AsNoTracking().FirstAsync(t => t.Id == tagId)).Name.ShouldBe(tagName);
    }

    [Fact]
    public async Task A_drops_report_counts_what_landed_and_names_each_file()
    {
        var batch = await OpenBatchAsync();
        await UploadAsync(owner, "one.txt", batchId: batch);

        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batch}");
        summary.GetProperty("totalCount").GetInt32().ShouldBe(1);
        summary.GetProperty("storedCount").GetInt32().ShouldBe(1);

        var items = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{batch}/items");
        var line = items.GetProperty("items").EnumerateArray().Single();
        line.GetProperty("sourcePath").GetString().ShouldBe("one.txt");
        line.GetProperty("outcome").GetString().ShouldBe("stored");
        line.GetProperty("documentTitle").GetString().ShouldBe("one.txt");
    }

    [Fact]
    public async Task A_drop_is_the_uploaders_own_and_takes_nothing_once_closed()
    {
        var batch = await OpenBatchAsync();

        // Somebody else's drop is answered as absent rather than forbidden: confirming an id
        // belongs to someone would make the ids a list of who uploaded when.
        (await member.GetAsync($"/api/v1/upload-batches/{batch}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.PostAsync($"/api/v1/upload-batches/{batch}/close", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var late = await PostUploadAsync(owner, "late.txt", $"?batchId={batch}");
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(late)).ShouldBe("upload_batch.closed");

        // Closing twice states the same fact, so a retried "done" is not an error.
        (await owner.PostAsync($"/api/v1/upload-batches/{batch}/close", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Naming a drop after a tag that does not exist yet coins it, and tags are one vocabulary
    /// the whole installation shares — so that takes the right to create tags, exactly as
    /// tagging an object with a new name does. Naming an existing tag takes nothing.
    /// </summary>
    [Fact]
    public async Task Opening_a_drop_under_a_new_tag_name_takes_the_right_to_coin_tags()
    {
        // The ordinary member may upload but holds no right to create tags.
        var coined = $"coin-{Guid.NewGuid():N}"[..14];
        var before = await ListBatchIdsAsync(member);
        var refused = await member.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Scans", tagName = coined });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await ReadCodeAsync(refused)).ShouldBe(CreateRules.ForbiddenCode);
        (await TagCountAsync(coined)).ShouldBe(0);

        // Nor was a drop opened on the way to the refusal.
        (await ListBatchIdsAsync(member)).ShouldBe(before, ignoreOrder: true);

        // An existing tag is applied, not coined, so the same member may name it.
        var existingName = $"have-{Guid.NewGuid():N}"[..14];
        var existingId = await CreateTagAsync(existingName);
        var reused = await member.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Scans", tagName = existingName });
        reused.StatusCode.ShouldBe(HttpStatusCode.Created, await reused.Content.ReadAsStringAsync());
        (await ReadJsonAsync(reused)).GetProperty("tagId").GetInt64().ShouldBe(existingId);
        (await TagCountAsync(existingName)).ShouldBe(1);

        // The owner, who may create tags, coins the new one — the refusal was the rule.
        var allowed = await owner.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Scans", tagName = coined });
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
        (await TagCountAsync(coined)).ShouldBe(1);
    }

    /// <summary>
    /// Drops opened at the same moment under one new tag name all open, and the tag exists once:
    /// a loser of the race on the unique slug adopts the winner's tag instead of failing.
    /// </summary>
    [Fact]
    public async Task Drops_opened_at_once_under_one_new_tag_name_all_open_and_share_one_tag()
    {
        var tagName = $"race-{Guid.NewGuid():N}"[..14];

        // Fired without awaiting, so the look-ups land inside each other's insert window. A
        // sequential series would take the found-it branch and prove nothing.
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            owner.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Race", tagName })));

        var tagIds = new List<long>();
        foreach (var response in responses)
        {
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
            tagIds.Add(JsonDocument.Parse(payload).RootElement.GetProperty("tagId").GetInt64());
        }

        (await TagCountAsync(tagName)).ShouldBe(1);
        tagIds.Distinct().Count().ShouldBe(1);
    }

    /// <summary>
    /// The race made to happen every time: another writer coins the tag after this request looked
    /// for it and before its own insert. The insert then collides on the tag's unique identity, and
    /// the drop still opens, carrying the tag the other writer made.
    /// </summary>
    [Fact]
    public async Task A_drop_whose_new_tag_is_coined_by_someone_else_mid_request_adopts_that_tag()
    {
        var tagName = $"mid-{Guid.NewGuid():N}"[..14];
        var coinFirst = new CoinTheSameTagFirst(tagName);
        using var host = new SilexGisApiFactory(connectionString, Settings(), services =>
            services.ConfigureDbContext<SilexGisDbContext>(options => options.AddInterceptors(coinFirst)));
        using var client = await AuthHelper.BearerClientAsync(host, ownerEmail);

        var response = await client.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Mid-request", tagName });
        var payload = await response.Content.ReadAsStringAsync();

        coinFirst.CoinedTagId.ShouldNotBeNull("the other writer never ran, so no race was made");
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("tagId").GetInt64().ShouldBe(coinFirst.CoinedTagId.Value);
        (await TagCountAsync(tagName)).ShouldBe(1);
    }

    /// <summary>
    /// The list of drops is the caller's own and a full administrator sees everybody's; somebody
    /// else's drop cannot be closed or read item by item; none of it answers without an account.
    /// </summary>
    [Fact]
    public async Task The_list_of_drops_is_the_callers_own_and_another_users_drop_is_absent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"dest-adm-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"dest-adm-{suffix}@t.local");
        using var anonymous = factory.CreateClient();

        var ownersDrop = await OpenBatchAsync(label: "Owner's");
        var membersResponse = await member.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Member's" });
        membersResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await membersResponse.Content.ReadAsStringAsync());
        var membersDrop = (await ReadJsonAsync(membersResponse)).GetProperty("id").GetGuid();

        var ownersList = await ListBatchIdsAsync(owner);
        ownersList.ShouldContain(ownersDrop);
        ownersList.ShouldNotContain(membersDrop);

        var membersList = await ListBatchIdsAsync(member);
        membersList.ShouldContain(membersDrop);
        membersList.ShouldNotContain(ownersDrop);

        var adminsList = await ListBatchIdsAsync(admin);
        adminsList.ShouldContain(ownersDrop);
        adminsList.ShouldContain(membersDrop);

        // Somebody else's drop is absent, not forbidden, on every route that names it — and it
        // stays open, because the refused close changed nothing.
        (await member.PostAsync($"/api/v1/upload-batches/{ownersDrop}/close", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.GetAsync($"/api/v1/upload-batches/{ownersDrop}/items"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var stillOpen = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/upload-batches/{ownersDrop}");
        stillOpen.GetProperty("status").GetString().ShouldBe("open");
        (await owner.GetAsync($"/api/v1/upload-batches/{ownersDrop}/items"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await anonymous.GetAsync("/api/v1/upload-batches")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/upload-batches", new { label = "Nobody's" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/v1/upload-batches/{ownersDrop}/close", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Many_documents_are_refiled_in_one_request_and_a_copy_costs_nothing()
    {
        var from = await CreateCabinetAsync($"From {Guid.NewGuid():N}"[..24]);
        var to = await CreateCabinetAsync($"To {Guid.NewGuid():N}"[..24]);

        var first = await DocumentIdOfAsync(await UploadAsync(owner, "one.txt", cabinetId: from));
        var second = await DocumentIdOfAsync(await UploadAsync(owner, "two.txt", cabinetId: from));

        // Filing without unfiling is a copy, which the many-to-many model makes free.
        var copied = await owner.PostAsJsonAsync("/api/v1/cabinets/filing", new
        {
            documentIds = new[] { first, second },
            fileIntoCabinetIds = new[] { to },
        });
        copied.StatusCode.ShouldBe(HttpStatusCode.OK, await copied.Content.ReadAsStringAsync());
        (await FiledInAsync(first)).ShouldBe([from, to], ignoreOrder: true);

        // Naming both sides is a move, and both halves land together.
        var moved = await owner.PostAsJsonAsync("/api/v1/cabinets/filing", new
        {
            documentIds = new[] { first, second },
            unfileFromCabinetIds = new[] { from },
        });
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FiledInAsync(first)).ShouldBe([to]);
        (await FiledInAsync(second)).ShouldBe([to]);
    }

    [Fact]
    public async Task A_bulk_refile_reports_what_it_could_not_move_rather_than_failing_whole()
    {
        var shelf = await CreateCabinetAsync($"Shelf {Guid.NewGuid():N}"[..24]);
        await GrantAsync(memberId, AccessDomain.Documents, AccessAction.Write, AccessScopeKind.Cabinet, shelf);

        var mine = await DocumentIdOfAsync(await UploadAsync(member, "mine.txt"));

        // A document the caller may read but not write. Granting the read by name is what
        // puts it in the answer at all — without it it would be absent rather than refused,
        // which is the separate assertion the test below makes.
        var theirs = await DocumentIdOfAsync(await UploadAsync(owner, "theirs.txt"));
        await GrantAsync(memberId, AccessDomain.Documents, AccessAction.Read, AccessScopeKind.Object, theirs);

        var response = await member.PostAsJsonAsync("/api/v1/cabinets/filing", new
        {
            documentIds = new[] { mine, theirs },
            fileIntoCabinetIds = new[] { shelf },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var result = await ReadJsonAsync(response);
        result.GetProperty("filed").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([mine]);
        result.GetProperty("refused").GetProperty(theirs.ToString()).GetString()
            .ShouldBe("document.write_forbidden");

        // The one it could move actually moved, so the partial result is a result and not a
        // polite way of doing nothing.
        (await FiledInAsync(mine)).ShouldBe([shelf]);
    }

    [Fact]
    public async Task A_bulk_refile_says_nothing_at_all_about_a_document_the_caller_may_not_read()
    {
        var shelf = await CreateCabinetAsync($"Shelf {Guid.NewGuid():N}"[..24]);
        await GrantAsync(memberId, AccessDomain.Documents, AccessAction.Write, AccessScopeKind.Cabinet, shelf);
        var hidden = await DocumentIdOfAsync(await UploadAsync(owner, "hidden.txt"));

        var response = await member.PostAsJsonAsync("/api/v1/cabinets/filing", new
        {
            documentIds = new[] { hidden },
            fileIntoCabinetIds = new[] { shelf },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Absent from both lists. A refusal naming it would confirm it exists.
        var result = await ReadJsonAsync(response);
        result.GetProperty("filed").GetArrayLength().ShouldBe(0);
        result.GetProperty("refused").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bulk_refile_into_a_shelf_the_caller_may_not_administer_is_refused_outright()
    {
        var shelf = await CreateCabinetAsync($"Shelf {Guid.NewGuid():N}"[..24]);
        var documentId = await DocumentIdOfAsync(await UploadAsync(member, "theirs.txt"));

        // Unlike a document they may not write — a property of the selection — the destination
        // is the caller's own choice, so it fails the request rather than one line of it.
        var response = await member.PostAsJsonAsync("/api/v1/cabinets/filing", new
        {
            documentIds = new[] { documentId },
            fileIntoCabinetIds = new[] { shelf },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner may, so the refusal above is the rule rather than the route being closed.
        (await owner.PostAsJsonAsync("/api/v1/cabinets/filing", new
        {
            documentIds = new[] { await DocumentIdOfAsync(await UploadAsync(owner, "mine.txt")) },
            fileIntoCabinetIds = new[] { shelf },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static IReadOnlyList<string> Keys(JsonElement row, string property) =>
        [.. row.GetProperty(property).EnumerateArray().Select(k => k.GetString()!)];

    private async Task<Guid> UploadAsync(
        HttpClient client,
        string fileName,
        Guid? cabinetId = null,
        string? relativePath = null,
        string? attachEntityType = null,
        Guid? attachEntityId = null,
        Guid? batchId = null)
    {
        var query = new List<string>();
        if (cabinetId is { } cabinet)
        {
            query.Add($"cabinetId={cabinet}");
        }

        if (relativePath is not null)
        {
            query.Add($"relativePath={Uri.EscapeDataString(relativePath)}");
        }

        if (attachEntityType is not null)
        {
            query.Add($"attachEntityType={attachEntityType}&attachEntityId={attachEntityId}");
        }

        if (batchId is { } batch)
        {
            query.Add($"batchId={batch}");
        }

        var response = await PostUploadAsync(
            client, fileName, query.Count == 0 ? string.Empty : "?" + string.Join('&', query));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostUploadAsync(
        HttpClient client, string fileName, string query)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"contents of {fileName} {Guid.NewGuid()}"));
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };

        // Awaited inside the using: returning the task would dispose the form before the
        // request body had been read off it.
        return await client.PostAsync($"/api/v1/files/{query}", form);
    }

    private async Task<Guid> OpenBatchAsync(string? label = null, string? tagName = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/upload-batches", new { label, tagName });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCabinetAsync(string name, Guid? parentId = null, object? defaults = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = null,
            ["parentId"] = parentId,
        };

        if (defaults is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(defaults).EnumerateObject())
            {
                body[property.Name] = property.Value;
            }
        }

        var response = await owner.PostAsJsonAsync("/api/v1/cabinets", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private sealed record CabinetNode(Guid Id, Guid? ParentId, string Name);

    private static async Task<List<CabinetNode>> TreeAsync(HttpClient client)
    {
        var tree = await client.GetFromJsonAsync<JsonElement>("/api/v1/cabinets");
        return
        [
            .. tree.EnumerateArray().Select(c => new CabinetNode(
                c.GetProperty("id").GetGuid(),
                c.GetProperty("parentId").ValueKind == JsonValueKind.Null
                    ? null
                    : c.GetProperty("parentId").GetGuid(),
                c.GetProperty("name").GetString()!)),
        ];
    }

    private static async Task<List<JsonElement>> ListingAsync(HttpClient client, Guid cabinetId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/v1/cabinets/{cabinetId}/documents");
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private static async Task<List<Guid>> UnfiledIdsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/documents/unfiled?pageSize=100");
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
    }

    private async Task<List<Guid>> FiledInAsync(Guid documentId)
    {
        var response = await owner.GetAsync($"/api/v1/documents/{documentId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await ReadJsonAsync(response)).GetProperty("cabinetIds").EnumerateArray().Select(c => c.GetGuid())];
    }

    private async Task<Guid> DocumentIdOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var file = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == fileId);
        var version = await db.DocumentVersions.AsNoTracking().FirstAsync(v => v.Id == file.DocumentVersionId);
        return version.DocumentId;
    }

    private async Task<Guid> CurrentFileOfAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.StoredFiles.AsNoTracking()
            .Where(f => db.DocumentVersions.Any(
                v => v.Id == f.DocumentVersionId && v.DocumentId == documentId && v.IsCurrent))
            .Select(f => f.Id)
            .FirstAsync();
    }

    /// <summary>
    /// A tag row. Written directly rather than over HTTP: tags are minted as a side effect of
    /// tagging something, and this fixture needs one to exist before anything is tagged.
    /// </summary>
    private async Task<long> CreateTagAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var tag = new Tag { Name = name, Slug = Tag.Slugify(name) };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag.Id;
    }

    private async Task<int> TagCountAsync(string name)
    {
        var slug = Tag.Slugify(name);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Tags.AsNoTracking().CountAsync(t => t.Slug == slug);
    }

    private static async Task<List<Guid>> ListBatchIdsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/upload-batches?pageSize=500");
        return [.. page.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())];
    }

    private async Task<Guid> CreateCaveAsync()
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cave {Guid.NewGuid():N}"[..20],
            caveTypeId,
            visibility = "public",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task SetMetadataAsync(Guid documentId, string metadata)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.FirstAsync(d => d.Id == documentId);
        document.Metadata = metadata;
        await db.SaveChangesAsync();
    }

    /// <summary>Writes a direct access entry, so a negative can be paired with a positive.</summary>
    private async Task GrantAsync(
        Guid userId, AccessDomain domain, AccessAction actions, AccessScopeKind scopeKind, Guid? scopeId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = domain,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Just before a save that inserts the named tag, inserts that same tag on a connection of its
    /// own and commits it — the other writer of a race, placed exactly between the request's look-up
    /// and its insert. Fires once.
    /// </summary>
    private sealed class CoinTheSameTagFirst(string tagName) : SaveChangesInterceptor
    {
        private int fired;

        public long? CoinedTagId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<Tag>().Any(e => e.State == EntityState.Added && e.Entity.Name == tagName)
                && Interlocked.Exchange(ref fired, 1) == 0)
            {
                var added = context.ChangeTracker.Entries<Tag>().First(e => e.Entity.Name == tagName).Entity;
                var options = new DbContextOptionsBuilder<SilexGisDbContext>()
                    .UseNpgsql(context.Database.GetConnectionString(), npgsql => npgsql.UseNetTopologySuite())
                    .UseSnakeCaseNamingConvention()
                    .UseOpenIddict()
                    .Options;
                await using var other = new SilexGisDbContext(options);
                var tag = new Tag { Name = added.Name, Slug = added.Slug };
                other.Tags.Add(tag);
                await other.SaveChangesAsync(cancellationToken);
                CoinedTagId = tag.Id;
            }

            return result;
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
