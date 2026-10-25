// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The tables a cave's surveys and their uploads live in, and the invariants the database holds
/// for them whatever the code above it does: one survey per tool-given name per cave, one version
/// per stamp per survey, a progress inside 0–100, and archived sources that outlive the survey they
/// were filed under.
/// </summary>
public sealed class SurveyUploadSchemaTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private Guid ownerId;
    private long caveTypeId;

    public SurveyUploadSchemaTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-sus-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sus-own-{suffix}@t.local");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"sus-own-{suffix}@t.local");
    }

    [Fact]
    public async Task A_survey_is_matched_by_one_name_per_tool_per_cave_and_its_versions_never_share_a_stamp()
    {
        var caveId = await CreateCaveAsync();
        var version = new DateTimeOffset(2026, 10, 25, 1, 32, 0, TimeSpan.Zero);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var survey = Survey(caveId, "inventata-1");
        db.CaveSurveys.Add(survey);
        var batch = Batch();
        db.UploadBatches.Add(batch);
        db.SurveyUploads.Add(Upload(caveId, survey.Id, batch.Id, version));
        await db.SaveChangesAsync();

        // The same name from another tool, a different name, and any number with no name at all
        // are all separate surveys of the one cave.
        db.CaveSurveys.AddRange(
            Survey(caveId, "inventata-1", SurveyOrigin.Therion),
            Survey(caveId, "inventata-2"),
            Survey(caveId, null),
            Survey(caveId, null));
        await db.SaveChangesAsync();

        // A second survey from the same tool under the same name is refused: it is the first one.
        db.CaveSurveys.Add(Survey(caveId, "inventata-1"));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // A later version is a second row; a second version under the same stamp is refused.
        db.SurveyUploads.Add(Upload(caveId, survey.Id, batch.Id, version.AddSeconds(1)));
        await db.SaveChangesAsync();
        db.SurveyUploads.Add(Upload(caveId, survey.Id, batch.Id, version));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        (await db.SurveyUploads.CountAsync(u => u.CaveSurveyId == survey.Id)).ShouldBe(2);
        (await db.CaveSurveys.CountAsync(s => s.CaveFeatureId == caveId)).ShouldBe(5);
    }

    [Fact]
    public async Task Progress_is_held_to_0_to_100_by_the_database()
    {
        var caveId = await CreateCaveAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var survey = Survey(caveId, "progress");
        var batch = Batch();
        var upload = Upload(caveId, survey.Id, batch.Id, DateTimeOffset.UtcNow);
        db.AddRange(survey, batch, upload);
        await db.SaveChangesAsync();

        // The worker's own write, as it is made: a direct update.
        await db.SurveyUploads.Where(u => u.Id == upload.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Progress, 100));
        await Should.ThrowAsync<Exception>(() => db.SurveyUploads.Where(u => u.Id == upload.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Progress, 101)));
        await Should.ThrowAsync<Exception>(() => db.SurveyUploads.Where(u => u.Id == upload.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Progress, -1)));

        (await db.SurveyUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id)).Progress.ShouldBe(100);
    }

    [Fact]
    public async Task An_archived_source_outlives_the_survey_and_the_upload_it_was_filed_under()
    {
        var caveId = await CreateCaveAsync();
        var sourceId = await ArchiveSourceAsync(caveId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var survey = Survey(caveId, "outlived");
        var batch = Batch();
        var upload = Upload(caveId, survey.Id, batch.Id, DateTimeOffset.UtcNow);
        db.AddRange(survey, batch, upload);
        db.SurveyUploadFiles.Add(new SurveyUploadFile
        {
            SurveyUploadId = upload.Id,
            Name = "cave.svx",
            Role = SurveyUploadFileRole.Other,
            Status = SurveyUploadFileStatus.Stored,
            DeclaredSize = 10,
            SurveySourceId = sourceId,
        });
        await db.SaveChangesAsync();
        await db.SurveySources.Where(s => s.Id == sourceId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.CaveSurveyId, survey.Id)
            .SetProperty(x => x.SurveyUploadId, upload.Id));

        // Filed under the survey, and listed with it.
        (await db.SurveySources.CountAsync(s => s.CaveSurveyId == survey.Id && s.SurveyUploadId == upload.Id)).ShouldBe(1);

        // The survey goes, its uploads and their file rows with it — and the archived source stays,
        // no longer filed under anything.
        await db.CaveSurveys.Where(s => s.Id == survey.Id).ExecuteDeleteAsync();

        (await db.SurveyUploads.CountAsync(u => u.Id == upload.Id)).ShouldBe(0);
        (await db.SurveyUploadFiles.CountAsync(f => f.SurveyUploadId == upload.Id)).ShouldBe(0);
        var kept = await db.SurveySources.AsNoTracking().SingleAsync(s => s.Id == sourceId);
        kept.CaveSurveyId.ShouldBeNull();
        kept.SurveyUploadId.ShouldBeNull();
        kept.CaveFeatureId.ShouldBe(caveId);
    }

    private static CaveSurvey Survey(Guid caveId, string? key, SurveyOrigin source = SurveyOrigin.TopoDroid) => new()
    {
        CaveFeatureId = caveId,
        Title = "Invented survey",
        Source = source,
        ExternalKey = key,
    };

    private UploadBatch Batch() => new()
    {
        StartedByUserId = ownerId,
        Source = UploadSource.Interactive,
        Label = "invented survey upload",
    };

    private SurveyUpload Upload(Guid caveId, Guid surveyId, Guid batchId, DateTimeOffset version) => new()
    {
        CaveFeatureId = caveId,
        CaveSurveyId = surveyId,
        UploadBatchId = batchId,
        Source = SurveyOrigin.TopoDroid,
        SurveyTitle = "Invented survey",
        VersionAt = version,
        RequestedBy = ownerId,
        ExpiresAt = version.AddHours(24),
    };

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Survey Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> ArchiveSourceAsync(Guid caveId)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "*begin main\n*data normal from to tape compass clino\n1 2 12.50 145 -3\n*end main\n"));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", "cave.svx" } };
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        factory.Dispose();
        try
        {
            if (Directory.Exists(filesRoot))
            {
                Directory.Delete(filesRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held open by the host being torn down; the scratch root is swept anyway.
        }
    }
}
