// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace SilexGis.Api.Tests;

public class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Fact]
    public async Task Health_live_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_returns_ok()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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

    private sealed record AboutResponse(string Name, string Version, string License, string SourceUrl);
}
