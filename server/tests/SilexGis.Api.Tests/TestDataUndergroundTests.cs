// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Api.Tests;

/// <summary>
/// The underground a test-data run writes for the caves whose survey it keeps: a survey model
/// read by the ordinary reader into the stations, legs and centreline it was grown with, at the
/// cave's main entrance, with its source, its survey, its depth places and its levels — and a
/// removal that takes all of it back, the centrelines the reading drew and the stored files
/// included.
/// </summary>
public sealed class TestDataUndergroundTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Base = "/api/v1/admin/test-data";

    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private HttpClient admin = null!;

    public TestDataUndergroundTests(PostgresFixture postgres)
    {
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tdu-admin-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tdu-admin-{suffix}@t.local");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task A_run_writes_surveys_the_reader_reads_into_what_they_were_grown_as_and_removal_takes_them_back()
    {
        var profile = new
        {
            seed = 5151,
            regions = new[] { "padurea-craiului" },
            caves = new { count = 12, withSurveyPercent = 50 },
            people = new { clubs = 1, organisations = 0, cavers = 4, accounts = 2 },
        };
        var plan = TestDataPlanner.Plan(TestDataProfile.FromJson(JsonSerializer.Serialize(profile, TestDataProfile.JsonOptions)));
        var start = await admin.PostAsJsonAsync($"{Base}/runs", new { profile, confirmAlongsideExistingData = true });
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted, await start.Content.ReadAsStringAsync());
        var runId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var run = await RunJobAsync(ProcessingJobKinds.TestDataGenerate, runId);
        run.GetProperty("status").GetString().ShouldBe("succeeded", run.ToString());
        run.GetProperty("counts").GetProperty("surveyModels").GetInt32().ShouldBe(6);

        // The readings the run queued, run as the worker would.
        List<Guid> models;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            models = (await db.TestDataRows.Where(r => r.RunId == runId && r.TableName == "survey_models").Select(r => r.RowId).ToListAsync())
                .Select(Guid.Parse).ToList();
        }

        models.Count.ShouldBe(6);
        foreach (var job in await QueuedJob.OfKindAsync(factory.Services, ProcessingJobKinds.SurveyGraph))
        {
            if (models.Any(m => job.Payload.Contains(m.ToString(), StringComparison.Ordinal)))
            {
                await QueuedJob.RunAsync(factory.Services, job.Id);
            }
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var read = await db.SurveyModels.Where(m => models.Contains(m.Id)).ToListAsync();
            read.ShouldAllBe(m => m.Status == SurveyModelStatus.Ready, string.Join("; ", read.Select(m => m.ProcessingError)));
            read.ShouldAllBe(m => m.IsCurrent);

            // Each model holds the stations its cave was grown with, and the reader drew its centreline.
            foreach (var model in read)
            {
                var cave = await db.Features.SingleAsync(f => f.Id == model.CaveFeatureId);
                var planned = plan.Caves.Single(c => c.Name == cave.Name);
                var stations = await db.SurveyStations.CountAsync(s => s.SurveyModelId == model.Id);
                stations.ShouldBe(planned.Network!.Stations.Count(s => s.Name.Length > 0), cave.Name);
                (await db.Centerlines.AnyAsync(c => c.SurveyModelId == model.Id)).ShouldBeTrue(cave.Name);
            }

            (await db.SurveySources.CountAsync(s => read.Select(m => m.CaveFeatureId).Contains(s.CaveFeatureId))).ShouldBe(6);
            (await db.CaveDepthPlaces.CountAsync(p => read.Select(m => m.CaveFeatureId).Contains(p.CaveFeatureId))).ShouldBeGreaterThan(0);
        }

        // Removal takes it all back: models, the readings' centrelines and stations, documents and bytes.
        (await admin.PostAsJsonAsync($"{Base}/runs/{runId}/remove", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RunJobAsync(ProcessingJobKinds.TestDataRemove, runId)).GetProperty("status").GetString().ShouldBe("removed");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.SurveyModels.CountAsync(m => models.Contains(m.Id))).ShouldBe(0);
            (await db.SurveyStations.CountAsync(s => models.Contains(s.SurveyModelId))).ShouldBe(0);
            (await db.Centerlines.IgnoreQueryFilters().CountAsync(c => c.SurveyModelId != null && models.Contains(c.SurveyModelId.Value))).ShouldBe(0);
            (await TestDataCoverageSql.MeasureAsync(db, runId, CancellationToken.None)).ShouldAllBe(t => t.Rows == 0);
        }
    }

    private async Task<JsonElement> RunJobAsync(string kind, Guid runId)
    {
        var job = (await QueuedJob.OfKindAsync(factory.Services, kind))
            .First(j => j.Payload.Contains(runId.ToString(), StringComparison.Ordinal));
        await QueuedJob.RunAsync(factory.Services, job.Id);
        return await admin.GetFromJsonAsync<JsonElement>($"{Base}/runs/{runId}");
    }
}
