// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Catalogue;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The file imports of an account under review wait for a reviewer instead of landing: a vector
/// file, a drop of photographs and a catalogue selection are kept as their commit request, with a
/// waiting line per candidate, and nothing is written until a reviewer accepts — whereupon the
/// source's own commit runs, under the uploader's access, writing exactly what the same commit
/// writes unheld, and only the candidates accepted. Every file is generated here, every picture
/// carries an invented position, and the catalogue is a stub.
/// </summary>
public sealed class FileImportReviewHoldTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly PostgresFixture postgres;
    private readonly string filesRoot;
    private readonly CatalogueStub catalogue = new();
    private HttpClient importer = null!;
    private HttpClient reviewer = null!;
    private HttpClient outsider = null!;
    private Guid importerId;
    private Guid reviewerId;
    private string marker = null!;

    public FileImportReviewHoldTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-importhold-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Speologie:ApiKey"] = "not-a-real-key-0000",
                ["Speologie:MinRequestIntervalMs"] = "0",
            },
            services => services
                .AddHttpClient(SpeologieClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => catalogue));
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        importerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fih-imp-{marker}@t.local");
        importer = await AuthHelper.BearerClientAsync(factory, $"fih-imp-{marker}@t.local");
        reviewerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"fih-adm-{marker}@t.local");
        reviewer = await AuthHelper.BearerClientAsync(factory, $"fih-adm-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"fih-out-{marker}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"fih-out-{marker}@t.local");
    }

    // ---------- vector files ----------

    [Fact]
    public async Task A_held_vector_import_writes_nothing_and_its_acceptance_writes_what_the_commit_writes_unheld()
    {
        // The same confirmation twice: once before the account is put under review, once after,
        // accepted whole. Positions differ so neither is the other's duplicate.
        var plainFile = await UploadGpxAsync(("Plain", 45.10, 22.10));
        var plainIds = await SourceIdsAsync(plainFile);
        await RunVectorCommitAsync(plainFile, plainIds, "Plain");

        await MarkAsync(true);
        var heldFile = await UploadGpxAsync(("Held", 45.30, 22.30));
        var heldIds = await SourceIdsAsync(heldFile);
        var jobsBefore = await CountAsync(db => db.ProcessingJobs.CountAsync(j => j.RequestedBy == importerId));
        var answer = await VectorCommitAsync(heldFile, heldIds, "Held");
        answer.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var held = await answer.Content.ReadFromJsonAsync<JsonElement>();
        held.GetProperty("held").GetBoolean().ShouldBeTrue();
        held.GetProperty("jobId").ValueKind.ShouldBe(JsonValueKind.Null);
        held.GetProperty("queued").GetInt32().ShouldBe(1);
        var batchId = held.GetProperty("batchId").GetGuid();

        // Nothing queued, nothing written, and the batch waits with one line per candidate and the
        // request it will run.
        (await CountAsync(db => db.ProcessingJobs.CountAsync(j => j.RequestedBy == importerId))).ShouldBe(jobsBefore);
        (await FeaturesNamedAsync("Held")).ShouldBeEmpty();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
            batch.ReviewState.ShouldBe(ImportReviewState.Pending);
            batch.HeldRequest.ShouldNotBeNull();
            batch.GeofileId.ShouldBe(heldFile);
            var lines = await db.ImportBatchItems.AsNoTracking().Where(i => i.ImportBatchId == batchId).ToListAsync();
            lines.Count.ShouldBe(1);
            lines.ShouldAllBe(l => l.ReviewDecision == ImportReviewDecision.Pending && l.ReviewId != null && l.FeatureId == null);
        }

        // Somebody who reviews nothing is not even told the batch exists; the reviewer is shown it,
        // with what it proposes.
        (await outsider.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/review/batches/{batchId}");
        var line = detail.GetProperty("lines")[0];
        line.GetProperty("section").GetString().ShouldBe("vectorFile");
        line.GetProperty("proposal").GetProperty("name").GetString().ShouldBe($"Held 0 {marker}");

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { note = "ok" });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("decided").EnumerateArray().ShouldAllBe(l => l.GetProperty("decision").GetString() == "accepted");
        result.GetProperty("batches")[0].GetProperty("reviewState").GetString().ShouldBe("accepted");

        await using var check = factory.Services.CreateAsyncScope();
        var read = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var plain = await read.Features.AsNoTracking().SingleAsync(f => f.Name == $"Plain 0 {marker}");
        var made = await read.Features.AsNoTracking().SingleAsync(f => f.Name == $"Held 0 {marker}");
        (made.Kind, made.FeatureTypeId, made.OwnerUserId, made.Visibility, made.CavingGroupId, made.LocationProtected)
            .ShouldBe((plain.Kind, plain.FeatureTypeId, plain.OwnerUserId, plain.Visibility, plain.CavingGroupId, plain.LocationProtected));

        // The held line became the provenance line undo works from, and the batch counts it.
        var stamped = await read.ImportBatchItems.AsNoTracking().SingleAsync(i => i.ImportBatchId == batchId);
        stamped.FeatureId.ShouldBe(made.Id);
        stamped.SourceFeatureId.ShouldBe(heldIds[0]);
        stamped.ReviewDecision.ShouldBe(ImportReviewDecision.Accepted);
        var settled = await read.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        settled.CreatedCount.ShouldBe(1);
        settled.DecidedByUserId.ShouldBe(reviewerId);

        // Written in the review's change set, filed under the held batch, by the reviewer.
        var history = await read.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.EntityId == made.Id.ToString() && a.Action == AuditActions.Created);
        history.Source.ShouldBe(ChangeSource.ReviewDecision);
        history.ImportBatchId.ShouldBe(batchId);
        history.UserId.ShouldBe(reviewerId);
        (await read.GeofileImportSessions.AsNoTracking().CountAsync(s => s.GeofileId == heldFile)).ShouldBe(0);
    }

    [Fact]
    public async Task Accepting_some_candidates_writes_only_those_and_rejecting_writes_nothing_and_keeps_the_review()
    {
        await MarkAsync(true);
        var file = await UploadGpxAsync(("Part", 45.40, 22.40), ("Part", 45.41, 22.41));
        var ids = await SourceIdsAsync(file);
        ids.Count.ShouldBe(2);
        var batchId = await HeldBatchOf(await VectorCommitAsync(file, ids, "Part"));
        var reviewIds = await ReviewIdsByKeyAsync(batchId);

        var first = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewIds[ids[0].ToString(CultureInfo.InvariantCulture)]}/accept", new { });
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var second = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewIds[ids[1].ToString(CultureInfo.InvariantCulture)]}/reject", new { });
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());

        (await FeaturesNamedAsync("Part")).ShouldBe([$"Part 0 {marker}"]);
        (await CountAsync(db => db.ImportBatches.Where(b => b.Id == batchId).Select(b => (int)b.ReviewState).SingleAsync()))
            .ShouldBe((int)ImportReviewState.PartlyAccepted);

        // A rejection writes nothing and spends nothing: the account's saved review stays, and the
        // same file can be confirmed again.
        var other = await UploadGpxAsync(("Turned", 45.50, 22.50));
        var otherIds = await SourceIdsAsync(other);
        var saved = await importer.PutAsJsonAsync($"/api/v1/geofiles/{other}/import/session", new
        {
            options = VectorOptions(),
            decisions = Decisions(otherIds, "Turned"),
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var turned = await HeldBatchOf(await VectorCommitAsync(other, otherIds, "Turned"));
        var rejected = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{turned}/reject", new { note = "no" });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        (await FeaturesNamedAsync("Turned")).ShouldBeEmpty();
        (await CountAsync(db => db.GeofileImportSessions.CountAsync(s => s.GeofileId == other && s.UserId == importerId))).ShouldBe(1);
        (await VectorCommitAsync(other, otherIds, "Turned")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task An_uploader_who_lost_the_right_to_create_since_is_answered_failed()
    {
        await MarkAsync(true);
        var file = await UploadGpxAsync(("Lost", 45.60, 22.60));
        var batchId = await HeldBatchOf(await VectorCommitAsync(file, await SourceIdsAsync(file), "Lost"));

        await DenyAsync(importerId, AccessDomain.Features, AccessAction.Create);

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var line = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided")[0];
        line.GetProperty("decision").GetString().ShouldBe("failed");
        line.GetProperty("failureCode").GetString().ShouldBe(CreateRules.ForbiddenCode);
        (await FeaturesNamedAsync("Lost")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_held_vector_import_tells_the_reviewers_once_and_its_decision_tells_the_uploader()
    {
        await MarkAsync(true);
        var file = await UploadGpxAsync(("Told", 45.65, 22.65), ("Told", 45.66, 22.66));
        var batchId = await HeldBatchOf(await VectorCommitAsync(file, await SourceIdsAsync(file), "Told"));

        // One notice per reviewer for the batch, none for the account that is under review, and
        // nothing yet for the decision.
        var awaiting = await NoticesAsync(batchId, NotificationCategory.UploadAwaitingReview);
        awaiting.Count(n => n.RecipientUserId == reviewerId).ShouldBe(1);
        awaiting.Select(n => n.RecipientUserId).ShouldNotContain(importerId);
        JsonDocument.Parse(awaiting.Single(n => n.RecipientUserId == reviewerId).Placeholders).RootElement
            .GetProperty("lineCount").GetString().ShouldBe("2");
        (await NoticesAsync(batchId, NotificationCategory.UploadReviewed)).ShouldBeEmpty();

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var told = (await NoticesAsync(batchId, NotificationCategory.UploadReviewed)).ShouldHaveSingleItem();
        told.RecipientUserId.ShouldBe(importerId);
        JsonDocument.Parse(told.Placeholders).RootElement.GetProperty("acceptedCount").GetString().ShouldBe("2");
        (await NoticesAsync(batchId, NotificationCategory.UploadAwaitingReview)).Count.ShouldBe(awaiting.Count);
    }

    [Fact]
    public async Task A_vector_confirmation_that_sets_every_row_aside_is_refused_and_not_held()
    {
        await MarkAsync(true);
        var file = await UploadGpxAsync(("Aside", 45.67, 22.67));
        var ids = await SourceIdsAsync(file);
        var batchesBefore = await HeldBatchCountAsync();
        var answer = await importer.PostAsJsonAsync($"/api/v1/geofiles/{file}/import/commit", new
        {
            options = VectorOptions(),
            selection = ids,
            decisions = ids.ToDictionary(id => id.ToString(CultureInfo.InvariantCulture), _ => (object)new { action = "skip" }),
            withoutReview = false,
        });
        await ShouldBeRefusedAsync(answer, HttpStatusCode.BadRequest, "import.nothing_created");
        (await HeldBatchCountAsync()).ShouldBe(batchesBefore);
    }

    // ---------- photographs ----------

    [Fact]
    public async Task A_held_drop_of_photographs_files_only_the_accepted_places_as_the_drop_files_them_unheld()
    {
        var plainPhoto = await UploadPhotoAsync(At(45.70, 22.70));
        var plainKey = await PhotoKeyAsync(plainPhoto);
        var plain = await importer.PostAsJsonAsync("/api/v1/photo-import/commit", PhotoBody([plainPhoto], [plainKey], "PhotoPlain"));
        plain.StatusCode.ShouldBe(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());

        await MarkAsync(true);
        var one = await UploadPhotoAsync(At(45.80, 22.80));
        var two = await UploadPhotoAsync(At(45.90, 22.90));
        var keyOne = await PhotoKeyAsync(one);
        var keyTwo = await PhotoKeyAsync(two);
        var answer = await importer.PostAsJsonAsync("/api/v1/photo-import/commit", PhotoBody([one, two], [keyOne, keyTwo], "PhotoHeld"));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var batch = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batch");
        batch.GetProperty("reviewState").GetString().ShouldBe("pending");
        var batchId = batch.GetProperty("id").GetGuid();
        (await FeaturesNamedAsync("PhotoHeld")).ShouldBeEmpty();

        var reviewIds = await ReviewIdsByKeyAsync(batchId);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewIds[keyOne.ToString("D")]}/accept", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewIds[keyTwo.ToString("D")]}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await FeaturesNamedAsync("PhotoHeld")).ShouldBe([$"PhotoHeld {marker} {keyOne:D}"]);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var plainPlace = await db.Features.AsNoTracking().SingleAsync(f => f.Name == $"PhotoPlain {marker} {plainKey:D}");
        var heldPlace = await db.Features.AsNoTracking().SingleAsync(f => f.Name == $"PhotoHeld {marker} {keyOne:D}");
        (heldPlace.Kind, heldPlace.FeatureTypeId, heldPlace.OwnerUserId, heldPlace.Visibility, heldPlace.LocationProtected)
            .ShouldBe((plainPlace.Kind, plainPlace.FeatureTypeId, plainPlace.OwnerUserId, plainPlace.Visibility, plainPlace.LocationProtected));
        heldPlace.Geom!.Coordinate.Y.ShouldBe(45.80, 1e-6);
        (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).ReviewState.ShouldBe(ImportReviewState.PartlyAccepted);
        var history = await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.EntityId == heldPlace.Id.ToString() && a.Action == AuditActions.Created);
        history.Source.ShouldBe(ChangeSource.ReviewDecision);
        history.ImportBatchId.ShouldBe(batchId);
    }

    [Fact]
    public async Task A_held_drop_is_invisible_to_others_and_rejecting_it_files_nothing_and_keeps_the_saved_review()
    {
        await MarkAsync(true);
        var photo = await UploadPhotoAsync(At(45.95, 22.95));
        var key = await PhotoKeyAsync(photo);
        var saved = await importer.PutAsJsonAsync("/api/v1/photo-import/session", new
        {
            fileIds = new[] { photo },
            options = PhotoOptions(),
            decisions = new Dictionary<string, object> { [key.ToString("D")] = new { action = "create", kind = "cave", name = $"PhotoTurned {marker} {key:D}" } },
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var answer = await importer.PostAsJsonAsync("/api/v1/photo-import/commit", PhotoBody([photo], [key], "PhotoTurned"));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var batchId = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batch").GetProperty("id").GetGuid();
        (await outsider.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var rejected = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/reject", new { note = "no" });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        (await FeaturesNamedAsync("PhotoTurned")).ShouldBeEmpty();
        (await CountAsync(db => db.PhotoImportSessions.CountAsync(s => s.UserId == importerId))).ShouldBe(1);
        (await CountAsync(db => db.ImportBatches.Where(b => b.Id == batchId).Select(b => (int)b.ReviewState).SingleAsync()))
            .ShouldBe((int)ImportReviewState.Rejected);
    }

    [Fact]
    public async Task A_held_drop_accepted_after_the_uploader_lost_the_right_to_create_is_answered_failed()
    {
        await MarkAsync(true);
        var photo = await UploadPhotoAsync(At(45.96, 22.96));
        var key = await PhotoKeyAsync(photo);
        var answer = await importer.PostAsJsonAsync("/api/v1/photo-import/commit", PhotoBody([photo], [key], "PhotoLost"));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var batchId = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batch").GetProperty("id").GetGuid();

        await DenyAsync(importerId, AccessDomain.Features, AccessAction.Create);

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var line = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").EnumerateArray().ShouldHaveSingleItem();
        line.GetProperty("decision").GetString().ShouldBe("failed");
        line.GetProperty("failureCode").GetString().ShouldBe(CreateRules.ForbiddenCode);
        (await FeaturesNamedAsync("PhotoLost")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_photo_confirmation_the_commit_would_refuse_is_refused_and_not_held()
    {
        await MarkAsync(true);
        var photo = await UploadPhotoAsync(At(45.97, 22.97));
        var key = await PhotoKeyAsync(photo);
        var batchesBefore = await HeldBatchCountAsync();

        // Every group set aside.
        var aside = await importer.PostAsJsonAsync("/api/v1/photo-import/commit", new
        {
            options = PhotoOptions(),
            fileIds = new[] { photo },
            selection = new[] { key },
            decisions = new Dictionary<string, object> { [key.ToString("D")] = new { action = "skip" } },
        });
        await ShouldBeRefusedAsync(aside, HttpStatusCode.BadRequest, "photo_import.nothing_created");

        // More places than one confirmation may create: refused now, as it is for anybody else,
        // rather than held to fail at every acceptance.
        var keys = Enumerable.Range(0, 251).Select(_ => Guid.NewGuid()).Prepend(key).ToList();
        var tooMany = await importer.PostAsJsonAsync("/api/v1/photo-import/commit", PhotoBody([photo], keys, "PhotoMany"));
        await ShouldBeRefusedAsync(tooMany, HttpStatusCode.BadRequest, "photo_import.selection_too_large");
        (await HeldBatchCountAsync()).ShouldBe(batchesBefore);
    }

    // ---------- the catalogue ----------

    [Fact]
    public async Task A_held_catalogue_selection_asks_the_catalogue_nothing_until_a_reviewer_accepts_it()
    {
        catalogue.Holds(
            (81001, $"Peștera Ținută {marker}"),
            (81002, $"Peștera Refuzată {marker}"),
            (81003, $"Peștera Liberă {marker}"));

        var plain = await importer.PostAsJsonAsync("/api/v1/catalogue/speologie/import", CatalogueBody([81003]));
        plain.StatusCode.ShouldBe(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());
        catalogue.Reset();

        await MarkAsync(true);
        var answer = await importer.PostAsJsonAsync("/api/v1/catalogue/speologie/import", CatalogueBody([81001, 81002]));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var held = await answer.Content.ReadFromJsonAsync<JsonElement>();
        held.GetProperty("held").GetBoolean().ShouldBeTrue();
        held.GetProperty("createdCount").GetInt32().ShouldBe(0);
        var batchId = held.GetProperty("batchId").GetGuid();
        catalogue.CallCount.ShouldBe(0);
        (await FeaturesNamedAsync("Ținută")).ShouldBeEmpty();

        var reviewIds = await ReviewIdsByKeyAsync(batchId);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewIds["81002"]}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        catalogue.CallCount.ShouldBe(0);
        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/items/{reviewIds["81001"]}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        catalogue.CallCount.ShouldBeGreaterThan(0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var made = await db.Features.AsNoTracking().SingleAsync(f => f.Name == $"Peștera Ținută {marker}");
        var free = await db.Features.AsNoTracking().SingleAsync(f => f.Name == $"Peștera Liberă {marker}");
        (made.Kind, made.FeatureTypeId, made.OwnerUserId, made.Visibility).ShouldBe((free.Kind, free.FeatureTypeId, free.OwnerUserId, free.Visibility));
        (await db.Features.AsNoTracking().AnyAsync(f => f.Name == $"Peștera Refuzată {marker}")).ShouldBeFalse();
        (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).ReviewState.ShouldBe(ImportReviewState.PartlyAccepted);
    }

    [Fact]
    public async Task A_catalogue_that_does_not_answer_leaves_the_held_selection_waiting()
    {
        await MarkAsync(true);
        var answer = await importer.PostAsJsonAsync("/api/v1/catalogue/speologie/import", CatalogueBody([81101]));
        var batchId = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetGuid();

        catalogue.Fails();
        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await accepted.Content.ReadAsStringAsync());
        (await CountAsync(db => db.ImportBatchItems.CountAsync(i => i.ImportBatchId == batchId && i.ReviewDecision == ImportReviewDecision.Pending)))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_held_catalogue_selection_is_invisible_to_others_and_rejecting_it_imports_nothing()
    {
        catalogue.Holds((81301, $"Peștera Respinsă {marker}"));
        await MarkAsync(true);
        var answer = await importer.PostAsJsonAsync("/api/v1/catalogue/speologie/import", CatalogueBody([81301]));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var batchId = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetGuid();
        (await outsider.GetAsync($"/api/v1/review/batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var rejected = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/reject", new { });
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        catalogue.CallCount.ShouldBe(0);
        (await FeaturesNamedAsync("Respinsă")).ShouldBeEmpty();
        (await CountAsync(db => db.ImportBatches.Where(b => b.Id == batchId).Select(b => (int)b.ReviewState).SingleAsync()))
            .ShouldBe((int)ImportReviewState.Rejected);
    }

    [Fact]
    public async Task A_held_catalogue_selection_accepted_after_the_uploader_lost_the_right_to_create_is_answered_failed()
    {
        catalogue.Holds((81401, $"Peștera Pierdută {marker}"), (81402, $"Peștera Pierdută Doi {marker}"));
        await MarkAsync(true);
        var answer = await importer.PostAsJsonAsync("/api/v1/catalogue/speologie/import", CatalogueBody([81401, 81402]));
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        var batchId = (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetGuid();

        await DenyAsync(importerId, AccessDomain.Features, AccessAction.Create);

        var accepted = await reviewer.PostAsJsonAsync($"/api/v1/review/batches/{batchId}/accept", new { });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var lines = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decided").EnumerateArray().ToList();
        lines.Count.ShouldBe(2);
        lines.ShouldAllBe(l => l.GetProperty("decision").GetString() == "failed"
            && l.GetProperty("failureCode").GetString() == CreateRules.ForbiddenCode);
        catalogue.CallCount.ShouldBe(0);
        (await FeaturesNamedAsync("Pierdută")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_catalogue_selection_the_import_would_refuse_is_refused_and_not_held()
    {
        await MarkAsync(true);
        var batchesBefore = await HeldBatchCountAsync();

        var aside = await importer.PostAsJsonAsync("/api/v1/catalogue/speologie/import", new
        {
            selection = new[] { 81501 },
            decisions = new Dictionary<string, object> { ["81501"] = new { action = "skip" } },
            visibility = "private",
            cavingGroupId = (Guid?)null,
            locationProtected = false,
            parentId = (Guid?)null,
        });
        await ShouldBeRefusedAsync(aside, HttpStatusCode.BadRequest, "speologie.nothing_created");

        var tooMany = await importer.PostAsJsonAsync(
            "/api/v1/catalogue/speologie/import", CatalogueBody([.. Enumerable.Range(82000, 101)]));
        await ShouldBeRefusedAsync(tooMany, HttpStatusCode.BadRequest, "speologie.selection_too_large");
        (await HeldBatchCountAsync()).ShouldBe(batchesBefore);
        catalogue.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_catalogue_selection_on_an_installation_without_the_catalogue_is_refused_and_not_held()
    {
        // The same database, served by an installation with no catalogue key.
        using var bare = new SilexGisApiFactory(
            postgres.ConnectionString, new Dictionary<string, string?> { ["Files:Root"] = filesRoot });
        using var client = await AuthHelper.BearerClientAsync(bare, $"fih-imp-{marker}@t.local");
        await MarkAsync(true);
        var batchesBefore = await HeldBatchCountAsync();

        var answer = await client.PostAsJsonAsync("/api/v1/catalogue/speologie/import", CatalogueBody([81601]));
        await ShouldBeRefusedAsync(answer, HttpStatusCode.ServiceUnavailable, SpeologieException.NotConfiguredCode);
        (await HeldBatchCountAsync()).ShouldBe(batchesBefore);
    }

    // ---------- helpers ----------

    private Task<int> HeldBatchCountAsync() =>
        CountAsync(db => db.ImportBatches.CountAsync(b => b.ConfirmedByUserId == importerId));

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, raw);
        JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString().ShouldBe(code, raw);
    }

    private async Task<List<Notification>> NoticesAsync(Guid batchId, NotificationCategory category)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var page = UploadReviewNotices.PageOf(batchId);
        var notices = await db.Notifications.AsNoTracking().Where(n => n.Category == category).ToListAsync();
        return [.. notices.Where(n =>
            JsonDocument.Parse(n.Placeholders).RootElement.TryGetProperty("url", out var url) && url.GetString() == page)];
    }

    private async Task<Guid> HeldBatchOf(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, raw);
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("held").GetBoolean().ShouldBeTrue();
        return body.GetProperty("batchId").GetGuid();
    }

    private async Task<Dictionary<string, Guid>> ReviewIdsByKeyAsync(Guid batchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var lines = await db.ImportBatchItems.AsNoTracking().Where(i => i.ImportBatchId == batchId).ToListAsync();
        return lines.ToDictionary(
            l => JsonDocument.Parse(l.Proposal!).RootElement.GetProperty("key").GetString()!,
            l => l.ReviewId!.Value);
    }

    private async Task<List<string>> FeaturesNamedAsync(string prefix)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Features.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Name != null && f.Name.Contains(prefix) && f.Name.Contains(marker))
            .Select(f => f.Name!)
            .OrderBy(n => n)
            .ToListAsync();
    }

    private async Task<T> CountAsync<T>(Func<SilexGisDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<SilexGisDbContext>());
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await reviewer.PutAsJsonAsync(
            $"/api/v1/users/{importerId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task DenyAsync(Guid userId, AccessDomain domain, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Deny,
            Domain = domain,
            Actions = actions,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    // vector

    private static Dictionary<string, object?> VectorOptions() => new()
    {
        ["termRuleSetId"] = null,
        ["languages"] = Array.Empty<string>(),
        ["mapping"] = new { },
        ["elevation"] = "keep",
        ["tracks"] = "ignore",
        ["visibility"] = "private",
        ["duplicateRadiusMeters"] = 1,
        ["tagIds"] = Array.Empty<long>(),
    };

    private Dictionary<string, object> Decisions(IReadOnlyList<long> ids, string prefix) =>
        ids.Select((id, i) => (id, i)).ToDictionary(
            x => x.id.ToString(CultureInfo.InvariantCulture),
            x => (object)new { action = "create", kind = "cave", caveTypeCode = "cave", name = $"{prefix} {x.i} {marker}" });

    private Task<HttpResponseMessage> VectorCommitAsync(Guid geofileId, IReadOnlyList<long> ids, string prefix) =>
        importer.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = VectorOptions(),
            selection = ids,
            decisions = Decisions(ids, prefix),
            withoutReview = false,
        });

    private async Task RunVectorCommitAsync(Guid geofileId, IReadOnlyList<long> ids, string prefix)
    {
        var response = await VectorCommitAsync(geofileId, ids, prefix);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, raw);
        var jobId = JsonDocument.Parse(raw).RootElement.GetProperty("jobId").GetInt64();
        await QueuedJob.RunAsync(factory.Services, jobId);
    }

    private async Task<List<long>> SourceIdsAsync(Guid geofileId)
    {
        var response = await importer.PostAsJsonAsync(
            $"/api/v1/geofiles/{geofileId}/import/preview", new { options = VectorOptions(), pageSize = 200 });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return [.. JsonDocument.Parse(raw).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("sourceId").GetInt64()).Order()];
    }

    private async Task<Guid> UploadGpxAsync(params (string Name, double Lat, double Lon)[] points)
    {
        var waypoints = string.Concat(points.Select((p, i) => string.Create(
            CultureInfo.InvariantCulture, $"""<wpt lat="{p.Lat}" lon="{p.Lon}"><name>{p.Name} wpt {i} {marker}</name></wpt>""")));
        var gpx = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">{waypoints}</gpx>
            """;
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(gpx));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", $"held-{marker}.gpx" } };
        var response = await importer.PostAsync("/api/v1/geofiles", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await importer.GetFromJsonAsync<JsonElement>($"/api/v1/geofiles/{id}/status");
            var value = status.GetProperty("importStatus").GetString();
            if (value is "imported" or "failed")
            {
                value.ShouldBe("imported", status.ToString());
                return id;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the file was not read in time");
            await Task.Delay(200);
        }
    }

    // photographs

    private static Dictionary<string, object?> PhotoOptions() => new()
    {
        ["defaultKind"] = "caveEntrance",
        ["defaultCaveTypeCode"] = "cave",
        ["defaultEntranceTypeCode"] = "natural",
        ["clusterRadiusMeters"] = 25,
        ["proximityRadiusMeters"] = 1,
        ["visibility"] = "private",
        ["elevation"] = "keep",
        ["locationProtected"] = false,
        ["tagIds"] = Array.Empty<long>(),
        ["cameraClockOffsetSeconds"] = 0,
        ["trackMatchToleranceSeconds"] = 120,
    };

    private object PhotoBody(IReadOnlyList<Guid> files, IReadOnlyList<Guid> keys, string prefix) => new
    {
        options = PhotoOptions(),
        fileIds = files,
        selection = keys,
        decisions = keys.ToDictionary(
            k => k.ToString("D"),
            k => (object)new { action = "create", kind = "cave", name = $"{prefix} {marker} {k:D}" }),
    };

    private async Task<Guid> PhotoKeyAsync(Guid file)
    {
        var response = await importer.PostAsJsonAsync("/api/v1/photo-import/preview", new
        {
            options = PhotoOptions(),
            fileIds = new[] { file },
            pageSize = 100,
        });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        return JsonDocument.Parse(raw).RootElement.GetProperty("items")[0].GetProperty("key").GetGuid();
    }

    private async Task<Guid> UploadPhotoAsync(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/jpeg");
        using var form = new MultipartFormDataContent { { content, "file", $"held-{marker}.jpg" } };
        var response = await importer.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A generated JPEG carrying an invented GPS fix and a capture time.</summary>
    private static byte[] At(double lat, double lon)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, 64, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(lat));
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(lon));
        exif.SetValue(ExifTag.Make, "SilexTest");
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:06:01 12:00:00");
        exif.SetValue(ExifTag.OffsetTimeOriginal, "+00:00");
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        var seconds = (minutesFull - m) * 60d;
        return [new Rational(d), new Rational(m), new Rational(seconds)];
    }

    // catalogue

    private static object CatalogueBody(int[] selection) => new
    {
        selection,
        decisions = (object?)null,
        visibility = "private",
        cavingGroupId = (Guid?)null,
        locationProtected = false,
        parentId = (Guid?)null,
    };

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        importer?.Dispose();
        reviewer?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
        catalogue.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    /// <summary>
    /// Stands in for the national catalogue: answers a detail query out of invented records, one
    /// aliased field per identifier asked about, and counts the calls.
    /// </summary>
    private sealed class CatalogueStub : HttpMessageHandler
    {
        private readonly Dictionary<int, string> titles = [];
        private int calls;
        private bool failing;

        public int CallCount => Volatile.Read(ref calls);

        public void Holds(params (int Id, string Title)[] records)
        {
            foreach (var (id, title) in records)
            {
                titles[id] = title;
            }
        }

        public void Reset() => Interlocked.Exchange(ref calls, 0);

        public void Fails() => failing = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            if (failing)
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
            var data = new Dictionary<string, object?>();
            foreach (var variable in body.GetProperty("variables").EnumerateObject())
            {
                if (variable.Name.StartsWith("id", StringComparison.Ordinal))
                {
                    var id = variable.Value.GetInt32();
                    data["c" + variable.Name[2..]] = titles.TryGetValue(id, out var title) ? Record(id, title) : null;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data }, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
            };
        }

        private static Dictionary<string, object?> Record(int id, string title) => new()
        {
            ["id"] = id,
            ["title"] = title,
            ["slug"] = null,
            ["descriere"] = null,
            ["judet"] = null,
            ["localitate"] = null,
            ["munte"] = null,
            ["lungime"] = null,
            ["denivelare"] = null,
            ["denNegativa"] = null,
            ["altitudine"] = null,
            ["nrHidro"] = null,
            ["bazinHidroId"] = null,
            ["roca"] = null,
            ["scufundabila"] = null,
            ["clasificare"] = null,
            ["stiinta"] = null,
            ["disparuta"] = null,
            ["codAp"] = null,
        };
    }
}
