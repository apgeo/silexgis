// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Api.Tests;

/// <summary>
/// The phone number as a credential, written as the abuses it refuses rather than as the routes
/// it covers: repointing a confirmed number from the profile form, buying another text by
/// deleting the number, and guessing a six-digit code for free.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PhoneCredentialTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];
    private string email = null!;
    private Guid userId;
    private HttpClient me = null!;

    public PhoneCredentialTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            // The phone routes are on the rate-limited credential surface and each test here
            // makes several calls across it.
            ["Auth:RateLimitPerMinute"] = "200",
        });

    public async Task InitializeAsync()
    {
        email = $"phone-{suffix}@t.local";
        userId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, email);
        me = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Fact]
    public async Task A_confirmed_number_cannot_be_repointed_by_saving_the_profile()
    {
        // The abuse: confirm one number, then save a different one from the profile form. If the
        // save could assign the column the confirmed flag would stand over a number nobody
        // proved, and the next sign-in code would be texted there.
        var confirmed = await ConfirmAsync(Number(1));

        var saved = await me.PutAsJsonAsync("/api/v1/me", ProfileBody(Number(2)));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // The rest of the save went through — this is not a rejected request, it is a field the
        // request no longer has.
        (await me.GetFromJsonAsync<JsonElement>("/api/v1/me"))
            .GetProperty("firstName").GetString().ShouldBe("Ana");

        var status = await me.GetFromJsonAsync<JsonElement>("/api/v1/me/phone/");
        status.GetProperty("phoneNumber").GetString().ShouldBe(confirmed);
        status.GetProperty("confirmed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_the_number_does_not_buy_another_text()
    {
        // The abuse: change, delete, change. The cooldown used to live on the user row and
        // removal nulled it, so this loop texted a caller-chosen international number once per
        // two requests, bounded only by a per-IP budget shared with everyone behind that address.
        factory.Messages.Clear();

        var first = await me.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = Number(3) });
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        (await me.DeleteAsync("/api/v1/me/phone")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await me.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = Number(4) });

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("me.phone_resend_too_soon");
        factory.Messages.Messages.Count(m => m.Channel == "sms").ShouldBe(1);
    }

    [Fact]
    public async Task A_wrong_code_costs_the_account_something()
    {
        // The abuse: six digits, a five-minute life, and nothing per account counting the misses.
        var number = Number(5);
        var change = await me.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = number });
        change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        var texted = factory.Messages.LastTo(number).Code;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            (await me.PostAsJsonAsync("/api/v1/me/phone/confirm", new { code = WrongCode(texted) }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await FailedAttemptsAsync()).ShouldBe(2);

        // And the positive half: the right code still works, and clears the count — otherwise a
        // few mistypes spread over a week would eventually lock someone out for no reason.
        var confirm = await me.PostAsJsonAsync("/api/v1/me/phone/confirm", new { code = texted });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        (await FailedAttemptsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_number_another_account_already_holds_is_refused_once_the_caller_has_proved_it_is_theirs()
    {
        var number = Number(6);
        await ConfirmAsync(number);

        var otherEmail = $"phone-other-{suffix}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, otherEmail);
        using var other = await AuthHelper.BearerClientAsync(factory, otherEmail);

        // Starting the change says nothing. Answering "another account already uses that number"
        // here would let any signed-in caller ask, one number at a time, whether a number belongs
        // to a member of this installation — a question the profile rules refuse, because a phone
        // number is a visibility-governed field. The refusal comes at confirmation, where the
        // caller has returned a code texted to the number and so controls it.
        var change = await other.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = number });
        change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        var confirm = await other.PostAsJsonAsync(
            "/api/v1/me/phone/confirm", new { code = factory.Messages.LastTo(number).Code });

        confirm.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await confirm.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().ShouldBe("me.phone_taken");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        me?.Dispose();
        factory.Dispose();
    }

    /// <summary>
    /// A number unique to this class and this test. Every test class shares one database and the
    /// number is unique across accounts now, so a hard-coded one would collide with a neighbour.
    /// </summary>
    private string Number(int index) =>
        "+4" + ((uint)suffix.GetHashCode(StringComparison.Ordinal) % 100000000U)
            .ToString("D8", CultureInfo.InvariantCulture) + index.ToString("D2", CultureInfo.InvariantCulture);

    private static string WrongCode(string real) =>
        real == "000000" ? "111111" : "000000";

    private async Task<string> ConfirmAsync(string number)
    {
        var change = await me.PostAsJsonAsync("/api/v1/me/phone/change", new { phoneNumber = number });
        change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        var confirm = await me.PostAsJsonAsync(
            "/api/v1/me/phone/confirm", new { code = factory.Messages.LastTo(number).Code });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        return number;
    }

    private async Task<int> FailedAttemptsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.GetAccessFailedCountAsync(user!);
    }

    private static object ProfileBody(string phoneNumber) => new
    {
        firstName = "Ana",
        lastName = (string?)null,
        displayName = (string?)null,
        bio = (string?)null,
        phoneNumber,
        cavingClubId = (Guid?)null,
        locale = "en",
        visibility = new
        {
            realName = "private",
            bio = "private",
            email = "private",
            phone = "private",
            cavingClub = "private",
            address = "private",
            addressPoint = "private",
        },
    };
}
