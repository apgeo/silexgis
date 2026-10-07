// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// Asking for a stored survey to be read again: one model at a time, by somebody who may write the
/// cave's surveys, and every line plot of the installation at once, by an operator.
///
/// <para>
/// What is held here is that a second reading replaces the first and does not add to it, that it
/// puts back what a model was missing, that a model which could not be read gets another attempt,
/// that nobody queues a reading behind one already waiting, and that the action is closed to a
/// caller who may not write the cave or may not know where it is.
/// </para>
///
/// <para>
/// And that a model which has been read is never without its reading: it stays ready while
/// another reading waits or runs, and keeps what it had when that reading fails. Everything that
/// draws or measures a survey takes "not ready" to mean "holds nothing", so a finished model sent
/// back to waiting would lose its cave its figures and a running watch its drawing for as long as
/// the queue took.
/// </para>
/// </summary>
public sealed class SurveyModelReadingTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private long caveTypeId;

    public SurveyModelReadingTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            // No background worker: every test here says when a queued reading runs, because what
            // is under test is the state a model is in between being asked for and being read.
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"srd-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"srd-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"srd-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"srd-read-{suffix}@t.local");
    }

    [Fact]
    public async Task Reading_a_model_again_queues_one_reading_and_leaves_one_set_of_rows_where_they_were()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, NamedRootCave());
        await RunQueuedReadingAsync(modelId);

        var first = await GetModelAsync(owner, modelId);
        first.GetProperty("status").GetString().ShouldBe("ready", first.ToString());
        var rowsAfterFirst = await CountRowsAsync(modelId);
        rowsAfterFirst.ShouldBe((Stations: 3, Shots: 2));
        var placedAfterFirst = await PlacesAsync(modelId);

        var asked = await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null);
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.Content.ReadFromJsonAsync<JsonElement>();
        waiting.GetProperty("id").GetGuid().ShouldBe(modelId);

        // Still ready, and said to be on its way to being read again: the reading it holds is all
        // there, so nothing that uses the survey is told it has none.
        first.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        waiting.GetProperty("status").GetString().ShouldBe("ready");
        waiting.GetProperty("readingAgain").GetBoolean().ShouldBeTrue();
        (await StatusAsync(modelId)).ShouldBe(SurveyModelStatus.Ready);
        (await ListedAsync(caveId, modelId)).GetProperty("readingAgain").GetBoolean().ShouldBeTrue();

        // Exactly the job an upload queues, and one of it. The rows of the first reading are still
        // there while it waits: nothing is taken away before there is something to put in its place.
        (await QueuedReadingsAsync(modelId)).ShouldBe(1);
        (await CountRowsAsync(modelId)).ShouldBe(rowsAfterFirst);

        await RunQueuedReadingAsync(modelId);

        var second = await GetModelAsync(owner, modelId);
        second.GetProperty("status").GetString().ShouldBe("ready", second.ToString());
        second.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        (await CountRowsAsync(modelId)).ShouldBe(rowsAfterFirst);

        // And where they were. A reading writes back the anchor it placed the file by and the next
        // reading is handed that anchor, so this would drift if the two were not the same point.
        second.GetProperty("anchorLongitude").GetDouble()
            .ShouldBe(first.GetProperty("anchorLongitude").GetDouble(), 1e-9);
        second.GetProperty("anchorLatitude").GetDouble()
            .ShouldBe(first.GetProperty("anchorLatitude").GetDouble(), 1e-9);
        var placedAfterSecond = await PlacesAsync(modelId);
        placedAfterSecond.Keys.ShouldBe(placedAfterFirst.Keys, ignoreOrder: true);
        foreach (var (name, place) in placedAfterFirst)
        {
            placedAfterSecond[name].Longitude.ShouldBe(place.Longitude, 1e-9);
            placedAfterSecond[name].Latitude.ShouldBe(place.Latitude, 1e-9);
            placedAfterSecond[name].AltitudeM.ShouldBe(place.AltitudeM, 1e-6);
        }

        // One centerline, rewritten, and still the cave's.
        (await CenterlinesOfAsync(caveId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reading_a_model_again_puts_back_the_root_surveys_name_it_lacked()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, NamedRootCave());
        await RunQueuedReadingAsync(modelId);
        (await RootSurveyNameAsync(modelId)).ShouldBe("cave");

        // The state of a model read before the reader recorded the name: everything else in
        // place, this one column empty. Written directly, because no path through the application
        // takes it away.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyModels.Where(m => m.Id == modelId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.RootSurveyName, (string?)null));
        }

        (await RootSurveyNameAsync(modelId)).ShouldBeNull();

        (await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        await RunQueuedReadingAsync(modelId);

        (await RootSurveyNameAsync(modelId)).ShouldBe("cave");
    }

    /// <summary>
    /// A write to the cave's surveys, gated like one: refused to somebody who may only read the
    /// cave, and not acknowledged at all where the cave's position is closed to the caller.
    /// </summary>
    [Fact]
    public async Task Reading_a_model_again_is_gated_like_every_other_write_to_a_survey_model()
    {
        var open = await CreateCaveAsync(locationProtected: false);
        var visible = await UploadLocalLoxAsync(open, NamedRootCave());
        await RunQueuedReadingAsync(visible);

        var closed = await CreateCaveAsync(locationProtected: true);
        var withheld = await UploadLocalLoxAsync(closed, NamedRootCave());
        await RunQueuedReadingAsync(withheld);

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsync($"/api/v1/survey-models/{visible}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        // The reader can see the open cave's model — the list answers it — and still may not ask.
        (await reader.GetAsync($"/api/v1/survey-models/{visible}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostAsync($"/api/v1/survey-models/{visible}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // On the protected cave the same reader is told there is no such model, in the words used
        // for a model that never existed, and the answer carries nothing read out of the survey.
        var refused = await reader.PostAsync($"/api/v1/survey-models/{withheld}/reading", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var refusal = await refused.Content.ReadAsStringAsync();
        var never = await reader.PostAsync($"/api/v1/survey-models/{Guid.NewGuid()}/reading", null);
        never.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonDocument.Parse(refusal).RootElement.GetProperty("code").GetString()
            .ShouldBe("survey_model.not_found");
        JsonDocument.Parse(await never.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()
            .ShouldBe("survey_model.not_found");
        foreach (var name in (await PlacesAsync(withheld)).Keys)
        {
            refusal.ShouldNotContain(name);
        }

        // Neither refusal queued anything, and neither model was touched.
        (await QueuedReadingsAsync(visible)).ShouldBe(0);
        (await QueuedReadingsAsync(withheld)).ShouldBe(0);
        (await GetModelAsync(owner, visible)).GetProperty("status").GetString().ShouldBe("ready");
        (await GetModelAsync(owner, withheld)).GetProperty("status").GetString().ShouldBe("ready");

        // The same two requests from somebody who may write the caves and place them go through,
        // so what stopped the reader was who was asking and not the state the models were in.
        (await owner.PostAsync($"/api/v1/survey-models/{visible}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/survey-models/{withheld}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await QueuedReadingsAsync(visible)).ShouldBe(1);
        (await QueuedReadingsAsync(withheld)).ShouldBe(1);
    }

    [Fact]
    public async Task A_reading_is_not_queued_behind_one_that_is_waiting_or_running()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, NamedRootCave());

        // Just uploaded: its first reading is still in the queue.
        await ShouldBeRefusedAsInProgressAsync(modelId);
        (await QueuedReadingsAsync(modelId)).ShouldBe(1);

        // Picked up by the worker and not yet finished.
        await SetStatusAsync(modelId, SurveyModelStatus.Processing);
        await ShouldBeRefusedAsInProgressAsync(modelId);
        (await QueuedReadingsAsync(modelId)).ShouldBe(1);

        await SetStatusAsync(modelId, SurveyModelStatus.Pending);
        await RunQueuedReadingAsync(modelId);

        // Finished: now it may be asked for, once. The model says it is ready throughout, so what
        // refuses the second request is the reading that is queued, not the model's status.
        (await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(modelId)).ShouldBe(SurveyModelStatus.Ready);
        await ShouldBeRefusedAsInProgressAsync(modelId);
        (await QueuedReadingsAsync(modelId)).ShouldBe(1);

        // Picked up by the worker and not yet finished: still refused, and still one job.
        var running = (await QueuedForAsync(ProcessingJobKinds.SurveyGraph, modelId)).Single().Id;
        await SetJobStatusAsync(running, ProcessingJobStatus.Running);
        await ShouldBeRefusedAsInProgressAsync(modelId);
        (await GetModelAsync(owner, modelId)).GetProperty("readingAgain").GetBoolean().ShouldBeTrue();

        // Over, however it ended: it may be asked for again.
        await SetJobStatusAsync(running, ProcessingJobStatus.Succeeded);
        (await GetModelAsync(owner, modelId)).GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        (await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        (await QueuedReadingsAsync(modelId)).ShouldBe(1);
    }

    /// <summary>
    /// Two requests arriving together for a model that is ready both find it ready, and there is
    /// no status for one of them to take from the other. Exactly one may queue a reading.
    /// </summary>
    [Fact]
    public async Task Two_requests_arriving_together_queue_one_reading()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, NamedRootCave());
        await RunQueuedReadingAsync(modelId);

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(
            _ => owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)));

        answers.Count(a => a.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        answers.Count(a => a.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
        (await QueuedReadingsAsync(modelId)).ShouldBe(1);
    }

    /// <summary>
    /// A model that has been read keeps its reading when another one fails. The transaction that
    /// would have replaced the rows never commits, so they are all still there, and a model marked
    /// as failed would be reported as holding nothing by every screen that uses it.
    /// </summary>
    [Fact]
    public async Task A_model_keeps_the_reading_it_had_when_another_reading_of_it_fails()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalLoxAsync(caveId, NamedRootCave());
        await RunQueuedReadingAsync(modelId);
        var rows = await CountRowsAsync(modelId);
        rows.ShouldBe((Stations: 3, Shots: 2));
        var placed = await PlacesAsync(modelId);

        // The file can no longer be placed: a survey in plain metres whose zero point has no
        // position. Taken off the row directly, standing in for any reason a later reading fails
        // where an earlier one succeeded.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.SurveyModels.Where(m => m.Id == modelId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Anchor, (Point?)null)
                    .SetProperty(m => m.AnchorHeightM, (double?)null));
        }

        (await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var job = (await QueuedForAsync(ProcessingJobKinds.SurveyGraph, modelId)).Single().Id;
        await RunQueuedReadingAsync(modelId, expectFailure: true);

        // The failure is the job's, with its reason, and reaches whoever asked from there.
        var failedJob = await JobAsync(job);
        failedJob.Status.ShouldBe(ProcessingJobStatus.Failed);
        failedJob.Error.ShouldNotBeNullOrWhiteSpace();

        // The model is as it was: ready, uncomplaining, and holding every row in the same place.
        var after = await GetModelAsync(owner, modelId);
        after.GetProperty("status").GetString().ShouldBe("ready", after.ToString());
        after.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        (await CountRowsAsync(modelId)).ShouldBe(rows);
        (await PlacesAsync(modelId)).Keys.ShouldBe(placed.Keys, ignoreOrder: true);
        (await CenterlinesOfAsync(caveId)).ShouldHaveSingleItem();

        // And it may be asked for again, which a model stuck as being read never could be.
        (await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A wall mesh may be asked for again like any other model, and its second conversion takes
    /// the place of the first. An upload may have one file derived from it and no more, so a
    /// conversion that only added its result would be refused at the save.
    /// </summary>
    [Fact]
    public async Task Converting_a_wall_mesh_again_replaces_the_mesh_it_had()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalStlAsync(caveId);
        await RunQueuedAsync(ProcessingJobKinds.SurveyMesh, modelId);

        var first = await GetModelAsync(owner, modelId);
        first.GetProperty("status").GetString().ShouldBe("ready", first.ToString());
        var uploadId = first.GetProperty("fileId").GetGuid();
        var firstMesh = (await DerivedFromAsync(uploadId)).ShouldHaveSingleItem();
        File.Exists(firstMesh.AbsolutePath).ShouldBeTrue();

        var asked = await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null);
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.Content.ReadFromJsonAsync<JsonElement>();
        waiting.GetProperty("status").GetString().ShouldBe("ready");
        waiting.GetProperty("readingAgain").GetBoolean().ShouldBeTrue();
        waiting.GetProperty("meshUrl").GetString().ShouldNotBeNullOrWhiteSpace();
        await ShouldBeRefusedAsInProgressAsync(modelId);

        var job = (await QueuedForAsync(ProcessingJobKinds.SurveyMesh, modelId)).Single().Id;
        await RunQueuedAsync(ProcessingJobKinds.SurveyMesh, modelId);
        (await JobAsync(job)).Status.ShouldBe(ProcessingJobStatus.Succeeded);

        var second = await GetModelAsync(owner, modelId);
        second.GetProperty("status").GetString().ShouldBe("ready", second.ToString());
        second.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        second.GetProperty("triangleCount").GetInt32().ShouldBe(first.GetProperty("triangleCount").GetInt32());

        // One derived file, the new one, named by the model; the earlier one gone, row and bytes.
        var secondMesh = (await DerivedFromAsync(uploadId)).ShouldHaveSingleItem();
        secondMesh.Id.ShouldNotBe(firstMesh.Id);
        (await ConvertedFileIdAsync(modelId)).ShouldBe(secondMesh.Id);
        File.Exists(secondMesh.AbsolutePath).ShouldBeTrue();
        File.Exists(firstMesh.AbsolutePath).ShouldBeFalse();
    }

    /// <summary>
    /// A second conversion that fails leaves the model drawing the mesh it had, and leaves both
    /// the model and the job with an outcome: neither is left saying work is under way.
    /// </summary>
    [Fact]
    public async Task A_wall_mesh_keeps_the_mesh_it_had_when_converting_it_again_fails()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadLocalStlAsync(caveId);
        await RunQueuedAsync(ProcessingJobKinds.SurveyMesh, modelId);

        var first = await GetModelAsync(owner, modelId);
        var uploadId = first.GetProperty("fileId").GetGuid();
        var mesh = (await DerivedFromAsync(uploadId)).ShouldHaveSingleItem();

        // The stored upload is no longer a mesh anybody can read.
        await File.WriteAllBytesAsync(await AbsolutePathOfAsync(uploadId), [1, 2, 3, 4, 5]);

        (await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var job = (await QueuedForAsync(ProcessingJobKinds.SurveyMesh, modelId)).Single().Id;
        await RunQueuedAsync(ProcessingJobKinds.SurveyMesh, modelId, expectFailure: true);

        (await JobAsync(job)).Status.ShouldBe(ProcessingJobStatus.Failed);

        var after = await GetModelAsync(owner, modelId);
        after.GetProperty("status").GetString().ShouldBe("ready", after.ToString());
        after.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);
        after.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        (await ConvertedFileIdAsync(modelId)).ShouldBe(mesh.Id);
        (await DerivedFromAsync(uploadId)).ShouldHaveSingleItem().Id.ShouldBe(mesh.Id);
        File.Exists(mesh.AbsolutePath).ShouldBeTrue();
    }

    /// <summary>
    /// A wall mesh whose first conversion failed is marked as failed and can be asked for again:
    /// the outcome of a failed conversion is recorded, and is not the end of the model.
    /// </summary>
    [Fact]
    public async Task A_wall_mesh_that_could_not_be_converted_says_so_and_may_be_asked_for_again()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);
        var modelId = await UploadAsync(caveId, "Walls.stl", [0, 1, 2, 3], DeclareOrigin);
        var job = (await QueuedForAsync(ProcessingJobKinds.SurveyMesh, modelId)).Single().Id;
        await RunQueuedAsync(ProcessingJobKinds.SurveyMesh, modelId, expectFailure: true);

        (await JobAsync(job)).Status.ShouldBe(ProcessingJobStatus.Failed);
        var failed = await GetModelAsync(owner, modelId);
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("processingError").GetString().ShouldNotBeNullOrWhiteSpace();

        var asked = await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null);
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.Content.ReadFromJsonAsync<JsonElement>();
        waiting.GetProperty("status").GetString().ShouldBe("pending");
        waiting.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        (await QueuedOfKindAsync(ProcessingJobKinds.SurveyMesh, modelId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_model_that_could_not_be_read_is_read_once_the_reason_is_gone()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        // A survey in plain metres with no position given for its zero point: it cannot be placed.
        var modelId = await UploadAsync(caveId, "Unplaced.lox", NamedRootCave());
        await RunQueuedReadingAsync(modelId, expectFailure: true);

        var failed = await GetModelAsync(owner, modelId);
        failed.GetProperty("status").GetString().ShouldBe("failed");
        failed.GetProperty("processingError").GetString().ShouldNotBeNullOrWhiteSpace();
        (await CountRowsAsync(modelId)).ShouldBe((Stations: 0, Shots: 0));

        // The reason goes away. Given to the row directly: the reading takes the position off the
        // row, and what is under test is that a failed model gets another reading at all.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var model = await db.SurveyModels.SingleAsync(m => m.Id == modelId);
            model.Anchor = new Point(25.2, 45.5) { SRID = 4326 };
            model.AnchorHeightM = 1200;
            await db.SaveChangesAsync();
        }

        var asked = await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null);
        asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.Content.ReadFromJsonAsync<JsonElement>();

        // Waiting, and no longer carrying the complaint of a reading that is about to be replaced.
        // It holds nothing, so waiting is what is true of it — unlike a model that has been read.
        waiting.GetProperty("status").GetString().ShouldBe("pending");
        waiting.GetProperty("readingAgain").GetBoolean().ShouldBeFalse();
        waiting.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);

        await RunQueuedReadingAsync(modelId);

        var read = await GetModelAsync(owner, modelId);
        read.GetProperty("status").GetString().ShouldBe("ready", read.ToString());
        read.GetProperty("processingError").ValueKind.ShouldBe(JsonValueKind.Null);
        (await CountRowsAsync(modelId)).ShouldBe((Stations: 3, Shots: 2));
    }

    [Fact]
    public async Task The_sweep_over_every_survey_is_an_operators_to_start()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"srd-adm-{suffix}@t.local");
        using var admin = await AuthHelper.BearerClientAsync(factory, $"srd-adm-{suffix}@t.local");
        using var anonymous = factory.CreateClient();

        (await anonymous.PostAsync("/api/v1/jobs/survey-reading-backfill", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await reader.PostAsync("/api/v1/jobs/survey-reading-backfill", null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Writing a cave's surveys is not running the installation's maintenance.
        (await owner.PostAsync("/api/v1/jobs/survey-reading-backfill", null)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var enqueue = await admin.PostAsync("/api/v1/jobs/survey-reading-backfill", null);
        var payload = await enqueue.Content.ReadAsStringAsync();
        enqueue.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("kind").GetString()
            .ShouldBe("survey-reading-backfill");
    }

    [Fact]
    public async Task The_sweep_queues_a_reading_for_each_line_plot_that_has_none_waiting()
    {
        var caveId = await CreateCaveAsync(locationProtected: false);

        var read = await UploadLocalLoxAsync(caveId, NamedRootCave());
        await RunQueuedReadingAsync(read);

        var failed = await UploadAsync(caveId, "Unplaced.lox", NamedRootCave());
        await RunQueuedReadingAsync(failed, expectFailure: true);

        // Still waiting for the reading its upload queued.
        var waiting = await UploadLocalLoxAsync(caveId, NamedRootCave());

        // A wall mesh that has been converted. The bytes are never opened here: what is under
        // test is that the sweep passes over the format, whatever state the model is in.
        var mesh = await UploadAsync(caveId, "Walls.stl", [0, 1, 2, 3], DeclareOrigin);
        await SetStatusAsync(mesh, SurveyModelStatus.Ready);
        (await QueuedOfKindAsync(ProcessingJobKinds.SurveyMesh, mesh)).ShouldBe(1);

        await RunSweepAsync();

        (await QueuedReadingsAsync(read)).ShouldBe(1);
        (await QueuedReadingsAsync(failed)).ShouldBe(1);
        // The one that has been read goes on saying so, and goes on holding its rows, for as long
        // as the queue takes to reach it; the one that holds nothing is waiting.
        (await StatusAsync(read)).ShouldBe(SurveyModelStatus.Ready);
        (await CountRowsAsync(read)).ShouldBe((Stations: 3, Shots: 2));
        (await StatusAsync(failed)).ShouldBe(SurveyModelStatus.Pending);

        // Nothing queued behind the reading that was already there, and nothing for the mesh.
        (await QueuedReadingsAsync(waiting)).ShouldBe(1);
        (await QueuedOfKindAsync(ProcessingJobKinds.SurveyMesh, mesh)).ShouldBe(1);
        (await QueuedReadingsAsync(mesh)).ShouldBe(0);
        (await StatusAsync(mesh)).ShouldBe(SurveyModelStatus.Ready);

        // A sweep is nobody's request: the queue writes to whoever asked, once per job.
        (await RequestersOfQueuedReadingsAsync(read)).ShouldBe(new Guid?[] { null });

        // Started again before the queue has been worked through, it adds nothing.
        await RunSweepAsync();
        (await QueuedReadingsAsync(read)).ShouldBe(1);
        (await QueuedReadingsAsync(failed)).ShouldBe(1);
        (await QueuedReadingsAsync(waiting)).ShouldBe(1);

        // And what it queued is a reading like any other: it runs, and the model is read.
        await RunQueuedReadingAsync(read);
        (await StatusAsync(read)).ShouldBe(SurveyModelStatus.Ready);
        (await CountRowsAsync(read)).ShouldBe((Stations: 3, Shots: 2));
    }

    /// <summary>
    /// A survey tree two levels deep with a named root: one station in the root survey and two in
    /// a sub-survey. Invented, and the smallest file whose root survey has a name to record.
    /// </summary>
    private static byte[] NamedRootCave()
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Surveys =
            [
                new CaveSurvey(Id: 1, ParentId: 1, Name: "cave", Title: null),
                new CaveSurvey(Id: 2, ParentId: 1, Name: "entrance", Title: null),
            ],
            Stations =
            [
                new CaveStation { Id = 1, SurveyId = 1, Name = "0", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 2, SurveyId = 2, Name = "1", Position = new CaveVector3(10, 0, -5) },
                new CaveStation { Id = 3, SurveyId = 2, Name = "2", Position = new CaveVector3(20, 0, -9) },
            ],
            Shots =
            [
                new CaveShot { FromStationId = 1, ToStationId = 2, SurveyId = 1 },
                new CaveShot { FromStationId = 2, ToStationId = 3, SurveyId = 1 },
            ],
        };

        return LoxWriter.Write(model);
    }

    private async Task ShouldBeRefusedAsInProgressAsync(Guid modelId)
    {
        var refused = await owner.PostAsync($"/api/v1/survey-models/{modelId}/reading", null);
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, body);
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()
            .ShouldBe("survey_model.reading_in_progress");
    }

    private async Task<Guid> CreateCaveAsync(bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Reading Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadAsync(
        Guid caveId, string fileName, byte[] bytes, Action<MultipartFormDataContent>? declare = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        declare?.Invoke(form);
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-models", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static void DeclareOrigin(MultipartFormDataContent form)
    {
        form.Add(new StringContent("25.2"), "originLongitude");
        form.Add(new StringContent("45.5"), "originLatitude");
        form.Add(new StringContent("1200"), "originHeightM");
    }

    /// <summary>Uploads a survey in plain metres with the position of its zero point declared.</summary>
    private Task<Guid> UploadLocalLoxAsync(Guid caveId, byte[] bytes) =>
        UploadAsync(caveId, "Local.lox", bytes, DeclareOrigin);

    /// <summary>
    /// Uploads an invented wall mesh — two triangles, twenty metres across, in plain metres about
    /// their own zero point — with the position of that point declared.
    /// </summary>
    private Task<Guid> UploadLocalStlAsync(Guid caveId)
    {
        (float X, float Y, float Z)[][] triangles =
        [
            [(-10, -10, 0), (10, -10, 0), (-10, 10, -5)],
            [(10, -10, 0), (10, 10, -5), (-10, 10, -5)],
        ];

        var bytes = new byte[84 + (triangles.Length * 50)];
        BitConverter.TryWriteBytes(bytes.AsSpan(80), triangles.Length);
        for (var t = 0; t < triangles.Length; t++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                // Twelve bytes of exporter face normal are left zero; the reader recomputes it.
                var at = 84 + (t * 50) + 12 + (corner * 12);
                BitConverter.TryWriteBytes(bytes.AsSpan(at), triangles[t][corner].X);
                BitConverter.TryWriteBytes(bytes.AsSpan(at + 4), triangles[t][corner].Y);
                BitConverter.TryWriteBytes(bytes.AsSpan(at + 8), triangles[t][corner].Z);
            }
        }

        return UploadAsync(caveId, "Walls.stl", bytes, DeclareOrigin);
    }

    /// <summary>The model as the cave's own list answers it.</summary>
    private async Task<JsonElement> ListedAsync(Guid caveId, Guid modelId)
    {
        var response = await owner.GetAsync($"/api/v1/caves/{caveId}/survey-models");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == modelId);
    }

    /// <summary>The files derived from one stored file, with where the store keeps each.</summary>
    private async Task<List<(Guid Id, string AbsolutePath)>> DerivedFromAsync(Guid uploadId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var files = await db.StoredFiles.AsNoTracking()
            .Where(f => f.ConvertedFromFileId == uploadId)
            .Select(f => new { f.Id, f.StoragePath })
            .ToListAsync();
        return [.. files.Select(f => (f.Id, store.GetAbsolutePath(f.StoragePath)))];
    }

    private async Task<string> AbsolutePathOfAsync(Guid fileId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IFileStore>();
        return store.GetAbsolutePath(
            await db.StoredFiles.AsNoTracking().Where(f => f.Id == fileId).Select(f => f.StoragePath).SingleAsync());
    }

    private async Task<Guid?> ConvertedFileIdAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == modelId).Select(m => m.ConvertedFileId).SingleAsync();
    }

    private async Task<ProcessingJob> JobAsync(long jobId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private async Task SetJobStatusAsync(long jobId, ProcessingJobStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ProcessingJobs.Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, status));
    }

    private static async Task<JsonElement> GetModelAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/survey-models/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<JsonElement>> CenterlinesOfAsync(Guid caveId)
    {
        var response = await owner.GetAsync($"/api/v1/caves/{caveId}/centerlines");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    /// <summary>Where each station of a model is stored, by its stored name.</summary>
    private async Task<Dictionary<string, (double Longitude, double Latitude, double AltitudeM)>> PlacesAsync(
        Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stations = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == modelId)
            .ToListAsync();
        return stations.ToDictionary(
            s => s.Name,
            s => (s.Position.X, s.Position.Y, s.Position.Z));
    }

    private async Task<(int Stations, int Shots)> CountRowsAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (
            await db.SurveyStations.CountAsync(s => s.SurveyModelId == modelId),
            await db.SurveyShots.CountAsync(s => s.SurveyModelId == modelId));
    }

    private async Task<string?> RootSurveyNameAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == modelId).Select(m => m.RootSurveyName).SingleAsync();
    }

    private async Task<SurveyModelStatus> StatusAsync(Guid modelId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == modelId).Select(m => m.Status).SingleAsync();
    }

    private async Task SetStatusAsync(Guid modelId, SurveyModelStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.SurveyModels.Where(m => m.Id == modelId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status));
    }

    /// <summary>The queued jobs of one kind that are for one model.</summary>
    private async Task<List<ProcessingJob>> QueuedForAsync(string kind, Guid modelId) =>
        [.. (await QueuedJob.OfKindAsync(factory.Services, kind))
            .Where(j => j.Status == ProcessingJobStatus.Queued)
            .Where(j => JsonSerializer.Deserialize<SurveyGraphPayload>(j.Payload, JsonSerializerOptions.Web)
                ?.SurveyModelId == modelId)];

    private async Task<int> QueuedOfKindAsync(string kind, Guid modelId) =>
        (await QueuedForAsync(kind, modelId)).Count;

    private Task<int> QueuedReadingsAsync(Guid modelId) =>
        QueuedOfKindAsync(ProcessingJobKinds.SurveyGraph, modelId);

    private async Task<List<Guid?>> RequestersOfQueuedReadingsAsync(Guid modelId) =>
        [.. (await QueuedForAsync(ProcessingJobKinds.SurveyGraph, modelId)).Select(j => j.RequestedBy)];

    /// <summary>
    /// Runs the one reading waiting for a model, the way the background worker would: claimed
    /// first, so that it is the job that was queued which runs and not a second one made here.
    /// </summary>
    private Task RunQueuedReadingAsync(Guid modelId, bool expectFailure = false) =>
        RunQueuedAsync(ProcessingJobKinds.SurveyGraph, modelId, expectFailure);

    private async Task RunQueuedAsync(string kind, Guid modelId, bool expectFailure = false)
    {
        var mine = await QueuedForAsync(kind, modelId);
        mine.ShouldHaveSingleItem();

        try
        {
            await QueuedJob.RunAsync(factory.Services, mine[0].Id);
        }
        catch (Exception) when (expectFailure)
        {
            // The handler records the reason on the model and rethrows so the failure is recorded
            // against the job too; what the test looks at is the record it left on the model.
        }
    }

    /// <summary>Runs the sweep directly, standing in for the worker picking up what an operator queued.</summary>
    private async Task RunSweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyReadingBackfill);
        await handler.ExecuteAsync(
            new ProcessingJob { Kind = ProcessingJobKinds.SurveyReadingBackfill },
            CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
