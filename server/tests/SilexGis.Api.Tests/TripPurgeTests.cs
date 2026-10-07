// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The pass that removes deleted trips for good: when it takes one, and what goes with it.
/// </summary>
/// <remarks>
/// <para>
/// Deleting a trip removes nothing, so this pass is where the clean-up lives. What goes with a
/// trip is what cannot mean anything without it — its roster and answers, its place in a camp,
/// the rules anchored on it, the pins and tags hung on it, its own links, and the write-up the
/// application generated from it, whole. What stays is what was only ever attached: a photograph,
/// a report a club wrote and uploaded by hand, and a generated write-up that something other than
/// the trip has since taken hold of.
/// </para>
/// <para>
/// The host runs a clock the test holds, so "past its window" is a moment that is set rather than
/// waited for — and the stamp a delete writes is read from that same clock, which is asserted,
/// because a stamp taken from the machine while the pass measures against the test's clock would
/// make every figure here mean nothing. No request is made once the clock has moved: a session is
/// good for an hour, and the window is a month.
/// </para>
/// </remarks>
public sealed class TripPurgeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private readonly SilexGisApiFactory factory;
    // To the whole second, because the instants below are compared exactly against what the
    // database kept, and the column keeps microseconds where the machine's clock counts in
    // tenths of them.
    private readonly TestTimeProvider clock =
        new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private Guid ownerId;
    private Guid readerId;
    private long caveTypeId;

    public TripPurgeTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: services =>
        {
            // The queue's own workers out: a pass is run here by hand, and a worker that claimed
            // a queued one would be a second runner over the same rows.
            JobWorkers.RemoveFrom(services);
            services.AddSingleton<TimeProvider>(clock);
        });

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpg-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpg-rdr-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tpg-own-{suffix}@t.local");

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
    public async Task The_pass_takes_a_trip_at_the_end_of_its_window_with_everything_that_meant_nothing_without_it()
    {
        var firstCave = await CreateCaveAsync("first");
        var secondCave = await CreateCaveAsync("second");
        var camp = await CreateCampAsync();
        var ownerCaver = await RosterHelper.CaverIdForAsync(factory, ownerId);
        var readerCaver = await RosterHelper.CaverIdForAsync(factory, readerId);

        var tripId = await CreateTripAsync([firstCave, secondCave], ownerCaver);
        await JoinAsync(camp, tripId);
        (await owner.PostAsJsonAsync($"/api/v1/trip-logs/{tripId}/invitations/", new { caverId = readerCaver }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await GrantAsync(tripId, readerId, AccessAction.Read);
        (await owner.PostAsJsonAsync(
                "/api/v1/taggings/", new { tagName = $"purge {suffix}", entityType = "tripLog", entityId = tripId }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var (reportDocumentId, reportFileId) = await KeepReportAsync(tripId);
        var photoFileId = await UploadPictureAsync("underground.jpg");
        await AttachAsync(photoFileId, "tripLog", tripId, "photoInterior");
        var clubReportFileId = await UploadTextAsync("club-report.txt", $"What we found {suffix}.");
        await AttachAsync(clubReportFileId, "tripLog", tripId, "report");

        // Three links: the trip's own role link over both caves, one somebody made by hand that
        // would be left one-ended, and one that still relates two caves without the trip.
        var oneEnded = await CreateLinkAsync(("feature", firstCave), ("tripLog", tripId));
        var stillTwo = await CreateLinkAsync(("feature", firstCave), ("feature", secondCave), ("tripLog", tripId));

        var files = await SnapshotAsync([reportFileId, photoFileId, clubReportFileId]);
        File.Exists(files[reportFileId].AbsolutePath).ShouldBeTrue();
        var before = await RowsAsync(tripId);
        before.ShouldBe(new TripRows(Trip: 1, Roster: 1, Answers: 1, Camp: 1, Rules: 1, Pins: 3, Tags: 1, LinkEnds: 3));
        (await LinkCountAsync(firstCave)).ShouldBe(3);

        var deletedAt = clock.Now;
        var deleted = await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        // The stamp is this host's clock, which is what lets the two instants below be exact.
        (await DeletedTrips.RowAsync(factory, tripId))!.DeletedAt.ShouldBe(deletedAt);

        // One second short of the window: nothing has gone, on disk or in any table.
        clock.Now = deletedAt + Window - TimeSpan.FromSeconds(1);
        await DeletedTrips.RunPurgeAsync(factory);
        (await RowsAsync(tripId)).ShouldBe(before);
        File.Exists(files[reportFileId].AbsolutePath).ShouldBeTrue();
        (await DocumentExistsAsync(reportDocumentId)).ShouldBeTrue();

        // The moment itself: the trip goes, with every row that hung on it by its key.
        clock.Now = deletedAt + Window;
        await DeletedTrips.RunPurgeAsync(factory);
        (await RowsAsync(tripId)).ShouldBe(new TripRows(0, 0, 0, 0, 0, 0, 0, 0));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The generated write-up is gone whole: not marked, not restorable, not on disk.
        (await DocumentExistsAsync(reportDocumentId)).ShouldBeFalse();
        (await db.DocumentVersions.AnyAsync(v => v.DocumentId == reportDocumentId)).ShouldBeFalse();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeFalse();
        File.Exists(files[reportFileId].AbsolutePath).ShouldBeFalse();

        // The photograph and the club's own report stay as documents with their bytes; only the
        // pin that held each to the trip went.
        foreach (var fileId in new[] { photoFileId, clubReportFileId })
        {
            (await db.StoredFiles.AnyAsync(f => f.Id == fileId)).ShouldBeTrue();
            (await db.Documents.AnyAsync(d => d.Id == files[fileId].DocumentId)).ShouldBeTrue();
            File.Exists(files[fileId].AbsolutePath).ShouldBeTrue();
        }

        // Its role link went whole and the hand-made link it left one-ended went with it; the one
        // that still relates two caves stays, without the trip.
        (await db.ResLinks.AnyAsync(l => l.Id == oneEnded)).ShouldBeFalse();
        (await db.ResLinkMembers.Where(m => m.ResLinkId == stillTwo).Select(m => m.FeatureId).ToListAsync())
            .ShouldBe([firstCave, secondCave], ignoreOrder: true);
        (await LinkCountAsync(firstCave)).ShouldBe(1);

        // Nothing the trip was merely attached to went with it.
        (await db.Expeditions.AnyAsync(x => x.Id == camp)).ShouldBeTrue();
        (await db.Features.CountAsync(f => f.Id == firstCave || f.Id == secondCave)).ShouldBe(2);

        // And nothing was left pointing at it.
        var orphans = (await scope.ServiceProvider.GetRequiredService<FeatureIntegrityVerifier>().VerifyAsync())
            .Where(p => p.Detail.Contains(tripId.ToString()))
            .ToList();
        orphans.ShouldBeEmpty(string.Join("; ", orphans.Select(p => p.Detail)));
    }

    /// <summary>
    /// A generated write-up somebody has since attached elsewhere has become library material
    /// by that act — removing it under them would take a document off a cave because a trip
    /// went — so it stays, and only its pin on the trip goes.
    /// </summary>
    [Fact]
    public async Task A_generated_write_up_something_else_holds_stays_when_the_trip_is_removed()
    {
        var ownerCaver = await RosterHelper.CaverIdForAsync(factory, ownerId);
        var tripId = await CreateTripAsync([], ownerCaver);
        var (reportDocumentId, reportFileId) = await KeepReportAsync(tripId);
        var caveId = await CreateCaveAsync("holder");
        await AttachAsync(reportFileId, "feature", caveId, "document");
        var files = await SnapshotAsync([reportFileId]);

        var deletedAt = clock.Now;
        (await owner.DeleteAsync($"/api/v1/trip-logs/{tripId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // While the trip is only deleted the write-up is exactly where it was: nothing is taken
        // down until the trip is.
        (await DocumentExistsAsync(reportDocumentId)).ShouldBeTrue();

        clock.Now = deletedAt + Window;
        await DeletedTrips.RunPurgeAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await DeletedTrips.RowAsync(factory, tripId)).ShouldBeNull();
        (await db.Documents.AnyAsync(d => d.Id == reportDocumentId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == reportFileId)).ShouldBeTrue();
        File.Exists(files[reportFileId].AbsolutePath).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.FileId == reportFileId && a.FeatureId == caveId)).ShouldBeTrue();
        (await db.Attachments.AnyAsync(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId))
            .ShouldBeFalse();
    }

    /// <summary>
    /// The pass takes what is due and nothing else: a trip still inside its window, a trip that
    /// was restored, and a trip nobody deleted are all there afterwards.
    /// </summary>
    [Fact]
    public async Task The_pass_leaves_a_trip_inside_its_window_a_restored_one_and_a_live_one()
    {
        var ownerCaver = await RosterHelper.CaverIdForAsync(factory, ownerId);
        var due = await CreateTripAsync([], ownerCaver);
        var restored = await CreateTripAsync([], ownerCaver);
        var live = await CreateTripAsync([], ownerCaver);

        var start = clock.Now;
        (await owner.DeleteAsync($"/api/v1/trip-logs/{due}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.DeleteAsync($"/api/v1/trip-logs/{restored}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.PostAsync($"/api/v1/trip-logs/{restored}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A fourth, deleted on a later day of this clock: still inside its window when the first
        // is past it. Written to the row because a request cannot be made on a moved clock.
        var recent = await CreateTripAsync([], ownerCaver);
        await StampDeletedAsync(recent, start + TimeSpan.FromDays(10));

        clock.Now = start + Window;
        await DeletedTrips.RunPurgeAsync(factory);

        (await DeletedTrips.RowAsync(factory, due)).ShouldBeNull();
        (await DeletedTrips.RowAsync(factory, recent))!.DeletedAt.ShouldNotBeNull();
        (await DeletedTrips.RowAsync(factory, restored))!.DeletedAt.ShouldBeNull();
        (await DeletedTrips.RowAsync(factory, live))!.DeletedAt.ShouldBeNull();
    }

    /// <summary>
    /// A tick queues exactly one pass, and a tick beside one still waiting queues none: the
    /// schedule is about time passing, and a queue that grew a pass per tick while the worker was
    /// busy would run them all back to back the moment it was free.
    /// </summary>
    [Fact]
    public async Task A_tick_queues_one_pass_and_a_tick_beside_a_pending_one_queues_none()
    {
        using var scheduler = new TripPurgeScheduler(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TripRetentionOptions()),
            NullLogger<TripPurgeScheduler>.Instance);

        var before = (await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.TripPurge)).Count;
        await scheduler.QueuePassAsync(CancellationToken.None);
        var afterFirst = await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.TripPurge);
        await scheduler.QueuePassAsync(CancellationToken.None);
        var afterSecond = await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.TripPurge);

        (afterFirst.Count - before).ShouldBe(1);
        afterSecond.Count.ShouldBe(afterFirst.Count);
        afterFirst[0].Status.ShouldBe(ProcessingJobStatus.Queued);
        afterFirst[0].RequestedBy.ShouldBeNull("a scheduled pass notifies nobody about itself");
    }

    // ---- fixtures

    private sealed record TripRows(
        int Trip, int Roster, int Answers, int Camp, int Rules, int Pins, int Tags, int LinkEnds);

    /// <summary>Every row that hangs on a trip by its key, counted past the filter that hides them.</summary>
    private async Task<TripRows> RowsAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return new TripRows(
            await db.TripLogs.IgnoreQueryFilters().CountAsync(t => t.Id == tripId),
            await db.TripLogParticipants.IgnoreQueryFilters().CountAsync(p => p.TripLogId == tripId),
            await db.TripInvitations.IgnoreQueryFilters().CountAsync(i => i.TripLogId == tripId),
            await db.ExpeditionTrips.IgnoreQueryFilters().CountAsync(m => m.TripLogId == tripId),
            await db.AccessEntries.CountAsync(e => e.Domain == AccessDomain.TripLogs && e.ScopeId == tripId),
            await db.Attachments.CountAsync(a => a.EntityType == AttachedEntityType.TripLog && a.EntityId == tripId),
            await db.Taggings.CountAsync(t => t.EntityType == AttachedEntityType.TripLog && t.EntityId == tripId),
            await db.ResLinkMembers.CountAsync(m => m.EntityType == AttachedEntityType.TripLog && m.EntityId == tripId));
    }

    /// <summary>How many links in storage name a cave, shown or not.</summary>
    private async Task<int> LinkCountAsync(Guid caveId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ResLinkMembers.Where(m => m.FeatureId == caveId).Select(m => m.ResLinkId).Distinct().CountAsync();
    }

    private async Task<bool> DocumentExistsAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == documentId);
    }

    private async Task StampDeletedAsync(Guid tripId, DateTimeOffset at)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripLogs
            .Where(t => t.Id == tripId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, at))).ShouldBe(1);
    }

    private async Task<Guid> CreateTripAsync(Guid[] caveIds, Guid onIt)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title = $"Purge trip {suffix} {Guid.NewGuid():N}"[..40],
            tripDate = "2026-07-01",
            caveIds,
            participants = new[] { new { caverId = onIt } },
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Purge cave {name} {suffix} {Guid.NewGuid():N}"[..48],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(created);
    }

    private async Task<Guid> CreateCampAsync()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name = $"Purge camp {suffix} {Guid.NewGuid():N}"[..40],
            description = "A camp.",
            startDate = "2026-06-28",
            endDate = "2026-07-05",
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        return await CreatedIdAsync(created);
    }

    private async Task JoinAsync(Guid camp, Guid trip)
    {
        var joined = await owner.PostAsJsonAsync($"/api/v1/expeditions/{camp}/trips", new { tripLogId = trip });
        joined.StatusCode.ShouldBe(HttpStatusCode.OK, await joined.Content.ReadAsStringAsync());
    }

    private async Task GrantAsync(Guid trip, Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.TripLogs,
            Actions = actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = trip,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateLinkAsync(params (string Type, Guid Id)[] ends)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/reslinks", new
        {
            relationTypeId = (long?)null,
            description = "A link somebody made by hand",
            members = ends.Select((end, index) => new
            {
                targetType = end.Type,
                targetId = end.Id,
                isMain = false,
                sortOrder = index,
                note = (string?)null,
                anchorKind = "whole",
                anchor = (object?)null,
                anchorFileId = (Guid?)null,
            }).ToArray(),
        });
        return await CreatedIdAsync(created);
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
        return await CreatedIdAsync(uploaded);
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

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
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
