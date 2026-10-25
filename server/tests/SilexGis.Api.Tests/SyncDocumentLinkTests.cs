// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
/// The upload's <c>documentLinks</c> section — a phone hanging its photos on the caves of its set
/// and taking them down — and the undo of everything the photo sections write: a created document
/// removed, new bytes stepped back to the revision before them, a link hung taken down and a link
/// taken down hung again.
/// </summary>
/// <remarks>
/// <para>
/// Two accounts. The owner is an Editor who creates every cave and may place each of them. The
/// viewer is a Viewer granted Read and Write on two caves by object rules and nothing else: it may
/// hang files on both, holds no exact-location right anywhere, and so is withheld the protected
/// one. A third cave it may read and not change. Every refusal below is asserted beside the same
/// link landing for an account the rule does not refuse.
/// </para>
/// <para>Every picture is generated, with an invented position.</para>
/// </remarks>
public sealed class SyncDocumentLinkTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string marker = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private Guid viewerId;
    private long caveTypeId;

    public SyncDocumentLinkTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-synclinks-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sdl-own-{marker}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"sdl-own-{marker}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sdl-view-{marker}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"sdl-view-{marker}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task A_photo_sent_with_its_link_hangs_on_the_cave_with_the_role_its_place_gives_it_and_a_resend_writes_nothing()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var set = await CreateSetAsync(owner, [cave]);
        var bytes = Corpus.GeotaggedJpeg(45.31, 25.61);
        var staged = await StageAsync(owner, "IMG_1.jpg", bytes);
        var documentId = Guid.CreateVersion7();

        var batchId = Guid.CreateVersion7();
        var body = new
        {
            batchId,
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, staged) },
            documentLinks = new[] { Link(documentId, cave) },
        };
        var answer = await UploadAsync(owner, set, body);

        answer.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("created");
        var link = answer.GetProperty("documentLinks").EnumerateArray().Single();
        link.GetProperty("status").GetString().ShouldBe("created");
        link.GetProperty("revision").ValueKind.ShouldBe(JsonValueKind.String);
        var linkId = link.GetProperty("id").GetGuid();

        // A picture on a cave as a whole says nothing about what it shows, so it is filed as other.
        var stored = await AttachmentAsync(linkId);
        stored.ShouldNotBeNull();
        stored.FeatureId.ShouldBe(cave);
        stored.Role.ShouldBe(AttachmentRole.Other);
        stored.FileId.ShouldBe(await CurrentFileAsync(documentId));

        // The same batch again is answered from the record, and hangs nothing twice.
        var replay = await UploadAsync(owner, set, body);
        replay.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        replay.GetProperty("documentLinks").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(linkId);
        (await LinkCountAsync(documentId, cave)).ShouldBe(1);

        // A new batch saying the same thing changes nothing either: a document hangs on a cave once.
        var again = await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, cave) },
        });
        again.GetProperty("documentLinks").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("unchanged");
        (await LinkCountAsync(documentId, cave)).ShouldBe(1);

        // Taken down by the phone, it is gone; the document itself stays.
        var removed = await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, cave, deleted: true) },
        });
        removed.GetProperty("documentLinks").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("deleted");
        (await LinkCountAsync(documentId, cave)).ShouldBe(0);
        (await DocumentAliveAsync(documentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_link_on_a_cave_withheld_from_the_caller_is_refused_and_one_on_a_cave_they_may_change_lands()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var guarded = await CreateCaveAsync(locationProtected: true);
        var readOnly = await CreateCaveAsync(locationProtected: false);
        await GrantAsync(open, AccessAction.Read | AccessAction.Write);
        await GrantAsync(guarded, AccessAction.Read | AccessAction.Write);

        // The owner's photo, which the viewer may read by a rule naming it and nothing else.
        var ownerSet = await CreateSetAsync(owner, [open, guarded, readOnly]);
        var bytes = Corpus.GeotaggedJpeg(45.32, 25.62);
        var documentId = Guid.CreateVersion7();
        var made = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, await StageAsync(owner, "IMG_2.jpg", bytes)) },
        });
        made.GetProperty("documents").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("created");
        await AllowDocumentReadAsync(viewerId, documentId);

        var viewerSet = await CreateSetAsync(viewer, [open, guarded, readOnly]);
        var answer = await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, open), Link(documentId, guarded), Link(documentId, readOnly) },
        });
        var results = answer.GetProperty("documentLinks").EnumerateArray().ToList();
        results[0].GetProperty("status").GetString().ShouldBe("created");
        results[1].GetProperty("status").GetString().ShouldBe("rejected");
        results[1].GetProperty("code").GetString().ShouldBe("sync.location_forbidden");
        results[2].GetProperty("status").GetString().ShouldBe("rejected");
        results[2].GetProperty("code").GetString().ShouldBe("sync.row_forbidden");
        (await LinkCountAsync(documentId, open)).ShouldBe(1);
        (await LinkCountAsync(documentId, guarded)).ShouldBe(0);
        (await LinkCountAsync(documentId, readOnly)).ShouldBe(0);

        // The owner, who may place the protected cave and change the third, hangs the same photo on both.
        var byOwner = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, guarded), Link(documentId, readOnly) },
        });
        byOwner.GetProperty("documentLinks").EnumerateArray()
            .Select(r => r.GetProperty("status").GetString())
            .ShouldBe(["created", "created"]);
    }

    [Fact]
    public async Task A_link_outside_the_set_or_on_a_document_the_caller_cannot_read_is_refused()
    {
        var inside = await CreateCaveAsync(locationProtected: false);
        var outside = await CreateCaveAsync(locationProtected: false);
        await GrantAsync(inside, AccessAction.Read | AccessAction.Write);
        await GrantAsync(outside, AccessAction.Read | AccessAction.Write);

        var ownerSet = await CreateSetAsync(owner, [inside]);
        var bytes = Corpus.GeotaggedJpeg(45.33, 25.63);
        var documentId = Guid.CreateVersion7();
        await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, await StageAsync(owner, "IMG_3.jpg", bytes)) },
        });

        // Unreadable: the owner's private photo, with no rule handing it to the viewer.
        var viewerSet = await CreateSetAsync(viewer, [inside]);
        var unreadable = await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, inside) },
        });
        Code(unreadable.GetProperty("documentLinks")[0]).ShouldBe("sync.document_unavailable");

        // Readable now, on a cave in the set and on one outside it.
        await AllowDocumentReadAsync(viewerId, documentId);
        var answer = await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, inside), Link(documentId, outside) },
        });
        answer.GetProperty("documentLinks")[0].GetProperty("status").GetString().ShouldBe("created");
        Code(answer.GetProperty("documentLinks")[1]).ShouldBe("sync.target_not_found");
        (await LinkCountAsync(documentId, outside)).ShouldBe(0);
    }

    [Fact]
    public async Task Undoing_an_upload_removes_the_document_it_created_and_takes_down_the_link_it_hung()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var set = await CreateSetAsync(owner, [cave]);
        var bytes = Corpus.GeotaggedJpeg(45.34, 25.64);
        var documentId = Guid.CreateVersion7();
        var answer = await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, await StageAsync(owner, "IMG_4.jpg", bytes)) },
            documentLinks = new[] { Link(documentId, cave) },
        });
        (await LinkCountAsync(documentId, cave)).ShouldBe(1);

        var undo = await RevertAsync(owner, answer.GetProperty("importBatchId").GetGuid());
        var items = undo.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items.ShouldAllBe(i => i.GetProperty("outcome").GetString() == "undone");
        items.ShouldAllBe(i => i.GetProperty("documentId").GetGuid() == documentId);
        undo.GetProperty("batch").GetProperty("revertedAt").ValueKind.ShouldBe(JsonValueKind.String);

        (await DocumentAliveAsync(documentId)).ShouldBeFalse();
        (await LinkCountAsync(documentId, cave)).ShouldBe(0);
    }

    [Fact]
    public async Task Undoing_new_bytes_makes_the_earlier_revision_current_again_and_keeps_the_undone_one_in_the_list()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var set = await CreateSetAsync(owner, [cave]);
        var first = Corpus.GeotaggedJpeg(45.35, 25.65);
        var second = Corpus.GeotaggedJpeg(45.36, 25.66);
        var documentId = Guid.CreateVersion7();
        var created = await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, first, await StageAsync(owner, "IMG_5.jpg", first)) },
            documentLinks = new[] { Link(documentId, cave) },
        });
        var firstFile = await CurrentFileAsync(documentId);
        var revision = created.GetProperty("documents")[0].GetProperty("revision").GetDateTimeOffset();

        var replaced = await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[]
            {
                new
                {
                    id = documentId,
                    baseRevision = revision,
                    fileName = "IMG_5.jpg",
                    sha256 = Hash(second),
                    sizeBytes = second.Length,
                    stagedUploadId = await StageAsync(owner, "IMG_5.jpg", second),
                },
            },
        });
        replaced.GetProperty("documents")[0].GetProperty("status").GetString().ShouldBe("updated");
        var secondFile = await CurrentFileAsync(documentId);
        secondFile.ShouldNotBe(firstFile);

        // The link moved with the new bytes, as every link does.
        var linkFile = await LinkFileAsync(documentId, cave);
        linkFile.ShouldBe(secondFile);

        var undo = await RevertAsync(owner, replaced.GetProperty("importBatchId").GetGuid());
        var line = undo.GetProperty("items").EnumerateArray().Single();
        line.GetProperty("outcome").GetString().ShouldBe("undone");
        line.GetProperty("applied").EnumerateArray().Select(a => a.GetString()).ShouldContain("file");

        (await CurrentFileAsync(documentId)).ShouldBe(firstFile);
        (await LinkFileAsync(documentId, cave)).ShouldBe(firstFile);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.VersionNumber)
            .Select(v => new { v.VersionNumber, v.IsCurrent })
            .ToListAsync();
        versions.Count.ShouldBe(2);
        versions[0].IsCurrent.ShouldBeTrue();
        versions[1].IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task Undoing_an_upload_that_took_a_link_down_hangs_it_up_again_as_it_was()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var set = await CreateSetAsync(owner, [cave]);
        var bytes = Corpus.GeotaggedJpeg(45.37, 25.67);
        var documentId = Guid.CreateVersion7();
        await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, await StageAsync(owner, "IMG_6.jpg", bytes)) },
            documentLinks = new[] { new { documentId, target = new { kind = "feature", id = cave }, role = "photoSurface", caption = "From the road", sortOrder = 3 } },
        });

        var removed = await UploadAsync(owner, set, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, cave, deleted: true) },
        });
        (await LinkCountAsync(documentId, cave)).ShouldBe(0);

        var undo = await RevertAsync(owner, removed.GetProperty("importBatchId").GetGuid());
        var line = undo.GetProperty("items").EnumerateArray().Single();
        line.GetProperty("outcome").GetString().ShouldBe("undone");
        line.GetProperty("change").GetString().ShouldBe("deleted");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var back = await db.Attachments.AsNoTracking().SingleAsync(a => a.FeatureId == cave);
        back.Role.ShouldBe(AttachmentRole.PhotoSurface);
        back.Caption.ShouldBe("From the road");
        back.SortOrder.ShouldBe(3);
        back.FileId.ShouldBe(await CurrentFileAsync(documentId));
    }

    [Fact]
    public async Task A_document_sent_under_a_withheld_caves_identifier_does_not_pass_for_the_cave_being_created_here()
    {
        // The phone chooses its documents' identifiers as it chooses its features'. A document
        // created under the guarded cave's own identifier must not make a link on that cave look
        // like one on a feature this upload created, which is exempt from what is withheld.
        var guarded = await CreateCaveAsync(locationProtected: true);
        await GrantAsync(guarded, AccessAction.Read | AccessAction.Write);
        await AllowDocumentCreateAsync(viewerId);

        var viewerSet = await CreateSetAsync(viewer, [guarded]);
        var bytes = Corpus.GeotaggedJpeg(45.38, 25.68);
        var answer = await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(guarded, bytes, await StageAsync(viewer, "IMG_7.jpg", bytes)) },
            documentLinks = new[] { Link(guarded, guarded) },
        });
        answer.GetProperty("documents")[0].GetProperty("status").GetString().ShouldBe("created");
        Code(answer.GetProperty("documentLinks")[0]).ShouldBe("sync.location_forbidden");
        (await LinkCountAsync(guarded, guarded)).ShouldBe(0);

        // The owner, who may place the cave, hangs the same document there.
        var ownerSet = await CreateSetAsync(owner, [guarded]);
        var byOwner = await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(guarded, guarded) },
        });
        byOwner.GetProperty("documentLinks")[0].GetProperty("status").GetString().ShouldBe("created");
    }

    [Fact]
    public async Task A_phone_document_holding_bytes_its_sender_may_not_see_elsewhere_is_stored_and_the_pair_recorded()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        var bytes = Corpus.GeotaggedJpeg(45.39, 25.69);

        // The owner's private photo, which the viewer may not read.
        var ownerSet = await CreateSetAsync(owner, [cave]);
        var hidden = Guid.CreateVersion7();
        await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(hidden, bytes, await StageAsync(owner, "IMG_8.jpg", bytes)) },
        });

        await AllowDocumentCreateAsync(viewerId);
        var viewerSet = await CreateSetAsync(viewer, [cave]);
        var copy = Guid.CreateVersion7();
        var answer = await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(copy, bytes, await StageAsync(viewer, "IMG_8.jpg", bytes)) },
        });
        answer.GetProperty("documents")[0].GetProperty("status").GetString().ShouldBe("created");

        // A copy the sender may see is no secret, and leaves no record: the owner's own second copy.
        var ownCopy = Guid.CreateVersion7();
        await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(ownCopy, bytes, await StageAsync(owner, "IMG_8.jpg", bytes)) },
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var records = await db.DuplicateUploadRecords.AsNoTracking()
            .Where(r => r.NewDocumentId == copy || r.NewDocumentId == ownCopy)
            .ToListAsync();
        var record = records.ShouldHaveSingleItem();
        record.NewDocumentId.ShouldBe(copy);
        record.ExistingDocumentId.ShouldBe(hidden);
        record.UploadedByUserId.ShouldBe(viewerId);
        record.Sha256.ShouldBe(Hash(bytes));
    }

    [Fact]
    public async Task Undoing_a_link_taken_down_does_not_hang_up_a_document_the_person_undoing_may_no_longer_read()
    {
        var cave = await CreateCaveAsync(locationProtected: false);
        await GrantAsync(cave, AccessAction.Read | AccessAction.Write);
        var ownerSet = await CreateSetAsync(owner, [cave]);
        var bytes = Corpus.GeotaggedJpeg(45.41, 25.71);
        var documentId = Guid.CreateVersion7();
        await UploadAsync(owner, ownerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documents = new[] { NewDocument(documentId, bytes, await StageAsync(owner, "IMG_9.jpg", bytes)) },
        });
        await AllowDocumentReadAsync(viewerId, documentId);

        var viewerSet = await CreateSetAsync(viewer, [cave]);
        await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, cave) },
        });
        var removed = await UploadAsync(viewer, viewerSet, new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            documentLinks = new[] { Link(documentId, cave, deleted: true) },
        });
        (await LinkCountAsync(documentId, cave)).ShouldBe(0);

        // The rule that let the viewer read the document is withdrawn; hanging it back up would
        // hand it to every reader of the cave.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.AccessEntries
                .Where(e => e.SubjectId == viewerId && e.Domain == AccessDomain.Documents && e.ScopeId == documentId)
                .ExecuteDeleteAsync();
        }

        var undo = await RevertAsync(viewer, removed.GetProperty("importBatchId").GetGuid());
        var line = undo.GetProperty("items").EnumerateArray().Single();
        line.GetProperty("outcome").GetString().ShouldBe("not_undone");
        line.GetProperty("reason").GetString().ShouldBe("forbidden");
        (await LinkCountAsync(documentId, cave)).ShouldBe(0);

        // Readable again, the same undo hangs it back up.
        await AllowDocumentReadAsync(viewerId, documentId);
        var again = await RevertAsync(viewer, removed.GetProperty("importBatchId").GetGuid());
        again.GetProperty("items").EnumerateArray().Single().GetProperty("outcome").GetString().ShouldBe("undone");
        (await LinkCountAsync(documentId, cave)).ShouldBe(1);
    }

    private static object NewDocument(Guid id, byte[] bytes, Guid staged) => new
    {
        id,
        fileName = "photo.jpg",
        sha256 = Hash(bytes),
        sizeBytes = bytes.Length,
        stagedUploadId = staged,
    };

    private static object Link(Guid documentId, Guid featureId, bool deleted = false) =>
        new { documentId, target = new { kind = "feature", id = featureId }, deleted };

    private static string? Code(JsonElement result) =>
        result.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<JsonElement> UploadAsync(HttpClient client, Guid setId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/sync/sets/{setId}/upload", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<JsonElement> RevertAsync(HttpClient client, Guid importBatchId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/import-batches/{importBatchId}/revert", new { });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker} {Guid.NewGuid():N}"[..30],
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Links {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Gives the viewer the named rights on one cave and nothing else — exact view never among them.</summary>
    private async Task GrantAsync(Guid featureId, AccessAction actions)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = viewerId,
                    effect = "allow",
                    actions = actions.ToString().Replace(" ", string.Empty),
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A rule letting one person read one document, written straight into storage.</summary>
    private async Task AllowDocumentReadAsync(Guid userId, Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Read,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = documentId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A rule letting one person add documents anywhere, written straight into storage.</summary>
    private async Task AllowDocumentCreateAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> StageAsync(HttpClient client, string fileName, byte[] bytes)
    {
        var opened = await client.PostAsJsonAsync("/api/v1/files/uploads", new { fileName, sizeBytes = bytes.Length, stageForSync = true });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    private async Task<Attachment?> AttachmentAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    }

    private async Task<int> LinkCountAsync(Guid documentId, Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from link in db.Attachments
                      join file in db.StoredFiles on link.FileId equals file.Id
                      join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                      where link.FeatureId == featureId && version.DocumentId == documentId
                      select link.Id).CountAsync();
    }

    private async Task<Guid> LinkFileAsync(Guid documentId, Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from link in db.Attachments
                      join file in db.StoredFiles on link.FileId equals file.Id
                      join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
                      where link.FeatureId == featureId && version.DocumentId == documentId
                      select link.FileId).SingleAsync();
    }

    private async Task<Guid> CurrentFileAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await (from version in db.DocumentVersions
                      join file in db.StoredFiles on version.Id equals file.DocumentVersionId
                      where version.DocumentId == documentId && version.IsCurrent
                      orderby file.CreatedAt, file.Id
                      select file.Id).FirstAsync();
    }

    private async Task<bool> DocumentAliveAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Documents.AsNoTracking().AnyAsync(d => d.Id == documentId);
    }
}
