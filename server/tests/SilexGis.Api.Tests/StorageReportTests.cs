// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Storage;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;
using SilexGis.Infrastructure.Storage;

namespace SilexGis.Api.Tests;

/// <summary>
/// The storage report and the deletions it offers: what each group holds and what deleting it
/// gives back, that an uploaded original is never deleted from here whatever is asked, that a
/// deletion is confirmed and recorded, and that only an administrator reads or acts.
/// </summary>
/// <remarks>
/// Every file is invented bytes written through the file store; scans, surveys and transfers are
/// rows seeded directly, as the runs that make them would leave them, so each group can be put in
/// a known state without a worker. The class owns its database and its worker hand-off directory,
/// so group counts are this class's own.
/// </remarks>
public sealed class StorageReportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private Guid adminId;

    public StorageReportTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-store-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(TestScratch.Root, $"silexgis-test-scans-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:HandoffRoot"] = handoffRoot,
            ["Storage:MinFreeBytes"] = "0",
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"report-admin-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"report-editor-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"report-admin-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"report-editor-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var root in new[] { filesRoot, handoffRoot })
        {
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task Only_an_administrator_reads_the_report_or_deletes_from_it()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/storage/report")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/admin/storage/reclaim/{StorageReclaimGroups.ExpiredUploads}", new { confirm = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // An editor writes caves and documents but holds nothing over the installation's settings.
        await ShouldBeForbiddenAsync(await editor.GetAsync("/api/v1/admin/storage/report"));
        await ShouldBeForbiddenAsync(await editor.PostAsync("/api/v1/admin/storage/report", null));

        // An expired transfer the editor's request must leave exactly where it is.
        var (sessionId, path) = await SeedExpiredTransferAsync(64);
        await ShouldBeForbiddenAsync(await editor.PostAsJsonAsync(
            $"/api/v1/admin/storage/reclaim/{StorageReclaimGroups.ExpiredUploads}", new { confirm = true }));
        (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        File.Exists(Path.Combine(filesRoot, path)).ShouldBeTrue();

        // Nor does a grant over the settings domain alone open it: the report names caves, files
        // and owners across the installation without a visibility filter, so it is for full
        // administrators, who read everything anyway.
        var operatorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"report-operator-{suffix}@t.local");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.AccessEntries.Add(new AccessEntry
            {
                SubjectKind = AccessSubjectKind.User,
                SubjectId = operatorId,
                Effect = AccessEffect.Allow,
                Domain = AccessDomain.Settings,
                Actions = AccessAction.Read | AccessAction.Execute,
                ScopeKind = AccessScopeKind.All,
            });
            await db.SaveChangesAsync();
        }

        var settingsOperator = await AuthHelper.BearerClientAsync(factory, $"report-operator-{suffix}@t.local");
        await ShouldBeForbiddenAsync(await settingsOperator.GetAsync("/api/v1/admin/storage/report"));
        await ShouldBeForbiddenAsync(await settingsOperator.PostAsync("/api/v1/admin/storage/report", null));
        await ShouldBeForbiddenAsync(await settingsOperator.PostAsJsonAsync(
            $"/api/v1/admin/storage/reclaim/{StorageReclaimGroups.ExpiredUploads}", new { confirm = true }));
        (await SessionExistsAsync(sessionId)).ShouldBeTrue();

        // The administrator reads and acts: the positive case of the same request.
        var measured = await admin.PostAsync("/api/v1/admin/storage/report", null);
        measured.StatusCode.ShouldBe(HttpStatusCode.OK, await measured.Content.ReadAsStringAsync());
        var latest = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/storage/report");
        latest.GetProperty("takenAt").GetDateTimeOffset()
            .ShouldBe((await measured.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("takenAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Superseded_scan_products_are_deleted_and_every_original_stays()
    {
        // Two versions of one scan: the first, no longer current, and a newer one tiled since.
        var caveId = await CreateCaveAsync();
        var surveyId = await SeedSurveyAsync(caveId, SurveyOrigin.PointCloud);
        var older = await SeedScanAsync(caveId, current: false, displayBytes: 3000, tileBytes: 5000, surveyId: surveyId);
        var newer = await SeedScanAsync(caveId, current: true, displayBytes: 700, tileBytes: 900, surveyId: surveyId);

        // A cave whose only scan is not marked current is still shown by it: nothing of it is offered.
        var loneCave = await CreateCaveAsync();
        var lone = await SeedScanAsync(loneCave, current: false, displayBytes: 400, tileBytes: 600);

        var offered = await GroupAsync(StorageReclaimGroups.SupersededScanProducts);
        offered.Items.ShouldBeGreaterThanOrEqualTo(2);
        offered.Bytes.ShouldBeGreaterThanOrEqualTo(8000);

        var result = await ReclaimAsync(StorageReclaimGroups.SupersededScanProducts);
        result.GetProperty("items").GetInt32().ShouldBeGreaterThanOrEqualTo(2);
        result.GetProperty("freedBytes").GetInt64().ShouldBeGreaterThanOrEqualTo(8000);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The superseded scan's products are gone, rows and bytes; it keeps its original and has
        // nothing to draw until it is run again.
        (await db.StoredFiles.AnyAsync(f => f.Id == older.DisplayFileId || f.Id == older.TilesFileId)).ShouldBeFalse();
        File.Exists(Path.Combine(filesRoot, older.TilesPath)).ShouldBeFalse();
        (await db.ScanArtifacts.CountAsync(a => a.SurveyUploadId == older.UploadId)).ShouldBe(1);
        (await db.SurveyModels.SingleAsync(m => m.Id == older.ModelId)).ConvertedFileId.ShouldBeNull();

        // Every original stays, with its bytes: the superseded scan's as much as the others'.
        foreach (var scan in new[] { older, newer, lone })
        {
            (await db.StoredFiles.AnyAsync(f => f.Id == scan.OriginalFileId)).ShouldBeTrue();
            File.Exists(Path.Combine(filesRoot, scan.OriginalPath)).ShouldBeTrue();
            (await db.ScanArtifacts.AnyAsync(a => a.FileId == scan.OriginalFileId && a.StepId == ScanSteps.UploadStepId)).ShouldBeTrue();
        }

        // The current scan and the lone one keep their products.
        foreach (var scan in new[] { newer, lone })
        {
            (await db.StoredFiles.CountAsync(f => f.Id == scan.DisplayFileId || f.Id == scan.TilesFileId)).ShouldBe(2);
            (await db.SurveyModels.SingleAsync(m => m.Id == scan.ModelId)).ConvertedFileId.ShouldBe(scan.TilesFileId);
        }

        // Recorded: who gave the room back, and how much.
        var audit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.StorageReclaimed && a.EntityId == StorageReclaimGroups.SupersededScanProducts)
            .OrderByDescending(a => a.Id)
            .FirstAsync();
        audit.UserId.ShouldBe(adminId);
        audit.Changes.ShouldNotBeNull().ShouldContain("freedBytes");

        // Nothing is left to offer.
        (await GroupAsync(StorageReclaimGroups.SupersededScanProducts)).Items.ShouldBe(0);
    }

    [Fact]
    public async Task A_scan_newer_than_the_current_one_or_running_again_is_not_superseded()
    {
        // The current mark stays on the first cloud a cave received, so the version without it is
        // here the newer one: its products are what it shows, and are not offered.
        var caveId = await CreateCaveAsync();
        var surveyId = await SeedSurveyAsync(caveId, SurveyOrigin.PointCloud);
        var first = await SeedScanAsync(caveId, current: true, displayBytes: 300, tileBytes: 400, surveyId: surveyId);
        var second = await SeedScanAsync(caveId, current: false, displayBytes: 500, tileBytes: 600, surveyId: surveyId);

        // An older version superseded by a newer one, but queued to run again: its products are
        // about to be taken up by that run.
        var rerunCave = await CreateCaveAsync();
        var rerunSurvey = await SeedSurveyAsync(rerunCave, SurveyOrigin.PointCloud);
        var rerun = await SeedScanAsync(rerunCave, current: false, displayBytes: 100, tileBytes: 200, surveyId: rerunSurvey);
        var rerunNewer = await SeedScanAsync(rerunCave, current: true, displayBytes: 100, tileBytes: 200, surveyId: rerunSurvey);

        // A scan of another passage, uploaded later to the same cave as a survey of its own, does
        // not supersede the first one either.
        var otherCave = await CreateCaveAsync();
        var passage = await SeedScanAsync(otherCave, current: false, displayBytes: 100, tileBytes: 200);
        var laterPassage = await SeedScanAsync(otherCave, current: true, displayBytes: 100, tileBytes: 200);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == rerun.UploadId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SurveyUploadStatus.Queued));
        }

        await ReclaimAsync(StorageReclaimGroups.SupersededScanProducts);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            foreach (var scan in new[] { first, second, rerun, rerunNewer, passage, laterPassage })
            {
                (await db.StoredFiles.CountAsync(f => f.Id == scan.DisplayFileId || f.Id == scan.TilesFileId)).ShouldBe(2);
                File.Exists(Path.Combine(filesRoot, scan.TilesPath)).ShouldBeTrue();
                (await db.SurveyModels.SingleAsync(m => m.Id == scan.ModelId)).ConvertedFileId.ShouldBe(scan.TilesFileId);
            }
        }

        // Once that run is over, the older version is offered like any other.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyUploads.Where(u => u.Id == rerun.UploadId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SurveyUploadStatus.Succeeded));
        }

        var offered = await GroupAsync(StorageReclaimGroups.SupersededScanProducts);
        offered.Items.ShouldBe(2);
        offered.Bytes.ShouldBe(300);
    }

    [Fact]
    public async Task Originals_have_no_group_and_a_deletion_needs_its_confirmation()
    {
        var caveId = await CreateCaveAsync();
        var scan = await SeedScanAsync(caveId, current: false, displayBytes: 100, tileBytes: 100);

        foreach (var group in new[] { "originals", "raw-recordings", "everything" })
        {
            var refused = await admin.PostAsJsonAsync($"/api/v1/admin/storage/reclaim/{group}", new { confirm = true });
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(refused)).ShouldBe("storage.group_unknown");
        }

        // A known group without the confirmation deletes nothing either.
        var unconfirmed = await admin.PostAsJsonAsync(
            $"/api/v1/admin/storage/reclaim/{StorageReclaimGroups.SupersededScanProducts}", new { confirm = false });
        unconfirmed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unconfirmed)).ShouldBe("storage.confirm_required");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.StoredFiles.CountAsync(f => f.Id == scan.OriginalFileId || f.Id == scan.DisplayFileId || f.Id == scan.TilesFileId))
            .ShouldBe(3);
        File.Exists(Path.Combine(filesRoot, scan.OriginalPath)).ShouldBeTrue();

        // The report counts originals apart from what processing made of them.
        var report = await MeasureAsync();
        report.GetProperty("scanByKind").EnumerateArray()
            .Single(k => k.GetProperty("kind").GetString() == "original")
            .GetProperty("bytes").GetInt64().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Work_directories_of_runs_that_are_over_are_deleted_and_a_running_ones_stays()
    {
        var caveId = await CreateCaveAsync();
        var failed = await SeedUploadAsync(caveId, SurveyOrigin.PointCloud, SurveyUploadStatus.Failed, DateTimeOffset.UtcNow);
        var running = await SeedUploadAsync(caveId, SurveyOrigin.PointCloud, SurveyUploadStatus.Running, DateTimeOffset.UtcNow.AddMinutes(1));

        var failedDir = WorkDirectory($"{failed:N}-py3dtiles-tile", 1000);
        var goneDir = WorkDirectory($"{Guid.NewGuid():N}-pointcloud-downsample", 500);
        var runningDir = WorkDirectory($"{running:N}-py3dtiles-tile", 2000);
        var foreignDir = WorkDirectory("not-an-upload", 300);

        var offered = await GroupAsync(StorageReclaimGroups.FailedRunWork);
        offered.Items.ShouldBe(2);
        offered.Bytes.ShouldBe(1500);
        (await MeasureAsync()).GetProperty("workDirectoryBytes").GetInt64().ShouldBe(3800);

        var result = await ReclaimAsync(StorageReclaimGroups.FailedRunWork);
        result.GetProperty("items").GetInt32().ShouldBe(2);
        result.GetProperty("freedBytes").GetInt64().ShouldBe(1500);

        Directory.Exists(failedDir).ShouldBeFalse();
        Directory.Exists(goneDir).ShouldBeFalse();
        Directory.Exists(runningDir).ShouldBeTrue();
        Directory.Exists(foreignDir).ShouldBeTrue();
    }

    [Fact]
    public async Task An_older_survey_versions_outputs_are_deleted_and_what_it_was_compiled_from_stays()
    {
        var caveId = await CreateCaveAsync();
        var surveyId = await SeedSurveyAsync(caveId);
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var older = await SeedSurveyVersionAsync(caveId, surveyId, at, outputBytes: 4000);
        var newer = await SeedSurveyVersionAsync(caveId, surveyId, at.AddDays(1), outputBytes: 1000);

        var offered = await GroupAsync(StorageReclaimGroups.OlderSurveyVersions);
        offered.Items.ShouldBeGreaterThanOrEqualTo(1);
        offered.Bytes.ShouldBeGreaterThanOrEqualTo(4000);

        var result = await ReclaimAsync(StorageReclaimGroups.OlderSurveyVersions);
        result.GetProperty("items").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        result.GetProperty("freedBytes").GetInt64().ShouldBeGreaterThanOrEqualTo(4000);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.StoredFiles.AnyAsync(f => f.Id == older.OutputFileId)).ShouldBeFalse();
        (await db.StoredFiles.AnyAsync(f => f.Id == older.InputFileId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == newer.OutputFileId)).ShouldBeTrue();
        (await db.StoredFiles.AnyAsync(f => f.Id == newer.InputFileId)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_older_versions_output_the_prune_keeps_is_not_offered()
    {
        var before = await GroupAsync(StorageReclaimGroups.OlderSurveyVersions);

        // The older version's only output is a model somebody made the cave's current one: the
        // prune keeps it, so there is nothing of that version to offer.
        var caveId = await CreateCaveAsync();
        var surveyId = await SeedSurveyAsync(caveId);
        var at = DateTimeOffset.UtcNow.AddDays(-2);
        var older = await SeedSurveyVersionAsync(caveId, surveyId, at, outputBytes: 4000, currentModel: true);
        await SeedSurveyVersionAsync(caveId, surveyId, at.AddDays(1), outputBytes: 1000);

        (await GroupAsync(StorageReclaimGroups.OlderSurveyVersions)).ShouldBe(before);

        await ReclaimAsync(StorageReclaimGroups.OlderSurveyVersions);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.StoredFiles.AnyAsync(f => f.Id == older.OutputFileId)).ShouldBeTrue();
        (await GroupAsync(StorageReclaimGroups.OlderSurveyVersions)).Items.ShouldBe(0);
    }

    [Fact]
    public async Task Expired_transfers_are_deleted_with_their_partial_bytes()
    {
        var (sessionId, path) = await SeedExpiredTransferAsync(250);

        var offered = await GroupAsync(StorageReclaimGroups.ExpiredUploads);
        offered.Items.ShouldBeGreaterThanOrEqualTo(1);
        offered.Bytes.ShouldBeGreaterThanOrEqualTo(250);

        var result = await ReclaimAsync(StorageReclaimGroups.ExpiredUploads);
        result.GetProperty("freedBytes").GetInt64().ShouldBeGreaterThanOrEqualTo(250);
        (await SessionExistsAsync(sessionId)).ShouldBeFalse();
        File.Exists(Path.Combine(filesRoot, path)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_report_names_the_largest_files_their_cave_and_owner_and_the_growth_since_a_week_ago()
    {
        var caveId = await CreateCaveAsync();
        var big = await SeedScanAsync(caveId, current: true, displayBytes: 10, tileBytes: 10, originalBytes: 3 * 1024 * 1024);

        // A report of eight days ago, when the store held nothing.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.StorageReportSnapshots.Add(new StorageReportSnapshot
            {
                TakenAt = DateTimeOffset.UtcNow.AddDays(-8),
                StoreUsedBytes = 0,
                ScanUsedBytes = 0,
                Report = "{}",
            });
            await db.SaveChangesAsync();
        }

        var report = await MeasureAsync();
        var largest = report.GetProperty("largest").EnumerateArray().First();
        largest.GetProperty("fileId").GetGuid().ShouldBe(big.OriginalFileId);
        largest.GetProperty("caveId").GetGuid().ShouldBe(caveId);
        largest.GetProperty("ownerUserId").GetGuid().ShouldBe(adminId);
        largest.GetProperty("kind").GetString().ShouldBe("survey");

        report.GetProperty("byCave").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == caveId)
            .GetProperty("bytes").GetInt64().ShouldBeGreaterThanOrEqualTo(3 * 1024 * 1024);
        report.GetProperty("byAccount").EnumerateArray()
            .Single(a => a.GetProperty("id").GetGuid() == adminId)
            .GetProperty("bytes").GetInt64().ShouldBeGreaterThanOrEqualTo(3 * 1024 * 1024);

        var growth = report.GetProperty("growth");
        growth.GetProperty("sevenDayBytes").GetInt64().ShouldBe(report.GetProperty("storeUsedBytes").GetInt64());
        growth.GetProperty("bytesPerDay").GetInt64().ShouldBeGreaterThan(0);

        // The point clouds' own pace is measured beside the store's; with no cap of their own
        // there is no day they fill.
        growth.GetProperty("scanBytesPerDay").GetInt64().ShouldBeGreaterThan(0);
        growth.GetProperty("scansFullOn").ValueKind.ShouldBe(JsonValueKind.Null);

        // Kept: the latest report is the one just measured.
        var latest = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/storage/report");
        latest.GetProperty("takenAt").GetDateTimeOffset().ShouldBe(report.GetProperty("takenAt").GetDateTimeOffset());
        latest.GetProperty("reclaimable").GetArrayLength().ShouldBe(StorageReclaimGroups.All.Count);
    }

    [Fact]
    public async Task A_report_is_queued_when_the_newest_is_older_than_the_interval_not_when_the_process_has_run_that_long()
    {
        var scheduler = factory.Services.GetServices<IHostedService>().OfType<StorageReportScheduler>().Single();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.StorageReport).ExecuteDeleteAsync();
        await db.StorageReportSnapshots.ExecuteDeleteAsync();
        Task<int> QueuedAsync() => db.ProcessingJobs.AsNoTracking()
            .CountAsync(j => j.Kind == ProcessingJobKinds.StorageReport && j.Status == ProcessingJobStatus.Queued);

        // No report at all: one is due at the first look, however recently the server started.
        await scheduler.QueueIfDueAsync(TimeSpan.FromDays(1), TimeSpan.FromHours(1), CancellationToken.None);
        (await QueuedAsync()).ShouldBe(1);

        // Still waiting: a second look does not stack another.
        await scheduler.QueueIfDueAsync(TimeSpan.FromDays(1), TimeSpan.FromHours(1), CancellationToken.None);
        (await QueuedAsync()).ShouldBe(1);

        // Written two hours ago: not due again under a daily interval, due under a shorter one.
        await db.ProcessingJobs.Where(j => j.Kind == ProcessingJobKinds.StorageReport).ExecuteDeleteAsync();
        db.StorageReportSnapshots.Add(new StorageReportSnapshot
        {
            TakenAt = DateTimeOffset.UtcNow.AddHours(-2),
            StoreUsedBytes = 0,
            ScanUsedBytes = 0,
            Report = "{}",
        });
        await db.SaveChangesAsync();
        await scheduler.QueueIfDueAsync(TimeSpan.FromDays(1), TimeSpan.FromHours(1), CancellationToken.None);
        (await QueuedAsync()).ShouldBe(0);
        await scheduler.QueueIfDueAsync(TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(10), CancellationToken.None);
        (await QueuedAsync()).ShouldBe(1);
    }

    // ---- requests ------------------------------------------------------------------------------

    private async Task<JsonElement> MeasureAsync()
    {
        var response = await admin.PostAsync("/api/v1/admin/storage/report", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(int Items, long Bytes)> GroupAsync(string group)
    {
        var entry = (await MeasureAsync()).GetProperty("reclaimable").EnumerateArray()
            .Single(g => g.GetProperty("group").GetString() == group);
        return (entry.GetProperty("items").GetInt32(), entry.GetProperty("bytes").GetInt64());
    }

    private async Task<JsonElement> ReclaimAsync(string group)
    {
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/storage/reclaim/{group}", new { confirm = true });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ShouldBeForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe("access.forbidden");
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    // ---- fixtures ------------------------------------------------------------------------------

    private sealed record SeededScan(
        Guid UploadId, Guid ModelId, Guid OriginalFileId, string OriginalPath, Guid DisplayFileId, Guid TilesFileId, string TilesPath);

    private sealed record SeededVersion(Guid UploadId, Guid InputFileId, Guid OutputFileId);

    private async Task<Guid> CreateCaveAsync()
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            caveTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var response = await admin.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Report Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedSurveyAsync(Guid caveId, SurveyOrigin origin = SurveyOrigin.TopoDroid)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var survey = new CaveSurvey { CaveFeatureId = caveId, Title = "Invented survey", Source = origin };
        db.CaveSurveys.Add(survey);
        await db.SaveChangesAsync();
        return survey.Id;
    }

    private async Task<Guid> SeedUploadAsync(Guid caveId, SurveyOrigin origin, SurveyUploadStatus status, DateTimeOffset versionAt, Guid? surveyId = null)
    {
        var survey = surveyId ?? await SeedSurveyAsync(caveId, origin);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var batch = new UploadBatch { StartedByUserId = adminId, Source = UploadSource.Interactive, Label = "invented upload" };
        db.UploadBatches.Add(batch);
        var upload = new SurveyUpload
        {
            CaveFeatureId = caveId,
            CaveSurveyId = survey,
            UploadBatchId = batch.Id,
            Source = origin,
            SurveyTitle = "Invented survey",
            VersionAt = versionAt,
            RequestedBy = adminId,
            ExpiresAt = versionAt.AddHours(24),
            Status = status,
        };
        db.SurveyUploads.Add(upload);
        await db.SaveChangesAsync();
        return upload.Id;
    }

    /// <summary>
    /// A point cloud as a finished run leaves it: the original archived as a document, a thinned
    /// copy and a tileset as further files of that document, an artefact row for each, and a model
    /// reading the original and drawing the tileset.
    /// </summary>
    private async Task<SeededScan> SeedScanAsync(
        Guid caveId, bool current, int displayBytes, int tileBytes, int originalBytes = 2000, Guid? surveyId = null)
    {
        var survey = surveyId ?? await SeedSurveyAsync(caveId, SurveyOrigin.PointCloud);
        var uploadId = await SeedUploadAsync(caveId, SurveyOrigin.PointCloud, SurveyUploadStatus.Succeeded, DateTimeOffset.UtcNow, survey);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();

        var (originalContent, originalPath) = await WriteAsync(scope, "scan.laz", originalBytes);
        var created = documents.Create(originalContent, "scan", adminId, adminId);
        var (displayContent, _) = await WriteAsync(scope, "scan-display.laz", displayBytes);
        var display = documents.AddFile(created.Version.Id, displayContent);
        var (tilesContent, tilesPath) = await WriteAsync(scope, "scan-tiles.zip", tileBytes);
        var tiles = documents.AddFile(created.Version.Id, tilesContent);
        await db.SaveChangesAsync();

        var model = new SurveyModel
        {
            CaveFeatureId = caveId,
            Name = "scan",
            FileId = created.File.Id,
            Format = SurveyModelFormat.Laz,
            Status = SurveyModelStatus.Ready,
            IsCurrent = current,
            ConvertedFileId = tiles.Id,
            CaveSurveyId = survey,
            SurveyUploadId = uploadId,
        };
        db.SurveyModels.Add(model);
        db.SurveyUploadFiles.Add(new SurveyUploadFile
        {
            SurveyUploadId = uploadId,
            Name = "scan.laz",
            Role = SurveyUploadFileRole.Archive,
            Status = SurveyUploadFileStatus.Stored,
            DeclaredSize = originalBytes,
            DocumentId = created.Version.DocumentId,
        });
        await db.SaveChangesAsync();

        var original = Artefact(uploadId, null, ScanArtifactKind.PointCloudLaz, created.File, ScanSteps.UploadStepId);
        db.ScanArtifacts.AddRange(
            original,
            Artefact(uploadId, model.Id, ScanArtifactKind.PointCloudLazDisplay, display, "pointcloud.downsample", original.Id),
            Artefact(uploadId, model.Id, ScanArtifactKind.PointCloudTiles, tiles, "py3dtiles.tile", original.Id));
        await db.SaveChangesAsync();

        return new SeededScan(uploadId, model.Id, created.File.Id, originalPath, display.Id, tiles.Id, tilesPath);
    }

    private static ScanArtifact Artefact(Guid uploadId, Guid? modelId, ScanArtifactKind kind, StoredFile file, string step, params Guid[] inputs) => new()
    {
        SurveyUploadId = uploadId,
        SurveyModelId = modelId,
        Kind = kind,
        Format = kind == ScanArtifactKind.PointCloudTiles ? "3dtiles-zip" : "laz",
        FileId = file.Id,
        SizeBytes = file.SizeBytes,
        Sha256 = file.Sha256,
        StepId = step,
        Tool = "invented",
        ParamsHash = "0",
        InputArtifactIds = inputs,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>A survey version as a compile leaves it: the device's file it was compiled from and one output.</summary>
    private async Task<SeededVersion> SeedSurveyVersionAsync(
        Guid caveId, Guid surveyId, DateTimeOffset versionAt, int outputBytes, bool currentModel = false)
    {
        var uploadId = await SeedUploadAsync(caveId, SurveyOrigin.TopoDroid, SurveyUploadStatus.Succeeded, versionAt, surveyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var documents = scope.ServiceProvider.GetRequiredService<DocumentWriteService>();

        var (inputContent, _) = await WriteAsync(scope, "survey.th", 300);
        var input = documents.Create(inputContent, "survey", adminId, adminId);
        var (outputContent, _) = await WriteAsync(scope, "survey.pdf", outputBytes);
        var output = documents.Create(outputContent, "survey plan", adminId, adminId);
        await db.SaveChangesAsync();

        // The output as a compiled model the cave is now represented by, when asked for.
        SurveyModel? model = null;
        if (currentModel)
        {
            model = new SurveyModel
            {
                CaveFeatureId = caveId,
                Name = "survey",
                FileId = output.File.Id,
                Format = SurveyModelFormat.Lox,
                Status = SurveyModelStatus.Ready,
                IsCurrent = true,
                CaveSurveyId = surveyId,
                SurveyUploadId = uploadId,
            };
            db.SurveyModels.Add(model);
            await db.SaveChangesAsync();
        }

        db.SurveyUploadFiles.AddRange(
            new SurveyUploadFile
            {
                SurveyUploadId = uploadId, Name = "survey.th", Role = SurveyUploadFileRole.Archive,
                Status = SurveyUploadFileStatus.Stored, DeclaredSize = 300, DocumentId = input.Version.DocumentId,
            },
            new SurveyUploadFile
            {
                SurveyUploadId = uploadId, Name = "survey.pdf", Role = SurveyUploadFileRole.Output,
                Status = SurveyUploadFileStatus.Stored, DeclaredSize = outputBytes, DocumentId = output.Version.DocumentId,
                SurveyModelId = model?.Id,
            });
        await db.SaveChangesAsync();
        return new SeededVersion(uploadId, input.File.Id, output.File.Id);
    }

    private async Task<(Guid Id, string Path)> SeedExpiredTransferAsync(int bytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var (content, path) = await WriteAsync(scope, "partial.jpg", bytes);
        var session = new UploadSession
        {
            UserId = adminId,
            OriginalName = "partial.jpg",
            DeclaredSizeBytes = bytes * 4,
            ReceivedBytes = bytes,
            StoragePath = content.StoragePath,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        db.UploadSessions.Add(session);
        await db.SaveChangesAsync();
        return (session.Id, path);
    }

    private async Task<bool> SessionExistsAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().UploadSessions.AnyAsync(s => s.Id == id);
    }

    /// <summary>Invented bytes written through the file store.</summary>
    private static async Task<(StoredContent Content, string Path)> WriteAsync(AsyncServiceScope scope, string name, int size)
    {
        var bytes = new byte[size];
        RandomNumberGenerator.Fill(bytes);
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        string path;
        await using (var stream = new MemoryStream(bytes))
        {
            path = await store.SaveAsync(stream, Path.GetExtension(name));
        }

        return (new StoredContent(path, name, "application/octet-stream", size, Convert.ToHexStringLower(SHA256.HashData(bytes)), FileKind.Survey), path);
    }

    private string WorkDirectory(string name, int bytes)
    {
        var directory = Path.Combine(handoffRoot, ScanWorkerHandoff.JobsDirectory, name);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "input.laz"), new byte[bytes]);
        return directory;
    }
}
