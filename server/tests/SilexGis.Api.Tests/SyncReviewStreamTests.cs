// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What an account under review is told after it uploaded: the decisions on its held rows through
/// the reviews stream, with a turned-down edit's server row only for a caller who may place it; the
/// messages a hold and a decision send, and to whom; how long a held photo's bytes are kept; and
/// who the people looking after what phones send are.
/// </summary>
/// <remarks>Every picture here is generated, with an invented position.</remarks>
public sealed class SyncReviewStreamTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient uploader = null!;
    private HttpClient reviewer = null!;
    private Guid uploaderId;
    private Guid reviewerId;
    private Guid set;
    private string marker = null!;

    public SyncReviewStreamTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-syncreviews-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        uploaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"srs-up-{marker}@t.local");
        uploader = await AuthHelper.BearerClientAsync(factory, $"srs-up-{marker}@t.local");
        reviewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"srs-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"srs-adm-{marker}@t.local");

        var response = await uploader.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_turned_down_edit_is_told_with_the_servers_row_only_to_a_caller_who_may_place_it()
    {
        var caveId = Guid.CreateVersion7();
        var entranceId = Guid.CreateVersion7();
        var created = await UploadAsync(held: false, Cave(caveId, "Peștera Z"), Entrance(entranceId, caveId));
        var revision = created.GetProperty("rows")[1].GetProperty("revision").GetDateTimeOffset();

        await MarkAsync(true);
        var held = await UploadAsync(held: true, Entrance(entranceId, caveId, revision, [22.6333, 45.6444]));
        var reviewId = held.GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewId}/reject", new { note = "Wrong shaft" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The caller may place the entrance: the decision comes with the server's row, at the
        // point the server holds, not the one the phone proposed.
        var recorder = new ContractFixture()
            .Literal(marker, "marker").Name(set, "set").Name(caveId, "cave")
            .Name(entranceId, "entrance").Name(reviewId, "review");
        var told = await recorder.RecordGetAsync("21-reviews-first-page", uploader, $"/api/v1/sync/sets/{set}/reviews");
        var review = told.GetProperty("reviews").EnumerateArray().Single();
        review.GetProperty("reviewId").GetGuid().ShouldBe(reviewId);
        review.GetProperty("entity").GetString().ShouldBe("rows");
        review.GetProperty("rowId").GetGuid().ShouldBe(entranceId);
        review.GetProperty("outcome").GetString().ShouldBe("rejected");
        review.GetProperty("note").GetString().ShouldBe("Wrong shaft");
        review.GetProperty("revision").GetDateTimeOffset().ShouldBe(revision, TimeSpan.FromMilliseconds(1));
        var current = review.GetProperty("current");
        current.GetProperty("id").GetGuid().ShouldBe(entranceId);
        current.GetRawText().ShouldContain("22.51");
        current.GetRawText().ShouldNotContain("22.6333");

        // Now the entrance is protected, belongs to somebody else (an owner is always told where
        // their own rows are), and the caller is explicitly denied exact positions: the same
        // decision is told, the caller still reads the row's revision, and the row is absent
        // rather than blurred.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Features.Where(f => f.Id == caveId || f.Id == entranceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.OwnerUserId, reviewerId)
                    .SetProperty(f => f.LocationProtected, true)
                    .SetProperty(f => f.IsProtectedEffective, true));
        }

        await GrantAsync(uploaderId, AccessEffect.Deny, AccessDomain.Features, AccessAction.ViewExactLocation);
        var withheld = await recorder.RecordGetAsync(
            "22-reviews-current-withheld", uploader, $"/api/v1/sync/sets/{set}/reviews");
        var blind = withheld.GetProperty("reviews").EnumerateArray().Single();
        blind.GetProperty("reviewId").GetGuid().ShouldBe(reviewId);
        blind.GetProperty("outcome").GetString().ShouldBe("rejected");
        blind.TryGetProperty("revision", out _).ShouldBeTrue();
        blind.TryGetProperty("current", out _).ShouldBeFalse();
        withheld.GetRawText().ShouldNotContain("22.51");
        withheld.GetRawText().ShouldNotContain("45.61");
    }

    [Fact]
    public async Task Decisions_are_told_in_decision_order_with_revisions_across_pages_and_only_to_their_uploader()
    {
        await MarkAsync(true);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var heldFirst = await UploadAsync(held: true, Cave(first, "Avenul A"));
        var replaced = (await UploadAsync(held: true, Cave(second, "Avenul B"))).GetProperty("rows")[0].GetProperty("reviewId").GetGuid();

        // The same row again with other content replaces the proposal still waiting.
        var replacing = (await UploadAsync(held: true, Cave(second, "Avenul B bis"))).GetProperty("rows")[0].GetProperty("reviewId").GetGuid();
        replacing.ShouldNotBe(replaced);

        (await reviewer.PostAsJsonAsync(
            $"/api/v1/review/batches/{heldFirst.GetProperty("importBatchId").GetGuid()}/accept", new { })).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{replacing}/accept", new { })).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        // One at a time, resumed by the cursor each page hands back.
        var seen = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < 5; page++)
        {
            var answer = await ReviewsAsync(uploader, cursor, pageSize: 1);
            seen.AddRange(answer.GetProperty("reviews").EnumerateArray());
            cursor = answer.GetProperty("nextCursor").GetString();
            if (!answer.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        seen.Select(r => r.GetProperty("outcome").GetString()).ShouldBe(["superseded", "accepted", "accepted"]);
        seen[0].GetProperty("reviewId").GetGuid().ShouldBe(replaced);
        seen[0].GetProperty("rowId").GetGuid().ShouldBe(second);
        seen.Select(r => r.GetProperty("rowId").GetGuid()).Skip(1).ShouldBe([first, second]);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var written = await db.Features.AsNoTracking().Where(f => f.Id == first || f.Id == second).ToDictionaryAsync(f => f.Id);
            written[second].Name.ShouldBe("Avenul B bis");
            foreach (var accepted in seen.Skip(1))
            {
                accepted.GetProperty("revision").GetDateTimeOffset()
                    .ShouldBe(written[accepted.GetProperty("rowId").GetGuid()].UpdatedAt, TimeSpan.FromMilliseconds(1));
            }
        }

        // Caught up: the position is kept, and nothing more is told.
        var caughtUp = await ReviewsAsync(uploader, cursor);
        caughtUp.GetProperty("reviews").GetArrayLength().ShouldBe(0);
        caughtUp.GetProperty("nextCursor").GetString().ShouldBe(cursor);

        // Somebody else's decisions are not this account's to read, and a position another stream
        // issued is refused rather than read as one of these.
        using var stranger = await AnotherPhoneAsync("stranger");
        (await ReviewsAsync(stranger.Client, setId: stranger.Set)).GetProperty("reviews").GetArrayLength().ShouldBe(0);
        var foreign = await uploader.GetAsync($"/api/v1/sync/sets/{set}/trips?cursor={Uri.EscapeDataString(cursor!)}");
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await uploader.GetAsync($"/api/v1/sync/sets/{stranger.Set}/reviews")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/sync/sets/{set}/reviews")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The part is announced on every installation.
        var capabilities = await uploader.GetFromJsonAsync<JsonElement>("/api/v1/sync/capabilities");
        capabilities.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ShouldContain("reviews");
    }

    [Fact]
    public async Task A_hold_tells_the_reviewers_and_a_decision_tells_the_uploader()
    {
        // A reader of the queue who may not decide is not told; a holder of the right outside any
        // administrators' group is.
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"srs-rd-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        var deciderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"srs-dc-{marker}@t.local");
        await GrantAsync(deciderId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read | AccessAction.Write);

        await MarkAsync(true);
        var caves = new[] { Cave(Guid.CreateVersion7(), "Peștera N1"), Cave(Guid.CreateVersion7(), "Peștera N2") };
        var held = await UploadAsync(held: true, caves);
        var batchId = held.GetProperty("importBatchId").GetGuid();

        var awaiting = await NoticesAsync(NotificationCategory.UploadAwaitingReview);
        awaiting.Select(n => n.RecipientUserId).ShouldContain(reviewerId);
        awaiting.Select(n => n.RecipientUserId).ShouldContain(deciderId);
        awaiting.Select(n => n.RecipientUserId).ShouldNotContain(readerId);
        awaiting.Select(n => n.RecipientUserId).ShouldNotContain(uploaderId);
        awaiting.ShouldAllBe(n => n.TemplateKey == MessageTemplateCatalog.NotifyUploadAwaitingReview);
        var placeholders = JsonDocument.Parse(awaiting.First(n => n.RecipientUserId == reviewerId).Placeholders).RootElement;
        placeholders.GetProperty("lineCount").GetString().ShouldBe("2");
        placeholders.GetProperty("url").GetString().ShouldBe($"/review/batches/{batchId}");
        (await NoticesAsync(NotificationCategory.UploadReviewed)).ShouldBeEmpty();

        // The same content again holds nothing new and tells nobody again.
        var countBefore = awaiting.Count;
        var again = await UploadAsync(held: true, caves);
        again.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid())
            .ShouldBe(held.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("reviewId").GetGuid()));
        (await NoticesAsync(NotificationCategory.UploadAwaitingReview)).Count.ShouldBe(countBefore);

        var rejected = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/reject", new { note = "No" });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var reviewed = await NoticesAsync(NotificationCategory.UploadReviewed);
        var told = reviewed.ShouldHaveSingleItem();
        told.RecipientUserId.ShouldBe(uploaderId);
        told.TemplateKey.ShouldBe(MessageTemplateCatalog.NotifyUploadReviewed);
        var counts = JsonDocument.Parse(told.Placeholders).RootElement;
        (counts.GetProperty("acceptedCount").GetString(), counts.GetProperty("rejectedCount").GetString(),
            counts.GetProperty("failedCount").GetString()).ShouldBe(("0", "2", "0"));
        (await NoticesAsync(NotificationCategory.UploadAwaitingReview)).Count.ShouldBe(countBefore);
    }

    [Fact]
    public async Task A_held_photos_bytes_wait_with_it_and_go_after_the_retention_once_turned_down()
    {
        await MarkAsync(true);
        var bytes = Corpus.GeotaggedJpeg(45.31, 25.47);
        var staged = await StageAsync("IMG_0777.jpg", bytes);
        var held = await UploadDocumentsAsync(new
        {
            id = Guid.CreateVersion7(),
            title = "Held shaft",
            fileName = "IMG_0777.jpg",
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            sizeBytes = bytes.Length,
            stagedUploadId = staged,
        });
        var result = held.GetProperty("documents").EnumerateArray().Single();
        result.GetProperty("status").GetString().ShouldBe("held");

        // Waiting: kept past anything the sweep reaches, and a sweep leaves it.
        (await ExpiryAsync(staged)).ShouldBe(HeldUploadBytes.Parked);
        await RunSweepAsync();
        (await ExpiryAsync(staged)).ShouldNotBeNull();

        var rejected = await reviewer.PostAsJsonAsync(
            $"/api/v1/review/items/{result.GetProperty("reviewId").GetGuid()}/reject", new { });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());

        // Turned down: given back to the sweep for the installation's retention, thirty days unless
        // changed — and still there the moment after.
        var expiry = (await ExpiryAsync(staged))!.Value;
        expiry.ShouldBe(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
        await RunSweepAsync();
        (await ExpiryAsync(staged)).ShouldNotBeNull();

        // Once the retention has passed, the sweep takes it.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.UploadSessions.Where(s => s.Id == staged)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await RunSweepAsync();
        (await ExpiryAsync(staged)).ShouldBeNull();
    }

    [Fact]
    public async Task The_people_looking_after_what_phones_send_are_the_holders_of_the_right_to_decide_uploads()
    {
        var deciderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"srs-w-{marker}@t.local");
        await GrantAsync(deciderId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read | AccessAction.Write);
        var readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"srs-r-{marker}@t.local");
        await GrantAsync(readerId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read);
        var deniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"srs-d-{marker}@t.local");
        await GrantAsync(deniedId, AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read | AccessAction.Write);
        await GrantAsync(deniedId, AccessEffect.Deny, AccessDomain.UploadReview, AccessAction.Write);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var candidates = await TripAttentionRecipients.CandidatesAsync(db, uploaderId);
        candidates.ShouldContain(deciderId);
        candidates.ShouldContain(reviewerId);
        candidates.ShouldContain(uploaderId);
        candidates.ShouldNotContain(readerId);
        candidates.ShouldNotContain(deniedId);

        (await UploadReviewers.WriteHolderIdsAsync(db)).ShouldNotContain(uploaderId);
    }

    /// <summary>The notices of one category about this class's own uploader's batches.</summary>
    private async Task<List<Notification>> NoticesAsync(NotificationCategory category)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var pages = (await db.ImportBatches.AsNoTracking()
                .Where(b => b.ConfirmedByUserId == uploaderId)
                .Select(b => b.Id)
                .ToListAsync())
            .Select(UploadReviewNotices.PageOf)
            .ToHashSet();
        var notices = await db.Notifications.AsNoTracking().Where(n => n.Category == category).ToListAsync();
        return [.. notices.Where(n =>
            JsonDocument.Parse(n.Placeholders).RootElement.TryGetProperty("url", out var url)
            && pages.Contains(url.GetString() ?? ""))];
    }

    private async Task<DateTimeOffset?> ExpiryAsync(Guid sessionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.UploadSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => (DateTimeOffset?)s.ExpiresAt).FirstOrDefaultAsync();
    }

    private async Task RunSweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new UploadSessionSweepHandler(
            scope.ServiceProvider.GetRequiredService<SilexGisDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStore>(),
            NullLogger<UploadSessionSweepHandler>.Instance);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.UploadSessionSweep }, CancellationToken.None);
    }

    private async Task<Guid> StageAsync(string fileName, byte[] bytes)
    {
        var opened = await uploader.PostAsJsonAsync("/api/v1/files/uploads", new
        {
            fileName,
            sizeBytes = bytes.Length,
            stageForSync = true,
        });
        var payload = await opened.Content.ReadAsStringAsync();
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var session = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await uploader.PutAsync($"/api/v1/files/uploads/{session}?offset=0", content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.PostAsync($"/api/v1/files/uploads/{session}/complete", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return session;
    }

    private async Task<OtherPhone> AnotherPhoneAsync(string who)
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"srs-{who}-{marker}@t.local");
        var client = await AuthHelper.BearerClientAsync(factory, $"srs-{who}-{marker}@t.local");
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Other {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new OtherPhone(client, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private sealed record OtherPhone(HttpClient Client, Guid Set) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private async Task GrantAsync(Guid userId, AccessEffect effect, AccessDomain domain, AccessAction actions)
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
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{uploaderId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> ReviewsAsync(HttpClient client, string? cursor = null, int? pageSize = null, Guid? setId = null)
    {
        var query = new List<string>();
        if (cursor is not null)
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        if (pageSize is not null)
        {
            query.Add($"pageSize={pageSize}");
        }

        var url = $"/api/v1/sync/sets/{setId ?? set}/reviews" + (query.Count > 0 ? "?" + string.Join('&', query) : "");
        var response = await client.GetAsync(url);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private async Task<JsonElement> UploadAsync(bool held, params object[] rows)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = held ? new[] { "held" } : Array.Empty<string>(),
            rows,
        });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private async Task<JsonElement> UploadDocumentsAsync(params object[] documents)
    {
        var response = await uploader.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            clientFeatures = new[] { "held" },
            documents,
        });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static object Cave(Guid id, string name) => new
    {
        id,
        kind = "cave",
        baseRevision = (DateTimeOffset?)null,
        deleted = false,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private static object Entrance(Guid id, Guid caveId, DateTimeOffset? revision = null, double[]? at = null) => new
    {
        id,
        kind = "caveEntrance",
        parentId = caveId,
        baseRevision = revision,
        deleted = false,
        name = "Intrare",
        entranceTypeCode = "natural",
        isMain = true,
        geometry = new { type = "Point", coordinates = at ?? [22.51, 45.61] },
        altitude = 640.0,
        positionQuality = "Unknown",
    };

    public ValueTask DisposeAsync()
    {
        uploader?.Dispose();
        reviewer?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
