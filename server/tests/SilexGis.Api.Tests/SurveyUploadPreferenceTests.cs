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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// An account's own survey-upload choices: the outputs every upload produces on top of what it
/// asks for, and whether its uploads get public result links.
/// </summary>
public sealed class SurveyUploadPreferenceTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private const string Route = "/api/v1/me/survey-upload-preferences";

    private readonly SilexGisApiFactory factory = new(postgres.ConnectionString);

    [Fact]
    public async Task An_account_that_never_chose_has_nothing_added_and_public_links_on()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "sup-defaults@test.local");
        var web = await AuthHelper.BearerClientAsync(factory, "sup-defaults@test.local");

        var answer = await web.GetFromJsonAsync<JsonElement>(Route);

        answer.GetProperty("alwaysOutputs").GetArrayLength().ShouldBe(0);
        answer.GetProperty("publicLinks").GetBoolean().ShouldBeTrue();
        answer.GetProperty("defaultLinkAudience").GetString().ShouldBe("public");
    }

    [Fact]
    public async Task Choices_are_stored_in_catalogue_form_replaced_as_a_whole_and_kept_to_their_account()
    {
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "sup-owner@test.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "sup-other@test.local");
        var owner = await AuthHelper.BearerClientAsync(factory, "sup-owner@test.local");
        var other = await AuthHelper.BearerClientAsync(factory, "sup-other@test.local");

        var saved = await owner.PutAsJsonAsync(Route, new { alwaysOutputs = new[] { "KML", "lox", "kml" }, publicLinks = false, defaultLinkAudience = "organization" });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Outputs(body).ShouldBe(["lox", "kml"]);
        body.GetProperty("publicLinks").GetBoolean().ShouldBeFalse();
        body.GetProperty("defaultLinkAudience").GetString().ShouldBe("organization");

        // Read back as saved, and stored that way: an explicit "no links" survives the insert.
        var read = await owner.GetFromJsonAsync<JsonElement>(Route);
        Outputs(read).ShouldBe(["lox", "kml"]);
        read.GetProperty("publicLinks").GetBoolean().ShouldBeFalse();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = await db.SurveyUploadPreferences.AsNoTracking().SingleAsync(p => p.UserId == ownerId);
            row.AlwaysOutputs.ShouldBe(["lox", "kml"]);
            row.PublicLinks.ShouldBeFalse();
            row.DefaultLinkAudience.ShouldBe(SurveyUploadLinkAudience.Organization);
        }

        // A second save replaces the first rather than adding to it.
        (await owner.PutAsJsonAsync(Route, new { alwaysOutputs = new[] { "dxf" }, publicLinks = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var replaced = await owner.GetFromJsonAsync<JsonElement>(Route);
        Outputs(replaced).ShouldBe(["dxf"]);
        replaced.GetProperty("publicLinks").GetBoolean().ShouldBeTrue();

        // A save that does not name an audience keeps the one chosen; naming one replaces it.
        replaced.GetProperty("defaultLinkAudience").GetString().ShouldBe("organization");
        (await owner.PutAsJsonAsync(Route, new { alwaysOutputs = new[] { "dxf" }, publicLinks = true, defaultLinkAudience = "public" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.GetFromJsonAsync<JsonElement>(Route)).GetProperty("defaultLinkAudience").GetString().ShouldBe("public");

        // Another account still has the defaults: each account reads only its own choices.
        var others = await other.GetFromJsonAsync<JsonElement>(Route);
        others.GetProperty("alwaysOutputs").GetArrayLength().ShouldBe(0);
        others.GetProperty("publicLinks").GetBoolean().ShouldBeTrue();
        others.GetProperty("defaultLinkAudience").GetString().ShouldBe("public");
    }

    [Fact]
    public async Task An_output_outside_the_catalogue_or_a_missing_list_is_refused_and_nothing_is_stored()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "sup-invalid@test.local");
        var web = await AuthHelper.BearerClientAsync(factory, "sup-invalid@test.local");

        var unknown = await web.PutAsJsonAsync(Route, new { alwaysOutputs = new[] { "lox", "pdf" }, publicLinks = true });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unknown)).ShouldBe("survey_upload_preferences.output_unknown");

        var missing = await web.PutAsJsonAsync(Route, new { publicLinks = true });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // An audience outside the two, by name or by number, is refused rather than stored.
        (await web.PutAsJsonAsync(Route, new { alwaysOutputs = Array.Empty<string>(), publicLinks = true, defaultLinkAudience = "everyone" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await web.PutAsJsonAsync(Route, new { alwaysOutputs = Array.Empty<string>(), publicLinks = true, defaultLinkAudience = 7 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var read = await web.GetFromJsonAsync<JsonElement>(Route);
        read.GetProperty("alwaysOutputs").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_choices_are_the_account_holders_and_refused_to_no_sign_in_and_to_a_device_key()
    {
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, "sup-key@test.local");
        var web = await AuthHelper.BearerClientAsync(factory, "sup-key@test.local");
        var device = AuthHelper.AccessKeyClient(factory, await AuthHelper.CreateAccessKeyAsync(web));
        var anonymous = factory.CreateClient();

        (await web.GetAsync(Route)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(Route)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var read = await device.GetAsync(Route);
        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(read)).ShouldBe("auth.access_key_not_accepted");
        var write = await device.PutAsJsonAsync(Route, new { alwaysOutputs = new[] { "kml" }, publicLinks = false });
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(write)).ShouldBe("auth.access_key_not_accepted");

        var unchanged = await web.GetFromJsonAsync<JsonElement>(Route);
        unchanged.GetProperty("publicLinks").GetBoolean().ShouldBeTrue();
    }

    private static string[] Outputs(JsonElement body) =>
        [.. body.GetProperty("alwaysOutputs").EnumerateArray().Select(e => e.GetString()!)];

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    public void Dispose() => factory.Dispose();
}
