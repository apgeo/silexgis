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
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Saved filters as objects of their own: written, read, shared with a caving group, changed and
/// removed — and, above all, run by somebody other than their author.
/// </summary>
/// <remarks>
/// The people are a Viewer author with a clubmate and an outsider, all Viewers on purpose: the
/// seeded Editors group reads past visibility at the widest scope, so a test that an Editor cannot
/// see a filter would prove nothing about the audience column.
/// </remarks>
public sealed class SavedFilterTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Url = "/api/v1/saved-filters";

    private readonly SilexGisApiFactory factory;
    private string tag = null!;
    private Guid authorId;
    private Guid cavingGroupId;
    private HttpClient author = null!;
    private HttpClient clubmate = null!;
    private HttpClient outsider = null!;

    public SavedFilterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        authorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sf-author-{tag}@t.local");
        var clubmateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sf-club-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"sf-out-{tag}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Filter Club {tag}", Slug = $"filter-club-{tag}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            cavingGroupId = group.Id;
            await RosterHelper.AddMemberAsync(db, cavingGroupId, authorId);
            await RosterHelper.AddMemberAsync(db, cavingGroupId, clubmateId);
        }

        author = await AuthHelper.BearerClientAsync(factory, $"sf-author-{tag}@t.local");
        clubmate = await AuthHelper.BearerClientAsync(factory, $"sf-club-{tag}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"sf-out-{tag}@t.local");
    }

    // ---------- the routes are for signed-in callers ----------

    [Fact]
    public async Task An_anonymous_caller_is_turned_away_from_every_route()
    {
        using var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(Url, Body("x"), Wire)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"{Url}/{id}", Body("x"), Wire)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- writing one ----------

    [Fact]
    public async Task A_filter_is_saved_read_back_and_listed_as_its_authors()
    {
        var created = await CreateAsync(author, Body($"Bihor {tag}", description: "  weekly  "));

        created.GetProperty("state").GetString().ShouldBe("ready");
        created.GetProperty("mine").GetBoolean().ShouldBeTrue();
        created.GetProperty("description").GetString().ShouldBe("weekly");
        created.GetProperty("visibility").GetString().ShouldBe("private");
        var id = created.GetProperty("id").GetGuid();

        var read = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        read.GetProperty("name").GetString().ShouldBe($"Bihor {tag}");
        var document = read.GetProperty("document");
        document.GetProperty("scope")[0].GetProperty("world").GetString().ShouldBe("feature");

        var listed = await author.GetFromJsonAsync<JsonElement>(Url);
        listed.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).ShouldContain(id);
    }

    [Theory]
    [InlineData("", "private", false)]
    [InlineData("ok", "authenticated", false)]
    [InlineData("ok", "public", false)]
    [InlineData("ok", "cavingGroup", false)] // shared, but with no group named
    public async Task A_filter_that_breaks_a_rule_is_refused_before_it_is_stored(
        string name, string visibility, bool withGroup)
    {
        var response = await author.PostAsJsonAsync(Url, new
        {
            name,
            document = Region("Bihor"),
            visibility,
            cavingGroupId = withGroup ? cavingGroupId : (Guid?)null,
        }, Wire);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_private_filter_names_no_caving_group_on_save_or_on_update()
    {
        // Bound to a group, a row is reached by the rules scoped to that group whatever its
        // visibility column says — so "private" with a group would not be private at all.
        var before = await CountMineAsync();
        var created = await author.PostAsJsonAsync(Url, new
        {
            name = $"Not private {tag}",
            document = Region("Bihor"),
            visibility = "private",
            cavingGroupId = cavingGroupId,
        }, Wire);
        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await created.Content.ReadAsStringAsync());
        (await CodeAsync(created)).ShouldBe("validation.failed");
        (await CountMineAsync()).ShouldBe(before);

        var id = (await CreateAsync(author, Body($"Private {tag}"))).GetProperty("id").GetGuid();
        var updated = await author.PutAsJsonAsync($"{Url}/{id}", new
        {
            name = $"Private {tag}",
            visibility = "private",
            cavingGroupId = cavingGroupId,
        }, Wire);
        updated.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await updated.Content.ReadAsStringAsync());
        (await CodeAsync(updated)).ShouldBe("validation.failed");
        (await clubmate.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_filter_with_no_question_or_a_question_nobody_may_ask_is_refused()
    {
        var empty = await author.PostAsJsonAsync(Url, new { name = "x", visibility = "private" }, Wire);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).ShouldContain(SilexGis.Api.Features.SavedFilters.SavedFilterEndpoints.DocumentRequiredCode);

        var altitude = await author.PostAsJsonAsync(Url, new
        {
            name = "x",
            visibility = "private",
            document = About(new ConditionNode("altitude", FilterOp.LessThan, [new NumberValue(800)])),
        }, Wire);
        altitude.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await altitude.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("filter.invalid");
        // Each problem is handed over with its code and its place, so the editor can mark the row.
        var only = problem.GetProperty("problems").EnumerateArray().ShouldHaveSingleItem();
        only.GetProperty("code").GetString().ShouldBe(FilterProblemCodes.UnknownField);
        only.GetProperty("path").GetString().ShouldBe("/scope/0/where");
        only.GetProperty("field").GetString().ShouldBe("altitude");
    }

    [Fact]
    public async Task An_anchor_is_never_stored()
    {
        var before = await CountMineAsync();
        var document = Region("Bihor") with { Anchor = new PointAnchor(22.61873, 46.58219) };

        var response = await author.PostAsJsonAsync(Url, Body($"Anchored {tag}", document), Wire);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(SavedFilterRules.AnchorRefusedCode);
        (await CountMineAsync()).ShouldBe(before);

        // And an ordinary save stores the server's own spelling of the document, which has no room
        // for one: nothing that travelled beside the document's members reaches the row.
        var created = await CreateAsync(author, Body($"Plain {tag}"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var stored = await db.SavedFilters.AsNoTracking()
            .SingleAsync(f => f.Id == created.GetProperty("id").GetGuid());
        stored.Document.ShouldNotContain("anchor", Case.Insensitive);
        stored.Document.ShouldNotContain("22.61873");
    }

    [Fact]
    public async Task An_anchor_is_refused_on_an_update_too_and_the_stored_question_is_kept()
    {
        var created = await CreateAsync(author, Body($"Keep {tag}"));
        var id = created.GetProperty("id").GetGuid();

        var response = await author.PutAsJsonAsync($"{Url}/{id}", Body($"Keep {tag}",
            Region("Alba") with { Anchor = new ObjectAnchor("feature", Guid.NewGuid()) }), Wire);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var read = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        read.GetProperty("document").GetRawText().ShouldContain("Bihor");
    }

    [Fact]
    public async Task A_question_with_a_member_missing_is_refused_as_unreadable_on_save_and_on_update()
    {
        // Written by the wire's own serialiser, which leaves a null member out: the body arrives
        // with the member missing, and model binding fills it back in with null.
        FilterDocument[] holed =
        [
            About(new AllOfNode(null!)),
            About(new ConditionNode(FeatureFilterFields.Region, FilterOp.Contains, null!)),
            About(new ConditionNode(FeatureFilterFields.Region, FilterOp.Contains, [new TextValue(null!)])),
        ];
        var id = (await CreateAsync(author, Body($"Whole {tag}"))).GetProperty("id").GetGuid();

        foreach (var document in holed)
        {
            var created = await author.PostAsJsonAsync(Url, Body($"Holed {tag}", document), Wire);
            created.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await created.Content.ReadAsStringAsync());
            (await CodeAsync(created)).ShouldBe("filter.malformed");

            var updated = await author.PutAsJsonAsync($"{Url}/{id}", Body($"Whole {tag}", document), Wire);
            updated.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await updated.Content.ReadAsStringAsync());
            (await CodeAsync(updated)).ShouldBe("filter.malformed");
        }

        var kept = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        kept.GetProperty("state").GetString().ShouldBe("ready");
        kept.GetProperty("document").GetRawText().ShouldContain("Bihor");
    }

    [Fact]
    public async Task A_question_that_does_not_say_what_its_parts_are_or_leaves_a_value_out_is_refused_as_unreadable()
    {
        // Raw bodies, wrapped as the client sends them, because the shapes below cannot be written
        // by the typed serialiser: a node or a value with no discriminator made the reader throw an
        // exception request binding does not catch (a server fault), and an operator or a number
        // left out used to be read as "equals" or 0 and saved as a question nobody asked.
        string[] documents =
        [
            """{"version":1,"scope":[{"world":"feature","where":{"field":"region","op":"contains","values":[{"type":"text","value":"Bihor"}]}}]}""",
            """{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"region","op":"contains","values":[{"kind":"text","value":"Bihor"}]}}]}""",
            """{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"region","values":[{"type":"text","value":"Bihor"}]}}]}""",
            """{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"surveyedLength","op":"greaterThan","values":[{"type":"number"}]}}]}""",
        ];
        var id = (await CreateAsync(author, Body($"Whole {tag}"))).GetProperty("id").GetGuid();

        foreach (var document in documents)
        {
            var body = $$"""{"name":"Holed {{tag}}","visibility":"private","document":{{document}}}""";

            var created = await author.PostAsync(Url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            created.StatusCode.ShouldBe(HttpStatusCode.BadRequest, document);
            (await CodeAsync(created)).ShouldBe("filter.malformed", document);

            var updated = await author.PutAsync($"{Url}/{id}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            updated.StatusCode.ShouldBe(HttpStatusCode.BadRequest, document);
            (await CodeAsync(updated)).ShouldBe("filter.malformed", document);
        }

        var kept = await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        kept.GetProperty("state").GetString().ShouldBe("ready");
        kept.GetProperty("document").GetRawText().ShouldContain("Bihor");
    }

    // ---------- who may read it ----------

    [Fact]
    public async Task A_private_filter_is_its_authors_alone_and_reads_as_absent_to_anybody_else()
    {
        var id = (await CreateAsync(author, Body($"Mine {tag}"))).GetProperty("id").GetGuid();

        (await clubmate.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListedIdsAsync(clubmate)).ShouldNotContain(id);
        (await ListedIdsAsync(author)).ShouldContain(id);
    }

    [Fact]
    public async Task A_filter_shared_with_a_caving_group_reaches_its_members_and_nobody_else()
    {
        var id = (await CreateAsync(author, Body($"Club's {tag}", group: cavingGroupId)))
            .GetProperty("id").GetGuid();

        var seen = await clubmate.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        seen.GetProperty("mine").GetBoolean().ShouldBeFalse();
        seen.GetProperty("state").GetString().ShouldBe("ready");
        (await ListedIdsAsync(clubmate)).ShouldContain(id);

        (await outsider.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListedIdsAsync(outsider)).ShouldNotContain(id);
    }

    [Fact]
    public async Task Sharing_with_a_group_one_is_not_in_is_refused()
    {
        Guid strangers;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var group = new CavingGroup { Name = $"Other Club {tag}", Slug = $"other-club-{tag}" };
            db.CavingGroups.Add(group);
            await db.SaveChangesAsync();
            strangers = group.Id;
        }

        var response = await author.PostAsJsonAsync(Url, Body($"Theirs {tag}", group: strangers), Wire);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- who may change it ----------

    [Fact]
    public async Task A_reader_may_not_change_or_remove_it_and_an_outsider_is_told_it_is_not_there()
    {
        var id = (await CreateAsync(author, Body($"Shared {tag}", group: cavingGroupId)))
            .GetProperty("id").GetGuid();

        (await clubmate.PutAsJsonAsync($"{Url}/{id}", Body("Renamed", group: cavingGroupId), Wire))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await clubmate.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.PutAsJsonAsync($"{Url}/{id}", Body("Renamed"), Wire))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And its author may, which is what makes the refusals above about the reader.
        var renamed = await author.PutAsJsonAsync($"{Url}/{id}", new
        {
            name = $"Renamed {tag}",
            visibility = "private",
        }, Wire);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var body = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().ShouldBe($"Renamed {tag}");
        // An update without a document keeps the question it had.
        body.GetProperty("document").GetRawText().ShouldContain("Bihor");
        (await clubmate.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await author.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await author.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Under_a_clubs_starter_rules_a_member_may_run_and_copy_anothers_filter_but_not_change_reshare_or_remove_it()
    {
        // The clubs are made through the application, so they carry the starter rules every club
        // made that way carries — among them Write over saved workspace bound to the club. A club
        // inserted straight into its table has none, which is how this stayed unseen.
        var founderEmail = $"sf-founder-{tag}@t.local";
        var ownerEmail = $"sf-owner-{tag}@t.local";
        var memberEmail = $"sf-member-{tag}@t.local";
        var adminEmail = $"sf-admin-{tag}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, founderEmail);
        var ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, ownerEmail);
        var memberId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, memberEmail);
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, adminEmail);
        using var founder = await AuthHelper.BearerClientAsync(factory, founderEmail);
        var club = await CreateClubThroughTheAppAsync(founder, $"Starter Club {tag}");
        var otherClub = await CreateClubThroughTheAppAsync(founder, $"Second Club {tag}");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await RosterHelper.AddMemberAsync(db, club, ownerId);
            await RosterHelper.AddMemberAsync(db, club, memberId);
            await RosterHelper.AddMemberAsync(db, otherClub, memberId);
        }

        using var owner = await AuthHelper.BearerClientAsync(factory, ownerEmail);
        using var member = await AuthHelper.BearerClientAsync(factory, memberEmail);
        using var admin = await AuthHelper.BearerClientAsync(factory, adminEmail);
        var id = (await CreateAsync(owner, Body($"Club question {tag}", group: club))).GetProperty("id").GetGuid();

        // The member reads it, runs it under their own access, and may keep a copy of their own.
        var seen = await member.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        seen.GetProperty("mine").GetBoolean().ShouldBeFalse();
        (await member.GetAsync($"/api/v1/caves?savedFilterId={id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var copy = await CreateAsync(member, new
        {
            name = $"My copy {tag}",
            document = JsonSerializer.Deserialize<FilterDocument>(seen.GetProperty("document").GetRawText(), Wire),
            visibility = "private",
        });
        copy.GetProperty("mine").GetBoolean().ShouldBeTrue();

        // But the original is not theirs to rewrite, to hand on to their other club, or to remove.
        var rewritten = await member.PutAsJsonAsync($"{Url}/{id}", Body("Rewritten", Region("Alba"), group: club), Wire);
        rewritten.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await rewritten.Content.ReadAsStringAsync());
        var rebound = await member.PutAsJsonAsync($"{Url}/{id}", Body($"Club question {tag}", group: otherClub), Wire);
        rebound.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await rebound.Content.ReadAsStringAsync());
        (await member.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.SavedFilters.AsNoTracking().SingleAsync(f => f.Id == id);
            stored.Name.ShouldBe($"Club question {tag}");
            stored.CavingGroupId.ShouldBe(club);
            stored.Document.ShouldContain("Bihor");
        }

        // Its author may, and so may a full administrator.
        (await owner.PutAsJsonAsync($"{Url}/{id}", Body($"Renamed {tag}", group: club), Wire))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"{Url}/{id}", Body($"Tidied {tag}", group: club), Wire))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---------- running somebody else's ----------

    [Fact]
    public async Task A_shared_filter_grants_nothing_and_its_runner_sees_only_their_own_rows()
    {
        // Two caves the filter matches: one the author's own and private, one anybody signed in may
        // read. Seeded straight into the table so the author can stay a Viewer.
        var privateCave = await SeedCaveAsync($"SF private {tag}", Visibility.Private);
        var openCave = await SeedCaveAsync($"SF open {tag}", Visibility.Authenticated);
        var id = (await CreateAsync(author, Body($"Run me {tag}", Named(tag), group: cavingGroupId)))
            .GetProperty("id").GetGuid();

        var authors = await IdsOnTheListAsync(author, id);
        var clubmates = await IdsOnTheListAsync(clubmate, id);

        authors.Ids.ShouldBe([privateCave, openCave], ignoreOrder: true);
        authors.Total.ShouldBe(2);

        // The clubmate holds the very same question and is answered under their own access: the
        // author's private cave is neither listed nor counted for them.
        clubmates.Ids.ShouldBe([openCave]);
        clubmates.Total.ShouldBe(1);
    }

    // ---------- a filter that has stopped meaning what it meant ----------

    [Fact]
    public async Task A_filter_naming_a_tag_that_has_since_been_removed_says_it_is_stale()
    {
        long tagId;
        var slug = $"gone-{tag}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = new Tag { Name = slug, Slug = slug };
            db.Tags.Add(row);
            await db.SaveChangesAsync();
            tagId = row.Id;
        }

        var document = About(new ConditionNode(FeatureFilterFields.Tag, FilterOp.Equals, [new IdValue(slug)]));
        var id = (await CreateAsync(author, Body($"Tagged {tag}", document))).GetProperty("id").GetGuid();
        (await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}")).GetProperty("state").GetString()
            .ShouldBe("ready");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.Tags.Where(t => t.Id == tagId).ExecuteDeleteAsync();
        }

        // Still runnable — it simply matches nothing on that condition — but it says so rather
        // than looking like a filter that found no caves.
        (await author.GetFromJsonAsync<JsonElement>($"{Url}/{id}")).GetProperty("state").GetString()
            .ShouldBe("stale");
        (await author.GetAsync($"/api/v1/caves?savedFilterId={id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"altitude","op":"lessThan","values":[{"type":"number","value":800}]}}],"sort":"updated","descending":true}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"noSuchField","op":"contains","values":[{"type":"text","value":"x"}]}}],"sort":"updated","descending":true}""")]
    [InlineData("""{"version":7,"scope":[]}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"nodeType":"condition"}}],"sort":"updated","descending":true}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"allOf"}}],"sort":"updated","descending":true}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"region","op":"contains"}}],"sort":"updated","descending":true}""")]
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"region","op":"contains","values":[{"type":"text","value":null}]}}],"sort":"updated","descending":true}""")]
    [InlineData("""{"version":1,"scope":null,"sort":"updated","descending":true}""")]
    // A position written into the row by hand: no route stores one, and none may hand one back.
    [InlineData("""{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"region","op":"contains","values":[{"type":"text","value":"Bihor"}]}}],"sort":"updated","descending":true,"anchor":{"anchor":"point","longitude":22.61873,"latitude":46.58219}}""")]
    public async Task A_stored_question_its_reader_cannot_run_reads_as_unreadable_in_the_same_words(string stored)
    {
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var row = new SavedFilter
            {
                Name = $"Broken {tag}",
                Document = stored,
                OwnerUserId = authorId,
                CavingGroupId = cavingGroupId,
                Visibility = Visibility.CavingGroup,
            };
            db.SavedFilters.Add(row);
            await db.SaveChangesAsync();
            id = row.Id;
        }

        foreach (var reader in new[] { clubmate, author })
        {
            var single = await reader.GetAsync($"{Url}/{id}");
            var raw = await single.Content.ReadAsStringAsync();
            single.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
            var seen = JsonDocument.Parse(raw).RootElement;
            seen.GetProperty("state").GetString().ShouldBe("unreadable");

            // Nothing of a question the reader cannot run is shown, and never a position.
            seen.GetProperty("document").ValueKind.ShouldBe(JsonValueKind.Null);
            raw.ShouldNotContain("22.61873");
            raw.ShouldNotContain("anchor", Case.Insensitive);
        }

        // And one broken row never takes the list down for the people it is shared with.
        var listed = await clubmate.GetAsync(Url);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        var onTheList = (await listed.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(f => f.GetProperty("id").GetGuid() == id);
        onTheList.GetProperty("state").GetString().ShouldBe("unreadable");
        onTheList.GetProperty("document").ValueKind.ShouldBe(JsonValueKind.Null);

        var run = await clubmate.GetAsync($"/api/v1/caves?savedFilterId={id}");
        run.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await run.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(SavedFilterRules.UnreadableCode);

        // One sentence for every reason, so nothing in it tells the reader whether the field is
        // withheld from them, gone, or never existed.
        problem.GetProperty("detail").GetString().ShouldBe("This saved filter cannot be read with your access.");
    }

    // ---------- helpers ----------

    private static readonly JsonSerializerOptions Wire = FilterDocument.SerializerOptions;

    private static FilterDocument About(FilterNode? where) => new() { Scope = [new WorldScope("feature", where)] };

    private static FilterDocument Region(string text) =>
        About(new ConditionNode(FeatureFilterFields.Region, FilterOp.Contains, [new TextValue(text)]));

    private static FilterDocument Named(string text) =>
        About(new ConditionNode(FeatureFilterFields.Name, FilterOp.Contains, [new TextValue(text)]));

    private static object Body(
        string name, FilterDocument? document = null, Guid? group = null, string? description = null) => new
        {
            name,
            description,
            document = document ?? Region("Bihor"),
            cavingGroupId = group,
            visibility = group is null ? "private" : "cavingGroup",
        };

    private static async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Url, body, Wire);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> CreateClubThroughTheAppAsync(HttpClient founder, string name)
    {
        var created = await founder.PostAsJsonAsync("/api/v1/caving-groups", new
        {
            name,
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<List<Guid>> ListedIdsAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(Url)).EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    private async Task<int> CountMineAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.SavedFilters.CountAsync(f => f.OwnerUserId == authorId);
    }

    private static async Task<(List<Guid> Ids, int Total)> IdsOnTheListAsync(HttpClient client, Guid savedFilterId)
    {
        var response = await client.GetAsync($"/api/v1/caves?pageSize=100&savedFilterId={savedFilterId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return ([.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())],
            page.GetProperty("totalItems").GetInt32());
    }

    /// <summary>A cave owned by the author, written through the feature write service.</summary>
    private async Task<Guid> SeedCaveAsync(string name, Visibility visibility)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<FeatureWriteService>();
        var caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();

        var feature = await writer.CreateCaveAsync(
            new Feature { Name = name, OwnerUserId = authorId, Visibility = visibility },
            new Cave { CaveTypeId = caveTypeId },
            parents: []);
        await db.SaveChangesAsync();
        return feature.Id;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();
}
