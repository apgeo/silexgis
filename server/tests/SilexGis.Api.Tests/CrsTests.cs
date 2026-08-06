// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Shouldly;
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
    public async Task An_esri_authority_code_still_resolves_even_though_the_authority_is_lost()
    {
        // Survey files name a system as "epsg:31700" or "esri:102008", but the viewer that asks
        // for a definition passes on only the number — the authority never reaches this endpoint.
        // 102008 (North America Albers Equal Area Conic) is an ESRI code with no EPSG meaning, so
        // resolving it proves such a survey keeps the georeferencing it used to get from the
        // public web service this endpoint replaced.
        var response = await reader.GetAsync("/api/v1/crs/102008.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("+proj=aea");
    }

    [Fact]
    public async Task An_epsg_code_wins_over_an_esri_code_of_the_same_number()
    {
        // The two registers overlap at the low numbers and EPSG is the authoritative one, so an
        // ambiguous number must resolve the EPSG way.
        var response = await reader.GetAsync("/api/v1/crs/4326.proj4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("+proj=longlat");
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
