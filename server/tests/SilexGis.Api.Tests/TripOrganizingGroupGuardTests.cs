// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Naming a club as a trip's organizer is a claim about that club, and only its own people may
/// make it.
/// </summary>
/// <remarks>
/// <para>
/// The organizing group began as description — who ran the trip — and became load-bearing when the
/// calendar started keying "the club's calendar" on it. Unguarded, it let anybody put a row on
/// anybody's calendar. It is not a disclosure: the filter only ever narrows what a reader was
/// already allowed to read, and that is pinned elsewhere. It is an integrity rule, and the thing
/// it protects is a club's account of itself — a row on their calendar that none of their members
/// can explain.
/// </para>
/// <para>
/// The rule is the one already applied to the owning group, reused rather than restated, so the
/// refusal carries the same code and a reader meets one rule about binding a club rather than two
/// that could drift apart.
/// </para>
/// <para>
/// <b>Not yet run.</b> This class needs a PostGIS database, and the machine-wide gate is held by
/// another effort. It compiles and follows the shape of its neighbours, but no verdict exists for
/// it and none should be claimed until one does.
/// </para>
/// </remarks>
public sealed class TripOrganizingGroupGuardTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string tag = Guid.NewGuid().ToString("N")[..8];

    private HttpClient member = null!;
    private HttpClient outsider = null!;
    private Guid clubId;

    public TripOrganizingGroupGuardTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tog-mem-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tog-out-{tag}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var club = new CavingGroup { Name = $"Organizer Club {tag}", Slug = $"organizer-club-{tag}" };
            db.CavingGroups.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;

            await RosterHelper.AddMemberAsync(db, clubId, memberId);
        }

        member = await AuthHelper.BearerClientAsync(factory, $"tog-mem-{tag}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"tog-out-{tag}@t.local");
    }

    /// <summary>
    /// An outsider cannot name the club as organizer; a member can. Both halves, because the
    /// refusal on its own would read the same against a field that had stopped being accepted at
    /// all.
    /// </summary>
    [Fact]
    public async Task Only_a_clubs_own_people_may_name_it_as_the_organizer_of_a_trip()
    {
        var refused = await CreateAsync(outsider, organizingCavingGroupId: clubId);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("code").GetString()
            .ShouldBe(CavingGroupBindingRules.ForbiddenCode, "the refusal reuses the rule that already guards the owning group");

        var allowed = await CreateAsync(member, organizingCavingGroupId: clubId);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// Naming no organizer at all stays open to everybody — the guard is about claiming a club,
    /// not about writing a trip.
    /// </summary>
    [Fact]
    public async Task A_trip_that_names_no_organizer_is_unaffected()
    {
        var response = await CreateAsync(outsider, organizingCavingGroupId: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid? organizingCavingGroupId)
    {
        object body = organizingCavingGroupId is { } group
            ? new
            {
                title = $"Tura {tag}",
                tripDate = "2026-07-01",
                participants = Array.Empty<object>(),
                organizingCavingGroupId = group,
            }
            : new
            {
                title = $"Tura {tag}",
                tripDate = "2026-07-01",
                participants = Array.Empty<object>(),
            };

        return client.PostAsJsonAsync("/api/v1/trip-logs/", body);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        member?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
    }
}
