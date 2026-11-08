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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Processing profiles over real HTTP: who sees which, who may change which, the values checked
/// against the step's schema, and the odometry schema following the defaults the worker dumped —
/// with the copy the application keeps answering once the worker's file is gone.
/// </summary>
public sealed class ProcessingProfileTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Estimate = "pointcloud.estimate";
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

    public ProcessingProfileTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-pprof-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/scan-profiles")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/scan-profiles/schemas/{Estimate}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_personal_profile_is_its_authors_alone_to_see_and_change()
    {
        var (_, author) = await ViewerAsync("author");
        var (_, other) = await ViewerAsync("other");

        var created = await CreateAsync(author, Estimate, "Mine", ProcessingProfileScope.Personal, null, """{"voxelsCm":[2,4]}""");
        created.Origin.ShouldBe(ProcessingProfileOrigins.Edited);
        created.MayEdit.ShouldBeTrue();

        (await author.GetAsync($"/api/v1/scan-profiles/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(author, Estimate)).ShouldContain(p => p.Id == created.Id);

        // Another account with no grant: absent everywhere, not forbidden.
        (await other.GetAsync($"/api/v1/scan-profiles/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(other, Estimate)).ShouldNotContain(p => p.Id == created.Id);
        (await other.PutAsJsonAsync($"/api/v1/scan-profiles/{created.Id}", new { name = "Taken", values = new { } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/v1/scan-profiles/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var renamed = await author.PutAsJsonAsync($"/api/v1/scan-profiles/{created.Id}",
            new { name = "Mine, renamed", description = "coarser", values = JsonDocument.Parse("""{"voxelsCm":[5]}""").RootElement });
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await renamed.Content.ReadFromJsonAsync<ProcessingProfileDto>(Wire))!;
        after.Name.ShouldBe("Mine, renamed");
        after.Values.GetProperty("voxelsCm")[0].GetDouble().ShouldBe(5);

        (await author.DeleteAsync($"/api/v1/scan-profiles/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await author.GetAsync($"/api/v1/scan-profiles/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Installation_profiles_need_the_right_to_write_and_are_read_by_everyone()
    {
        var (_, plain) = await ViewerAsync("plain");
        var (managerId, manager) = await ViewerAsync("manager");
        var (_, reader) = await ViewerAsync("reader");

        // Without the right: refused.
        (await PostAsync(plain, Estimate, "Shared", ProcessingProfileScope.Installation, null, """{"voxelsCm":[3]}"""))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await GrantAsync(managerId, AccessAction.Create | AccessAction.Write);
        var shared = await CreateAsync(manager, Estimate, "Shared", ProcessingProfileScope.Installation, null, """{"voxelsCm":[3]}""");

        // Everyone reads it; one who holds no right may not change or delete it.
        var seen = (await ListAsync(reader, Estimate)).Single(p => p.Id == shared.Id);
        seen.MayEdit.ShouldBeFalse();
        (await reader.PutAsJsonAsync($"/api/v1/scan-profiles/{shared.Id}", new { name = "Mine now", values = new { } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/v1/scan-profiles/{shared.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The manager may edit it but, holding no Delete, not delete it.
        (await manager.PutAsJsonAsync($"/api/v1/scan-profiles/{shared.Id}", new { name = "Shared, edited", values = new { } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.DeleteAsync($"/api/v1/scan-profiles/{shared.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_group_profile_is_read_by_the_groups_members_and_by_holders_of_read()
    {
        var groupId = await NewGroupAsync();
        var (managerId, manager) = await ViewerAsync("gmanager");
        var (memberId, member) = await ViewerAsync("member");
        var (_, outsider) = await ViewerAsync("outsider");
        var (overseerId, overseer) = await ViewerAsync("overseer");
        await GrantAsync(managerId, AccessAction.Create);
        await GrantAsync(overseerId, AccessAction.Read);
        await JoinAsync(memberId, groupId);

        (await PostAsync(manager, Estimate, "Club", ProcessingProfileScope.CavingGroup, Guid.NewGuid(), """{"voxelsCm":[3]}"""))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var club = await CreateAsync(manager, Estimate, "Club", ProcessingProfileScope.CavingGroup, groupId, """{"voxelsCm":[3]}""");

        // The member and the holder of read see it; an account in no group and with no grant does not.
        (await ListAsync(member, Estimate)).ShouldContain(p => p.Id == club.Id);
        (await member.GetAsync($"/api/v1/scan-profiles/{club.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(overseer, Estimate)).ShouldContain(p => p.Id == club.Id);
        (await ListAsync(outsider, Estimate)).ShouldNotContain(p => p.Id == club.Id);
        (await outsider.GetAsync($"/api/v1/scan-profiles/{club.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A member may use it but not change it without the right.
        (await member.PutAsJsonAsync($"/api/v1/scan-profiles/{club.Id}", new { name = "x", values = new { } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Values_are_checked_against_the_steps_schema()
    {
        var (_, author) = await ViewerAsync("values");

        var wrongType = await PostAsync(author, Downsample, "Bad", ProcessingProfileScope.Personal, null, """{"everyNth":"two"}""");
        await ShouldBeProblemAsync(wrongType, HttpStatusCode.BadRequest, ProcessingProfileEndpoints.ValuesInvalidCode);

        var unknownKey = await PostAsync(author, Downsample, "Bad", ProcessingProfileScope.Personal, null, """{"cellCm":2}""");
        await ShouldBeProblemAsync(unknownKey, HttpStatusCode.BadRequest, ProcessingProfileEndpoints.ValuesInvalidCode);

        var unknownStep = await PostAsync(author, "no.such.step", "Bad", ProcessingProfileScope.Personal, null, "{}");
        await ShouldBeProblemAsync(unknownStep, HttpStatusCode.BadRequest, ProcessingProfileEndpoints.StepUnknownCode);

        var noParameters = await PostAsync(author, "hdmapping.export", "Bad", ProcessingProfileScope.Personal, null, "{}");
        await ShouldBeProblemAsync(noParameters, HttpStatusCode.BadRequest, ProcessingProfileEndpoints.StepWithoutParametersCode);

        var noName = await PostAsync(author, Downsample, "", ProcessingProfileScope.Personal, null, "{}");
        noName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var groupOnPersonal = await PostAsync(author, Downsample, "x", ProcessingProfileScope.Personal, Guid.NewGuid(), "{}");
        groupOnPersonal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await PostAsync(author, Downsample, "Good", ProcessingProfileScope.Personal, null, """{"method":"everyNth","everyNth":4}"""))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_built_in_profile_is_never_changed_in_place()
    {
        var builtInId = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            db.ProcessingProfiles.Add(new ProcessingProfile
            {
                Id = builtInId,
                StepId = Estimate,
                Name = $"Shipped {suffix}",
                Scope = ProcessingProfileScope.Installation,
                Values = """{"voxelsCm":[1]}""",
                Origin = ProcessingProfileOrigins.BuiltIn,
            });
            await db.SaveChangesAsync();
        }

        var (_, admin) = await UserAsync(GlobalRoles.Admin, "admin");
        var listed = (await ListAsync(admin, Estimate)).Single(p => p.Id == builtInId);
        listed.BuiltIn.ShouldBeTrue();
        listed.MayEdit.ShouldBeFalse();

        await ShouldBeProblemAsync(
            await admin.PutAsJsonAsync($"/api/v1/scan-profiles/{builtInId}", new { name = "Changed", values = new { } }),
            HttpStatusCode.Conflict, ProcessingProfileEndpoints.BuiltInCode);
        await ShouldBeProblemAsync(
            await admin.DeleteAsync($"/api/v1/scan-profiles/{builtInId}"),
            HttpStatusCode.Conflict, ProcessingProfileEndpoints.BuiltInCode);
    }

    [Fact]
    public async Task The_odometry_schema_follows_the_workers_dump_and_is_kept_when_the_worker_is_gone()
    {
        var (_, author) = await ViewerAsync("odometry");

        // Before any worker has run: the registered shape only.
        var before = await SchemaAsync(author, Odometry);
        before.Source.ShouldBe(Infrastructure.Scans.ScanStepSchemaSource.Registry);

        WriteDump();
        var live = await SchemaAsync(author, Odometry);
        live.Source.ShouldBe(Infrastructure.Scans.ScanStepSchemaSource.Worker);
        live.ToolVersion.ShouldBe("9.9.9@feedbee");
        var sections = live.Schema.GetProperty("properties").GetProperty("values").GetProperty("properties");
        sections.TryGetProperty("paths", out _).ShouldBeFalse();
        sections.TryGetProperty("version_info", out _).ShouldBeFalse();
        sections.GetProperty("odometry_control").GetProperty("properties").GetProperty("iterations")
            .GetProperty("type").GetString().ShouldBe("integer");
        live.Defaults.GetProperty("values").GetProperty("odometry_control").GetProperty("iterations").GetInt32().ShouldBe(30);
        live.Defaults.GetProperty("values").GetProperty("filtering").TryGetProperty("ceiling", out _).ShouldBeFalse();

        // Values are checked against the dumped keys and their kinds.
        var good = await CreateAsync(author, Odometry, "Tuned", ProcessingProfileScope.Personal, null,
            """{"values":{"odometry_control":{"iterations":12,"step_motion":false}}}""");
        good.ToolVersion.ShouldBe("9.9.9@feedbee");
        await ShouldBeProblemAsync(
            await PostAsync(author, Odometry, "Bad", ProcessingProfileScope.Personal, null,
                """{"values":{"odometry_control":{"iterations":"many"}}}"""),
            HttpStatusCode.BadRequest, ProcessingProfileEndpoints.ValuesInvalidCode);
        await ShouldBeProblemAsync(
            await PostAsync(author, Odometry, "Bad", ProcessingProfileScope.Personal, null,
                """{"values":{"odometry_control":{"no_such_key":1}}}"""),
            HttpStatusCode.BadRequest, ProcessingProfileEndpoints.ValuesInvalidCode);
        await ShouldBeProblemAsync(
            await PostAsync(author, Odometry, "Bad", ProcessingProfileScope.Personal, null,
                """{"values":{"paths":{"current_output_dir":"/elsewhere"}}}"""),
            HttpStatusCode.BadRequest, ProcessingProfileEndpoints.ValuesInvalidCode);

        // The worker's file gone: the stored copy answers.
        File.Delete(Path.Combine(handoffRoot, "defaults", "hdmapping.odometry.json"));
        var stored = await SchemaAsync(author, Odometry);
        stored.Source.ShouldBe(Infrastructure.Scans.ScanStepSchemaSource.Stored);
        stored.ToolVersion.ShouldBe("9.9.9@feedbee");
        await ShouldBeProblemAsync(
            await PostAsync(author, Odometry, "Bad", ProcessingProfileScope.Personal, null,
                """{"values":{"odometry_control":{"iterations":2.5}}}"""),
            HttpStatusCode.BadRequest, ProcessingProfileEndpoints.ValuesInvalidCode);
    }

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
        var email = $"pprof-{tag}-{suffix}@t.local";
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

    private async Task<Guid> NewGroupAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var group = new CavingGroup { Name = $"Profiles club {suffix}", Slug = $"pprof-{suffix}" };
        db.CavingGroups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    private async Task JoinAsync(Guid userId, Guid groupId)
    {
        var caverId = await RosterHelper.CaverIdForAsync(factory, userId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.CavingGroupMemberships.Add(new CavingGroupMembership { CavingGroupId = groupId, CaverId = caverId });
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
