// SPDX-License-Identifier: AGPL-3.0-or-later
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
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What goes with a camp when it is deleted, and what stays — the rule a trip already follows,
/// asked of a camp. The write-up the application generated is a derivative of the camp and goes
/// whole: document, version, stored file and bytes. A photograph pinned to the camp, and a report
/// a club wrote and uploaded by hand into the same slot, are library material: their attachment
/// rows go and the documents stay, as does a generated write-up that something other than the
/// camp has since taken hold of.
/// </summary>
/// <remarks>
/// A camp has one thing a trip has not: it gathers trips, each of which may carry a generated
/// write-up of its own. Those are the trips', are named after the trips, and stay with them — the
/// last case here pins that, because "the trips it gathered stay" is the first thing a camp's
/// delete promises and a delete that reached one of their documents would break it silently.
/// </remarks>
public sealed class ExpeditionDeleteCleanupTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private long caveTypeId;

    public ExpeditionDeleteCleanupTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xdc-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xdc-own-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Deleting_a_camp_takes_its_generated_write_up_whole_and_leaves_its_photograph_and_a_club_report()
    {
        var campId = await CreateCampAsync();
        var (reportDocumentId, reportFileId) = await KeepReportAsync($"/api/v1/expeditions/{campId}/report");

        var photoFileId = await UploadPictureAsync("base-camp.jpg");
        await AttachAsync(photoFileId, "expedition", campId, "photoSurface");
        var clubReportFileId = await UploadTextAsync("club-report.txt", $"What the fortnight found {suffix}.");
        await AttachAsync(clubReportFileId, "expedition", campId, "report");

        var before = await SnapshotAsync([reportFileId, photoFileId, clubReportFileId]);
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeTrue();

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{campId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The generated write-up is gone whole: not marked, not restorable, not on disk.
        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == reportDocumentId)).ShouldBeFalse();
        (await db.DocumentVersions.AnyAsync(v => v.DocumentId == reportDocumentId)).ShouldBeFalse();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeFalse();
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeFalse();

        // The photograph and the club's own report stay as documents with their bytes; only the
        // pin that held each to the camp went.
        foreach (var fileId in new[] { photoFileId, clubReportFileId })
        {
            (await db.StoredFiles.AnyAsync(f => f.Id == fileId)).ShouldBeTrue();
            (await db.Documents.AnyAsync(d => d.Id == before[fileId].DocumentId)).ShouldBeTrue();
            File.Exists(before[fileId].AbsolutePath).ShouldBeTrue();
        }

        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.Expedition && a.EntityId == campId))
            .ShouldBeFalse();
    }

    /// <summary>
    /// A generated write-up somebody has since filed on a shelf has become library material by
    /// that act — the shelf is where a club keeps what it means to keep — so it stays where it
    /// was filed, and only its pin on the camp goes.
    /// </summary>
    [Fact]
    public async Task A_generated_write_up_filed_in_a_cabinet_stays_when_the_camp_goes()
    {
        var campId = await CreateCampAsync();
        var (reportDocumentId, reportFileId) = await KeepReportAsync($"/api/v1/expeditions/{campId}/report");
        var cabinetId = await CreateCabinetAsync();
        await FileAsync(cabinetId, reportDocumentId);
        var before = await SnapshotAsync([reportFileId]);

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{campId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Documents.AnyAsync(d => d.Id == reportDocumentId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeTrue();
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeTrue();
        (await db.CabinetDocuments.AnyAsync(c => c.CabinetId == cabinetId && c.DocumentId == reportDocumentId))
            .ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.Expedition && a.EntityId == campId))
            .ShouldBeFalse();
    }

    /// <summary>
    /// The same for one somebody has attached elsewhere: deleting it under them would take a
    /// document off a cave because a camp went.
    /// </summary>
    [Fact]
    public async Task A_generated_write_up_something_else_holds_stays_when_the_camp_goes()
    {
        var campId = await CreateCampAsync();
        var (reportDocumentId, reportFileId) = await KeepReportAsync($"/api/v1/expeditions/{campId}/report");
        var caveId = await CreateCaveAsync();
        await AttachAsync(reportFileId, "feature", caveId, "document");
        var before = await SnapshotAsync([reportFileId]);

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{campId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Documents.AnyAsync(d => d.Id == reportDocumentId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeTrue();
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.FileId == reportFileId && a.FeatureId == caveId)).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.Expedition && a.EntityId == campId))
            .ShouldBeFalse();
    }

    /// <summary>
    /// A trip the camp gathered keeps the write-up generated for it. The camp's delete recognises
    /// its own write-ups by the camp's name on them and in the camp's own report slot, so one
    /// named after a trip and pinned to that trip is not its to take — and both write-ups are on
    /// file here when the camp goes, so the one that survives is told apart from the one that
    /// does not rather than merely found.
    /// </summary>
    [Fact]
    public async Task The_trips_a_camp_gathered_keep_their_own_write_ups()
    {
        var campId = await CreateCampAsync();
        var tripId = await CreateTripAsync();
        using (var joined = await owner.PostAsJsonAsync(
            $"/api/v1/expeditions/{campId}/trips", new { tripLogId = tripId }))
        {
            joined.IsSuccessStatusCode.ShouldBeTrue(await joined.Content.ReadAsStringAsync());
        }

        var (tripReportDocumentId, tripReportFileId) = await KeepReportAsync($"/api/v1/trip-logs/{tripId}/report");
        var (campReportDocumentId, campReportFileId) = await KeepReportAsync($"/api/v1/expeditions/{campId}/report");
        var before = await SnapshotAsync([tripReportFileId, campReportFileId]);

        var deleted = await owner.DeleteAsync($"/api/v1/expeditions/{campId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == campReportDocumentId)).ShouldBeFalse();
        File.Exists(before[campReportFileId].AbsolutePath).ShouldBeFalse();

        (await db.TripLogs.AnyAsync(t => t.Id == tripId)).ShouldBeTrue();
        (await db.Documents.AnyAsync(d => d.Id == tripReportDocumentId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == tripReportFileId)).ShouldBeTrue();
        File.Exists(before[tripReportFileId].AbsolutePath).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.TripLog
            && a.EntityId == tripId && a.FileId == tripReportFileId)).ShouldBeTrue();
    }

    /// <summary>
    /// The mark is all the cleanup has to recognise a generated write-up by, and an empty one
    /// would recognise every report in the slot — a club's own included — as something to delete.
    /// So it is refused outright, for any kind of record, before anything is looked for.
    /// </summary>
    [Fact]
    public async Task The_cleanup_refuses_a_mark_that_would_recognise_every_report()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        foreach (var mark in new[] { string.Empty, "  " })
        {
            await Should.ThrowAsync<ArgumentException>(() => GeneratedWriteUpCleanup.FiledOnAsync(
                db, AttachedEntityType.Expedition, Guid.NewGuid(), mark, CancellationToken.None));
        }
    }

    // ---- helpers

    private async Task<Guid> CreateCampAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Cleanup camp {suffix} {Guid.NewGuid():N}"[..40],
            description = "A fortnight on the plateau.",
            startDate = "2026-07-01",
            endDate = "2026-07-14",
            visibility = "authenticated",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Camp trip {suffix} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-07-03",
            caveIds = Array.Empty<Guid>(),
            participants = Array.Empty<object>(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Cleanup cave {suffix}",
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCabinetAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/cabinets", new
        {
            name = $"Shelf {Guid.NewGuid():N}"[..24],
            description = (string?)null,
            parentId = (Guid?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task FileAsync(Guid cabinetId, Guid documentId)
    {
        var filed = await owner.PutAsync($"/api/v1/cabinets/{cabinetId}/documents/{documentId}", null);
        filed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await filed.Content.ReadAsStringAsync());
    }

    /// <summary>Has a write-up generated and filed against its subject, through that subject's own route.</summary>
    private async Task<(Guid DocumentId, Guid FileId)> KeepReportAsync(string route)
    {
        var kept = await owner.PostAsync(route, null);
        kept.StatusCode.ShouldBe(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        var report = await kept.Content.ReadFromJsonAsync<JsonElement>();
        return (report.GetProperty("documentId").GetGuid(), report.GetProperty("fileId").GetGuid());
    }

    private async Task<Guid> UploadPictureAsync(string name)
    {
        using var picture = new MagickImage(MagickColors.SlateGray, 320, 240);
        var content = new ByteArrayContent(picture.ToByteArray(MagickFormat.Jpeg));
        content.Headers.ContentType = new("image/jpeg");
        return await UploadAsync(content, name);
    }

    private async Task<Guid> UploadTextAsync(string name, string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new("text/plain");
        return await UploadAsync(content, name);
    }

    private async Task<Guid> UploadAsync(HttpContent content, string name)
    {
        using var form = new MultipartFormDataContent { { content, "file", name } };
        var uploaded = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await uploaded.Content.ReadAsStringAsync();
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task AttachAsync(Guid fileId, string entityType, Guid entityId, string role)
    {
        var attached = await owner.PostAsJsonAsync("/api/v1/attachments/", new
        {
            fileId,
            entityType,
            entityId,
            role,
            sortOrder = 0,
        });
        attached.StatusCode.ShouldBe(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());
    }

    /// <summary>Where each file's bytes live and which document holds it, read before the delete.</summary>
    private async Task<Dictionary<Guid, (Guid DocumentId, string AbsolutePath)>> SnapshotAsync(Guid[] fileIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var rows = await db.StoredFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .Join(db.DocumentVersions.AsNoTracking(), f => f.DocumentVersionId, v => v.Id,
                (f, v) => new { f.Id, v.DocumentId, f.StoragePath })
            .ToListAsync();
        rows.Count.ShouldBe(fileIds.Length);
        return rows.ToDictionary(r => r.Id, r => (r.DocumentId, store.GetAbsolutePath(r.StoragePath)));
    }
}
