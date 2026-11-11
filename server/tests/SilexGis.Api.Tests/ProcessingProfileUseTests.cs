// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Features.ScanProfiles;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// Using processing profiles over real HTTP: the built-ins every installation ships, changed only
/// by saving a copy; a raw recording opened and rerun with a profile, the upload storing the
/// profile's values laid over the defaults the worker dumped; and the profile used last remembered
/// per person and step.
/// </summary>
/// <remarks>
/// The dump and every recording here are invented by the test; nothing comes from a real scanner
/// or a real HDMapping build.
/// </remarks>
public sealed class ProcessingProfileUseTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Odometry = HdmappingOdometryStep.StepId;
    private const string Estimate = PointCloudEstimateStep.StepId;

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private long caveTypeId;

    public ProcessingProfileUseTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-ppuse-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "scans");
        Directory.CreateDirectory(handoffRoot);
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:HandoffRoot"] = handoffRoot,
        }, JobWorkers.RemoveFrom);
    }

    public async ValueTask InitializeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
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
            // A scratch directory left behind is harmless.
        }
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/scan-profiles/remembered")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/scan-profiles/{BuiltInProcessingProfiles.FastPreviewId}/copy", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_built_ins_are_offered_to_everyone_and_changed_only_by_saving_a_copy()
    {
        // The class's database keeps the last dump any test wrote, so this test writes its own and
        // edits a key that dump has.
        WriteDump();
        var (viewerId, viewer) = await UserAsync(GlobalRoles.Viewer, "viewer");

        var listed = await ListAsync(viewer, Odometry);
        foreach (var builtIn in BuiltInProcessingProfiles.All.Where(p => p.StepId == Odometry))
        {
            var row = listed.Single(p => p.Id == builtIn.Id);
            row.BuiltIn.ShouldBeTrue();
            row.Scope.ShouldBe(ProcessingProfileScope.Installation);
            row.MayEdit.ShouldBeFalse();
            row.MayDelete.ShouldBeFalse();
        }

        listed.Single(p => p.Id == BuiltInProcessingProfiles.CaveNarrowPassagesId).Description!.ShouldContain("not yet measured on a cave recording");

        await ShouldBeProblemAsync(
            await viewer.PutAsJsonAsync($"/api/v1/scan-profiles/{BuiltInProcessingProfiles.FastPreviewId}", new { name = "Mine", values = new { } }),
            HttpStatusCode.Conflict, ProcessingProfileEndpoints.BuiltInCode);

        // Editing a built-in is saving a copy with the new values: the copy is the caller's own.
        var edited = await viewer.PostAsJsonAsync($"/api/v1/scan-profiles/{BuiltInProcessingProfiles.FastPreviewId}/copy", new
        {
            values = Json("""{"values":{"odometry_control":{"iterations":200}}}"""),
        });
        edited.StatusCode.ShouldBe(HttpStatusCode.Created, await edited.Content.ReadAsStringAsync());
        var copy = (await edited.Content.ReadFromJsonAsync<ProcessingProfileDto>(Wire))!;
        copy.Name.ShouldBe("Fast preview" + ProcessingProfileUseEndpoints.CopySuffix);
        copy.Scope.ShouldBe(ProcessingProfileScope.Personal);
        copy.OwnerUserId.ShouldBe(viewerId);
        copy.BuiltIn.ShouldBeFalse();
        copy.Origin.ShouldBe(ProcessingProfileOrigins.Edited);
        copy.MayEdit.ShouldBeTrue();
        copy.Values.GetProperty("values").GetProperty("odometry_control").GetProperty("iterations").GetInt32().ShouldBe(200);

        // A copy kept for the installation needs the scan-profiles right; given it, it is made.
        await ShouldBeProblemAsync(
            await viewer.PostAsJsonAsync($"/api/v1/scan-profiles/{BuiltInProcessingProfiles.HdmappingDefaultsId}/copy",
                new { name = "Club defaults", scope = "installation" }),
            HttpStatusCode.Forbidden, ProcessingProfileEndpoints.ForbiddenCode);
        await GrantAsync(viewerId, AccessAction.Create);
        var shared = await viewer.PostAsJsonAsync($"/api/v1/scan-profiles/{BuiltInProcessingProfiles.HdmappingDefaultsId}/copy",
            new { name = "Club defaults", scope = "installation" });
        shared.StatusCode.ShouldBe(HttpStatusCode.Created, await shared.Content.ReadAsStringAsync());
        var sharedCopy = (await shared.Content.ReadFromJsonAsync<ProcessingProfileDto>(Wire))!;
        sharedCopy.Scope.ShouldBe(ProcessingProfileScope.Installation);
        sharedCopy.BuiltIn.ShouldBeFalse();
    }

    [Fact]
    public async Task A_profile_the_caller_cannot_read_is_neither_copied_nor_used()
    {
        var (_, author) = await UserAsync(GlobalRoles.Editor, "author");
        var (_, other) = await UserAsync(GlobalRoles.Editor, "other");
        var mine = await CreateAsync(author, Estimate, "Mine", """{"voxelsCm":[3]}""");

        // Somebody with no grant over another's personal profile: absent, not forbidden.
        (await other.PostAsJsonAsync($"/api/v1/scan-profiles/{mine.Id}/copy", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherCave = await CreateCaveAsync(other);
        await ShouldBeProblemAsync(await OpenAsync(other, otherCave, mine.Id), HttpStatusCode.BadRequest, ProcessingProfileResolver.UnknownCode);

        // Its author does both.
        (await author.PostAsJsonAsync($"/api/v1/scan-profiles/{mine.Id}/copy", new { })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var authorCave = await CreateCaveAsync(author);
        var opened = await OpenAsync(author, authorCave, mine.Id);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_upload_stores_the_profiles_values_over_the_dumped_defaults_and_remembers_the_choice()
    {
        WriteDump();
        var (_, owner) = await UserAsync(GlobalRoles.Editor, "owner");
        var profile = await CreateAsync(owner, Odometry, "Twelve iterations", """{"values":{"odometry_control":{"iterations":12}}}""");
        var caveId = await CreateCaveAsync(owner);

        var opened = await OpenAsync(owner, caveId, profile.Id, BuiltInProcessingProfiles.HdmappingDefaultsId);
        await ShouldBeProblemAsync(opened, HttpStatusCode.BadRequest, ProcessingProfileResolver.StepTwiceCode);

        opened = await OpenAsync(owner, caveId, profile.Id);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var uploadId = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var values = (await OdometryAsync(uploadId)).GetProperty("values");
        values.GetProperty("odometry_control").GetProperty("iterations").GetInt32().ShouldBe(12);
        // Every other key of the dump is written out, so the upload records the whole set.
        values.GetProperty("odometry_control").GetProperty("resolution").GetDouble().ShouldBe(0.3);
        values.GetProperty("filtering").GetProperty("min_distance").GetDouble().ShouldBe(0.5);

        var remembered = (await owner.GetFromJsonAsync<List<ProcessingProfileChoiceDto>>("/api/v1/scan-profiles/remembered", Wire))!;
        var choice = remembered.ShouldHaveSingleItem();
        choice.StepId.ShouldBe(Odometry);
        choice.ProfileId.ShouldBe(profile.Id);

        // Editing the profile afterwards does not change what the upload holds.
        (await owner.PutAsJsonAsync($"/api/v1/scan-profiles/{profile.Id}",
            new { name = "Twelve", values = Json("""{"values":{"odometry_control":{"iterations":40}}}""") })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OdometryAsync(uploadId)).GetProperty("values").GetProperty("odometry_control").GetProperty("iterations").GetInt32().ShouldBe(12);

        // A rerun with another profile resolves it into the recipe in turn and is remembered instead.
        await SetStatusAsync(uploadId, SurveyUploadStatus.Failed);
        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/rerun",
            new { profiles = new[] { BuiltInProcessingProfiles.HdmappingDefaultsId } });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());
        (await OdometryAsync(uploadId)).GetProperty("values").GetProperty("odometry_control").GetProperty("iterations").GetInt32().ShouldBe(30);
        remembered = (await owner.GetFromJsonAsync<List<ProcessingProfileChoiceDto>>($"/api/v1/scan-profiles/remembered?stepId={Odometry}", Wire))!;
        remembered.ShouldHaveSingleItem().ProfileId.ShouldBe(BuiltInProcessingProfiles.HdmappingDefaultsId);
    }

    [Fact]
    public async Task A_rerun_with_a_display_choice_keeps_the_values_a_profile_set_for_the_other_steps()
    {
        WriteDump();
        var (_, owner) = await UserAsync(GlobalRoles.Editor, "keeper");
        var profile = await CreateAsync(owner, Estimate, "Three cm", """{"voxelsCm":[3]}""");
        var caveId = await CreateCaveAsync(owner);
        var opened = await OpenAsync(owner, caveId, profile.Id);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        var uploadId = (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var before = await StepAsync(uploadId, Estimate);
        before.GetProperty("voxelsCm").GetRawText().ShouldBe("[3]");
        var odometryBefore = await StepAsync(uploadId, Odometry);

        await SetStatusAsync(uploadId, SurveyUploadStatus.Failed);
        var rerun = await owner.PostAsJsonAsync($"/api/v1/survey-uploads/{uploadId}/rerun",
            new { display = new { method = "voxel", voxelCm = 2 } });
        rerun.StatusCode.ShouldBe(HttpStatusCode.Accepted, await rerun.Content.ReadAsStringAsync());

        (await StepAsync(uploadId, Estimate)).GetProperty("voxelsCm").GetRawText().ShouldBe("[3]");
        (await StepAsync(uploadId, PointCloudDownsampleStep.StepId)).GetProperty("method").GetString().ShouldBe("voxel");
        JsonElement.DeepEquals(await StepAsync(uploadId, Odometry), odometryBefore).ShouldBeTrue();
    }

    [Fact]
    public async Task A_profile_that_does_not_fit_the_upload_is_refused_with_its_reason()
    {
        WriteDump();
        var (ownerId, owner) = await UserAsync(GlobalRoles.Editor, "refused");
        var caveId = await CreateCaveAsync(owner);

        // Written for another version: a key this dump does not have. Saved before the dump
        // existed, so only the use catches it.
        var stale = await SeedPersonalAsync(ownerId, Odometry, """{"values":{"odometry_control":{"gone_key":1}}}""");
        await ShouldBeProblemAsync(await OpenAsync(owner, caveId, stale), HttpStatusCode.BadRequest, ProcessingProfileResolver.ValuesInvalidCode);

        var thinning = await CreateAsync(owner, PointCloudDownsampleStep.StepId, "Thin", """{"method":"everyNth","everyNth":4}""");
        await ShouldBeProblemAsync(await OpenAsync(owner, caveId, thinning.Id), HttpStatusCode.BadRequest, ProcessingProfileResolver.DisplayStepCode);

        // Nothing refused is remembered.
        (await owner.GetFromJsonAsync<List<ProcessingProfileChoiceDto>>("/api/v1/scan-profiles/remembered", Wire))!.ShouldBeEmpty();
    }

    private void WriteDump()
    {
        var folder = Path.Combine(handoffRoot, "defaults");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "hdmapping.odometry.json"), """
            {
              "step": "hdmapping.odometry",
              "tool": "hdmapping",
              "version": "9.9.9@feedbee",
              "softwareVersion": "9.9.9",
              "commit": "feedbee",
              "writtenAt": "2026-11-08T10:00:00Z",
              "values": {
                "version_info": { "software_version": "9.9.9", "config_version": "1.0" },
                "paths": { "current_output_dir": "/work" },
                "odometry_control": { "iterations": 30, "step_motion": true, "resolution": 0.3 },
                "filtering": { "min_distance": 0.5 }
              },
              "toml": "[odometry_control]\niterations = 30\n"
            }
            """);
    }

    private async Task<(Guid Id, HttpClient Client)> UserAsync(string role, string tag)
    {
        var email = $"ppuse-{tag}-{suffix}@t.local";
        var id = await AuthHelper.CreateUserAsync(factory, role, email);
        return (id, await AuthHelper.BearerClientAsync(factory, email));
    }

    private async Task GrantAsync(Guid userId, AccessAction actions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.ScanProfiles,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = AccessScopeKind.All,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A personal profile written straight to the database, past the schema check a save makes.</summary>
    private async Task<Guid> SeedPersonalAsync(Guid ownerId, string stepId, string valuesJson)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var profile = new ProcessingProfile
        {
            StepId = stepId,
            Name = "Older version",
            Scope = ProcessingProfileScope.Personal,
            OwnerUserId = ownerId,
            Values = valuesJson,
        };
        db.ProcessingProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private async Task SetStatusAsync(Guid uploadId, SurveyUploadStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.SurveyUploads.Where(u => u.Id == uploadId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, status));
    }

    private async Task<JsonElement> StepAsync(Guid uploadId, string stepId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var options = await db.SurveyUploads.AsNoTracking().Where(u => u.Id == uploadId).Select(u => u.Options).SingleAsync();
        return ScanRecipes.FromOptionsJson(options).ShouldNotBeNull().Steps.Single(s => s.StepId == stepId).Parameters;
    }

    private async Task<JsonElement> OdometryAsync(Guid uploadId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var options = await db.SurveyUploads.AsNoTracking().Where(u => u.Id == uploadId).Select(u => u.Options).SingleAsync();
        var recipe = ScanRecipes.FromOptionsJson(options).ShouldNotBeNull();
        return recipe.Steps.Single(s => s.StepId == Odometry).Parameters;
    }

    private async Task<Guid> CreateCaveAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Profile Cave {Guid.NewGuid():N}"[..28],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, Guid caveId, params Guid[] profiles) =>
        client.PostAsJsonAsync("/api/v1/survey-uploads", new
        {
            source = "mandeye",
            cave = new { id = caveId },
            client = new { name = "silexgis tests", version = "1" },
            surveyTitle = "Scan",
            files = new[]
            {
                new { name = "lidar0000.laz", size = 10L, role = "archive" },
                new { name = "imu0000.csv", size = 10L, role = "archive" },
            },
            profiles,
        });

    private static async Task<ProcessingProfileDto> CreateAsync(HttpClient client, string stepId, string name, string valuesJson)
    {
        var response = await client.PostAsJsonAsync("/api/v1/scan-profiles", new
        {
            stepId,
            name,
            scope = "personal",
            values = Json(valuesJson),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProcessingProfileDto>(Wire))!;
    }

    private static async Task<List<ProcessingProfileDto>> ListAsync(HttpClient client, string stepId) =>
        (await client.GetFromJsonAsync<List<ProcessingProfileDto>>($"/api/v1/scan-profiles?stepId={stepId}", Wire))!;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        body.ShouldContain(code);
    }
}
