// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Shouldly;
using SilexGis.Api.Common;

namespace SilexGis.Api.Tests;

/// <summary>
/// The requests-a-minute settings, and the refusal to start on one no window can be built from.
/// </summary>
/// <remarks>
/// No database and no host: the subject is what is done with a number while the application is
/// being put together, before anything is listening.
/// </remarks>
public class PerMinuteLimitTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void The_limiter_itself_refuses_a_limit_that_is_not_positive_but_only_when_first_used()
    {
        // The fact the check exists for, asserted against the framework rather than taken on
        // trust: a window is built the first time a request needs one, and building it from zero
        // throws. Without a check at start that is a server error on every request of the surface.
        Should.Throw<ArgumentException>(() => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 0,
            QueueLimit = 0,
        }));
    }

    [Theory]
    [InlineData("Auth:RateLimitPerMinute", "SILEXGIS__Auth__RateLimitPerMinute")]
    [InlineData("Qr:RateLimitPerMinute", "SILEXGIS__Qr__RateLimitPerMinute")]
    [InlineData("TripTracking:PublicRateLimitPerMinute", "SILEXGIS__TripTracking__PublicRateLimitPerMinute")]
    [InlineData("CalendarFeed:RateLimitPerMinute", "SILEXGIS__CalendarFeed__RateLimitPerMinute")]
    public void A_limit_of_nothing_or_less_refuses_to_start_and_names_the_variable(string key, string variable)
    {
        foreach (var value in new[] { "0", "-1", "-120" })
        {
            var refusal = Should.Throw<InvalidOperationException>(
                () => PerMinuteLimit.Read(Configuration((key, value)), key, 60));

            refusal.Message.ShouldContain(key);
            refusal.Message.ShouldContain(variable);
            refusal.Message.ShouldContain($"got {value}");
            // What somebody writing zero most likely meant, and how that is said instead.
            refusal.Message.ShouldContain("100000");
        }
    }

    [Fact]
    public void A_positive_limit_is_read_as_written_and_an_absent_one_is_the_default()
    {
        const string key = "TripTracking:PublicRateLimitPerMinute";

        PerMinuteLimit.Read(Configuration((key, "1")), key, 120).ShouldBe(1);
        PerMinuteLimit.Read(Configuration((key, "3")), key, 120).ShouldBe(3);
        PerMinuteLimit.Read(Configuration((key, "100000")), key, 120).ShouldBe(100000);
        PerMinuteLimit.Read(Configuration(), key, 120).ShouldBe(120);
        // Another surface's limit is not this one's.
        PerMinuteLimit.Read(Configuration(("Auth:RateLimitPerMinute", "0")), key, 120).ShouldBe(120);
    }

    [Fact]
    public void Only_a_value_that_is_not_positive_has_a_problem()
    {
        PerMinuteLimit.Problem("Auth:RateLimitPerMinute", 1).ShouldBeNull();
        PerMinuteLimit.Problem("Auth:RateLimitPerMinute", int.MaxValue).ShouldBeNull();
        PerMinuteLimit.Problem("Auth:RateLimitPerMinute", 0).ShouldNotBeNull();
        PerMinuteLimit.Problem("Auth:RateLimitPerMinute", int.MinValue).ShouldNotBeNull();
    }
}
