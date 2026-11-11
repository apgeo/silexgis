// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
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
using SilexGis.Domain.Storage;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Storage;

namespace SilexGis.Api.Tests;

/// <summary>
/// Administrators told that storage is filling up: once when a threshold is crossed upwards,
/// not again while it stays there, nothing when it falls back, and once more when it climbs past
/// the same threshold again — for a configured cap measured over real rows, and for a disk's free
/// space. Nobody but an unlocked administrator is told.
/// </summary>
/// <remarks>
/// The point clouds' cap is a few mebibytes, and what fills it is the room a point-cloud upload
/// reserves while it is still arriving — a declared size, which the test sets directly, so a level
/// can be moved up and down without writing a byte. The free-space case passes measured figures
/// straight to the check, since a test cannot fill a real disk. The state the check keeps is one
/// row per ceiling for the whole installation, so every expectation is a count of this test's own
/// administrator's messages, taken before and after.
/// </remarks>
public sealed class StorageAlertTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const long ScanCap = 8L * 1024 * 1024;

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient editor = null!;
    private Guid adminId;
    private Guid lockedAdminId;
    private Guid editorId;
    private Guid groupOnlyAdminId;
    private Guid roleOnlyFormerAdminId;

    public StorageAlertTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-store-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:MaxUploadBytes"] = (4 * ScanCap).ToString(CultureInfo.InvariantCulture),
            ["Scans:MaxTotalBytes"] = ScanCap.ToString(CultureInfo.InvariantCulture),
            // The real disks stay out of the cap case: with no minimum asked for, a disk is only
            // ever reported when it is completely full.
            ["Storage:MinFreeBytes"] = "0",
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        adminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"alert-admin-{suffix}@t.local");
        lockedAdminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"alert-locked-{suffix}@t.local");
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"alert-editor-{suffix}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"alert-editor-{suffix}@t.local");

        // Made a full administrator later, through the permission groups, without the legacy role;
        // and an account that keeps the legacy role after leaving the full administrators.
        groupOnlyAdminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"alert-group-admin-{suffix}@t.local");
        roleOnlyFormerAdminId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"alert-former-admin-{suffix}@t.local");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.Users.Where(u => u.Id == lockedAdminId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LockoutEnd, DateTimeOffset.UtcNow.AddYears(1)));
        var fullAdminsId = await db.PermissionGroups
            .Where(g => g.Slug == SeededPermissionGroups.FullAdministratorsSlug).Select(g => g.Id).SingleAsync();
        db.PermissionGroupMembers.Add(new PermissionGroupMember
        {
            PermissionGroupId = fullAdminsId,
            MemberKind = AccessSubjectKind.User,
            MemberId = groupOnlyAdminId,
        });
        await db.SaveChangesAsync();
        await db.PermissionGroupMembers
            .Where(m => m.PermissionGroupId == fullAdminsId && m.MemberKind == AccessSubjectKind.User && m.MemberId == roleOnlyFormerAdminId)
            .ExecuteDeleteAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            try
            {
                Directory.Delete(filesRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task A_cap_crossed_is_told_to_administrators_once_and_again_only_after_falling_back_and_climbing_again()
    {
        // Whatever an earlier test of the class left reserved is settled first, so the scans start
        // this test at a known, normal level.
        await RunCheckAsync();
        await SetReservedAsync(null, 0);
        await RunCheckAsync();
        var told = await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageScansCap);

        var uploadId = await OpenScanAsync(ScanCap * 13 / 16);

        // Past the notice threshold (75 %), at 81.25 %: told once, at the level reached.
        await RunCheckAsync();
        var afterNotice = await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageScansCap);
        afterNotice.Count.ShouldBe(told.Count + 1);
        var notice = Placeholders(afterNotice[^1]);
        notice["percent"].ShouldBe("81");
        notice["url"].ShouldBe(StorageAlerts.ReportPage);
        (await LevelAsync(StorageLimitKind.Scans)).ShouldBe(StorageLevel.Notice);

        // Still past it at the next pass: nothing new to tell.
        await RunCheckAsync();
        (await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageScansCap)).Count.ShouldBe(told.Count + 1);

        // Fallen back below it: remembered, not told.
        await SetReservedAsync(uploadId, ScanCap / 8);
        await RunCheckAsync();
        (await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageScansCap)).Count.ShouldBe(told.Count + 1);
        (await LevelAsync(StorageLimitKind.Scans)).ShouldBe(StorageLevel.Normal);

        // Climbing past the same threshold again is a new crossing.
        await SetReservedAsync(uploadId, ScanCap * 13 / 16);
        await RunCheckAsync();
        (await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageScansCap)).Count.ShouldBe(told.Count + 2);

        // And on past the warning threshold (90 %), to 93.75 %, is one more.
        await SetReservedAsync(uploadId, ScanCap * 15 / 16);
        await RunCheckAsync();
        var afterWarning = await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageScansCap);
        afterWarning.Count.ShouldBe(told.Count + 3);
        Placeholders(afterWarning[^1])["percent"].ShouldBe("93");
        afterWarning[^1].Category.ShouldBe(NotificationCategory.StorageAlert);
        (await LevelAsync(StorageLimitKind.Scans)).ShouldBe(StorageLevel.Warning);

        // Only an administrator who can act on it was told: not a locked one, not an editor.
        (await ToldAsync(lockedAdminId, MessageTemplateCatalog.NotifyStorageScansCap)).ShouldBeEmpty();
        (await ToldAsync(editorId, MessageTemplateCatalog.NotifyStorageScansCap)).ShouldBeEmpty();

        // Who is an administrator is membership of the full administrators: one made so through
        // the permission groups alone is told as often as the others, and one who left them is
        // not told although the legacy role is still on the account.
        (await ToldAsync(groupOnlyAdminId, MessageTemplateCatalog.NotifyStorageScansCap)).Count.ShouldBe(afterWarning.Count);
        (await ToldAsync(roleOnlyFormerAdminId, MessageTemplateCatalog.NotifyStorageScansCap)).ShouldBeEmpty();

        // The installation has no cap of its own here, so it was never reported as filling.
        (await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageStoreCap)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_disk_running_short_is_told_once_per_crossing_naming_its_directory()
    {
        const long gb = 1_000_000_000;
        const long minFree = 20 * gb;
        var told = (await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageDiskLow)).Count;

        // Plenty free; under twice the minimum (a notice); under the minimum (a warning); plenty
        // again; and under the minimum once more.
        IReadOnlyList<StorageCrossing>[] passes =
        [
            await CheckWorkerDiskAsync(500 * gb, minFree),
            await CheckWorkerDiskAsync(30 * gb, minFree),
            await CheckWorkerDiskAsync(30 * gb, minFree),
            await CheckWorkerDiskAsync(5 * gb, minFree),
            await CheckWorkerDiskAsync(500 * gb, minFree),
            await CheckWorkerDiskAsync(5 * gb, minFree),
        ];

        passes[0].ShouldNotContain(c => c.Kind == StorageLimitKind.WorkerDisk);
        passes[1].ShouldContain(new StorageCrossing(StorageLimitKind.WorkerDisk, StorageLevel.Normal, StorageLevel.Notice));
        passes[2].ShouldBeEmpty();
        passes[3].ShouldContain(new StorageCrossing(StorageLimitKind.WorkerDisk, StorageLevel.Notice, StorageLevel.Warning));
        passes[4].ShouldBeEmpty();
        passes[5].ShouldContain(new StorageCrossing(StorageLimitKind.WorkerDisk, StorageLevel.Normal, StorageLevel.Warning));

        var messages = await ToldAsync(adminId, MessageTemplateCatalog.NotifyStorageDiskLow);
        messages.Count.ShouldBe(told + 3);
        var last = Placeholders(messages[^1]);
        last["free"].ShouldBe("5.0 GB");
        last["minFree"].ShouldBe("20.0 GB");
        await using var scope = factory.Services.CreateAsyncScope();
        last["directory"].ShouldBe(scope.ServiceProvider.GetRequiredService<StorageFiguresService>().WorkerRoot);
        (await ToldAsync(editorId, MessageTemplateCatalog.NotifyStorageDiskLow)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_check_with_nothing_crossed_tells_nobody_and_records_nothing_for_a_normal_ceiling()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var alerts = scope.ServiceProvider.GetRequiredService<StorageAlerts>();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        var quiet = new StorageFigures(0, null, 10, 1000, 0, null, null, null, 0, StorageThresholds.Default);
        await alerts.CheckAsync(quiet);
        var crossings = await alerts.CheckAsync(quiet);

        crossings.ShouldBeEmpty();
        (await db.StorageAlertStates.AsNoTracking().CountAsync(s => s.Level != StorageLevel.Normal)).ShouldBe(0);
        (await db.Notifications.AsNoTracking().CountAsync(n => n.RecipientUserId == adminId)).ShouldBe(0);
    }

    private async Task<IReadOnlyList<StorageCrossing>> CheckWorkerDiskAsync(long free, long minFree)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var alerts = scope.ServiceProvider.GetRequiredService<StorageAlerts>();
        return await alerts.CheckAsync(new StorageFigures(
            0, null, 0, null, 0, null, StoreFreeBytes: null, WorkerFreeBytes: free, MinFreeBytes: minFree, StorageThresholds.Default));
    }

    /// <summary>Runs the check the way the schedule does: as a queued job of its kind.</summary>
    private async Task RunCheckAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.StorageCheck);
        await handler.ExecuteAsync(new ProcessingJob { Kind = ProcessingJobKinds.StorageCheck }, CancellationToken.None);
    }

    /// <summary>
    /// Sets what point-cloud uploads still arriving have reserved: <paramref name="uploadId"/>'s
    /// declared file to <paramref name="bytes"/>, or — with no upload — every point-cloud
    /// reservation to nothing.
    /// </summary>
    private async Task SetReservedAsync(Guid? uploadId, long bytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var files = db.SurveyUploadFiles.Where(f => db.SurveyUploads.Any(u => u.Id == f.SurveyUploadId && u.Source == SurveyOrigin.PointCloud));
        if (uploadId is { } id)
        {
            files = files.Where(f => f.SurveyUploadId == id);
        }

        await files.ExecuteUpdateAsync(u => u.SetProperty(f => f.DeclaredSize, bytes));
    }

    private async Task<Guid> OpenScanAsync(long size)
    {
        var caveId = await CreateCaveAsync();
        var response = await editor.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "pointcloud",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = "Alert scan",
            files = new[] { new { name = "alert.laz", size, role = "archive" } },
        });
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync()
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            caveTypeId = await scope.ServiceProvider.GetRequiredService<SilexGisDbContext>().CaveTypes.Select(t => t.Id).FirstAsync();
        }

        var response = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Alert Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<List<Notification>> ToldAsync(Guid userId, string templateKey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.TemplateKey == templateKey)
            .OrderBy(n => n.Id)
            .ToListAsync();
    }

    private async Task<StorageLevel> LevelAsync(StorageLimitKind kind)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.StorageAlertStates.AsNoTracking()
            .Where(s => s.LimitKind == kind)
            .Select(s => s.Level)
            .SingleOrDefaultAsync();
    }

    private static Dictionary<string, string> Placeholders(Notification notification) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(notification.Placeholders, JsonSerializerOptions.Web)!;
}
