// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ApiSmokeTests : IDisposable
{
    private readonly SilexGisApiFactory factory;

    public ApiSmokeTests(PostgresFixture postgres) => factory = new SilexGisApiFactory(postgres.ConnectionString);

    [Fact]
    public async Task Health_live_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_returns_ok_with_database_check()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task About_reports_name_version_license_and_source()
    {
        var about = await factory.CreateClient().GetFromJsonAsync<AboutResponse>("/api/v1/about");

        about.ShouldNotBeNull();
        about.Name.ShouldBe("SilexGIS");
        about.License.ShouldBe("AGPL-3.0-or-later");
        about.Version.ShouldNotBeNullOrWhiteSpace();
        about.SourceUrl.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenApi_document_is_served_and_publishes_the_feature_surface()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The generated TS client is built from this document, so the routes the client
        // navigates by must actually be in it.
        var document = await response.Content.ReadAsStringAsync();
        document.ShouldContain("/api/v1/caves/{id}/summary");
        document.ShouldContain("/api/v1/caves/{caveId}/entrances");
        document.ShouldContain("/api/v1/features/{id}");
        document.ShouldContain("/api/v1/features/{id}/parents");
        document.ShouldContain("/api/v1/features/{id}/links");
        document.ShouldContain("/api/v1/centerlines/{id}");
        document.ShouldContain("/api/v1/trip-logs/{id}/state");
        document.ShouldContain("/api/v1/stats/cavers/{id}");
        document.ShouldContain("/api/v1/stats/caves/{id}");
        document.ShouldContain("/api/v1/stats/caving-groups/{id}");
        document.ShouldContain("/api/v1/stats/cavers/{id}/export");
        document.ShouldContain("/api/v1/stats/caves/{id}/export");
        document.ShouldContain("/api/v1/stats/caving-groups/{id}/export");
        document.ShouldContain("/api/v1/stats/expeditions/{id}");
        document.ShouldContain("/api/v1/stats/expeditions/{id}/export");
        document.ShouldContain("/api/v1/export/features");
        document.ShouldContain("/api/v1/shared/features/{token}");

        // Routes the feature supertype replaced must be gone, not merely unused.
        document.ShouldNotContain("surface-features");
        document.ShouldNotContain("/api/v1/cave-centerlines/");

        // A trip's lifecycle is one route naming the state it moves to, so the two verbs it
        // replaced must be gone rather than left beside it: two roads into the same table are how
        // the two come to disagree.
        document.ShouldNotContain("/api/v1/trip-logs/{id}/publish");
        document.ShouldNotContain("/api/v1/trip-logs/{id}/unpublish");
    }

    public void Dispose() => factory.Dispose();

    private sealed record AboutResponse(string Name, string Version, string License, string SourceUrl);
}
