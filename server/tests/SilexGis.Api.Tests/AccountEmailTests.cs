// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The verified email-change flow. The live address is never touched until the token sent to the
/// new one comes back, and a request for an address that already has an account discloses nothing.
/// </summary>
/// <remarks>
/// The harness cannot replace registered services, so the token is minted here through the same
/// Identity provider the endpoint uses rather than read out of a captured message.
/// </remarks>
public sealed class AccountEmailTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient me = null!;
    private Guid myId;

    public AccountEmailTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        myId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, StartingEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, NeighbourEmail);
        me = await AuthHelper.BearerClientAsync(factory, StartingEmail);
    }

    private string StartingEmail => $"mail-me-{suffix}@t.local";

    private string NeighbourEmail => $"mail-nb-{suffix}@t.local";

    private string TargetEmail => $"mail-new-{suffix}@t.local";

    [Fact]
    public async Task A_change_is_staged_and_only_applied_once_confirmed()
    {
        (await RequestChangeAsync(TargetEmail)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Staged, not applied: signing in still uses the old address.
        var staged = await GetMeAsync();
        staged.GetProperty("email").GetString().ShouldBe(StartingEmail);
        staged.GetProperty("pendingEmail").GetString().ShouldBe(TargetEmail);

        var confirm = await me.PostAsJsonAsync(
            "/api/v1/me/email/confirm", new { token = await ChangeTokenAsync(TargetEmail) });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());

        var applied = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync()).RootElement;
        applied.GetProperty("email").GetString().ShouldBe(TargetEmail);
        applied.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
        applied.GetProperty("pendingEmail").ValueKind.ShouldBe(JsonValueKind.Null);

        // The account is still reachable — the new address signs in.
        using var reauthenticated = await AuthHelper.BearerClientAsync(factory, TargetEmail);
        (await reauthenticated.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_warning_to_the_old_address_carries_the_installation_address()
    {
        // The warning is sent straight through the dispatcher rather than queued, and only the
        // queue's sender used to supply the installation address, so this link rendered empty.
        await RequestChangeAsync(TargetEmail);
        (await me.PostAsJsonAsync("/api/v1/me/email/confirm", new { token = await ChangeTokenAsync(TargetEmail) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var warning = factory.Messages.LastTo(StartingEmail);
        warning.Body.ShouldContain(TargetEmail);
        warning.Body.ShouldContain("http://localhost");
    }

    [Fact]
    public async Task Confirming_moves_the_user_name_only_when_it_was_the_old_address()
    {
        // Accounts are created with the address as the user name, so leaving it behind would make
        // people log in under an address that no longer exists.
        await RequestChangeAsync(TargetEmail);
        await me.PostAsJsonAsync("/api/v1/me/email/confirm", new { token = await ChangeTokenAsync(TargetEmail) });

        (await UserAsync()).UserName.ShouldBe(TargetEmail);
    }

    [Fact]
    public async Task A_chosen_user_name_survives_an_address_change()
    {
        var handle = $"ana-{suffix}";
        (await me.PutAsJsonAsync("/api/v1/me/username", new { username = handle }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await RequestChangeAsync(TargetEmail);
        await me.PostAsJsonAsync("/api/v1/me/email/confirm", new { token = await ChangeTokenAsync(TargetEmail) });

        var user = await UserAsync();
        user.Email.ShouldBe(TargetEmail);
        user.UserName.ShouldBe(handle);
    }

    [Fact]
    public async Task Asking_for_another_change_within_a_minute_is_refused_and_mails_nothing()
    {
        // The positive half: the first request stages the change and mails the new address.
        factory.Messages.Clear();
        (await RequestChangeAsync(TargetEmail)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Messages.Messages.Count(m => m.Recipient == TargetEmail).ShouldBe(1);

        // Each request mails an address the caller chose; without the gap the resend route keeps,
        // one session could mail a stranger as fast as the rate limit allows.
        var again = await RequestChangeAsync(TargetEmail);
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(again)).ShouldBe("me.email_resend_too_soon");
        var elsewhere = await RequestChangeAsync($"mail-other-{suffix}@t.local");
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(elsewhere)).ShouldBe("me.email_resend_too_soon");
        factory.Messages.Messages.Count(m => m.Recipient == TargetEmail).ShouldBe(1);
        factory.Messages.Messages.ShouldNotContain(m => m.Recipient == $"mail-other-{suffix}@t.local");
    }

    [Fact]
    public async Task A_request_for_a_taken_address_holds_the_gap_just_as_a_free_one_does()
    {
        // If only a free address started the gap, the answer to the next request would tell a
        // signed-in caller whether the first address has an account here.
        factory.Messages.Clear();
        (await RequestChangeAsync(NeighbourEmail)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var next = await RequestChangeAsync(TargetEmail);
        next.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(next)).ShouldBe("me.email_resend_too_soon");

        // And the taken address itself was never mailed, nor staged.
        factory.Messages.Messages.ShouldNotContain(m => m.Recipient == NeighbourEmail);
        factory.Messages.Messages.ShouldNotContain(m => m.Recipient == TargetEmail);
        (await GetMeAsync()).GetProperty("pendingEmail").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Abandoning_a_change_does_not_reopen_the_gap()
    {
        factory.Messages.Clear();
        (await RequestChangeAsync(TargetEmail)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await me.DeleteAsync("/api/v1/me/email/pending")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A change-and-cancel loop would otherwise mail a stranger once per pair of calls.
        var again = await RequestChangeAsync(TargetEmail);
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(again)).ShouldBe("me.email_resend_too_soon");
        factory.Messages.Messages.Count(m => m.Recipient == TargetEmail).ShouldBe(1);
    }

    [Fact]
    public async Task An_address_that_cannot_be_the_user_name_is_refused_before_anything_is_staged()
    {
        // A user name accepts fewer characters than an address; this account's user name follows
        // its address, so the change would confirm with the rename failing behind it and the old
        // address left as the account's public name.
        var response = await RequestChangeAsync($"o'brien-{suffix}@t.local");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_not_usable");
        (await GetMeAsync()).GetProperty("pendingEmail").ValueKind.ShouldBe(JsonValueKind.Null);

        // The positive half: an account with a chosen handle keeps it, so the same address is fine.
        (await me.PutAsJsonAsync("/api/v1/me/username", new { username = $"ana-{suffix}" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RequestChangeAsync($"o'brien-{suffix}@t.local")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await GetMeAsync()).GetProperty("pendingEmail").GetString().ShouldBe($"o'brien-{suffix}@t.local");
    }

    [Fact]
    public async Task A_change_whose_user_name_was_taken_meanwhile_is_refused_whole()
    {
        await RequestChangeAsync(TargetEmail);

        // After the request, another account takes the new address as its handle.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
            var neighbour = (await users.FindByEmailAsync(NeighbourEmail))!;
            (await users.SetUserNameAsync(neighbour, TargetEmail)).Succeeded.ShouldBeTrue();
        }

        var response = await me.PostAsJsonAsync(
            "/api/v1/me/email/confirm", new { token = await ChangeTokenAsync(TargetEmail) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_taken");
        // Neither half moved: the address and the user name are both still the old address.
        var user = await UserAsync();
        user.Email.ShouldBe(StartingEmail);
        user.UserName.ShouldBe(StartingEmail);
    }

    [Fact]
    public async Task Requesting_an_address_that_is_taken_stages_nothing_and_says_nothing()
    {
        // Answering "that address is taken" would tell any signed-in caller whether an address has
        // an account here — the probe the picker gating exists to remove. The password-reset flow
        // already makes this trade the same way.
        var response = await RequestChangeAsync(NeighbourEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await GetMeAsync()).GetProperty("pendingEmail").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Requesting_the_current_address_is_refused()
    {
        var response = await RequestChangeAsync(StartingEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_unchanged");
    }

    [Fact]
    public async Task A_bad_token_is_refused_and_leaves_the_change_pending()
    {
        await RequestChangeAsync(TargetEmail);

        var response = await me.PostAsJsonAsync("/api/v1/me/email/confirm", new { token = "not-a-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_confirm_invalid");
        (await GetMeAsync()).GetProperty("email").GetString().ShouldBe(StartingEmail);
    }

    [Fact]
    public async Task A_token_minted_for_one_address_cannot_confirm_another()
    {
        await RequestChangeAsync(TargetEmail);

        // Identity binds the token to the address it was generated for.
        var response = await me.PostAsJsonAsync(
            "/api/v1/me/email/confirm", new { token = await ChangeTokenAsync($"elsewhere-{suffix}@t.local") });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetMeAsync()).GetProperty("email").GetString().ShouldBe(StartingEmail);
    }

    [Fact]
    public async Task Confirming_without_a_pending_change_is_refused()
    {
        var response = await me.PostAsJsonAsync("/api/v1/me/email/confirm", new { token = "anything" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_change_missing");
    }

    [Fact]
    public async Task A_pending_change_can_be_abandoned()
    {
        await RequestChangeAsync(TargetEmail);

        (await me.DeleteAsync("/api/v1/me/email/pending")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetMeAsync()).GetProperty("pendingEmail").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Resending_is_throttled()
    {
        await RequestChangeAsync(TargetEmail);

        var immediate = await me.PostAsync("/api/v1/me/email/resend", null);

        immediate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(immediate)).ShouldBe("me.email_resend_too_soon");
    }

    [Fact]
    public async Task Resending_without_a_pending_change_is_refused()
    {
        var response = await me.PostAsync("/api/v1/me/email/resend", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_change_missing");
    }

    [Fact]
    public async Task Verifying_an_already_confirmed_address_is_refused()
    {
        var response = await me.PostAsync("/api/v1/me/email/verify", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("me.email_already_confirmed");
    }

    [Fact]
    public async Task A_malformed_address_is_rejected_before_anything_is_staged()
    {
        var response = await RequestChangeAsync("not-an-address");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).ShouldBe("validation.failed");
    }

    private Task<HttpResponseMessage> RequestChangeAsync(string newEmail) =>
        me.PostAsJsonAsync("/api/v1/me/email/change", new { newEmail });

    private async Task<string> ChangeTokenAsync(string newEmail)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await userManager.FindByIdAsync(myId.ToString());
        return await userManager.GenerateChangeEmailTokenAsync(user!, newEmail);
    }

    private async Task<SilexGisUser> UserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Users.AsNoTracking().FirstAsync(u => u.Id == myId);
    }

    private async Task<JsonElement> GetMeAsync()
    {
        var response = await me.GetAsync("/api/v1/me");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        me.Dispose();
        factory.Dispose();
    }
}
