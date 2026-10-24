// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The fixed bands the cave-density layer can colour by are an installation setting, published in
/// the map's configuration and refused at start when they are not a list of bands.
/// </summary>
/// <remarks>
/// Only the colouring reads them. The density route answers the same cells and counts whatever the
/// bands are, which the last test holds to.
/// </remarks>
public sealed class DensityBandsConfigurationTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;
    private readonly SilexGisApiFactory shipped;
    private readonly SilexGisApiFactory configured;

    private HttpClient shippedViewer = null!;
    private HttpClient configuredViewer = null!;

    public DensityBandsConfigurationTests(PostgresFixture postgres)
    {
        this.postgres = postgres;
        shipped = new SilexGisApiFactory(postgres.ConnectionString);
        configured = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:DensityFixedBands"] = " 0.5, 2,10 ,50",
        });
    }

    public async ValueTask InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(shipped, GlobalRoles.Viewer, $"bands-a-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(configured, GlobalRoles.Viewer, $"bands-b-{suffix}@t.local");
        shippedViewer = await AuthHelper.BearerClientAsync(shipped, $"bands-a-{suffix}@t.local");
        configuredViewer = await AuthHelper.BearerClientAsync(configured, $"bands-b-{suffix}@t.local");
    }

    [Fact]
    public async Task The_shipped_bands_are_one_five_and_twenty_caves_per_square_kilometre()
    {
        var config = (await shippedViewer.GetFromJsonAsync<JsonObject>("/api/v1/map/config"))!;
        config["densityFixedBands"]!.AsArray().Select(n => n!.GetValue<double>()).ShouldBe([1d, 5d, 20d]);
    }

    [Fact]
    public async Task Configured_bands_are_published_as_written_spaces_aside()
    {
        var config = (await configuredViewer.GetFromJsonAsync<JsonObject>("/api/v1/map/config"))!;
        config["densityFixedBands"]!.AsArray().Select(n => n!.GetValue<double>()).ShouldBe([0.5, 2d, 10d, 50d]);
    }

    [Fact]
    public async Task The_map_configuration_still_needs_a_signed_in_caller()
    {
        using var anonymous = shipped.CreateClient();
        (await anonymous.GetAsync("/api/v1/map/config")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5,1")]
    [InlineData("1,1")]
    [InlineData("0,1")]
    [InlineData("-1,2")]
    [InlineData("1,2,3,4,5")]
    [InlineData("one,two")]
    [InlineData("1;5;20")]
    [InlineData("NaN")]
    [InlineData("1,,5")]
    public void Bands_that_are_not_ascending_positive_numbers_refuse_to_start(string value)
    {
        MapOptions.TryParseDensityBands(value, out _).ShouldBeFalse();

        using var misconfigured = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Map:DensityFixedBands"] = value,
        });
        var refusal = Should.Throw<OptionsValidationException>(() => misconfigured.CreateClient());
        refusal.Message.ShouldContain("Map:DensityFixedBands");
    }

    [Theory]
    [InlineData("20", new[] { 20d })]
    [InlineData("1,5,20", new[] { 1d, 5d, 20d })]
    [InlineData("0.25,1.5,4,12.5", new[] { 0.25, 1.5, 4d, 12.5 })]
    public void A_list_of_one_to_four_ascending_bounds_reads_with_a_point_for_decimals(string value, double[] expected)
    {
        MapOptions.TryParseDensityBands(value, out var bounds).ShouldBeTrue();
        bounds.ShouldBe(expected);
    }

    [Fact]
    public async Task The_density_answer_does_not_depend_on_the_bands()
    {
        // Two installations that differ only in their bands answer the same window identically:
        // the bands are a colouring, and nothing the server counts or withholds moves with them.
        const string Window = "/api/v1/map/density?bbox=24.0,45.0,26.0,46.5";
        var first = await shippedViewer.GetStringAsync(Window);
        var second = await configuredViewer.GetStringAsync(Window);
        second.ShouldBe(first);
    }

    public ValueTask DisposeAsync()
    {
        shippedViewer.Dispose();
        configuredViewer.Dispose();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        shipped.Dispose();
        configured.Dispose();
    }
}
