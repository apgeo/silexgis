// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
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
/// Authoring a list over HTTP: that somebody can actually write one, that its lines keep the
/// order and the identity they were given, that an account with no right to a list is answered
/// as though it were not there, and that rewriting a line keeps the confirmations made against
/// it.
/// </summary>
public sealed class ChecklistAuthoringTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string suffix = Guid.NewGuid().ToString("N")[..8];

    private HttpClient owner = null!;
    private HttpClient outsider = null!;
    private Guid ownerId;

    public ChecklistAuthoringTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cla-own-{suffix}@t.local");

        // A Viewer, deliberately: the seeded Editors group reads past visibility at the widest
        // scope by design, so "an editor could not see it" would say nothing about the audience.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cla-out-{suffix}@t.local");

        owner = await AuthHelper.BearerClientAsync(factory, $"cla-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"cla-out-{suffix}@t.local");
    }

    [Fact]
    public async Task A_list_is_written_read_back_rewritten_and_deleted()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = "Before we set off",
            description = "The things that have to be settled.",
            visibility = (int)Visibility.Private,
            items = new[] { new { text = "Permit obtained" }, new { text = "Key collected" } },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var list = await created.Content.ReadFromJsonAsync<ChecklistPayload>();
        list.ShouldNotBeNull();
        list.OwnerUserId.ShouldBe(ownerId);
        list.Items.Select(x => x.Text).ShouldBe(["Permit obtained", "Key collected"]);
        list.Items.Select(x => x.SortOrder).ShouldBe([0, 1]);

        var read = await owner.GetFromJsonAsync<ChecklistPayload>($"/api/v1/checklists/{list.Id}");
        read.ShouldNotBeNull();
        read.Items.Select(x => x.Text).ShouldBe(["Permit obtained", "Key collected"]);

        var listing = await owner.GetFromJsonAsync<List<ChecklistPayload>>("/api/v1/checklists");
        listing.ShouldNotBeNull();
        listing.ShouldContain(x => x.Id == list.Id);

        // Reordered, reworded and extended in one request: the order is the order they arrive in.
        var keptId = list.Items[1].Id;
        var updated = await owner.PutAsJsonAsync($"/api/v1/checklists/{list.Id}", new
        {
            title = "Before we set off",
            description = (string?)null,
            visibility = (int)Visibility.Private,
            items = new object[]
            {
                new { id = keptId, text = "Key collected from the farm" },
                new { text = "Callout arranged" },
            },
        });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        var after = await updated.Content.ReadFromJsonAsync<ChecklistPayload>();
        after.ShouldNotBeNull();
        after.Items.Select(x => x.Text).ShouldBe(["Key collected from the farm", "Callout arranged"]);

        // The line named by id is the same row, so anything recorded against it still is.
        after.Items[0].Id.ShouldBe(keptId);

        // The line left out of the request is gone rather than kept somewhere invisible.
        after.Items.ShouldNotContain(x => x.Text == "Permit obtained");

        var deleted = await owner.DeleteAsync($"/api/v1/checklists/{list.Id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/checklists/{list.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_list_is_refused_to_an_account_its_audience_does_not_reach_and_offered_when_it_does()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = $"Private list {suffix}",
            visibility = (int)Visibility.Private,
            items = new[] { new { text = "Permit obtained" } },
        });
        var mine = await response.Content.ReadFromJsonAsync<ChecklistPayload>();
        mine.ShouldNotBeNull();

        // The unreadable state is constructed rather than assumed: an account with no grant of
        // any kind over this row, against a row whose audience is nobody but its owner.
        (await outsider.GetAsync($"/api/v1/checklists/{mine.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        var hidden = await outsider.GetFromJsonAsync<List<ChecklistPayload>>("/api/v1/checklists");
        hidden.ShouldNotBeNull();
        hidden.ShouldNotContain(x => x.Id == mine.Id);

        // Writing it is refused as absent too, so that probing by writing says no more than
        // reading does.
        var refused = await outsider.PutAsJsonAsync($"/api/v1/checklists/{mine.Id}", new
        {
            title = "Taken over",
            visibility = (int)Visibility.Private,
            items = Array.Empty<object>(),
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/v1/checklists/{mine.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // And the positive half in the same test, so the refusals above are the audience
        // answering rather than the route being broken for everyone: the same account reads the
        // same row the moment its audience reaches them.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Checklists.Where(x => x.Id == mine.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Visibility, Visibility.Authenticated));
        }

        (await outsider.GetAsync($"/api/v1/checklists/{mine.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var offered = await outsider.GetFromJsonAsync<List<ChecklistPayload>>("/api/v1/checklists");
        offered.ShouldNotBeNull();
        offered.ShouldContain(x => x.Id == mine.Id);

        // Reading it still is not writing it: a wider audience is not a wider right.
        var stillRefused = await outsider.PutAsJsonAsync($"/api/v1/checklists/{mine.Id}", new
        {
            title = "Taken over",
            visibility = (int)Visibility.Authenticated,
            items = Array.Empty<object>(),
        });
        stillRefused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_list_needs_a_title_and_lines_that_say_something_and_a_caller_at_all()
    {
        var untitled = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = string.Empty,
            visibility = (int)Visibility.Private,
            items = Array.Empty<object>(),
        });
        untitled.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var blankLine = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = "A list",
            visibility = (int)Visibility.Private,
            items = new[] { new { text = "   " } },
        });
        blankLine.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/checklists")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        var anonymousWrite = await anonymous.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = "A list",
            visibility = (int)Visibility.Private,
            items = Array.Empty<object>(),
        });
        anonymousWrite.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deleting_a_list_leaves_the_purposes_that_named_it_naming_none()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = $"Named by a purpose {suffix}",
            visibility = (int)Visibility.Authenticated,
            items = new[] { new { text = "Permit obtained" } },
        });
        var list = await response.Content.ReadFromJsonAsync<ChecklistPayload>();
        list.ShouldNotBeNull();

        long tripTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var type = new TripType
            {
                Code = $"cla-{suffix}",
                Name = $"Purpose {suffix}",
                DefaultChecklistId = list.Id,
            };
            db.TripTypes.Add(type);
            await db.SaveChangesAsync();
            tripTypeId = type.Id;
        }

        (await owner.DeleteAsync($"/api/v1/checklists/{list.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var named = await db.TripTypes.AsNoTracking()
                .Where(x => x.Id == tripTypeId)
                .Select(x => x.DefaultChecklistId)
                .FirstAsync();

            // The purpose outlives the list, naming none — rather than the list being
            // undeletable because something points at it.
            named.ShouldBeNull();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        factory.Dispose();
    }

    /// <summary>
    /// The audience arrives as the name the wire uses rather than as the enum, because that is
    /// what the client is handed and reading it any other way here would test a shape nobody
    /// receives.
    /// </summary>
    private sealed record ChecklistPayload(
        Guid Id,
        string Title,
        string? Description,
        Guid OwnerUserId,
        Guid? CavingGroupId,
        string Visibility,
        List<ChecklistItemPayload> Items);

    private sealed record ChecklistItemPayload(Guid Id, string Text, int SortOrder);
}
