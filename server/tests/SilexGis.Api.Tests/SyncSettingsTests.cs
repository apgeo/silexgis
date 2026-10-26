// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation's <c>sync</c> settings: the environment's default, an administrator's change
/// of it, what may be saved and by whom — and what the people directory's switch and scope do to
/// the directory, the announced parts and an upload's links.
/// </summary>
/// <remarks>
/// The directory starts switched off here, from the environment, so the switch is proved both
/// ways in one place: off as the deployment said, on once an administrator saves it.
/// </remarks>
public sealed class SyncSettingsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private HttpClient editor = null!;
    private Guid set;
    private Guid stranger;
    private string marker = null!;

    public SyncSettingsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Sync:PeopleDirectoryEnabled"] = "false" });

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"sst-adm-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"sst-ed-{marker}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"sst-adm-{marker}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"sst-ed-{marker}@t.local");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var outsider = new Caver { FullName = $"Stranger {marker}" };
            db.Cavers.Add(outsider);
            await db.SaveChangesAsync();
            stranger = outsider.Id;
        }

        var response = await editor.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = Array.Empty<Guid>(),
            settings = new { },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        set = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Switched_off_the_directory_is_refused_unannounced_and_takes_no_link_and_switched_on_it_answers()
    {
        var snapshot = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings/");
        var sync = snapshot.GetProperty("sync");
        sync.GetProperty("peopleDirectoryEnabled").GetBoolean().ShouldBeFalse();
        sync.GetProperty("peopleDirectoryScope").GetString().ShouldBe("myGroups");
        sync.GetProperty("convertTripsWithProblems").GetBoolean().ShouldBeTrue();
        sync.GetProperty("stagedUploadRetentionDays").GetInt32().ShouldBe(7);
        sync.GetProperty("rejectedHeldBytesRetentionDays").GetInt32().ShouldBe(30);

        // Recorded: the one refusal that says a part of the protocol is switched off rather than
        // that the caller did something wrong, so a phone stops offering it instead of retrying.
        var disabled = await new ContractFixture()
            .Literal(marker, "marker")
            .RecordRefusedGetAsync(Path.Combine("16-errors", "feature-disabled"), editor, "/api/v1/sync/people");
        disabled.GetProperty("code").GetString().ShouldBe("sync.feature_disabled");
        (await FeaturesAsync(editor)).ShouldNotContain("people");

        // A phone user is still recorded; a link is not taken while nobody is listed.
        var linkedUser = Guid.CreateVersion7();
        var plainUser = Guid.CreateVersion7();
        var upload = await editor.PostAsJsonAsync($"/api/v1/sync/sets/{set}/upload", new
        {
            batchId = Guid.CreateVersion7(),
            contractVersion = 1,
            people = new object[]
            {
                new { deviceUserId = linkedUser, username = "linked", caverId = stranger },
                new { deviceUserId = plainUser, username = "plain" },
            },
        });
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var people = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("people");
        people[0].GetProperty("status").GetString().ShouldBe("rejected");
        people[0].GetProperty("code").GetString().ShouldBe("sync.feature_disabled");
        people[1].GetProperty("status").GetString().ShouldBe("created");

        // Switched on with everybody listed: the stranger, in no group with anybody, is offered.
        var saved = await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: true, scope: "everyone"));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sync")
            .GetProperty("peopleDirectoryScope").GetString().ShouldBe("everyone");

        var directory = await editor.GetAsync("/api/v1/sync/people");
        directory.StatusCode.ShouldBe(HttpStatusCode.OK, await directory.Content.ReadAsStringAsync());
        (await directory.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("people").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ShouldContain(stranger);
        (await FeaturesAsync(editor)).ShouldContain("people");

        // Back to the clubmates-only default: the same stranger is not listed any more.
        (await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: true, scope: "myGroups")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await editor.GetFromJsonAsync<JsonElement>("/api/v1/sync/people")).GetProperty("people").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ShouldNotContain(stranger);
    }

    [Fact]
    public async Task Only_somebody_who_may_change_settings_saves_the_section_and_a_nonsense_window_is_refused()
    {
        await ShouldBeRefusedAsync(
            await editor.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: false, scope: "myGroups")),
            HttpStatusCode.Forbidden,
            "access.forbidden");

        await ShouldBeRefusedAsync(
            await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: false, scope: "myGroups", staged: 0)),
            HttpStatusCode.BadRequest,
            "validation.failed");
        await ShouldBeRefusedAsync(
            await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: false, scope: "myGroups", rejected: 366)),
            HttpStatusCode.BadRequest,
            "validation.failed");

        var saved = await admin.PutAsJsonAsync(
            "/api/v1/admin/settings/sync", Settings(enabled: false, scope: "myGroups", staged: 3, rejected: 90));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var sync = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sync");
        sync.GetProperty("stagedUploadRetentionDays").GetInt32().ShouldBe(3);
        sync.GetProperty("rejectedHeldBytesRetentionDays").GetInt32().ShouldBe(90);

        // Left as the deployment had it, for whatever runs next against this installation.
        (await admin.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: false, scope: "myGroups")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = factory.CreateClient();
        (await anonymous.PutAsJsonAsync("/api/v1/admin/settings/sync", Settings(enabled: true, scope: "myGroups")))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static object Settings(bool enabled, string scope, int staged = 7, int rejected = 30) => new
    {
        peopleDirectoryEnabled = enabled,
        peopleDirectoryScope = scope,
        convertTripsWithProblems = true,
        stagedUploadRetentionDays = staged,
        rejectedHeldBytesRetentionDays = rejected,
    };

    private static async Task<List<string>> FeaturesAsync(HttpClient client)
    {
        var capabilities = await client.GetFromJsonAsync<JsonElement>("/api/v1/sync/capabilities");
        return [.. capabilities.GetProperty("features").EnumerateArray().Select(f => f.GetString()!)];
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, payload);
        JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString().ShouldBe(code, payload);
    }

    public void Dispose()
    {
        admin?.Dispose();
        editor?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
