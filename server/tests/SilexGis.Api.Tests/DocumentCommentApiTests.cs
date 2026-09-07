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
/// The conversation about a document: who may read it, who may write on it, who may rewrite
/// what they wrote, and who may take a remark down.
///
/// Reading a remark is reading its document and nothing else, so the tests that matter most
/// here are the ones proving a comment route cannot be used to learn that a document exists.
/// Every negative builds its own unreadable state — a Viewer holds nothing over documents
/// beyond the built-ins, and where a domain-wide allow is in play the refusal is an explicit
/// deny — and asserts the matching positive in the same test, so a fixture that quietly
/// stopped working cannot pass as a passing security assertion.
/// </summary>
public sealed class DocumentCommentApiTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;   // Editor — uploads and owns the documents below
    private HttpClient reader = null!;  // Viewer — nothing over documents until granted
    private HttpClient editor = null!;  // second Editor — domain-wide rights from the seed
    private HttpClient admin = null!;   // full administrator — the only moderator of remarks
    private HttpClient anonymous = null!;
    private Guid ownerId;
    private Guid readerId;
    private Guid editorId;

    public DocumentCommentApiTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-comments-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dc-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"dc-own-{suffix}@t.local");

        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dc-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"dc-read-{suffix}@t.local");

        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dc-ed-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"dc-ed-{suffix}@t.local");

        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"dc-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"dc-adm-{suffix}@t.local");

        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task A_remark_is_written_listed_and_attributed_to_its_author()
    {
        var documentId = await UploadDocumentAsync("field-notes.txt", "notes"u8.ToArray());

        var created = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { body = "  The third page is upside down.  " });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        var comment = await ReadJsonAsync(created);
        // Stored as typed, minus the whitespace around it.
        comment.GetProperty("body").GetString().ShouldBe("The third page is upside down.");
        comment.GetProperty("authorId").GetGuid().ShouldBe(ownerId);
        comment.GetProperty("authorName").GetString().ShouldNotBeNullOrWhiteSpace();
        comment.GetProperty("anchorKind").GetString().ShouldBe("whole");
        comment.GetProperty("parentId").ValueKind.ShouldBe(JsonValueKind.Null);
        comment.GetProperty("editedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        comment.GetProperty("mayEdit").GetBoolean().ShouldBeTrue();
        comment.GetProperty("mayDelete").GetBoolean().ShouldBeTrue();

        var listed = await ReadJsonAsync(await owner.GetAsync($"/api/v1/documents/{documentId}/comments/"));
        listed.GetProperty("totalItems").GetInt32().ShouldBe(1);
        listed.GetProperty("items").EnumerateArray().Single()
            .GetProperty("id").GetGuid().ShouldBe(comment.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_reply_answers_a_remark_but_a_reply_to_a_reply_is_refused()
    {
        var documentId = await UploadDocumentAsync("survey.txt", "survey"u8.ToArray());
        var top = await CommentAsync(owner, documentId, "Is this the 1998 survey?");

        // One level of replies is what a thread here is.
        var reply = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { parentId = top, body = "It is — the cover page says so." });
        reply.StatusCode.ShouldBe(HttpStatusCode.OK, await reply.Content.ReadAsStringAsync());
        var replied = await ReadJsonAsync(reply);
        replied.GetProperty("parentId").GetGuid().ShouldBe(top);

        // A second level is refused rather than quietly flattened.
        var nested = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { parentId = replied.GetProperty("id").GetGuid(), body = "Thanks." });
        nested.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(nested)).ShouldBe("comment.reply_depth_exceeded");
    }

    [Fact]
    public async Task A_reply_cannot_name_a_parent_belonging_to_another_document()
    {
        var here = await UploadDocumentAsync("here.txt", "here"u8.ToArray());
        var elsewhere = await UploadDocumentAsync("elsewhere.txt", "elsewhere"u8.ToArray());
        var parentElsewhere = await CommentAsync(owner, elsewhere, "A remark on the other document.");
        var parentHere = await CommentAsync(owner, here, "A remark on this one.");

        // Naming this document's own remark is the ordinary case and works.
        var ok = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{here}/comments/",
            new { parentId = parentHere, body = "Agreed." });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());

        // A parent from another document answers as absent: a thread that spanned two
        // documents would be readable by the readers of either.
        var crossed = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{here}/comments/",
            new { parentId = parentElsewhere, body = "Agreed." });
        crossed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(crossed)).ShouldBe("comment.parent_not_found");
    }

    [Fact]
    public async Task A_remark_needs_words_and_an_anchor_that_makes_sense()
    {
        var documentId = await UploadDocumentAsync("blank.txt", "blank"u8.ToArray());

        var blank = await owner.PostAsJsonAsync($"/api/v1/documents/{documentId}/comments/", new { body = "   " });
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(blank)).ShouldBe("validation.failed");

        var tooLong = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { body = new string('x', 4001) });
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(tooLong)).ShouldBe("validation.failed");

        // Every anchor but the whole-document one carries a payload saying which part.
        var unanchored = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { body = "Page three.", anchorKind = "page" });
        unanchored.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(unanchored)).ShouldBe("comment.anchor_invalid");

        // The same request with a payload is accepted, so the refusals above are about the
        // request and not about the route being broken.
        var anchored = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { body = "Page three.", anchorKind = "page", anchor = new { page = 3 } });
        anchored.StatusCode.ShouldBe(HttpStatusCode.OK, await anchored.Content.ReadAsStringAsync());
        (await ReadJsonAsync(anchored)).GetProperty("anchor").GetProperty("page").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task A_pinned_anchor_names_a_file_of_this_document_and_no_other()
    {
        var documentId = await UploadDocumentAsync("scan.txt", "scan"u8.ToArray());
        var mine = await FileIdOfAsync(documentId);
        var otherDocument = await UploadDocumentAsync("other-scan.txt", "other"u8.ToArray());
        var theirs = await FileIdOfAsync(otherDocument);

        var pinned = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { body = "This mark.", anchorKind = "imageRegion", anchor = new { x = 1, y = 2 }, anchorFileId = mine });
        pinned.StatusCode.ShouldBe(HttpStatusCode.OK, await pinned.Content.ReadAsStringAsync());

        // A pin into another document would be a reference its author may never be allowed
        // to follow, and the reference itself would answer whether that file exists.
        var foreign = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { body = "This mark.", anchorKind = "imageRegion", anchor = new { x = 1, y = 2 }, anchorFileId = theirs });
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(foreign)).ShouldBe("comment.anchor_file_not_found");
    }

    [Fact]
    public async Task An_anonymous_caller_reaches_no_comment_route()
    {
        var documentId = await UploadDocumentAsync("public-ish.txt", "text"u8.ToArray());
        var commentId = await CommentAsync(owner, documentId, "Signed in only.");

        (await anonymous.GetAsync($"/api/v1/documents/{documentId}/comments/"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/documents/{documentId}/comments/", new { body = "Hello." }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"/api/v1/documents/{documentId}/comments/{commentId}", new { body = "Hi." }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/documents/{documentId}/comments/{commentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The signed-in owner reaches all of it, so the refusals above are about the caller.
        (await owner.GetAsync($"/api/v1/documents/{documentId}/comments/"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_caller_no_rule_reaches_learns_nothing_from_any_comment_route()
    {
        var documentId = await UploadDocumentAsync("private-notes.txt", "notes"u8.ToArray());
        var commentId = await CommentAsync(owner, documentId, "Only for people who may read this.");

        // A Viewer holds nothing over documents and this one is private, so every comment
        // route answers exactly the way it would for a document that is not there — no
        // listing, no count, no distinguishable refusal, and no writing on it either.
        var listed = await reader.GetAsync($"/api/v1/documents/{documentId}/comments/");
        listed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(listed)).ShouldBe("document.not_found");

        var absent = await reader.GetAsync($"/api/v1/documents/{Guid.NewGuid()}/comments/");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(absent)).ShouldBe("document.not_found");

        var wrote = await reader.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/", new { body = "Can I see this?" });
        wrote.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(wrote)).ShouldBe("document.not_found");

        // Naming the remark itself is refused the same way: the comment id is not a second
        // door onto a document its holder may not open.
        var edited = await reader.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/{commentId}", new { body = "Mine now." });
        edited.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(edited)).ShouldBe("document.not_found");

        var removed = await reader.DeleteAsync($"/api/v1/documents/{documentId}/comments/{commentId}");
        removed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(removed)).ShouldBe("document.not_found");

        // The same caller, named by a rule that lets them read the document, sees the whole
        // conversation and may join it. Same document, same requests — only the rule changed.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);
        var granted = await ReadJsonAsync(await reader.GetAsync($"/api/v1/documents/{documentId}/comments/"));
        granted.GetProperty("totalItems").GetInt32().ShouldBe(1);
        granted.GetProperty("items").EnumerateArray().Single().GetProperty("body").GetString()
            .ShouldBe("Only for people who may read this.");
        (await reader.PostAsJsonAsync($"/api/v1/documents/{documentId}/comments/", new { body = "Now I can." }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Posting_asks_only_that_the_document_can_be_read_and_never_that_it_can_be_changed()
    {
        var documentId = await UploadDocumentAsync("survey-notes.txt", "notes"u8.ToArray());

        // A Viewer holds nothing over documents; the one rule naming them hands over Read
        // and nothing else, so whatever they manage here they manage on reading alone.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);

        var starter = await CommentAsync(reader, documentId, "The passage in the sketch is the one below the pitch.");
        var replied = await reader.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/",
            new { parentId = starter, body = "Agreed — same trip." });
        replied.StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the fixture is genuinely without write: the same caller in the same state
        // cannot change one word of the document they just wrote a remark on.
        var changed = await reader.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}",
            new
            {
                title = "Renamed by a commenter",
                documentTypeId = (long?)null,
                metadata = (object?)null,
                visibility = "private",
                cavingGroupId = (Guid?)null,
                language = (string?)null,
            });
        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(changed)).ShouldBe("document.write_forbidden");
    }

    [Fact]
    public async Task A_deny_over_one_document_takes_its_conversation_with_it()
    {
        var denied = await UploadDocumentAsync("minutes.txt", "minutes"u8.ToArray());
        var untouched = await UploadDocumentAsync("agenda.txt", "agenda"u8.ToArray());
        await CommentAsync(owner, denied, "Point four was never agreed.");
        await CommentAsync(owner, untouched, "Nothing to add.");

        // The seeded editor ruleset carries Read on documents domain-wide, so both
        // conversations are readable before anything narrows that.
        (await editor.GetAsync($"/api/v1/documents/{denied}/comments/")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor.GetAsync($"/api/v1/documents/{untouched}/comments/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await GrantAsync(editorId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, denied);

        var refused = await editor.GetAsync($"/api/v1/documents/{denied}/comments/");
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(refused)).ShouldBe("document.not_found");
        (await editor.GetAsync($"/api/v1/documents/{untouched}/comments/")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_the_author_rewrites_a_remark()
    {
        var documentId = await UploadDocumentAsync("draft.txt", "draft"u8.ToArray());
        var commentId = await CommentAsync(owner, documentId, "The date is wrong.");
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);

        // A reader of the document sees the remark and is refused the rewrite — putting
        // different words in somebody else's mouth is not a right this system hands out.
        (await reader.GetAsync($"/api/v1/documents/{documentId}/comments/")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refused = await reader.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/{commentId}", new { body = "The date is fine." });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("comment.edit_forbidden");

        var mine = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/{commentId}", new { body = "The date is off by a year." });
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());
        var rewritten = await ReadJsonAsync(mine);
        rewritten.GetProperty("body").GetString().ShouldBe("The date is off by a year.");
        rewritten.GetProperty("editedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_remark_is_removed_by_its_author_or_by_an_administrator_and_by_nobody_else()
    {
        var documentId = await UploadDocumentAsync("notes.txt", "notes"u8.ToArray());
        var mine = await CommentAsync(owner, documentId, "Written by the owner.");
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);
        var theirs = await CommentAsync(reader, documentId, "Written by the reader.");

        // Reading the document is not moderating it: the reader may take their own remark
        // down and nobody else's.
        var refused = await reader.DeleteAsync($"/api/v1/documents/{documentId}/comments/{mine}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(refused)).ShouldBe("comment.delete_forbidden");

        // Neither is holding every right over the document. The owner uploaded it, so they
        // hold delete over it by ownership, and the reader is handed delete outright — and
        // still neither may erase what the other wrote. Taking down somebody else's words
        // is an administrator's power alone.
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Delete, AccessScopeKind.Object, documentId);
        var stillRefused = await reader.DeleteAsync($"/api/v1/documents/{documentId}/comments/{mine}");
        stillRefused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadCodeAsync(stillRefused)).ShouldBe("comment.delete_forbidden");
        (await owner.DeleteAsync($"/api/v1/documents/{documentId}/comments/{theirs}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The positives, in the same test, so a fixture that stopped granting anything
        // could not pass as a passing refusal: the author takes their own remark down, and
        // the administrator takes the other one down.
        (await reader.DeleteAsync($"/api/v1/documents/{documentId}/comments/{theirs}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/v1/documents/{documentId}/comments/{mine}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var left = await ReadJsonAsync(await owner.GetAsync($"/api/v1/documents/{documentId}/comments/"));
        left.GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task What_a_removed_remark_leaves_behind_is_an_audit_entry_for_it_and_for_every_reply()
    {
        var documentId = await UploadDocumentAsync("audited-thread.txt", "thread"u8.ToArray());
        var top = await CommentAsync(owner, documentId, "Which entrance is this?");
        await GrantAsync(readerId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);
        var replyResponse = await reader.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/", new { parentId = top, body = "The lower one." });
        replyResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await replyResponse.Content.ReadAsStringAsync());
        var replyId = (await ReadJsonAsync(replyResponse)).GetProperty("id").GetGuid();

        (await admin.DeleteAsync($"/api/v1/documents/{documentId}/comments/{top}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A reply removed with its parent is still somebody's words disappearing, so it
        // leaves the same record the parent does — text and all. A database cascade would
        // have removed the row and written nothing.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var deletions = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.EntityType == nameof(DocumentComment) && a.Action == AuditActions.Deleted)
            .ToListAsync();

        foreach (var removed in new[] { top, replyId })
        {
            var entry = deletions.SingleOrDefault(a => a.EntityId == removed.ToString());
            entry.ShouldNotBeNull();
            entry.RootEntityType.ShouldBe(nameof(Document));
            entry.RootEntityId.ShouldBe(documentId.ToString());
            entry.Changes.ShouldNotBeNull();
        }

        deletions.Single(a => a.EntityId == replyId.ToString()).Changes!.ShouldContain("The lower one.");
    }

    [Fact]
    public async Task Removing_a_remark_removes_the_replies_to_it()
    {
        var documentId = await UploadDocumentAsync("thread.txt", "thread"u8.ToArray());
        var top = await CommentAsync(owner, documentId, "Which entrance is this?");
        var standalone = await CommentAsync(owner, documentId, "Unrelated remark.");
        await owner.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments/", new { parentId = top, body = "The lower one." });

        var before = await ReadJsonAsync(await owner.GetAsync($"/api/v1/documents/{documentId}/comments/"));
        before.GetProperty("totalItems").GetInt32().ShouldBe(3);

        (await owner.DeleteAsync($"/api/v1/documents/{documentId}/comments/{top}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // An answer without its question is noise, so the reply goes with the remark it
        // answered — and nothing else does.
        var after = await ReadJsonAsync(await owner.GetAsync($"/api/v1/documents/{documentId}/comments/"));
        after.GetProperty("totalItems").GetInt32().ShouldBe(1);
        after.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(standalone);
    }

    [Fact]
    public async Task A_remark_is_not_addressable_through_a_document_it_does_not_belong_to()
    {
        var here = await UploadDocumentAsync("here.txt", "here"u8.ToArray());
        var elsewhere = await UploadDocumentAsync("elsewhere.txt", "elsewhere"u8.ToArray());
        var commentId = await CommentAsync(owner, elsewhere, "Belongs to the other document.");

        // Both documents are the owner's, so this is purely about the route: a remark is
        // reached through the document it sits on and through no other.
        var crossed = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{here}/comments/{commentId}", new { body = "Moved." });
        crossed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(crossed)).ShouldBe("comment.not_found");

        var direct = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{elsewhere}/comments/{commentId}", new { body = "Corrected." });
        direct.StatusCode.ShouldBe(HttpStatusCode.OK, await direct.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_page_of_remarks_counts_only_what_it_lists()
    {
        var documentId = await UploadDocumentAsync("long-thread.txt", "thread"u8.ToArray());
        for (var i = 0; i < 5; i++)
        {
            await CommentAsync(owner, documentId, $"Remark number {i}.");
        }

        var page = await ReadJsonAsync(
            await owner.GetAsync($"/api/v1/documents/{documentId}/comments/?page=1&pageSize=2"));
        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        page.GetProperty("totalItems").GetInt32().ShouldBe(5);
        page.GetProperty("pageSize").GetInt32().ShouldBe(2);

        // Oldest first: the thread reads in the order it was written.
        page.GetProperty("items").EnumerateArray().First().GetProperty("body").GetString()
            .ShouldBe("Remark number 0.");
    }

    private async Task<Guid> CommentAsync(HttpClient client, Guid documentId, string body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/documents/{documentId}/comments/", new { body });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A rule naming one person directly. Written straight into storage: the authoring
    /// surface refuses rules that hand out more than the author holds, which is exactly
    /// what a fixture needs to do.
    /// </summary>
    private async Task GrantAsync(
        Guid userId, AccessEffect effect, AccessAction actions, AccessScopeKind scopeKind, Guid? scopeId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = AccessDomain.Documents,
            Actions = actions,
            ScopeKind = scopeKind,
            ScopeId = scopeId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> UploadDocumentAsync(string fileName, byte[] bytes)
    {
        using var content = new ByteArrayContent(bytes);
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
        return version.DocumentId;
    }

    private async Task<Guid> FileIdOfAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from version in db.DocumentVersions.AsNoTracking()
                      join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                      where version.DocumentId == documentId
                      select file.Id).FirstAsync();
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
        reader?.Dispose();
        editor?.Dispose();
        admin?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
