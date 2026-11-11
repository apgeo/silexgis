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
    private Guid outsiderId;

    public ChecklistAuthoringTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cla-own-{suffix}@t.local");

        // A Viewer, deliberately: the seeded Editors group reads past visibility at the widest
        // scope by design, so "an editor could not see it" would say nothing about the audience.
        outsiderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"cla-out-{suffix}@t.local");

        owner = await AuthHelper.BearerClientAsync(factory, $"cla-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"cla-out-{suffix}@t.local");
    }

    /// <summary>
    /// The door the domain exists for: one entry naming one list, authored through the same
    /// route every other shareable object is shared through. The list's own routes answer the
    /// outsider as though it were not there until the grant, read it afterwards, and refuse what
    /// the entry did not name; the sharing route itself refuses a reader who may not manage it.
    /// </summary>
    [Fact]
    public async Task One_list_is_handed_to_one_person_by_name_through_the_object_access_route()
    {
        var created = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title = $"Shared list {suffix}",
            description = (string?)null,
            visibility = (int)Visibility.Private,
            items = new[] { new { text = "Permit obtained" } },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<ChecklistPayload>())!.Id;

        var grant = new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId = outsiderId,
                    effect = "allow",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        };

        // Before the grant: the target parses, the route answers, and the outsider is admitted
        // to nothing — not to the list, and not to its rules, which would state that it exists.
        (await outsider.GetAsync($"/api/v1/checklists/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.PutAsJsonAsync($"/api/v1/objects/checklist/{id}/access", grant))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var granted = await owner.PutAsJsonAsync($"/api/v1/objects/checklist/{id}/access", grant);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());

        (await outsider.GetAsync($"/api/v1/checklists/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var listing = await outsider.GetFromJsonAsync<List<ChecklistPayload>>("/api/v1/checklists");
        listing.ShouldNotBeNull();
        listing.ShouldContain(x => x.Id == id);

        // Reading is not writing, and it is not managing: the entry named one action and confers
        // that one only. A reader who may not manage the rules is refused rather than hidden
        // from, because they already know the list exists.
        (await outsider.PutAsJsonAsync($"/api/v1/checklists/{id}", new
        {
            title = $"Renamed by grantee {suffix}",
            description = (string?)null,
            visibility = (int)Visibility.Private,
            items = new[] { new { text = "Permit obtained" } },
        })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PutAsJsonAsync($"/api/v1/objects/checklist/{id}/access", grant))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var effective = await outsider.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/v1/objects/checklist/{id}/effective-access");
        (effective.GetProperty("actions").GetString() ?? string.Empty).ShouldContain("Read");

        // Withdrawing the entry withdraws the reach, so the grant above is what admitted the
        // outsider rather than anything the account already held.
        var withdrawn = await owner.PutAsJsonAsync($"/api/v1/objects/checklist/{id}/access",
            new { entries = Array.Empty<object>() });
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync($"/api/v1/checklists/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
    /// <summary>
    /// A caller with no account reaches none of a list's routes, and the answer is the same for a
    /// list that exists as for one that does not.
    /// </summary>
    /// <remarks>
    /// The update and delete handlers carry their own "no caller" branch beside the group's
    /// authorisation; this is what pins the answer either way, against a real list whose owner
    /// reads it in the same test.
    /// </remarks>
    [Fact]
    public async Task A_caller_with_no_account_reads_rewrites_and_deletes_nothing()
    {
        var list = await CreateAsync($"Not for strangers {suffix}", "Permit obtained");
        (await owner.GetAsync($"/api/v1/checklists/{list.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/checklists/{list.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"/api/v1/checklists/{list.Id}", Body("Taken", [])))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/v1/checklists/{list.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var after = await owner.GetFromJsonAsync<ChecklistPayload>($"/api/v1/checklists/{list.Id}");
        after!.Title.ShouldBe(list.Title);
        after.Items.Select(x => x.Text).ShouldBe(["Permit obtained"]);
    }

    /// <summary>
    /// More lines than a list may hold is refused whole on a rewrite, as it is on a create.
    /// </summary>
    [Fact]
    public async Task A_rewrite_with_more_lines_than_a_list_may_hold_is_refused_and_changes_nothing()
    {
        var list = await CreateAsync($"Bounded {suffix}", "Permit obtained");

        var refused = await owner.PutAsJsonAsync($"/api/v1/checklists/{list.Id}", new
        {
            title = list.Title,
            visibility = (int)Visibility.Private,
            items = Enumerable.Range(1, 201).Select(i => new { text = $"Line {i}" }).ToArray(),
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var exactly = await owner.PutAsJsonAsync($"/api/v1/checklists/{list.Id}", new
        {
            title = list.Title,
            visibility = (int)Visibility.Private,
            items = Enumerable.Range(1, 200).Select(i => new { text = $"Line {i}" }).ToArray(),
        });
        exactly.StatusCode.ShouldBe(HttpStatusCode.OK, await exactly.Content.ReadAsStringAsync());
        (await exactly.Content.ReadFromJsonAsync<ChecklistPayload>())!.Items.Count.ShouldBe(200);
    }

    /// <summary>
    /// A line naming a line of somebody else's list is a new line here, and the other list's line
    /// — and its text — is left exactly as it was.
    /// </summary>
    /// <remarks>
    /// The id is how a rewrite keeps the confirmations made against a line, so it is only ever
    /// looked up among this list's own lines. Looking it up anywhere else would let one author
    /// move, reword or take over a line of a list they may not write.
    /// </remarks>
    [Fact]
    public async Task A_line_naming_another_lists_line_is_a_new_line_and_the_other_list_is_untouched()
    {
        var theirs = await CreateAsync($"Theirs {suffix}", "Rope checked");
        var mine = await CreateAsync($"Mine {suffix}", "Permit obtained");
        var foreignLine = theirs.Items.Single().Id;

        var rewritten = await owner.PutAsJsonAsync(
            $"/api/v1/checklists/{mine.Id}",
            Body(mine.Title, [(foreignLine, "Borrowed line")]));
        rewritten.StatusCode.ShouldBe(HttpStatusCode.OK, await rewritten.Content.ReadAsStringAsync());
        var line = (await rewritten.Content.ReadFromJsonAsync<ChecklistPayload>())!.Items.ShouldHaveSingleItem();
        line.Id.ShouldNotBe(foreignLine);
        line.Text.ShouldBe("Borrowed line");

        var other = await owner.GetFromJsonAsync<ChecklistPayload>($"/api/v1/checklists/{theirs.Id}");
        other!.Items.ShouldHaveSingleItem().Id.ShouldBe(foreignLine);
        other.Items.Single().Text.ShouldBe("Rope checked");
    }

    /// <summary>
    /// Naming one line twice in one rewrite is refused, and the list is left as it was.
    /// </summary>
    /// <remarks>
    /// One row cannot stand in two places of one list. Accepted, the answer would list the line
    /// twice while the store held it once, at whichever place came last.
    /// </remarks>
    [Fact]
    public async Task A_rewrite_naming_one_line_twice_is_refused_and_changes_nothing()
    {
        var list = await CreateAsync($"Twice {suffix}", "Permit obtained", "Rope checked");
        var first = list.Items.Single(x => x.Text == "Permit obtained").Id;

        var refused = await owner.PutAsJsonAsync(
            $"/api/v1/checklists/{list.Id}",
            Body(list.Title, [(first, "Permit obtained"), (first, "Permit obtained again")]));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());

        var after = await owner.GetFromJsonAsync<ChecklistPayload>($"/api/v1/checklists/{list.Id}");
        after!.Items.OrderBy(x => x.SortOrder).Select(x => x.Text).ShouldBe(["Permit obtained", "Rope checked"]);
    }

    private async Task<ChecklistPayload> CreateAsync(string title, params string[] lines)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/checklists", new
        {
            title,
            visibility = (int)Visibility.Private,
            items = lines.Select(text => new { text }).ToArray(),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<ChecklistPayload>())!;
    }

    private static object Body(string title, (Guid? Id, string Text)[] lines) => new
    {
        title,
        visibility = (int)Visibility.Private,
        items = lines.Select(line => new { id = line.Id, text = line.Text }).ToArray(),
    };

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
