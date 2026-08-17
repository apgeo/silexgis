// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Narrowing the camp list: the date window a camp overlaps, a word in its name, the lifecycle
/// state, a word this application does not have, and what a narrowing does to visibility.
/// </summary>
/// <remarks>
/// Every case here carries its own marker word in the camp names and searches for it, because the
/// database outlives one test class and a bare state filter would answer with everybody's camps.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ExpeditionListFilterTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private HttpClient owner = null!;

    // A plain reader, and it has to be one: the seeded Editors group holds every content domain
    // at the widest scope, so an Editor who "cannot see" a camp proves nothing about visibility.
    private HttpClient outsider = null!;

    public ExpeditionListFilterTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"xpf-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"xpf-out-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"xpf-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"xpf-out-{suffix}@t.local");
    }

    /// <summary>
    /// The window asks what the camp overlapped, not what it started inside. A fortnight camp
    /// running across the end of a month belongs to both halves of the season, and a camp that
    /// lasted one day stores no end at all — reading the stored end alone would drop every
    /// single-day camp out of every window, silently, which is the failure this asserts against.
    /// </summary>
    [Fact]
    public async Task The_window_holds_a_camp_that_overlapped_it_including_one_that_lasted_a_day()
    {
        var marker = Marker();
        var across = await CreateAsync($"Across {marker}", "2031-05-28", end: "2031-06-04");
        var oneDay = await CreateAsync($"One day {marker}", "2031-06-02");
        var before = await CreateAsync($"Before {marker}", "2031-04-01", end: "2031-04-05");

        var june = await ListAsync($"search={marker}&from=2031-06-01&to=2031-06-30");
        june.ShouldContain(across, "a camp running across the first of the month overlaps it");
        june.ShouldContain(oneDay, "a camp with no end date ran for one day, not for no days");
        june.ShouldNotContain(before);

        // The open-ended forms narrow on one side only, and the unnarrowed list still holds all
        // three — so what the window removed above is the window's doing and not the search's.
        (await ListAsync($"search={marker}&to=2031-04-30")).ShouldBe([before]);
        (await ListAsync($"search={marker}")).Count.ShouldBe(3);
    }

    /// <summary>
    /// The name filter reaches past the accents somebody did or did not type, the same way the
    /// trips' does, and it narrows on the name rather than on the description.
    /// </summary>
    [Fact]
    public async Task A_word_in_the_name_finds_the_camp_however_it_was_accented()
    {
        var marker = Marker();
        var accented = await CreateAsync($"Tabăra {marker}", "2031-07-01");
        _ = await CreateAsync($"Something else {marker}", "2031-07-01");

        (await ListAsync($"search={Escape($"tabara {marker}")}")).ShouldBe([accented]);
        (await ListAsync($"search={Escape($"Tabăra {marker}")}")).ShouldBe([accented]);

        // The description of every camp this class writes is the same sentence; a search for a
        // word out of it finds none of them, which is what "the name only" means.
        (await ListAsync($"search={Escape("A camp.")}")).ShouldBeEmpty();
    }

    /// <summary>
    /// The state filter, and the refusal beside it. A word the application does not have is told
    /// so under a code rather than folded into "no filter" — a caller handed the whole list after
    /// asking for one state has no way to tell that from a state nothing is in.
    /// </summary>
    [Fact]
    public async Task The_state_narrows_the_list_and_a_state_that_does_not_exist_is_refused()
    {
        var marker = Marker();
        var moved = await CreateAsync($"Being organised {marker}", "2031-08-01");
        var left = await CreateAsync($"Still an idea {marker}", "2031-08-01");

        var response = await owner.PostWithIfMatchAsync(
            $"/api/v1/expeditions/{moved}/state", new { state = "planned" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await ListAsync($"search={marker}&state=planned")).ShouldBe([moved]);
        (await ListAsync($"search={marker}&state=draft")).ShouldBe([left]);

        var refused = await owner.GetAsync($"/api/v1/expeditions/?search={marker}&state=nowhere");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().ShouldBe("expedition.state_invalid");
    }

    /// <summary>
    /// A narrowed list is narrowed inside what the caller may read, never around it. The camp
    /// below is private to its owner and matches the filter exactly; the reader who holds nothing
    /// on it is answered with nothing, and the owner asking the same question is answered with
    /// the camp — the two halves together, because either alone would pass on an empty database.
    /// </summary>
    [Fact]
    public async Task A_filter_matching_a_camp_a_reader_holds_nothing_on_answers_them_nothing()
    {
        var marker = Marker();
        var camp = await CreateAsync($"Private {marker}", "2031-09-05");

        (await ListAsync($"search={marker}&state=draft", owner)).ShouldBe([camp]);
        (await ListAsync($"search={marker}&state=draft", outsider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_list_refuses_a_caller_it_does_not_know()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/expeditions/?search=anything"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string Marker() => $"mk{Guid.NewGuid().ToString("N")[..8]}";

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private async Task<List<Guid>> ListAsync(string query, HttpClient? client = null)
    {
        var response = await (client ?? owner).GetAsync($"/api/v1/expeditions/?pageSize=500&{query}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        var body = JsonDocument.Parse(payload).RootElement;

        // The total is counted over the same narrowed query the page is taken from; a filter
        // applied to one and not the other reads as a list with more rows somewhere else.
        var items = body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        body.GetProperty("totalItems").GetInt32().ShouldBe(items.Count);
        return items;
    }

    private async Task<Guid> CreateAsync(string name, string startDate, string? end = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/expeditions/", new
        {
            name,
            description = "A camp.",
            startDate,
            endDate = end,
            geom = (object?)null,
            cavingGroupId = (Guid?)null,
            visibility = "private",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
