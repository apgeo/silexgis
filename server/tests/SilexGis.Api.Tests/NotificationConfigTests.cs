// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// How a page learns the way it should watch for new notifications. The value is an operator's
/// choice, so the test that matters is the one where the operator has chosen something other than
/// the default: a setting that only ever answers its default proves nothing about being bound.
/// </summary>
public sealed class NotificationConfigTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed record Answer(string BadgeTransport);

    private async Task<Answer> AskAsync(string? configured)
    {
        var settings = new Dictionary<string, string?> { ["Auth:RateLimitPerMinute"] = "500" };
        if (configured is not null)
        {
            settings["Notifications:BadgeTransport"] = configured;
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
    public async Task An_installation_that_has_chosen_nothing_polls()
    {
        (await AskAsync(null)).BadgeTransport.ShouldBe("poll");
    }

    [Fact]
    public async Task The_operators_choice_is_what_the_page_is_told()
    {
        // The non-default value: it is what proves the setting is read at all, and it is the
        // seam a pushed stream lands in without the header being rewritten around it.
        (await AskAsync("sse")).BadgeTransport.ShouldBe("sse");
    }

    [Fact]
    public async Task A_name_this_server_does_not_know_is_a_typo_and_answers_as_the_default()
    {
        // A count that stops moving is a bug report, so a misspelt setting must not be able to
        // switch the timer off by accident.
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
