// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Naming the same cave, in the same role, on two trips written in one unit of work.
///
/// The naming asks first whether the trip already names that cave in that role, and a link
/// opened moments earlier has no row to find yet — so the question is partly answered out of
/// what is queued. The trap is answering it out of everything queued: a link opened for another
/// trip is not this trip's naming, and treating it as one makes the second trip name nothing at
/// all, silently, with the save reporting success. Anything writing several trips before saving
/// once — an import, a dataset laid down in a block — is exactly where that bites.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripRoleLinkUnitOfWorkTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private Guid ownerId;
    private long caveTypeId;

    public TripRoleLinkUnitOfWorkTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"trl-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"trl-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task Two_trips_naming_one_cave_in_one_role_and_one_save_both_name_it()
    {
        var cave = await CreateCaveAsync();
        var first = await CreateTripAsync("Naming trip one");
        var second = await CreateTripAsync("Naming trip two");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Both namings before either is saved, which is the whole point: with one save between
        // them the second finds rows and cannot go wrong.
        (await TripRoleLinks.NameFeatureAsync(db, first, cave, "trip-visited", ownerId, default))
            .ShouldBeTrue();
        (await TripRoleLinks.NameFeatureAsync(db, second, cave, "trip-visited", ownerId, default))
            .ShouldBeTrue();
        await db.SaveChangesAsync();

        (await TripRoleLinks.FeatureIdsNamedBy(db, first).Distinct().ToListAsync()).ShouldBe([cave]);
        (await TripRoleLinks.FeatureIdsNamedBy(db, second).Distinct().ToListAsync()).ShouldBe([cave]);
    }

    private async Task<Guid> CreateCaveAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Named Cave {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility = "authenticated",
            locationProtected = false,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateTripAsync(string title)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate = "2026-07-02",
            participants = Array.Empty<object>(),
            caveIds = Array.Empty<Guid>(),
            visibility = "private",
            hadIncident = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
