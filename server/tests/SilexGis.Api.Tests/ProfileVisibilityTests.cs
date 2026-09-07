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
/// The per-field profile visibility contract over real HTTP: what a groupMate, a stranger and an
/// administrator each see of a member, that the picker cannot be used as an address oracle, and
/// that no attribution row falls back to somebody's address.
/// </summary>
public sealed class ProfileVisibilityTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient subject = null!;   // publishes some fields
    private HttpClient groupMate = null!;  // shares a caving group with the subject
    private HttpClient outsider = null!;  // signed in, no shared caving group
    private HttpClient admin = null!;
    private HttpClient manager = null!;   // creates the caving group
    private Guid subjectId;
    private Guid groupMateId;
    private Guid cavingGroupId;

    public ProfileVisibilityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        subjectId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, SubjectEmail);
        groupMateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, GroupMateEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, OutsiderEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, AdminEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, ManagerEmail);

        subject = await AuthHelper.BearerClientAsync(factory, SubjectEmail);
        groupMate = await AuthHelper.BearerClientAsync(factory, GroupMateEmail);
        outsider = await AuthHelper.BearerClientAsync(factory, OutsiderEmail);
        admin = await AuthHelper.BearerClientAsync(factory, AdminEmail);
        manager = await AuthHelper.BearerClientAsync(factory, ManagerEmail);

        await PutSubjectInACavingGroupWithGroupMateAsync();
    }

    private string SubjectEmail => $"vis-subj-{suffix}@t.local";

    private string GroupMateEmail => $"vis-mate-{suffix}@t.local";

    private string OutsiderEmail => $"vis-out-{suffix}@t.local";

    private string AdminEmail => $"vis-adm-{suffix}@t.local";

    private string ManagerEmail => $"vis-mgr-{suffix}@t.local";

    [Fact]
    public async Task Each_viewer_sees_exactly_what_the_subject_shared_with_them()
    {
        await PublishAsync(realName: "cavingGroup", email: "private", phone: "authenticated", address: "cavingGroup");
        await subject.PostAsJsonAsync("/api/v1/me/addresses/", new
        {
            label = "Home",
            country = "Romania",
            city = "Braşov",
            addressText = "Str. Lungă 1",
            geom = new { type = "Point", coordinates = new[] { 25.59, 45.65 } },
            sortOrder = 0,
        });

        var mine = await MemberAsync(subject, subjectId);
        mine.GetProperty("firstName").GetString().ShouldBe("Ana");
        mine.GetProperty("email").GetString().ShouldBe(SubjectEmail);

        var asGroupMate = await MemberAsync(groupMate, subjectId);
        asGroupMate.GetProperty("firstName").GetString().ShouldBe("Ana");
        asGroupMate.GetProperty("lastName").GetString().ShouldBe("Pop");
        asGroupMate.GetProperty("phoneNumber").GetString().ShouldNotBeNull();
        asGroupMate.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
        asGroupMate.GetProperty("addresses").GetArrayLength().ShouldBe(1);

        var asOutsider = await MemberAsync(outsider, subjectId);
        asOutsider.GetProperty("phoneNumber").GetString().ShouldNotBeNull();
        asOutsider.GetProperty("firstName").ValueKind.ShouldBe(JsonValueKind.Null);
        asOutsider.GetProperty("lastName").ValueKind.ShouldBe(JsonValueKind.Null);
        asOutsider.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
        asOutsider.GetProperty("addresses").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task An_administrator_gets_no_bypass()
    {
        // Contact data is not content. An admin reads a member's address only if that member
        // published it. Reversing this decision means changing one arm of the rule and this test.
        await PublishAsync(realName: "private", email: "private", phone: "private", address: "private");

        var asAdmin = await MemberAsync(admin, subjectId);
        asAdmin.GetProperty("firstName").ValueKind.ShouldBe(JsonValueKind.Null);
        asAdmin.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
        asAdmin.GetProperty("phoneNumber").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_map_point_is_withheld_while_the_address_text_is_shown()
    {
        await PublishAsync(address: "authenticated", addressPoint: "private");
        await subject.PostAsJsonAsync("/api/v1/me/addresses/", new
        {
            label = "Home",
            country = "Romania",
            city = "Braşov",
            addressText = "Str. Lungă 1",
            geom = new { type = "Point", coordinates = new[] { 25.59, 45.65 } },
            sortOrder = 0,
        });

        var address = (await MemberAsync(outsider, subjectId)).GetProperty("addresses")[0];
        address.GetProperty("city").GetString().ShouldBe("Braşov");
        address.GetProperty("longitude").ValueKind.ShouldBe(JsonValueKind.Null);
        address.GetProperty("latitude").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_hidden_profile_never_reveals_which_fields_it_is_hiding()
    {
        await PublishAsync(realName: "private", email: "private", phone: "private");

        var raw = await (await outsider.GetAsync($"/api/v1/members/{subjectId}")).Content.ReadAsStringAsync();

        // The list of populated-but-hidden fields is itself information about the subject, so the
        // payload carries neither the settings nor a redaction list.
        raw.ShouldNotContain("visibility");
        raw.ShouldNotContain("redacted");
        raw.ShouldNotContain("hidden");
    }

    [Fact]
    public async Task An_unknown_member_is_not_found()
    {
        var response = await outsider.GetAsync($"/api/v1/members/{Guid.CreateVersion7()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(response)).ShouldBe("member.not_found");
    }

    [Fact]
    public async Task The_directory_pages_and_searches_only_on_the_public_name()
    {
        await PublishAsync(realName: "private", email: "private");

        var response = await outsider.GetAsync("/api/v1/members?search=Ana%20P&page=1&pageSize=10");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        payload.GetProperty("page").GetInt32().ShouldBe(1);
        payload.GetProperty("pageSize").GetInt32().ShouldBe(10);
        // Asserted by id rather than by count: the database is shared across test classes.
        payload.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ShouldContain(subjectId);
    }

    [Fact]
    public async Task The_picker_cannot_be_used_as_an_address_oracle()
    {
        await PublishAsync(email: "private");

        // Searching the exact address of an account whose address is private must find nothing.
        // Hiding it only from the response would still leave the match itself as a working probe.
        var hidden = await SearchAsync(outsider, SubjectEmail);
        hidden.Select(u => u.GetProperty("id").GetGuid()).ShouldNotContain(subjectId);

        // Published, the same query finds them and shows the address.
        await PublishAsync(email: "authenticated");
        var found = await SearchAsync(outsider, SubjectEmail);
        var row = found.Single(u => u.GetProperty("id").GetGuid() == subjectId);
        row.GetProperty("email").GetString().ShouldBe(SubjectEmail);
    }

    [Fact]
    public async Task The_picker_finds_people_by_name_without_disclosing_their_address()
    {
        await PublishAsync(email: "private");

        var rows = await SearchAsync(outsider, "Ana P");

        var row = rows.Single(u => u.GetProperty("id").GetGuid() == subjectId);
        row.GetProperty("label").GetString().ShouldBe("Ana P");
        row.GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task No_attribution_row_falls_back_to_an_address()
    {
        // Accounts are created with the address as the user name, so a "display name or user
        // name" fallback would print the address of everyone who never set a display name. The
        // groupMate here has no display name at all.

        var members = await (await manager.GetAsync($"/api/v1/caving-groups/{cavingGroupId}/members")).Content.ReadAsStringAsync();

        members.ShouldNotContain(GroupMateEmail);
        members.ShouldNotContain("@t.local");
        var row = JsonDocument.Parse(members).RootElement.EnumerateArray()
            .Single(m => m.GetProperty("userId").GetGuid() == groupMateId);
        row.GetProperty("name").GetString().ShouldStartWith("user-");
    }

    [Fact]
    public async Task A_chosen_display_name_is_what_attribution_shows()
    {
        await PublishAsync();

        var members = await (await manager.GetAsync($"/api/v1/caving-groups/{cavingGroupId}/members")).Content.ReadAsStringAsync();

        JsonDocument.Parse(members).RootElement.EnumerateArray()
            .Single(m => m.GetProperty("userId").GetGuid() == subjectId)
            .GetProperty("name").GetString().ShouldBe("Ana P");
    }

    private async Task PublishAsync(
        string realName = "private",
        string email = "private",
        string phone = "private",
        string address = "private",
        string addressPoint = "private")
    {
        // The number is a credential now, so it arrives the only way it can: verified. The profile
        // save has no field for it, and what is under test here is who may read it, not who set it.
        await ConfirmSubjectPhoneAsync();

        (await subject.PutAsJsonAsync("/api/v1/me", new
        {
            firstName = "Ana",
            lastName = "Pop",
            displayName = "Ana P",
            bio = (string?)null,
            cavingClub = (string?)null,
            locale = "en",
            visibility = new
            {
                realName,
                bio = "private",
                email,
                phone,
                cavingClub = "private",
                address,
                addressPoint,
            },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task ConfirmSubjectPhoneAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SilexGisUser>>();
        var user = await userManager.FindByIdAsync(subjectId.ToString());
        user!.PhoneNumber = "+4" + ((uint)subjectId.GetHashCode())
            .ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        user.PhoneNumberConfirmed = true;
        (await userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
    }

    private async Task PutSubjectInACavingGroupWithGroupMateAsync()
    {
        // The slug is derived server-side, so the id comes from the response rather than a lookup.
        var created = await manager.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name = $"Visibility caving group {suffix}",
            description = (string?)null,
            website = (string?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        cavingGroupId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        foreach (var userId in new[] { subjectId, groupMateId })
        {
            (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{cavingGroupId}/members",
                new { caverId = await RosterHelper.CaverIdForAsync(factory, userId), role = "Member" }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The caving group memberships must be in the bearer token's context, so re-issue both clients.
        subject.Dispose();
        groupMate.Dispose();
        subject = await AuthHelper.BearerClientAsync(factory, SubjectEmail);
        groupMate = await AuthHelper.BearerClientAsync(factory, GroupMateEmail);
    }

    private static async Task<JsonElement> MemberAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/members/{id}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    private static async Task<List<JsonElement>> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/users/search?q={Uri.EscapeDataString(query)}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return [.. JsonDocument.Parse(payload).RootElement.EnumerateArray()];
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        subject.Dispose();
        groupMate.Dispose();
        outsider.Dispose();
        admin.Dispose();
        manager.Dispose();
        factory.Dispose();
    }
}
