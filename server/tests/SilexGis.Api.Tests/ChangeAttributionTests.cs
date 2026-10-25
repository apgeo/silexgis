// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Every history row names the logical action that wrote it: its change set, its source, and the
/// import batch it belongs to. Proved over the four kinds of work that write history — a web edit,
/// an upload from a phone, a file import and a job — including the rows a feature's soft delete
/// and restore add by hand, which are the ones a stamp applied only to the interceptor's own rows
/// would miss.
/// </summary>
/// <remarks>
/// Every name, sheet and place here is invented, written to exercise the rules rather than to
/// resemble anybody's records.
/// </remarks>
public sealed class ChangeAttributionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string JobKind = "test-change-attribution";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient owner = null!;
    private Guid ownerId;
    private long caveTypeId;
    private long entranceTypeId;
    private string marker = null!;

    public ChangeAttributionTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Files:Root"] = filesRoot },
            configureServices: services =>
            {
                // The job below is run a turn at a time by the test, never by the host's own
                // workers polling in the background.
                JobWorkers.RemoveFrom(services);
                services.AddScoped<IProcessingJobHandler, EditsAFeatureHandler>();
            });
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        var email = $"attr-{marker}@t.local";
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        owner = await AuthHelper.BearerClientAsync(factory, email);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
        entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();

        // A spreadsheet import that creates the people it names needs the roster's own right,
        // which the shipped editor group does not hold.
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = ownerId,
            Effect = AccessEffect.Allow,
            Domain = AccessDomain.Cavers,
            Actions = AccessAction.Create,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_web_edit_and_its_soft_delete_and_restore_each_name_the_web_and_a_change_set_of_their_own()
    {
        var cave = await CreateCaveAsync($"Peștera web {marker}");
        var entrance = await CreateEntranceAsync(cave);
        (await owner.DeleteAsync($"/api/v1/caves/{cave}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var restored = await owner.PostAsync($"/api/v1/features/{cave}/restore", null);
        restored.StatusCode.ShouldBe(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());

        var rows = await RowsOfAsync(cave, entrance);

        // Four requests: the creation, the entrance (which also touches the cave's mirror), the
        // delete and the restore. Every row names the web, no batch, and the caller.
        rows.ShouldAllBe(r => r.Source == ChangeSource.Web && r.ImportBatchId == null && r.ChangeSetId != null);
        rows.ShouldAllBe(r => r.UserId == ownerId);
        rows.Select(r => r.ChangeSetId).Distinct().Count().ShouldBe(4);

        // The soft delete's rows are added by hand, one per feature of the subtree; they share the
        // delete's change set and no other row does.
        var deleted = rows.Where(r => r.Action == AuditActions.Deleted).ToList();
        deleted.Select(r => r.EntityId).ShouldBe([cave.ToString(), entrance.ToString()], ignoreOrder: true);
        deleted.Select(r => r.ChangeSetId).Distinct().Count().ShouldBe(1);

        // The same for the restore's hand-written rows, in a change set of its own.
        var undeleted = rows.Where(r => r.Changes != null && r.Changes.Contains("DeletedAt")).ToList();
        undeleted.Select(r => r.EntityId).ShouldBe([cave.ToString(), entrance.ToString()], ignoreOrder: true);
        undeleted.Select(r => r.ChangeSetId).Distinct().Single().ShouldNotBe(deleted[0].ChangeSetId);
    }

    [Fact]
    public async Task A_phone_upload_names_its_batch_on_every_row_it_writes()
    {
        var existing = await CreateCaveAsync($"Peștera existentă {marker}");
        var doomed = await CreateCaveAsync($"Peștera de șters {marker}");
        var made = Guid.CreateVersion7();
        var syncSet = await CreateSyncSetAsync();

        var response = await owner.PostAsJsonAsync($"/api/v1/sync/sets/{syncSet}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            rows = new[]
            {
                SyncCave(made, $"Peștera din telefon {marker}"),
                SyncCave(existing, $"Redenumită din telefon {marker}", await RevisionAsync(existing)),
                SyncCave(doomed, $"Peștera de șters {marker}", await RevisionAsync(doomed), deleted: true),
            },
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var answer = JsonDocument.Parse(body).RootElement;
        answer.GetProperty("written").GetInt32().ShouldBe(3);
        var batchId = answer.GetProperty("importBatchId").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var batchRows = await db.AuditEntries.AsNoTracking().Where(a => a.ImportBatchId == batchId).ToListAsync();

        // One upload, one change set, every row from the phone and done by its account.
        batchRows.ShouldAllBe(r => r.Source == ChangeSource.DeviceSync && r.UserId == ownerId);
        batchRows.Select(r => r.ChangeSetId).Distinct().Count().ShouldBe(1);
        batchRows[0].ChangeSetId.ShouldNotBeNull();

        // What it created, what it edited and what it deleted (the soft delete's hand-written row)
        // are all among them, and so is the record of the batch itself.
        batchRows.ShouldContain(r => r.EntityId == made.ToString() && r.Action == AuditActions.Created);
        batchRows.ShouldContain(r => r.EntityId == existing.ToString() && r.Action == AuditActions.Updated);
        batchRows.ShouldContain(r => r.EntityId == doomed.ToString() && r.Action == AuditActions.Deleted);
        batchRows.ShouldContain(r => r.EntityType == nameof(ImportBatch) && r.EntityId == batchId.ToString());

        // The web requests that made the two caves the phone then touched are not the upload's.
        var before = await RowsOfAsync(existing, doomed);
        before.Where(r => r.Action == AuditActions.Created)
            .ShouldAllBe(r => r.Source == ChangeSource.Web && r.ImportBatchId == null);
        before.Where(r => r.Source == ChangeSource.DeviceSync).ShouldAllBe(r => r.ImportBatchId == batchId);
    }

    [Fact]
    public async Task A_file_import_and_its_undo_each_name_the_batch_in_change_sets_of_their_own()
    {
        var fileId = await UploadSheetAsync();
        var commit = await owner.PostAsync($"/api/v1/trip-imports/{fileId}/commit", Json(new
        {
            options = new
            {
                delimiter = ",",
                multiValueSeparators = ";,",
                slashSeparatedFields = Array.Empty<string>(),
                dateOrder = "dayFirst",
                columns = new Dictionary<string, string>(),
                visibility = "private",
                cavingGroupId = (Guid?)null,
                createMissingCaves = true,
                createMissingAreas = true,
                createMissingCavers = true,
                createMissingTripTypes = false,
            },
            lines = new[] { 2 },
        }));
        var commitBody = await commit.Content.ReadAsStringAsync();
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, commitBody);
        var batchId = JsonDocument.Parse(commitBody).RootElement.GetProperty("batchId").GetGuid();

        List<Guid> created;
        AuditEntry[] imported;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            created = await db.ImportBatchItems.AsNoTracking()
                .Where(i => i.ImportBatchId == batchId && i.FeatureId != null)
                .Select(i => i.FeatureId!.Value)
                .ToListAsync();
            imported = [.. await db.AuditEntries.AsNoTracking().Where(a => a.ImportBatchId == batchId).ToListAsync()];
        }

        // The sheet named a massif, a sub-area and a cave, and the switches were on.
        created.Count.ShouldBeGreaterThan(0);
        imported.ShouldAllBe(r => r.Source == ChangeSource.FileImport && r.UserId == ownerId);
        imported.Select(r => r.ChangeSetId).Distinct().Count().ShouldBe(1);
        foreach (var feature in created)
        {
            imported.ShouldContain(r => r.EntityId == feature.ToString() && r.Action == AuditActions.Created);
        }

        imported.ShouldContain(r => r.EntityType == nameof(ImportBatch) && r.EntityId == batchId.ToString());

        var revert = await owner.PostAsJsonAsync($"/api/v1/import-batches/{batchId}/revert", new { });
        revert.StatusCode.ShouldBe(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var undo = await db.AuditEntries.AsNoTracking()
                .Where(a => a.ImportBatchId == batchId && a.Source == ChangeSource.Undo)
                .ToListAsync();

            // The undo is one change set of its own, filed under the batch it took back, and its
            // rows include every soft delete it added by hand.
            undo.Select(r => r.ChangeSetId).Distinct().Count().ShouldBe(1);
            undo[0].ChangeSetId.ShouldNotBe(imported[0].ChangeSetId);
            undo.ShouldAllBe(r => r.UserId == ownerId);
            foreach (var feature in created)
            {
                undo.ShouldContain(r => r.EntityId == feature.ToString()
                    && r.Action == AuditActions.Deleted && r.Changes == null);
            }

            // Nothing else was filed under the batch.
            (await db.AuditEntries.CountAsync(a => a.ImportBatchId == batchId))
                .ShouldBe(imported.Length + undo.Count);
        }
    }

    [Fact]
    public async Task A_job_names_itself_and_the_account_that_asked_for_it_on_every_row()
    {
        var cave = await CreateCaveAsync($"Peștera din coadă {marker}");

        long jobId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = new ProcessingJob
            {
                Kind = JobKind,
                RequestedBy = ownerId,
                Payload = JsonSerializer.Serialize(new { featureId = cave }),
            };
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        var worker = new TestLaneWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IConfiguration>());
        (await worker.TurnAsync(CancellationToken.None)).ShouldBeTrue();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            job.Status.ShouldBe(ProcessingJobStatus.Succeeded, job.Error);
        }

        var rows = await RowsOfAsync(cave);
        var byJob = rows.Where(r => r.Source == ChangeSource.Job).ToList();

        // The rename, the soft delete's hand-written row and the restore's: three saves of one run,
        // one change set, each recorded as done for the account that queued the job although no
        // request said who the caller was.
        byJob.Count.ShouldBe(3);
        byJob.ShouldContain(r => r.Action == AuditActions.Updated && r.Changes != null && r.Changes.Contains("\"Name\""));
        byJob.ShouldContain(r => r.Action == AuditActions.Deleted && r.Changes == null);
        byJob.ShouldContain(r => r.Action == AuditActions.Updated && r.Changes != null && r.Changes.Contains("DeletedAt"));
        byJob.ShouldAllBe(r => r.UserId == ownerId && r.ImportBatchId == null);
        byJob.Select(r => r.ChangeSetId).Distinct().Count().ShouldBe(1);

        // The web request that created the cave is not the job's.
        rows.Single(r => r.Action == AuditActions.Created).Source.ShouldBe(ChangeSource.Web);
    }

    [Fact]
    public void Starting_an_action_takes_a_fresh_change_set_and_keeps_whom_the_work_is_for()
    {
        var change = new ChangeContext(ChangeSource.Job);
        var first = change.ChangeSetId;
        var user = Guid.NewGuid();
        var batch = Guid.NewGuid();

        change.ActFor(user);
        change.Begin(ChangeSource.FileImport, batch);

        change.ChangeSetId.ShouldNotBe(first);
        change.Source.ShouldBe(ChangeSource.FileImport);
        change.ImportBatchId.ShouldBe(batch);
        // A job that confirms an import is still done for the account that asked for the job.
        change.ActingUserId.ShouldBe(user);

        change.Begin(ChangeSource.Restore);
        change.ImportBatchId.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_request_starts_as_a_web_edit_and_other_work_as_a_job_whatever_the_registration_order(bool persistenceFirst)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Db:ConnectionString"] = "Host=localhost;Database=unused" })
            .Build();
        var services = new ServiceCollection();
        if (persistenceFirst)
        {
            services.AddSilexGisPersistence(configuration);
            services.AddSilexGisAuth(configuration);
        }
        else
        {
            services.AddSilexGisAuth(configuration);
            services.AddSilexGisPersistence(configuration);
        }

        // One registration, per unit of work, and the web host's: the persistence layer's default
        // would start every request as a job.
        var descriptor = services.Single(d => d.ServiceType == typeof(IChangeContext));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);

        IChangeContext Resolve(HttpContext? request)
        {
            using var provider = new ServiceCollection()
                .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = request })
                .BuildServiceProvider();
            return (IChangeContext)descriptor.ImplementationFactory!(provider);
        }

        Resolve(new DefaultHttpContext()).Source.ShouldBe(ChangeSource.Web);
        Resolve(null).Source.ShouldBe(ChangeSource.Job);
    }

    // ---------- helpers ----------

    private async Task<List<AuditEntry>> RowsOfAsync(params Guid[] ids)
    {
        var keys = ids.Select(i => i.ToString()).ToList();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId != null && keys.Contains(a.EntityId))
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CreateEntranceAsync(Guid cave)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{cave}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 24.61234, 45.71234 } },
            positionQuality = "gps",
        });
        return await CreatedIdAsync(response);
    }

    private async Task<Guid> CreateSyncSetAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            cavingGroupId = (Guid?)null,
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        return await CreatedIdAsync(response);
    }

    private static object SyncCave(Guid id, string name, DateTimeOffset? baseRevision = null, bool deleted = false) => new
    {
        id,
        kind = "cave",
        baseRevision,
        deleted,
        name,
        caveTypeCode = "cave",
        isMain = false,
    };

    private async Task<DateTimeOffset> RevisionAsync(Guid featureId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await db.Features.AsNoTracking().SingleAsync(f => f.Id == featureId)).UpdatedAt;
    }

    /// <summary>One invented trip naming an invented massif, sub-area, cave and person.</summary>
    private async Task<Guid> UploadSheetAsync()
    {
        var sheet =
            "Nr crt.,Data inceput,Data sfarsit,Titlu,Tara,Masiv/zona,Subzona,Pesteri,Propus de,Participanti,Detalii,Tip,Erori\r\n"
            + $"1,05/03/2024,,Tura de proba,Romania,Masivul Proba {marker},Valea Proba {marker},Pestera Proba {marker},"
            + $"Ion Proba {marker},Ion Proba {marker},Nimic special,,\r\n";
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(sheet));
        content.Headers.ContentType = new("text/csv");
        using var form = new MultipartFormDataContent { { content, "file", "attribution.csv" } };
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        return await CreatedIdAsync(response);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private sealed class TestLaneWorker(IServiceScopeFactory scopes, IConfiguration configuration)
        : ProcessingJobWorkerBase(new JobLane([JobKind], Excluded: false), scopes, configuration, NullLogger.Instance)
    {
        public Task<bool> TurnAsync(CancellationToken ct) => RunNextAsync(ct);
    }

    /// <summary>
    /// A job that edits a feature three ways in three saves: a tracked rename, then a soft delete
    /// and a restore, whose history rows the write service adds by hand.
    /// </summary>
    private sealed class EditsAFeatureHandler(SilexGisDbContext db, FeatureWriteService writer) : IProcessingJobHandler
    {
        public string Kind => JobKind;

        public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
        {
            var featureId = JsonDocument.Parse(job.Payload).RootElement.GetProperty("featureId").GetGuid();
            var feature = await db.Features.SingleAsync(f => f.Id == featureId, ct);
            feature.Name = $"{feature.Name} (renamed by a job)";
            await db.SaveChangesAsync(ct);

            await writer.SoftDeleteAsync(featureId, ct);
            await db.SaveChangesAsync(ct);

            await writer.RestoreAsync(featureId, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        owner?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
