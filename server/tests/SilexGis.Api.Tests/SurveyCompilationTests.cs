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
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Storing what a compilation log reports, end to end: that archiving one queues the reading, that
/// the figures land with enough of the run attached to say which compilation produced them, that a
/// later reading replaces an earlier one rather than joining it, that a compilation which failed is
/// stored as a failed compilation rather than as a survey with no loops, and that the whole lot is
/// withheld from a caller who may not place the cave.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SurveyCompilationTests : IAsyncLifetime, IDisposable
{
    private const string FixtureName = "therion-compiler.log";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;
    private HttpClient reader = null!;
    private Guid readerId;
    private long caveTypeId;

    public SurveyCompilationTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-comp-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Files:Root"] = filesRoot,
                ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            },
            JobWorkers.RemoveFrom);
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"svc-own-{suffix}@t.local");
        readerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"svc-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"svc-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"svc-read-{suffix}@t.local");
    }

    [Fact]
    public async Task Archiving_a_log_queues_the_reading_and_lands_the_figures_with_the_run_attached()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        var sourceId = await ArchiveLogAsync(caveId, "compilation.log", LogText());

        // Queued, not read: the reading happens off the request thread, and until it has the record
        // says so rather than pretending the survey has no loops.
        var queued = await OneAsync(owner, caveId);
        queued.GetProperty("status").GetString().ShouldBe("pending");
        queued.GetProperty("outcome").ValueKind.ShouldBe(JsonValueKind.Null);
        queued.GetProperty("loops").GetArrayLength().ShouldBe(0);
        queued.GetProperty("surveySourceId").GetGuid().ShouldBe(sourceId);
        queued.GetProperty("logVersionNumber").GetInt32().ShouldBe(1);

        await RunReadingAsync(caveId);

        var read = await OneAsync(owner, caveId);
        read.GetProperty("status").GetString().ShouldBe("read");
        read.GetProperty("outcome").GetString().ShouldBe("succeeded");
        read.GetProperty("readError").ValueKind.ShouldBe(JsonValueKind.Null);

        // The run the figures came from, carried with them: which bytes, which revision of the
        // archive entry, when they were read here and what read them. The compiler's log has no
        // timestamp of its own, so when it was read is the only date there is.
        read.GetProperty("readAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        read.GetProperty("logFileId").GetGuid().ShouldNotBe(Guid.Empty);
        read.GetProperty("logVersionNumber").GetInt32().ShouldBe(1);
        read.GetProperty("compilerVersion").GetString().ShouldNotBeNullOrWhiteSpace();
        read.GetProperty("compilationSeconds").GetInt32().ShouldBeGreaterThan(0);

        // The loop table, in the compiler's own order and with both of its measures. The figures
        // are the fixture's real ones, so the reader has to agree with a compilation that happened.
        var loops = read.GetProperty("loops").EnumerateArray().ToList();
        loops.Count.ShouldBe(26);
        loops.Select(l => l.GetProperty("ordinal").GetInt32()).ShouldBe(Enumerable.Range(0, 26));
        loops.ShouldAllBe(l => l.GetProperty("stations").GetString()!.Length > 0);
        read.GetProperty("averageLoopErrorPercent").GetDouble().ShouldBe(1.49, 0.001);

        // The two measures do not order the survey the same way, and this fixture proves it: sorted
        // by ratio and sorted by closing distance are different lists of the same loops. Which is
        // why both are stored and why a panel may not show one under the other's name.
        var byRatio = loops.OrderByDescending(l => l.GetProperty("relativeErrorPercent").GetDouble())
            .Select(l => l.GetProperty("ordinal").GetInt32()).ToList();
        var byDistance = loops.OrderByDescending(l => l.GetProperty("absoluteErrorM").GetDouble())
            .Select(l => l.GetProperty("ordinal").GetInt32()).ToList();
        byRatio.ShouldNotBe(byDistance);

        // Concretely: some loop closes to a worse ratio than another while missing by less distance,
        // because it is the shorter loop. That inversion is the whole reason a single figure lies.
        loops.Any(worse => loops.Any(other =>
            worse.GetProperty("relativeErrorPercent").GetDouble() > other.GetProperty("relativeErrorPercent").GetDouble()
            && worse.GetProperty("absoluteErrorM").GetDouble() < other.GetProperty("absoluteErrorM").GetDouble()))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task A_corrected_log_uploaded_as_a_new_revision_replaces_the_figures_and_says_which_run_is_shown()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        // Taken before the first archive, because both uploads are being counted: the one that
        // lodges the log and the revision that corrects it. Every class in this assembly shares one
        // database and they run in sequence, so the job table already holds whatever ran before —
        // what is under test is how many runs *these two uploads* add, which is a difference and
        // never a total. (The reading below is executed by hand rather than queued, so it adds
        // none of its own; that is the point of asserting the queue at all.)
        var jobsBefore = await QueuedReadingCountAsync();

        var sourceId = await ArchiveLogAsync(caveId, "compilation.log", LogText());

        await RunReadingAsync(caveId);
        (await OneAsync(owner, caveId)).GetProperty("loops").GetArrayLength().ShouldBe(26);
        var first = (await OneAsync(owner, caveId)).GetProperty("readAt").GetDateTimeOffset();

        // A corrected log arrives the way the archive says corrections arrive: as a later revision
        // of the same entry, through the route that uploads revisions. Driven through that route
        // rather than staged in the database, because the guarantee under test is that uploading
        // one actually re-reads the figures — a hand-edited record would assert nothing about the
        // path a surveyor takes.
        using var revision = BuildForm("compilation.log", HaltedLogText());
        var uploaded = await owner.PostAsync($"/api/v1/files/{await CurrentFileIdAsync(sourceId)}/versions", revision);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        var revisedFileId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Between the upload and the reading the record shows the new revision and no figures at
        // all. The superseded run's numbers under the corrected run's revision number would be one
        // run's provenance over another run's figures, which is the thing this must never do.
        var queued = await OneAsync(owner, caveId);
        queued.GetProperty("status").GetString().ShouldBe("pending");
        queued.GetProperty("logVersionNumber").GetInt32().ShouldBe(2);
        queued.GetProperty("logFileId").GetGuid().ShouldBe(revisedFileId);
        queued.GetProperty("loops").GetArrayLength().ShouldBe(0);
        queued.GetProperty("outcome").ValueKind.ShouldBe(JsonValueKind.Null);
        queued.GetProperty("loopCount").ValueKind.ShouldBe(JsonValueKind.Null);
        queued.GetProperty("averageLoopErrorPercent").ValueKind.ShouldBe(JsonValueKind.Null);

        await RunReadingAsync(caveId);

        var shown = await OneAsync(owner, caveId);

        // Replaced, not joined. Twenty-six rows left standing under a run that reported none would
        // be one run's provenance over another run's figures, and nothing would say so.
        shown.GetProperty("loops").GetArrayLength().ShouldBe(0);
        shown.GetProperty("logVersionNumber").GetInt32().ShouldBe(2);
        shown.GetProperty("logFileId").GetGuid().ShouldBe(revisedFileId);
        shown.GetProperty("readAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(first);

        // And the run that produced no rows is recorded as the run it was: one that stopped, naming
        // the step it stopped in. A survey that simply has no loops reports the same empty table
        // under a different outcome, and the two must never read alike.
        shown.GetProperty("status").GetString().ShouldBe("read");
        shown.GetProperty("outcome").GetString().ShouldBe("failed");
        shown.GetProperty("incompleteStage").GetString().ShouldNotBeNullOrWhiteSpace();

        // Counted in the database as well as on the route: rows left behind by the run before would
        // be invisible to a reader and still be there. And the re-reading was queued as a job, not
        // only performed here by hand.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyCompilations.CountAsync(c => c.CaveFeatureId == caveId)).ShouldBe(1);
        (await db.SurveyCompilationLoops
            .CountAsync(l => db.SurveyCompilations
                .Any(c => c.Id == l.SurveyCompilationId && c.CaveFeatureId == caveId)))
            .ShouldBe(0);
        (await QueuedReadingCountAsync() - jobsBefore).ShouldBe(2);
    }

    /// <summary>
    /// Runs queued to re-read a compilation log, across the whole shared database.
    /// </summary>
    /// <remarks>
    /// Only ever meaningful as a difference across an action — see the caller. A total would count
    /// every sibling test's rows and would pass or fail on the order the classes happened to run in.
    /// </remarks>
    private async Task<int> QueuedReadingCountAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.SurveyCompilation);
    }

    [Fact]
    public async Task A_file_that_is_not_a_compilation_log_is_recorded_as_unreadable_rather_than_as_a_failure()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        _ = await ArchiveLogAsync(
            caveId, "notes.log", Encoding.UTF8.GetBytes("Trip notes.\nWet. Cold. Went home.\n"));

        await Should.ThrowAsync<Exception>(() => RunReadingAsync(caveId));

        var record = await OneAsync(owner, caveId);

        // "Nobody could read this file" is not "the compilation failed": the second is a claim about
        // the survey and this application is not entitled to make it from a file it could not open.
        record.GetProperty("status").GetString().ShouldBe("unreadable");
        record.GetProperty("outcome").ValueKind.ShouldBe(JsonValueKind.Null);
        record.GetProperty("readError").GetString().ShouldNotBeNullOrWhiteSpace();
        record.GetProperty("loops").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task An_archived_source_that_is_not_a_log_gets_no_compilation_record()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);

        using var form = BuildForm("cave.svx", SurvexText());
        (await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", form))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-compilations"))
            .GetArrayLength().ShouldBe(0);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.SurveyCompilations.CountAsync(c => c.CaveFeatureId == caveId)).ShouldBe(0);
    }

    [Fact]
    public async Task Compilations_of_a_protected_cave_are_withheld_without_exact_location()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        _ = await ArchiveLogAsync(caveId, "compilation.log", LogText());
        await RunReadingAsync(caveId);

        // The reader holds no grant of any kind on this cave beyond what being signed in gives:
        // the cave itself stays readable, and how well its survey closes does not follow it out.
        (await reader.GetAsync($"/api/v1/caves/{caveId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-compilations"))
            .GetArrayLength().ShouldBe(0);

        // Withheld whole rather than emptied out: no outcome, no counts, no lengths, not the fact
        // that a log was ever archived. The owner never lost sight of it, and an explicit grant of
        // the exact-location right flips it visible — which is what makes the refusal above a
        // refusal rather than an empty cave.
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-compilations"))
            .GetArrayLength().ShouldBe(1);

        await GrantExactViewAsync(caveId);

        var granted = await reader.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-compilations");
        granted.GetArrayLength().ShouldBe(1);
        granted[0].GetProperty("loops").GetArrayLength().ShouldBe(26);
    }

    [Fact]
    public async Task The_caves_timeline_does_not_hand_over_what_the_compilation_route_withholds()
    {
        // A compilation is audited into its cave's timeline, so the timeline is a second server
        // surface for the same figures. A caller refused them on the route must be refused them
        // here too, or the withholding is decorative.
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: true);
        _ = await ArchiveLogAsync(caveId, "compilation.log", LogText());
        await RunReadingAsync(caveId);

        var withheld = await reader.GetAsync($"/api/v1/history?entityType=feature&entityId={caveId}");
        if (withheld.StatusCode == HttpStatusCode.OK)
        {
            var rows = (await withheld.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("items").EnumerateArray()
                .Where(e => e.GetProperty("entityType").GetString() == nameof(SurveyCompilation))
                .ToList();

            // The event may stand — that a compilation happened is not the secret — but nothing it
            // carries may be a figure. Asserted over the whole payload rather than field by field,
            // so a column added later is covered without anybody remembering to add it here.
            foreach (var row in rows)
            {
                row.GetProperty("changes").ValueKind.ShouldBe(
                    JsonValueKind.Null,
                    "a compilation's figures reached a caller who may not place the cave");
                row.GetProperty("redactedProperties").GetArrayLength().ShouldBeGreaterThan(0);
            }
        }
        else
        {
            withheld.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        }

        // And the owner, who may place the cave, still sees them — which is what makes the above a
        // refusal rather than an empty timeline.
        var seen = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/history?entityType=feature&entityId={caveId}");
        seen.GetProperty("items").EnumerateArray()
            .Any(e => e.GetProperty("entityType").GetString() == nameof(SurveyCompilation))
            .ShouldBeTrue("the compilation is audited into its cave's timeline");
    }

    [Fact]
    public async Task A_private_caves_compilations_are_not_disclosed_to_outsiders()
    {
        var caveId = await CreateCaveAsync(visibility: "private", locationProtected: false);
        _ = await ArchiveLogAsync(caveId, "compilation.log", LogText());

        // Not an empty list here: an unreadable cave answers as no cave at all, because an empty
        // list would confirm that the cave exists.
        (await reader.GetAsync($"/api/v1/caves/{caveId}/survey-compilations"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-compilations"))
            .GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Compilations_are_not_answered_to_a_caller_who_is_not_signed_in()
    {
        var caveId = await CreateCaveAsync(visibility: "authenticated", locationProtected: false);
        _ = await ArchiveLogAsync(caveId, "compilation.log", LogText());

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/caves/{caveId}/survey-compilations");
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    private static byte[] LogText() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName));

    /// <summary>
    /// The same log cut off partway through, the way a run that died leaves one: the step it was in
    /// never printed that it finished, and nothing after it — the loop-error table included — was
    /// ever written.
    /// </summary>
    private static byte[] HaltedLogText()
    {
        var lines = Encoding.UTF8.GetString(LogText()).ReplaceLineEndings("\n").Split('\n');
        var stopped = Array.FindIndex(
            lines, l => l.StartsWith("calculating station coordinates", StringComparison.Ordinal));
        stopped.ShouldBeGreaterThan(0, "the fixture no longer contains the step this test cuts at");

        lines[stopped] = lines[stopped].Replace(" done", string.Empty, StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(string.Join('\n', lines[..(stopped + 1)]));
    }

    /// <summary>The one compilation record of a cave, as the route hands it back.</summary>
    private static async Task<JsonElement> OneAsync(HttpClient client, Guid caveId)
    {
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{caveId}/survey-compilations");
        body.GetArrayLength().ShouldBe(1);
        return body[0];
    }

    private async Task<Guid> ArchiveLogAsync(Guid caveId, string fileName, byte[] bytes)
    {
        using var form = BuildForm(fileName, bytes);
        var created = await owner.PostAsync($"/api/v1/caves/{caveId}/survey-sources", form);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Runs the queued reading for a cave's log, the way the worker would.</summary>
    private async Task RunReadingAsync(Guid caveId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
            .Single(h => h.Kind == ProcessingJobKinds.SurveyCompilation);

        var id = await db.SurveyCompilations.Where(c => c.CaveFeatureId == caveId)
            .Select(c => c.Id).SingleAsync();

        await handler.ExecuteAsync(
            new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyCompilation,
                Payload = JsonSerializer.Serialize(
                    new SurveyCompilationPayload(id), JsonSerializerOptions.Web),
            },
            CancellationToken.None);
    }

    /// <summary>The stored file the archived source currently resolves to.</summary>
    private async Task<Guid> CurrentFileIdAsync(Guid sourceId)
    {
        var caveSources = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/caves/{await CaveOfSourceAsync(sourceId)}/survey-sources");
        return caveSources.EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == sourceId)
            .GetProperty("fileId").GetGuid();
    }

    private async Task<Guid> CaveOfSourceAsync(Guid sourceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SurveySources.Where(s => s.Id == sourceId)
            .Select(s => s.CaveFeatureId).SingleAsync();
    }

    private async Task GrantExactViewAsync(Guid featureId)
    {
        var grant = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = readerId,
                    effect = "allow",
                    actions = "read, viewExactLocation",
                    scopeKind = "object",
                },
            },
        });
        grant.StatusCode.ShouldBe(HttpStatusCode.OK, await grant.Content.ReadAsStringAsync());
    }

    private async Task<Guid> CreateCaveAsync(string visibility, bool locationProtected)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Closure Cave {Guid.NewGuid():N}"[..30],
            caveTypeId,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static byte[] SurvexText() => Encoding.UTF8.GetBytes(
        "*begin main\n*data normal from to tape compass clino\n1 2 12.50 145 -3\n*end main\n");

    private static MultipartFormDataContent BuildForm(
        string fileName, byte[] bytes, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
            // Temp files; best effort.
        }
    }
}
