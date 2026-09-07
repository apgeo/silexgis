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
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Who is told about a new remark on a document, and who is deliberately not.
///
/// Every account here starts with nothing over documents and is opened to exactly the one
/// document a test needs, one explicit rule at a time — the seeded editors read past visibility
/// at the widest scope, so an ordinary editor proves nothing about being left out. Each negative
/// asserts the matching positive in the same test, so a fixture that quietly stopped producing
/// anything cannot pass as a security assertion.
/// </summary>
public sealed class DocumentCommentNotificationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;    // uploads and owns the document below
    private HttpClient speaker = null!;  // writes the remark that gets answered
    private HttpClient answerer = null!; // writes the answers
    private Guid ownerId;
    private Guid speakerId;
    private Guid answererId;

    public DocumentCommentNotificationTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-cnotify-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cn-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"cn-own-{suffix}@t.local");

        speakerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cn-say-{suffix}@t.local");
        speaker = await AuthHelper.BearerClientAsync(factory, $"cn-say-{suffix}@t.local");

        answererId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cn-ans-{suffix}@t.local");
        answerer = await AuthHelper.BearerClientAsync(factory, $"cn-ans-{suffix}@t.local");
    }

    [Fact]
    public async Task An_answer_tells_whoever_wrote_the_remark_it_answers_and_tells_them_once()
    {
        var documentId = await UploadDocumentAsync("answered.txt", "one"u8.ToArray());
        await GrantReadAsync(speakerId, documentId);
        await GrantReadAsync(answererId, documentId);

        var rootId = await PostAsync(speaker, documentId, null, "the first thing said");
        await PostAsync(answerer, documentId, rootId, "the answer to it");

        var told = await NoticesForAsync(speakerId);
        told.Count.ShouldBe(1);
        told[0].TemplateKey.ShouldBe(MessageTemplateCatalog.NotifyCommentReply);
        told[0].Category.ShouldBe(NotificationCategory.CommentReply);
    }

    [Fact]
    public async Task A_remark_on_a_document_tells_its_owner_under_the_other_heading()
    {
        var documentId = await UploadDocumentAsync("owned.txt", "two"u8.ToArray());
        await GrantReadAsync(speakerId, documentId);

        await PostAsync(speaker, documentId, null, "something about your upload");

        var told = await NoticesForAsync(ownerId);
        told.Count.ShouldBe(1);
        told[0].TemplateKey.ShouldBe(MessageTemplateCatalog.NotifyCommentOnMine);
        told[0].Category.ShouldBe(NotificationCategory.CommentOnMine);

        // Being answered and being commented on are two separate settings on purpose, so a
        // remark that answers nothing must never arrive under the answering one.
        told.ShouldAllBe(n => n.TemplateKey != MessageTemplateCatalog.NotifyCommentReply);
    }

    [Fact]
    public async Task Somebody_who_has_since_lost_the_document_is_not_told_about_it_at_all()
    {
        var documentId = await UploadDocumentAsync("withdrawn.txt", "three"u8.ToArray());
        await GrantReadAsync(speakerId, documentId);
        await GrantReadAsync(answererId, documentId);

        var rootId = await PostAsync(speaker, documentId, null, "said while it was open to me");

        // The positive half, proved before anything is taken away: this is a candidate the
        // producer really does find and really does queue for.
        await PostAsync(answerer, documentId, rootId, "the first answer");
        (await NoticesForAsync(speakerId)).Count.ShouldBe(1);

        // The unreadable state, built explicitly rather than mocked: a rule denying this one
        // account this one document, which outranks the allow it was given.
        await GrantAsync(speakerId, AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, documentId);

        // ...and proved through the ordinary read path, so the test cannot pass because the
        // fixture stopped working rather than because the rule bound.
        (await speaker.GetAsync($"/api/v1/documents/{documentId}/comments"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await PostAsync(answerer, documentId, rootId, "the second answer");

        // Still exactly the one from before: being named on a remark is not evidence of being
        // able to read the document now, and the producer decides that again per account.
        (await NoticesForAsync(speakerId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Nobody_is_told_about_their_own_remark_even_on_their_own_document()
    {
        var documentId = await UploadDocumentAsync("mine.txt", "four"u8.ToArray());

        var rootId = await PostAsync(owner, documentId, null, "a note to myself");
        await PostAsync(owner, documentId, rootId, "and a second thought");

        (await NoticesForAsync(ownerId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Answering_the_owner_tells_them_once_as_the_person_answered()
    {
        var documentId = await UploadDocumentAsync("both.txt", "five"u8.ToArray());
        await GrantReadAsync(answererId, documentId);

        var rootId = await PostAsync(owner, documentId, null, "my own opening remark");
        await PostAsync(answerer, documentId, rootId, "answering the owner");

        // One remark is one message. Of the two things this could be called, being answered is
        // the nearer, so the owner hears about it as an answer and not twice.
        var told = await NoticesForAsync(ownerId);
        told.Count.ShouldBe(1);
        told[0].TemplateKey.ShouldBe(MessageTemplateCatalog.NotifyCommentReply);
    }

    [Fact]
    public async Task Every_notice_names_the_document_it_is_about_and_needs_no_excuse_for_not_doing_so()
    {
        var documentId = await UploadDocumentAsync("named.txt", "six"u8.ToArray());
        await GrantReadAsync(speakerId, documentId);
        await GrantReadAsync(answererId, documentId);

        var rootId = await PostAsync(speaker, documentId, null, "opening");
        await PostAsync(answerer, documentId, rootId, "answering");

        foreach (var notice in await NoticesForAsync(speakerId, ownerId))
        {
            notice.TargetKind.ShouldBe(NotificationTargetKind.Document);
            notice.TargetId.ShouldBe(documentId);
            NotificationTargetPolicy.Exemptions.ShouldNotContainKey(notice.TemplateKey);
            NotificationTargetPolicy.RequiresTarget(notice.TemplateKey).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task No_message_carries_a_word_of_what_was_said()
    {
        var documentId = await UploadDocumentAsync("quiet.txt", "seven"u8.ToArray());
        await GrantReadAsync(speakerId, documentId);
        await GrantReadAsync(answererId, documentId);

        const string RootBody = "an-utterly-distinctive-opening-sentence";
        const string ReplyBody = "an-utterly-distinctive-answering-sentence";
        var rootId = await PostAsync(speaker, documentId, null, RootBody);
        await PostAsync(answerer, documentId, rootId, ReplyBody);

        var notices = await NoticesForAsync(speakerId, ownerId);
        notices.ShouldNotBeEmpty();
        foreach (var notice in notices)
        {
            notice.Placeholders.ShouldNotContain(RootBody);
            notice.Placeholders.ShouldNotContain(ReplyBody);

            // And not merely absent from the bag: the wording shipped in both languages has no
            // placeholder a body could arrive in, so rendering the whole message with everything
            // the producer knew still says nothing about what was written.
            var definition = MessageTemplateCatalog.Find(notice.TemplateKey).ShouldNotBeNull();
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(notice.Placeholders)!;
            foreach (var locale in MessageTemplateCatalog.Locales)
            {
                var text = MessageTemplateCatalog.Default(definition, locale);
                var whole = MessageTemplateRenderer.Render(text.Subject ?? string.Empty, values)
                    + "\n" + MessageTemplateRenderer.Render(text.Body, values);
                whole.ShouldNotContain(RootBody);
                whole.ShouldNotContain(ReplyBody);
            }
        }
    }

    [Fact]
    public async Task The_notice_opens_the_document_after_the_remark_goes_and_says_nothing_once_the_document_does()
    {
        var documentId = await UploadDocumentAsync("opened.txt", "eight"u8.ToArray());
        await GrantReadAsync(speakerId, documentId);
        await GrantReadAsync(answererId, documentId);

        var rootId = await PostAsync(speaker, documentId, null, "worth answering");
        var replyId = await PostAsync(answerer, documentId, rootId, "the answer");

        var line = await InboxLineAsync(speaker);
        line.GetProperty("url").GetString().ShouldBe($"/documents/{documentId}");
        line.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();
        line.GetProperty("title").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // A remark can be taken down outright by whoever wrote it, which is why the notice
        // names the document rather than the remark: the conversation is still where the
        // reader was going, and the link still opens it.
        (await answerer.DeleteAsync($"/api/v1/documents/{documentId}/comments/{replyId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        line = await InboxLineAsync(speaker);
        line.GetProperty("url").GetString().ShouldBe($"/documents/{documentId}");
        line.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();

        // The document itself going is the other half, and there the honest answer is that
        // there is nothing to open: the line stays, with its heading and its date and nothing
        // else, rather than becoming a link to nowhere.
        await DeleteDocumentAsync(documentId);

        line = await InboxLineAsync(speaker);
        line.GetProperty("targetWithheld").GetBoolean().ShouldBeTrue();
        line.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
        line.GetProperty("title").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A document is readable in two ways, and a notice about one must survive both. Someone who
    /// reaches a document only because its file hangs on a cave they may read holds no rule over
    /// the document at all — and the line in their inbox has to open it, not tell them they have
    /// lost something they can open in the next tab.
    /// </summary>
    [Fact]
    public async Task Somebody_who_reaches_the_document_only_through_what_it_hangs_on_is_told_and_can_open_it()
    {
        var caveId = await CreateCaveAsync();
        var (documentId, fileId) = await UploadFileAndDocumentAsync("hanging.txt", "nine"u8.ToArray());
        await AttachAsync(fileId, caveId);

        // The fixture proving this is the caller the rule is about: no entry of theirs names the
        // document, the cave is readable to them, and so is the document through it.
        (await speaker.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await speaker.GetAsync($"/api/v1/documents/{documentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rootId = await PostAsync(speaker, documentId, null, "said from the cave page");
        await PostAsync(answerer, documentId, rootId, "answered from the same place");

        (await NoticesForAsync(speakerId)).Count.ShouldBe(1);

        var line = await InboxLineAsync(speaker);
        line.GetProperty("targetWithheld").GetBoolean().ShouldBeFalse();
        line.GetProperty("url").GetString().ShouldBe($"/documents/{documentId}");
        line.GetProperty("title").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    private static async Task<JsonElement> InboxLineAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/notifications/");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var items = JsonDocument.Parse(payload).RootElement.GetProperty("items");
        items.GetArrayLength().ShouldBe(1, payload);
        return items[0].Clone();
    }

    private async Task DeleteDocumentAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var document = await db.Documents.FirstAsync(d => d.Id == documentId);
        document.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> PostAsync(HttpClient client, Guid documentId, Guid? parentId, string body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{documentId}/comments",
            new { parentId, body, anchorKind = "whole", anchor = (object?)null, anchorFileId = (Guid?)null });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private Task GrantReadAsync(Guid userId, Guid documentId) =>
        GrantAsync(userId, AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, documentId);

    /// <summary>
    /// A rule naming one person directly, written straight into storage: the authoring surface
    /// refuses rules that hand out more than the author holds, which is exactly what a fixture
    /// needs to do.
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
            // Non-feature domains anchor object scope in the plain scope id; the feature one is
            // reserved for the foreign key into features.
            ScopeId = scopeId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<Notification>> NoticesForAsync(params Guid[] recipients)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => recipients.Contains(n.RecipientUserId))
            .OrderBy(n => n.Id)
            .ToListAsync();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Remark {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachAsync(Guid fileId, Guid caveId)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType = "feature",
            entityId = caveId,
            role = "document",
            sortOrder = 0,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> UploadDocumentAsync(string fileName, byte[] bytes) =>
        (await UploadFileAndDocumentAsync(fileName, bytes)).DocumentId;

    private async Task<(Guid DocumentId, Guid FileId)> UploadFileAndDocumentAsync(string fileName, byte[] bytes)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/plain");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
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

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        speaker?.Dispose();
        answerer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
