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
    public async Task OpenApi_document_is_served()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public void Dispose() => factory.Dispose();

    private sealed record AboutResponse(string Name, string Version, string License, string SourceUrl);
}
