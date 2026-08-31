// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>
    /// The served description says the API is behind a bearer token, and names the operations a
    /// separate application is generated from.
    /// </summary>
    /// <remarks>
    /// Both are recent and both are invisible in the browser client — it addresses routes by path
    /// and sends its token from code — so nothing else here would notice either going away. The
    /// application on the other side of the mobile protocol is generated from this document alone:
    /// without the scheme it would emit calls carrying no credential, and without the names its
    /// method names would be invented from the paths and would move whenever a path is tidied.
    /// </remarks>
    [Fact]
    public async Task OpenApi_document_declares_the_bearer_scheme_and_names_the_sync_operations()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("bearerAuth");
        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");

        // Document-wide, not per operation: everything under the versioned prefix needs a token.
        document.GetProperty("security").EnumerateArray()
            .Select(entry => entry.EnumerateObject().Select(p => p.Name))
            .SelectMany(names => names)
            .ShouldContain("bearerAuth");

        var named = new List<(string Path, string Id)>();
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind is JsonValueKind.Object
                    && operation.Value.TryGetProperty("operationId", out var id))
                {
                    named.Add((path.Name, id.GetString()!));
                }
            }
        }

        // The whole list, not a containment check. Naming the rest of this server's operations is
        // a change of its own with its own consequences for every generated client; a name that
        // appeared here without that decision having been taken is what this catches.
        named.Select(x => x.Id).ShouldBe(
            [
                "syncCapabilities", "syncListSets", "syncCreateSet", "syncGetSet",
                "syncReplaceSet", "syncDeleteSet", "syncDownload", "syncUpload",
            ],
            ignoreOrder: true);
        named.ShouldAllBe(x => x.Path.StartsWith("/api/v1/sync", StringComparison.Ordinal));
    }

    /// <summary>
    /// The document-wide requirement is taken back off the routes that are deliberately open, and
    /// the sign-in calls are the reason it matters: a client has to issue them with no credential
    /// in hand, because obtaining one is what they are for.
    /// </summary>
    /// <remarks>
    /// Asserted on a route that is guarded as well, so this cannot pass by the exemption being
    /// applied to everything — which would put the description back to describing the whole server
    /// as open, the exact state the bearer declaration was added to end.
    /// </remarks>
    [Fact]
    public async Task OpenApi_document_exempts_the_deliberately_open_routes_from_the_bearer_requirement()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = document.GetProperty("paths");

        // "Present and empty" is how OpenAPI says "no requirement on this operation", and it is
        // what overrides the document-level entry. An absent property would inherit it instead.
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        login.TryGetProperty("security", out var loginSecurity).ShouldBeTrue(
            "The sign-in route must state that it needs no token, or a generated client will "
            + "demand one before it can obtain one.");
        loginSecurity.EnumerateArray().ShouldBeEmpty();

        // A guarded route says nothing of its own and inherits the document's requirement. Both
        // halves of the mobile contract are named, the write half included: a generated client
        // that issued an upload with no credential would be pushing a caver's survey at whatever
        // the server does with an unauthenticated write.
        foreach (var route in new[] { "download", "upload" })
        {
            var operation = paths.GetProperty($"/api/v1/sync/sets/{{id}}/{route}");
            var verb = operation.TryGetProperty("get", out var get) ? get : operation.GetProperty("post");
            verb.TryGetProperty("security", out _).ShouldBeFalse();
        }
    }

    /// <summary>
    /// The failures a device has to act on are in the served description, not only in the code
    /// that produces them.
    /// </summary>
    /// <remarks>
    /// The application on the other side of this protocol branches on these: a stale cursor means
    /// start again, a contract mismatch means stop and tell the caver to update, a batch that is
    /// too large means send fewer rows. A status a generated client has never been told about is
    /// a status it handles as "something went wrong", which for the first of those loses a
    /// device's place in the stream and for the last is an upload that never succeeds however
    /// many times it is retried.
    /// </remarks>
    [Fact]
    public async Task OpenApi_document_declares_the_statuses_a_device_has_to_branch_on()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = document.GetProperty("paths");

        var download = paths.GetProperty("/api/v1/sync/sets/{id}/download").GetProperty("get")
            .GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();
        download.ShouldContain("400");
        download.ShouldContain("404");
        download.ShouldContain("409");

        var upload = paths.GetProperty("/api/v1/sync/sets/{id}/upload").GetProperty("post")
            .GetProperty("responses").EnumerateObject().Select(p => p.Name).ToList();
        upload.ShouldContain("400");
        upload.ShouldContain("404");
        upload.ShouldContain("409");

        // A row that lost its arbitration is not one of these. It rides a 200 beside the rows
        // that were written, because a batch of forty is answered row by row — and a device told
        // about a conflict by a status code would have to throw the other thirty-nine away.
        upload.ShouldContain("200");
    }

    public void Dispose() => factory.Dispose();

    private sealed record AboutResponse(string Name, string Version, string License, string SourceUrl);
}
