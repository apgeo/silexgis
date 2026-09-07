// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    private HttpClient owner = null!;   // Editor — uploads, administers the tree
    private HttpClient member = null!;  // an ordinary member: adds documents, edits their own
    private Guid ownerId;
    private Guid memberId;

    public UploadDestinationTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-updest-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dest-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"dest-own-{suffix}@t.local");

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

    public Task DisposeAsync() => Task.CompletedTask;

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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
