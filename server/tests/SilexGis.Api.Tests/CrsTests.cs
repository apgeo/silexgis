// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// CRS definitions resolved from the PROJ database that ships with the application. This exists
/// so the vendored 3D survey viewer never has to reach a public web service to find out what
/// coordinate system a survey is in.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CrsTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient reader = null!;

    public CrsTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
            ["Scene3d:GeoidOffsetM"] = "43.5",
        });
    }

    public async Task InitializeAsync()
    {
        var email = $"crs-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        reader = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Theory]
    [InlineData(4326)]
    [InlineData(3857)]
    // Romanian Stereo70. The vendored viewer hard-codes only a handful of systems and this is not
    // one of them, so it is the code that used to leave a survey unreferenced on an offline
    // installation — the whole reason this endpoint exists.
    [InlineData(31700)]
    public async Task A_known_code_resolves_to_a_proj4_definition_the_viewer_can_use_as_is(int srid)
    {
        var response = await reader.GetAsync($"/api/v1/crs/{srid}.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        var body = await response.Content.ReadAsStringAsync();
        // The consumer hands the body straight to a proj4 parser, so it must be the definition
        // itself — no JSON envelope, no leading whitespace, nothing to unwrap.
        body.ShouldStartWith("+proj=");
        body.Trim().ShouldBe(body);
    }

    [Fact]
    public async Task A_definition_is_immutable_so_it_is_answered_identically_and_marked_cacheable()
    {
        var first = await reader.GetAsync("/api/v1/crs/31700.proj4");
        var second = await reader.GetAsync("/api/v1/crs/31700.proj4");

        (await first.Content.ReadAsStringAsync()).ShouldBe(await second.Content.ReadAsStringAsync());
        // Private, not public: the request carried a bearer token, so a shared cache must not
        // keep the answer under a key that ignores it.
        var cacheControl = second.Headers.CacheControl.ShouldNotBeNull();
        cacheControl.Private.ShouldBeTrue();
        cacheControl.MaxAge.ShouldNotBeNull().TotalDays.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task A_code_the_database_does_not_know_is_a_clean_not_found()
    {
        var response = await reader.GetAsync("/api/v1/crs/999998.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("crs.not_found");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000000)]
    public async Task A_nonsense_code_is_rejected_rather_than_looked_up(int srid)
    {
        var response = await reader.GetAsync($"/api/v1/crs/{srid}.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("crs.code_invalid");
    }

    [Fact]
    public async Task A_route_that_is_not_an_integer_code_never_reaches_the_handler()
    {
        // The route constraint, not the handler, is what keeps a path segment from being used as
        // an unbounded key into the definition cache.
        (await reader.GetAsync("/api/v1/crs/abc.proj4")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_endpoint_needs_a_bearer_token()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/crs/4326.proj4")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void The_scene_options_bind_from_configuration()
    {
        // Nothing consumes the geoid offset yet, so this is what proves the section name is right
        // before anything depends on it being right.
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<Scene3dOptions>>().Value;
        options.GeoidOffsetM.ShouldBe(43.5);
    }

    [Fact]
    public void An_installation_that_configures_nothing_gets_the_documented_default()
    {
        // The install guide publishes this number as the value an operator inherits by doing
        // nothing, and an operator outside Romania decides whether to override it by comparing
        // their own undulation against it. Changing the default without changing the guide would
        // leave that comparison being made against a figure the software no longer uses.
        new Scene3dOptions().GeoidOffsetM.ShouldBe(41.5);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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
            // Temp files; best effort.
        }
    }
}
