// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Each section of the installation settings is saved whole, so a save made from a page opened
/// before somebody else's save would put back every field they changed. The settings read hands
/// out each section's version, and a section save naming an older one is refused.
/// </summary>
/// <remarks>
/// The version is offered, not required: a save without <c>If-Match</c> keeps last-write-wins,
/// the way every other versioned route here treats a missing header.
/// </remarks>
public sealed class AdminSettingsVersionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;
    private HttpClient otherAdmin = null!;
    private HttpClient editor = null!;
    private HttpClient anonymous = null!;

    public AdminSettingsVersionTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    /// <summary>Each section: its name in the read's body and in its versions, and its save route.</summary>
    public static TheoryData<string, string> Sections => new()
    {
        { "mail", "mail" },
        { "sms", "sms" },
        { "security", "security" },
        { "protection", "protection" },
        { "import", "import" },
        { "interface", "interface" },
        { "notifications", "notifications" },
        { "announcements", "announcements" },
        { "photoLibraries", "photo-libraries" },
        { "sync", "sync" },
        { "surveyUploads", "survey-uploads" },
        { "testData", "test-data" },
        { "mapSymbols", "map-symbols" },
    };

    public async ValueTask InitializeAsync()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"asv-a-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"asv-b-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"asv-ed-{marker}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"asv-a-{marker}@t.local");
        otherAdmin = await AuthHelper.BearerClientAsync(factory, $"asv-b-{marker}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"asv-ed-{marker}@t.local");
        anonymous = factory.CreateClient();
    }

    /// <summary>
    /// Every section: the read names its version, a save naming an older one is refused with the
    /// stable code and changes nothing, a save naming the current one lands and answers with the
    /// version it produced, and a save naming none still lands.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sections))]
    public async Task A_section_save_is_held_to_the_version_it_was_read_at(string section, string route)
    {
        var read = await ReadAsync(admin);
        var version = read["versions"]![section]!.GetValue<string>();
        version.ShouldNotBeNullOrWhiteSpace();
        var body = read[section]!.DeepClone();

        using var stale = await admin.PutWithIfMatchAsync(
            $"/api/v1/admin/settings/{route}", body, "\"not-the-version\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await stale.Content.ReadAsStringAsync());
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("concurrency.version_mismatch");

        using var current = await admin.PutWithIfMatchAsync($"/api/v1/admin/settings/{route}", body, $"\"{version}\"");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
        var answered = JsonNode.Parse(await current.Content.ReadAsStringAsync())!;
        var produced = answered["versions"]![section]!.GetValue<string>();
        current.Headers.ETag!.Tag.ShouldBe($"\"{produced}\"");
        (await ReadAsync(admin))["versions"]![section]!.GetValue<string>().ShouldBe(produced);

        using var unversioned = await admin.PutAsJsonAsync($"/api/v1/admin/settings/{route}", body);
        unversioned.StatusCode.ShouldBe(HttpStatusCode.OK, await unversioned.Content.ReadAsStringAsync());
    }

    /// <summary>Each section with one yes/no, number or choice it carries.</summary>
    public static TheoryData<string, string, string> SectionValues => new()
    {
        { "mail", "mail", "port" },
        { "sms", "sms", "timeoutSeconds" },
        { "security", "security", "twoFactorCodeLifetimeMinutes" },
        { "protection", "protection", "describeLocationFreeCopies" },
        { "import", "import", "photoClusterRadiusMeters" },
        { "notifications", "notifications", "retentionDays" },
        { "announcements", "announcements", "dailyPaidMessageCap" },
        { "photoLibraries", "photo-libraries", "immichSuspended" },
        { "sync", "sync", "stagedUploadRetentionDays" },
        { "sync", "sync", "peopleDirectoryScope" },
        { "surveyUploads", "survey-uploads", "firstModelOfCreatedCave" },
        { "testData", "test-data", "enabled" },
    };

    /// <summary>
    /// A section is saved whole, so a save that leaves a value out is refused as a validation
    /// failure naming it and changes nothing — rather than saving the left-out number as 0 or the
    /// switch as off. The same body with the value in it lands.
    /// </summary>
    [Theory]
    [MemberData(nameof(SectionValues))]
    public async Task A_section_save_that_leaves_a_value_out_is_refused_and_changes_nothing(
        string section, string route, string member)
    {
        var read = await ReadAsync(admin);
        var version = read["versions"]![section]!.GetValue<string>();
        var body = read[section]!.DeepClone().AsObject();
        body.ContainsKey(member).ShouldBeTrue($"the read of {section} carries {member}");
        var incomplete = body.DeepClone().AsObject();
        incomplete.Remove(member);

        using var refused = await admin.PutAsJsonAsync($"/api/v1/admin/settings/{route}", incomplete);
        var text = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        var problem = JsonNode.Parse(text)!;
        problem["code"]!.GetValue<string>().ShouldBe("validation.failed", text);
        problem["errors"]!.AsObject().Select(e => e.Key.ToLowerInvariant()).ShouldContain(member.ToLowerInvariant(), text);
        (await ReadAsync(admin))["versions"]![section]!.GetValue<string>().ShouldBe(version);

        using var whole = await admin.PutAsJsonAsync($"/api/v1/admin/settings/{route}", body);
        whole.StatusCode.ShouldBe(HttpStatusCode.OK, await whole.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The lost update the version exists for: two administrators open the same section, the
    /// first saves a change, and the second's save from the page they opened earlier is refused
    /// instead of putting the old values back. Re-read, the second's save lands.
    /// </summary>
    [Fact]
    public async Task A_save_from_a_section_somebody_saved_over_since_is_refused_and_keeps_their_change()
    {
        var openedByA = await ReadAsync(admin);
        var openedByB = await ReadAsync(otherAdmin);
        var versionA = openedByA["versions"]!["security"]!.GetValue<string>();
        var versionB = openedByB["versions"]!["security"]!.GetValue<string>();
        versionB.ShouldBe(versionA);

        var changedByA = openedByA["security"]!.DeepClone();
        var lifetime = changedByA["twoFactorCodeLifetimeMinutes"]!.GetValue<int>();
        changedByA["twoFactorCodeLifetimeMinutes"] = lifetime == 10 ? 11 : 10;
        using var saved = await admin.PutWithIfMatchAsync("/api/v1/admin/settings/security", changedByA, $"\"{versionA}\"");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var changedByB = openedByB["security"]!.DeepClone();
        changedByB["emailTwoFactorEnabled"] = !changedByB["emailTwoFactorEnabled"]!.GetValue<bool>();
        using var refused = await otherAdmin.PutWithIfMatchAsync("/api/v1/admin/settings/security", changedByB, $"\"{versionB}\"");
        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await refused.Content.ReadAsStringAsync());

        var after = await ReadAsync(otherAdmin);
        after["security"]!["twoFactorCodeLifetimeMinutes"]!.GetValue<int>()
            .ShouldBe(changedByA["twoFactorCodeLifetimeMinutes"]!.GetValue<int>());
        after["security"]!["emailTwoFactorEnabled"]!.GetValue<bool>()
            .ShouldBe(openedByB["security"]!["emailTwoFactorEnabled"]!.GetValue<bool>());

        // Another section's save does not move this one's version: the two are saved apart.
        var securityVersion = after["versions"]!["security"]!.GetValue<string>();
        using var other = await admin.PutAsJsonAsync("/api/v1/admin/settings/notifications", after["notifications"]!.DeepClone());
        other.StatusCode.ShouldBe(HttpStatusCode.OK, await other.Content.ReadAsStringAsync());

        var reapplied = after["security"]!.DeepClone();
        reapplied["emailTwoFactorEnabled"] = changedByB["emailTwoFactorEnabled"]!.GetValue<bool>();
        using var landed = await otherAdmin.PutWithIfMatchAsync("/api/v1/admin/settings/security", reapplied, $"\"{securityVersion}\"");
        landed.StatusCode.ShouldBe(HttpStatusCode.OK, await landed.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The version changes nothing about who may save: without a session the save is refused as
    /// unauthenticated and without the settings right as forbidden, a current version
    /// notwithstanding — while an administrator's save with the same version lands.
    /// </summary>
    [Fact]
    public async Task A_current_version_does_not_open_the_save_to_anybody_else()
    {
        var read = await ReadAsync(admin);
        var version = read["versions"]!["import"]!.GetValue<string>();
        var body = read["import"]!.DeepClone();

        using var nobody = await anonymous.PutWithIfMatchAsync("/api/v1/admin/settings/import", body, $"\"{version}\"");
        nobody.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var forbidden = await editor.PutWithIfMatchAsync("/api/v1/admin/settings/import", body, $"\"{version}\"");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await forbidden.Content.ReadAsStringAsync());
        (await editor.GetAsync("/api/v1/admin/settings/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var allowed = await admin.PutWithIfMatchAsync("/api/v1/admin/settings/import", body, $"\"{version}\"");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    private static async Task<JsonNode> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/admin/settings/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    public void Dispose()
    {
        admin?.Dispose();
        otherAdmin?.Dispose();
        editor?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
