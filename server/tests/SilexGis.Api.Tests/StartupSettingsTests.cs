// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// An application given a setting that cannot mean anything does not start, and says which.
/// </summary>
/// <remarks>
/// <para>
/// The rules themselves — which values are refused and what the message says — are tested where
/// they live, without a host. What is asserted here is the part those tests cannot see: that the
/// rules are actually asked while a real application is being started. A validator nobody
/// registered passes every one of its own tests.
/// </para>
/// <para>
/// Each case starts the same application twice, once with the value that must be refused and
/// once with a neighbouring value that must not be, so that a start failing for some unrelated
/// reason cannot pass for the refusal.
/// </para>
/// </remarks>
public sealed class StartupSettingsTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture postgres;

    public StartupSettingsTests(PostgresFixture postgres) => this.postgres = postgres;

    private SilexGisApiFactory Factory(string key, string value) =>
        new(postgres.ConnectionString, new Dictionary<string, string?> { [key] = value });

    /// <summary>Every message of a failure and of what caused it, one after another.</summary>
    private static string Said(Exception? failure)
    {
        var said = new List<string>();
        for (; failure is not null; failure = failure.InnerException)
        {
            said.Add(failure.Message);
        }

        return string.Join(" | ", said);
    }

    [Theory]
    [InlineData("TripTracking:ShareLifetime", "00:00:00", "00:06:00", "SILEXGIS__TripTracking__ShareLifetime")]
    [InlineData("TripTracking:ShareGraceAfterClose", "-00:00:01", "00:00:00", "SILEXGIS__TripTracking__ShareGraceAfterClose")]
    [InlineData("TripPastTracks:Retention", "00:00:00", "30.00:00:00", "SILEXGIS__TripPastTracks__Retention")]
    [InlineData("TripTracking:PublicRateLimitPerMinute", "0", "3", "SILEXGIS__TripTracking__PublicRateLimitPerMinute")]
    [InlineData("Auth:RateLimitPerMinute", "0", "200", "SILEXGIS__Auth__RateLimitPerMinute")]
    [InlineData("Qr:RateLimitPerMinute", "-1", "6", "SILEXGIS__Qr__RateLimitPerMinute")]
    [InlineData("CalendarFeed:RateLimitPerMinute", "0", "3", "SILEXGIS__CalendarFeed__RateLimitPerMinute")]
    public async Task A_value_that_cannot_mean_anything_refuses_the_start_and_names_its_setting(
        string key, string refused, string accepted, string variable)
    {
        using (var broken = Factory(key, refused))
        {
            var failure = Should.Throw<Exception>(() => broken.CreateClient());

            Said(failure).ShouldContain(variable);
        }

        using var working = Factory(key, accepted);
        using var client = working.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
