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

namespace SilexGis.Api.Tests;

/// <summary>
/// Processing profiles in and out of files over real HTTP: HDMapping's own TOML reported key by
/// key against an invented dump, SilexGIS profile files for any step, round trips in both
/// formats, and who may import and export what.
/// </summary>
public sealed class ProcessingProfileTransferTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Downsample = "pointcloud.downsample";
    private const string Odometry = "hdmapping.odometry";

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly string handoffRoot;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    public ProcessingProfileTransferTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-pprofx-{Guid.NewGuid():N}");
        handoffRoot = Path.Combine(filesRoot, "scans");
        Directory.CreateDirectory(handoffRoot);
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scans:HandoffRoot"] = handoffRoot,
        }, JobWorkers.RemoveFrom);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

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

    /// <summary>A hand-written HDMapping parameter file in the program's layout, against the invented dump.</summary>
    private const string TunedToml = """
        [version_info]
        software_version = "9.8.0"
        config_version = "1.0"

        [paths]
        current_output_dir = "/home/somebody/run"

        [odometry_control]
        iterations = 12
        resolution = 0.5
        no_such_key = 3

        [misc]
        clear_color = [0.0, 0.0, 0.0]
        """;

    [Fact]
    public async Task Importing_needs_an_account_and_an_HDMapping_file_needs_the_installed_defaults()
    {
        using var anonymous = factory.CreateClient();
        (await ImportAsync(anonymous, "tuned.toml", TunedToml)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/scan-profiles/{Guid.NewGuid()}/export")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // No worker has dumped its defaults, now or before: nothing to check a file against.
        var (_, author) = await ViewerAsync("before-dump");
        await ForgetDumpAsync();
        await ShouldBeProblemAsync(await ImportAsync(author, "tuned.toml", TunedToml),
            HttpStatusCode.Conflict, ProcessingProfileTransferEndpoints.DefaultsUnknownCode);
    }

    [Fact]
    public async Task An_HDMapping_file_is_reported_key_by_key_and_saved_as_the_whole_set()
    {
        WriteDump();
        var (authorId, author) = await ViewerAsync("toml");

        // A dry run reports and stores nothing.
        var preview = await ImportAsync(author, "tuned.toml", TunedToml, dryRun: true);
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var dry = (await preview.Content.ReadFromJsonAsync<ProcessingProfileImportResultDto>(Wire))!;
        dry.Profile.ShouldBeNull();
        dry.Report.Format.ShouldBe("toml");
        dry.Report.Taken.ShouldBe(["odometry_control.iterations", "odometry_control.resolution"]);
        dry.Report.Unknown.ShouldBe(["odometry_control.no_such_key"]);
        dry.Report.DroppedSections.ShouldBe(["version_info", "paths", "misc"]);
        dry.Report.Defaulted.ShouldContain("odometry_control.step_motion");
        dry.Report.FileVersion.ShouldBe("9.8.0");
        dry.Report.InstalledVersion.ShouldBe("9.9.9");
        dry.Report.VersionDiffers.ShouldBeTrue();
        (await ListAsync(author, Odometry)).ShouldNotContain(p => p.OwnerUserId == authorId);

        var saved = await ImportAsync(author, "tuned.toml", TunedToml);
        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var profile = (await saved.Content.ReadFromJsonAsync<ProcessingProfileImportResultDto>(Wire))!.Profile!;
        profile.Name.ShouldBe("tuned");
        profile.Origin.ShouldBe("imported:tuned.toml");
        profile.ToolVersion.ShouldBe("9.9.9@feedbee");
        var control = profile.Values.GetProperty("values").GetProperty("odometry_control");
        control.GetProperty("iterations").GetInt32().ShouldBe(12);
        control.GetProperty("step_motion").GetBoolean().ShouldBeTrue();
        control.TryGetProperty("no_such_key", out _).ShouldBeFalse();
        profile.Values.GetProperty("values").TryGetProperty("paths", out _).ShouldBeFalse();

        // A value HDMapping would read as zero refuses the whole import, naming the key.
        var wrong = TunedToml.Replace("iterations = 12", "iterations = 12.0", StringComparison.Ordinal);
        await ShouldBeProblemAsync(await ImportAsync(author, "wrong.toml", wrong),
            HttpStatusCode.BadRequest, ProcessingProfileTransferEndpoints.RefusedCode);
        var report = await ImportAsync(author, "wrong.toml", wrong, dryRun: true);
        var refused = (await report.Content.ReadFromJsonAsync<ProcessingProfileImportResultDto>(Wire))!.Report;
        refused.Accepted.ShouldBeFalse();
        refused.Refused.ShouldHaveSingleItem().Key.ShouldBe("odometry_control.iterations");

        await ShouldBeProblemAsync(await ImportAsync(author, "broken.toml", "[odometry_control\niterations ="),
            HttpStatusCode.BadRequest, ProcessingProfileTransferEndpoints.UnreadableCode);
    }

    [Fact]
    public async Task An_exported_profile_imports_back_to_the_same_values_in_either_format()
    {
        WriteDump();
        var (_, author) = await ViewerAsync("roundtrip");
        var (_, other) = await ViewerAsync("roundtrip-other");
        var original = await CreateAsync(author, Odometry, "Tuned for caves", ProcessingProfileScope.Personal, null,
            """{"values":{"odometry_control":{"iterations":12,"step_motion":false,"resolution":1}}}""");

        var toml = await author.GetAsync($"/api/v1/scan-profiles/{original.Id}/export?format=toml");
        toml.StatusCode.ShouldBe(HttpStatusCode.OK);
        toml.Content.Headers.ContentType!.MediaType.ShouldBe("application/toml");
        var text = await toml.Content.ReadAsStringAsync();
        text.ShouldContain("software_version = \"9.9.9\"");
        text.ShouldContain("resolution = 1.0");
        text.ShouldNotContain("[paths]");

        var fromToml = (await (await ImportAsync(author, "tuned-for-caves.toml", text))
            .Content.ReadFromJsonAsync<ProcessingProfileImportResultDto>(Wire))!;
        fromToml.Report.Accepted.ShouldBeTrue();
        fromToml.Report.VersionDiffers.ShouldBeFalse();
        var section = fromToml.Profile!.Values.GetProperty("values").GetProperty("odometry_control");
        section.GetProperty("iterations").GetInt32().ShouldBe(12);
        section.GetProperty("step_motion").GetBoolean().ShouldBeFalse();
        section.GetProperty("resolution").GetDouble().ShouldBe(1);

        var json = await author.GetAsync($"/api/v1/scan-profiles/{original.Id}/export");
        json.StatusCode.ShouldBe(HttpStatusCode.OK);
        var file = await json.Content.ReadAsStringAsync();
        file.ShouldContain(ProcessingProfileImport.FileFormat);
        var fromJson = (await (await ImportAsync(author, "copy.json", file))
            .Content.ReadFromJsonAsync<ProcessingProfileImportResultDto>(Wire))!;
        fromJson.Profile!.Name.ShouldBe("Tuned for caves");
        fromJson.Report.Format.ShouldBe("json");
        fromJson.Profile.Values.GetProperty("values").GetProperty("odometry_control").GetProperty("iterations").GetInt32().ShouldBe(12);

        // Somebody who cannot read the profile cannot export it either.
        (await other.GetAsync($"/api/v1/scan-profiles/{original.Id}/export")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/v1/scan-profiles/{original.Id}/export")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Any_steps_profile_travels_as_a_SilexGIS_file_and_only_odometry_as_TOML()
    {
        var (_, author) = await ViewerAsync("json");
        var original = await CreateAsync(author, Downsample, "Coarse", ProcessingProfileScope.Personal, null,
            """{"method":"voxel","voxelCm":8}""");
        await ShouldBeProblemAsync(await author.GetAsync($"/api/v1/scan-profiles/{original.Id}/export?format=toml"),
            HttpStatusCode.BadRequest, ProcessingProfileTransferEndpoints.FormatUnsupportedCode);

        var file = await (await author.GetAsync($"/api/v1/scan-profiles/{original.Id}/export")).Content.ReadAsStringAsync();
        var withExtra = file.Replace("\"voxelCm\": 8", "\"voxelCm\": 8, \"colour\": \"red\"", StringComparison.Ordinal);
        var imported = await ImportAsync(author, "coarse.json", withExtra);
        imported.StatusCode.ShouldBe(HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
        var result = (await imported.Content.ReadFromJsonAsync<ProcessingProfileImportResultDto>(Wire))!;
        result.Report.Unknown.ShouldBe(["colour"]);
        result.Report.Defaulted.ShouldBe(["everyNth"]);
        result.Profile!.Values.GetProperty("voxelCm").GetInt32().ShouldBe(8);

        var outOfRange = file.Replace("\"voxelCm\": 8", "\"voxelCm\": 5000", StringComparison.Ordinal);
        await ShouldBeProblemAsync(await ImportAsync(author, "coarse.json", outOfRange),
            HttpStatusCode.BadRequest, ProcessingProfileTransferEndpoints.RefusedCode);
        await ShouldBeProblemAsync(await ImportAsync(author, "other.json", """{"format":"something-else","stepId":"x"}"""),
            HttpStatusCode.BadRequest, ProcessingProfileTransferEndpoints.NotAProfileCode);
    }

    [Fact]
    public async Task Importing_an_installation_profile_needs_the_right_to_create_one()
    {
        WriteDump();
        var (_, plain) = await ViewerAsync("import-plain");
        var (managerId, manager) = await ViewerAsync("import-manager");
        await GrantAsync(managerId, AccessAction.Create);

        (await ImportAsync(plain, "tuned.toml", TunedToml, scope: "installation")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ImportAsync(manager, "tuned.toml", TunedToml, scope: "installation")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private async Task ForgetDumpAsync()
    {
        var dump = Path.Combine(handoffRoot, "defaults", "hdmapping.odometry.json");
        if (File.Exists(dump))
        {
            File.Delete(dump);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        await db.ScanStepDefaults.Where(d => d.StepId == Odometry).ExecuteDeleteAsync();
    }

    private static Task<HttpResponseMessage> ImportAsync(
        HttpClient client, string fileName, string content, bool dryRun = false, string scope = "personal") =>
        client.PostAsJsonAsync("/api/v1/scan-profiles/import", new { fileName, content, scope, dryRun });

    /// <summary>An invented dump in the shape the HDMapping worker writes; no real program's values.</summary>
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
                "misc": { "clear_color": [0.1, 0.2, 0.3] },
                "odometry_control": { "iterations": 30, "step_motion": true, "resolution": 0.3, "label": "fixed" },
                "filtering": { "min_distance": 0.5, "ceiling": "inf" }
              },
              "toml": "[odometry_control]\niterations = 30\n"
            }
            """);
    }

    private async Task<(Guid Id, HttpClient Client)> ViewerAsync(string tag) => await UserAsync(GlobalRoles.Viewer, tag);

    private async Task<(Guid Id, HttpClient Client)> UserAsync(string role, string tag)
    {
        var email = $"pprofx-{tag}-{suffix}@t.local";
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

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string stepId, string name, ProcessingProfileScope scope, Guid? groupId, string valuesJson) =>
        client.PostAsJsonAsync("/api/v1/scan-profiles", new
        {
            stepId,
            name,
            scope = scope switch
            {
                ProcessingProfileScope.Installation => "installation",
                ProcessingProfileScope.CavingGroup => "cavingGroup",
                _ => "personal",
            },
            cavingGroupId = groupId,
            values = JsonDocument.Parse(valuesJson).RootElement,
        });

    private static async Task<ProcessingProfileDto> CreateAsync(
        HttpClient client, string stepId, string name, ProcessingProfileScope scope, Guid? groupId, string valuesJson)
    {
        var response = await PostAsync(client, stepId, name, scope, groupId, valuesJson);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProcessingProfileDto>(Wire))!;
    }

    private static async Task<List<ProcessingProfileDto>> ListAsync(HttpClient client, string stepId) =>
        (await client.GetFromJsonAsync<List<ProcessingProfileDto>>($"/api/v1/scan-profiles?stepId={stepId}", Wire))!;

    private static async Task<ScanStepParameterSchemaDto> SchemaAsync(HttpClient client, string stepId) =>
        (await client.GetFromJsonAsync<ScanStepParameterSchemaDto>($"/api/v1/scan-profiles/schemas/{stepId}", Wire))!;

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        body.ShouldContain(code);
    }
}
