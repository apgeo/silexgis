// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// How a page learns the way it should watch for new notifications. The answer is what this build
/// implements, not a setting: a value an operator could write that names a transport nobody built
/// would leave the unread count with nothing to move it.
/// </summary>
public sealed class NotificationConfigTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record Answer(string BadgeTransport);

    private async Task<Answer> AskAsync(string? leftoverSetting)
    {
        var settings = new Dictionary<string, string?> { ["Auth:RateLimitPerMinute"] = "500" };
        if (leftoverSetting is not null)
        {
            // The option an earlier release read. An installation that still carries it in its
            // environment must neither fail nor be told a transport that does not exist.
            settings["Notifications:BadgeTransport"] = leftoverSetting;
        }

        using var factory = new SilexGisApiFactory(postgres.ConnectionString, settings);
        var email = $"badge-{Guid.NewGuid().ToString("N")[..8]}@t.local";
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, email);
        var client = await AuthHelper.BearerClientAsync(factory, email);

        var response = await client.GetAsync("/api/v1/notifications/config");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Answer>())!;
    }

    [Fact]
    public async Task A_signed_in_page_is_told_to_poll()
    {
        (await AskAsync(null)).BadgeTransport.ShouldBe("poll");
    }

    [Fact]
    public async Task A_setting_left_over_from_an_earlier_release_names_a_transport_that_is_not_offered()
    {
        // 'sse' was once accepted and left the count moving only on a click. It is answered as
        // the one transport that exists, whatever an old environment still says.
        (await AskAsync("sse")).BadgeTransport.ShouldBe("poll");
        (await AskAsync("Websockets!")).BadgeTransport.ShouldBe("poll");
    }

    [Fact]
    public async Task Nobody_signed_in_is_told_nothing()
    {
        using var factory = new SilexGisApiFactory(postgres.ConnectionString);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/notifications/config");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
