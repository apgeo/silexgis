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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What goes with a trip when it is deleted, and what stays. The write-up the application
/// generated is a derivative of the trip and goes whole — document, version, stored file and
/// bytes. A photograph pinned to the trip, and a report a club wrote and uploaded by hand into the
/// same slot, are library material: their attachment rows go and the documents stay, as does a
/// generated write-up that something other than the trip has since taken hold of.
/// </summary>
public sealed class TripDeleteCleanupTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private long caveTypeId;

    public TripDeleteCleanupTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tdc-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tdc-own-{suffix}@t.local");

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
    public async Task Deleting_a_trip_takes_its_generated_write_up_whole_and_leaves_its_photograph_and_a_club_report()
    {
        var tripId = await CreateTripAsync();
        var (reportDocumentId, reportFileId) = await KeepReportAsync(tripId);

        var photoFileId = await UploadPictureAsync("underground.jpg");
        await AttachAsync(photoFileId, "tripLog", tripId, "photoInterior");
        var clubReportFileId = await UploadTextAsync("club-report.txt", $"What we found {suffix}.");
        await AttachAsync(clubReportFileId, "tripLog", tripId, "report");

        var before = await SnapshotAsync([reportFileId, photoFileId, clubReportFileId]);
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeTrue();

        var deleted = await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The generated write-up is gone whole: not marked, not restorable, not on disk.
        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == reportDocumentId)).ShouldBeFalse();
        (await db.DocumentVersions.AnyAsync(v => v.DocumentId == reportDocumentId)).ShouldBeFalse();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeFalse();
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeFalse();

        // The photograph and the club's own report stay as documents with their bytes; only the
        // pin that held each to the trip went.
        foreach (var fileId in new[] { photoFileId, clubReportFileId })
        {
            (await db.StoredFiles.AnyAsync(f => f.Id == fileId)).ShouldBeTrue();
            (await db.Documents.AnyAsync(d => d.Id == before[fileId].DocumentId)).ShouldBeTrue();
            File.Exists(before[fileId].AbsolutePath).ShouldBeTrue();
        }

        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId))
            .ShouldBeFalse();
    }

    /// <summary>
    /// A generated write-up somebody has since attached elsewhere has become library material
    /// by that act — deleting it under them would take a document off a cave because a trip
    /// went — so it stays, and only its pin on the trip goes.
    /// </summary>
    [Fact]
    public async Task A_generated_write_up_something_else_holds_stays_when_the_trip_goes()
    {
        var tripId = await CreateTripAsync();
        var (reportDocumentId, reportFileId) = await KeepReportAsync(tripId);
        var caveId = await CreateCaveAsync();
        await AttachAsync(reportFileId, "feature", caveId, "document");
        var before = await SnapshotAsync([reportFileId]);

        var deleted = await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Documents.AnyAsync(d => d.Id == reportDocumentId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeTrue();
        File.Exists(before[reportFileId].AbsolutePath).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.FileId == reportFileId && a.FeatureId == caveId)).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId))
            .ShouldBeFalse();
    }

    // ---- helpers

    private async Task<Guid> CreateTripAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Cleanup trip {suffix} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-07-01",
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

    private async Task<(Guid DocumentId, Guid FileId)> KeepReportAsync(Guid tripId)
    {
        var kept = await owner.PostAsync($"/api/v1/trip-logs/{tripId}/report", null);
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
