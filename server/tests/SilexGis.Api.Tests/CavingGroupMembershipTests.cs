// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Leaving a caving group, and who may take somebody else out of one. A member leaves of their
/// own accord without holding any right over the group; a member labelled as the group's owner
/// leaves just the same, because the role is a roster label and not a seat the group must keep
/// filled; and the one refusal on the way out — leaving would strand the installation without a
/// live full administrator — is the lockout guard's, pinned beside the guard's other doors in
/// <see cref="AccessModelTests"/>.
/// </summary>
public sealed class CavingGroupMembershipTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient manager = null!;
    private HttpClient member = null!;
    private HttpClient outsider = null!;
    private Guid memberId;
    private Guid outsiderId;

    public CavingGroupMembershipTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"cgm-mgr-{suffix}@t.local");
        memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cgm-mem-{suffix}@t.local");
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cgm-out-{suffix}@t.local");

        // The lockout guard refuses a departure that would leave no live full administrator, and
        // a class that mints none would depend on whichever class ran before it in the shared
        // database. One of its own keeps every departure below about the group and nothing else.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"cgm-adm-{suffix}@t.local");

        manager = await AuthHelper.BearerClientAsync(factory, $"cgm-mgr-{suffix}@t.local");
        member = await AuthHelper.BearerClientAsync(factory, $"cgm-mem-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"cgm-out-{suffix}@t.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        manager.Dispose();
        member.Dispose();
        outsider.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task A_member_leaves_on_their_own_without_holding_any_right_over_the_group()
    {
        var groupId = await CreateGroupAsync("Leavers");
        var caverId = await JoinAsync(groupId, memberId, CavingGroupRole.Member);

        // A viewer holds no write on the group: the same person cannot edit the roster, so the
        // departure below rides on the membership itself and on nothing else.
        (await member.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members",
            new { caverId, role = "Admin" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var left = await member.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}");
        left.StatusCode.ShouldBe(HttpStatusCode.NoContent, await left.Content.ReadAsStringAsync());

        (await MembershipExistsAsync(groupId, caverId)).ShouldBeFalse();

        // Gone means gone: asking again finds no membership to leave.
        var again = await member.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}");
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(again)).ShouldBe("caving_group.member_not_found");
    }

    /// <summary>
    /// The owner and admin roles on a roster label people and gate nothing — rights over a group
    /// flow from access entries alone — so there is no "last owner" a group has to keep. An owner
    /// leaving is the same act as a member leaving, and the group goes on without one.
    /// </summary>
    [Fact]
    public async Task The_only_owner_may_leave_because_the_role_is_a_label_and_not_a_seat()
    {
        // Creating a group enrols its creator as an owner, so the group starts with two.
        var groupId = await CreateGroupAsync("Ownerless");
        var caverId = await JoinAsync(groupId, memberId, CavingGroupRole.Owner);
        var creatorCaverId = await RosterHelper.CaverIdForAsync(
            factory, (await manager.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid());

        // The creator goes first, leaving the member as the only owner there is.
        var creatorLeft = await manager.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{creatorCaverId}");
        creatorLeft.StatusCode.ShouldBe(HttpStatusCode.NoContent, await creatorLeft.Content.ReadAsStringAsync());

        var left = await member.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}");
        left.StatusCode.ShouldBe(HttpStatusCode.NoContent, await left.Content.ReadAsStringAsync());

        (await MembershipExistsAsync(groupId, caverId)).ShouldBeFalse();

        // The group is still there, with nobody in it, and still administrable by somebody who
        // holds the right — the manager's listing proves it was not taken down with its owners.
        var members = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/caving-groups/{groupId}/members");
        members.GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// The exemption is exactly one person wide: it covers the membership whose account is the
    /// caller's, and nobody else's. Another member without the group's write right is refused
    /// the way any roster edit is.
    /// </summary>
    [Fact]
    public async Task Taking_somebody_else_out_is_a_roster_edit_and_needs_the_write_right()
    {
        var groupId = await CreateGroupAsync("Guarded");
        var caverId = await JoinAsync(groupId, memberId, CavingGroupRole.Member);
        _ = await JoinAsync(groupId, outsiderId, CavingGroupRole.Admin);

        // A fellow member — even one labelled admin, since the label grants nothing.
        var refused = await outsider.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
        (await CodeAsync(refused)).ShouldBe("access.forbidden");
        (await MembershipExistsAsync(groupId, caverId)).ShouldBeTrue();

        // Whereas the manager, who holds the write, may.
        var removed = await manager.DeleteAsync($"/api/v1/caving-groups/{groupId}/members/{caverId}");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
        (await MembershipExistsAsync(groupId, caverId)).ShouldBeFalse();
    }

    private async Task<Guid> CreateGroupAsync(string tag)
    {
        var created = await manager.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name = $"{tag} {suffix}",
            description = (string?)null,
            website = (string?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> JoinAsync(Guid groupId, Guid userId, CavingGroupRole role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await RosterHelper.AddMemberAsync(db, groupId, userId, role)).Id;
    }

    private async Task<bool> MembershipExistsAsync(Guid groupId, Guid caverId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CavingGroupMemberships.AnyAsync(m => m.CavingGroupId == groupId && m.CaverId == caverId);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
