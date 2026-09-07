// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Features.Sync;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// How large a download page is when the device asks for one without naming a size. The number is
/// an installation's setting rather than something compiled into the endpoint, so this class holds
/// three hosts against one database: the product with nothing configured, the same product with the
/// default moved, and the same product with the default set above the ceiling it has to obey.
/// </summary>
/// <remarks>
/// <para>
/// The shipped number is asserted against a host given <b>no</b> settings at all, and read out of
/// the configuration that host is running on. A test that injected the value it then asserted would
/// pass exactly as happily if the product shipped a different one, which is the failure this shape
/// exists to avoid; and a test that read the number out of a constant in the source would prove
/// only that the constant is the constant, not that anything binds it.
/// </para>
/// <para>
/// That the download actually consults the setting is proved separately, by moving it: the same
/// selection, read through two hosts that differ in nothing else, hands back a different number of
/// rows. The unmoved host is asserted in the same test as the moved one, so a fixture that had
/// silently built too few rows to page could not read as a working page limit.
/// </para>
/// </remarks>
public sealed class SyncPageSizeTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    /// <summary>The product as an operator who has configured nothing receives it.</summary>
    private readonly SilexGisApiFactory shipped;

    /// <summary>The same product with a default too small to carry the selection in one page.</summary>
    private readonly SilexGisApiFactory smallDefault;

    /// <summary>The same product with a default larger than the ceiling it is a default for.</summary>
    private readonly SilexGisApiFactory defaultAboveCeiling;

    private HttpClient owner = null!;
    private string ownerEmail = null!;
    private string marker = null!;
    private long caveTypeId;
    private long cavePlaceTypeId;
    private Guid set;

    public SyncPageSizeTests(PostgresFixture postgres)
    {
        shipped = new SilexGisApiFactory(postgres.ConnectionString);
        smallDefault = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Sync:DefaultPageSize"] = "2" });
        defaultAboveCeiling = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Sync:DefaultPageSize"] = "9000",
                ["Sync:PageSizeMax"] = "2",
            });
    }

    public async Task InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        ownerEmail = $"sp-page-{marker}@t.local";
        _ = await AuthHelper.CreateUserAsync(shipped, GlobalRoles.Editor, ownerEmail);

        await using (var scope = shipped.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
            cavePlaceTypeId = await db.FeatureTypes
                .Where(t => t.Code == "cave_place").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(shipped, ownerEmail);

        // Four rows: the cave the set names as its root, and three places under it. Small enough to
        // arrive whole under any sane default, and more than the two a moved default allows below.
        var cave = await CreateCaveAsync($"Page cave {marker}");
        _ = await CreatePlaceAsync(cave, $"Page one {marker}", 25.481, 45.571);
        _ = await CreatePlaceAsync(cave, $"Page two {marker}", 25.482, 45.572);
        _ = await CreatePlaceAsync(cave, $"Page three {marker}", 25.483, 45.573);
        set = await CreateSetAsync(owner, [cave]);
    }

    /// <summary>
    /// The number this build ships, read from the configuration the application is running on. It
    /// is a hundred, and it is a hundred because nothing was configured — the host under it was
    /// given no settings, so this is the answer an installation that sets nothing receives.
    /// </summary>
    [Fact]
    public async Task The_page_a_device_gets_when_it_names_no_size_is_the_hundred_the_product_ships()
    {
        var options = shipped.Services.GetRequiredService<IOptions<SyncOptions>>().Value;
        options.DefaultPageSize.ShouldBe(100);
        options.ResolvedDefaultPageSize.ShouldBe(100);

        // And the selection is well inside it, so a device that names no size is handed the set
        // whole. The rows arriving at all is what makes the paging assertions below mean something.
        var page = await DownloadAsync(owner);
        Features(page).Count.ShouldBe(4);
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The setting is what the download reads. The same four rows, the same set, the same account:
    /// only the configured default differs between the two hosts, and only the answer's length
    /// changes with it. Naming a size still reaches past the default, which is the point of it
    /// being a default rather than a limit.
    /// </summary>
    [Fact]
    public async Task A_configured_default_is_the_page_a_device_that_names_no_size_gets()
    {
        using var configured = await AuthHelper.BearerClientAsync(smallDefault, ownerEmail);

        var limited = await DownloadAsync(configured);
        Features(limited).Count.ShouldBe(2);
        limited.GetProperty("hasMore").GetBoolean().ShouldBeTrue();

        // The same request against the host that configured nothing, so the difference is the
        // setting and not the data.
        var whole = await DownloadAsync(owner);
        Features(whole).Count.ShouldBe(4);
        whole.GetProperty("hasMore").GetBoolean().ShouldBeFalse();

        // A device that names a size is unaffected: the default decides nothing for a request that
        // made its own choice, up to the announced ceiling.
        var asked = await DownloadAsync(configured, pageSize: 4);
        Features(asked).Count.ShouldBe(4);
        asked.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A default above the ceiling is served as the ceiling rather than accepted or refused. There
    /// is no caller to refuse: the request this applies to named no size at all, so the only
    /// answers available are the maximum and a page larger than the installation announced.
    /// </summary>
    [Fact]
    public async Task A_default_above_the_announced_maximum_is_served_as_the_maximum_rather_than_accepted()
    {
        var options = defaultAboveCeiling.Services.GetRequiredService<IOptions<SyncOptions>>().Value;
        options.DefaultPageSize.ShouldBe(9000);
        options.ResolvedPageSizeMax.ShouldBe(2);
        options.ResolvedDefaultPageSize.ShouldBe(2);

        using var configured = await AuthHelper.BearerClientAsync(defaultAboveCeiling, ownerEmail);

        var capabilities = await configured.GetFromJsonAsync<JsonElement>("/api/v1/sync/capabilities");
        capabilities.GetProperty("pageSizeMax").GetInt32().ShouldBe(2);

        var page = await DownloadAsync(configured);
        Features(page).Count.ShouldBe(2);
        page.GetProperty("hasMore").GetBoolean().ShouldBeTrue();

        // The other end of the range, which no host here can reach through configuration without a
        // fourth boot: a default of nothing is a page of one row rather than a page of none, which
        // would leave a device looping forever over a set it can never advance through.
        new SyncOptions { DefaultPageSize = 0 }.ResolvedDefaultPageSize.ShouldBe(1);
        new SyncOptions { DefaultPageSize = -5 }.ResolvedDefaultPageSize.ShouldBe(1);
    }

    private static Dictionary<Guid, JsonElement> Features(JsonElement page) =>
        page.GetProperty("features").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid(), f => f);

    private async Task<JsonElement> DownloadAsync(HttpClient client, int? pageSize = null)
    {
        var url = $"/api/v1/sync/sets/{set}/download"
            + (pageSize is { } size ? $"?pageSize={size}" : string.Empty);
        return await client.GetFromJsonAsync<JsonElement>(url);
    }

    private async Task<Guid> CreateSetAsync(HttpClient client, IReadOnlyList<Guid> roots)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sync/sets/", new
        {
            name = $"Phone {marker}",
            uploadVisibility = "private",
            rootFeatureIds = roots,
            settings = new { },
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "private",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePlaceAsync(Guid parent, string name, double lon, double lat)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name,
            featureTypeId = cavePlaceTypeId,
            geometry = new { type = "Point", coordinates = new[] { lon, lat } },
            visibility = "private",
            locationProtected = false,
            parents = new[] { new { parentId = parent, isPrimary = true } },
        });
        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public void Dispose()
    {
        owner?.Dispose();
        shipped.Dispose();
        smallDefault.Dispose();
        defaultAboveCeiling.Dispose();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
