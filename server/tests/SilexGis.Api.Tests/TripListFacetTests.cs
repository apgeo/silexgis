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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The trip listing's filter panel: the counts beside each option, the narrowings they describe,
/// and the order a page comes back in.
/// <para>
/// One property is asserted over and over, because it is the only one that matters: the number
/// beside an option and the page that option produces agree, <em>for a caller who cannot see
/// everything</em>. A facet that says twelve and yields four tells the reader they are being
/// shown less than they may see, which is a sentence about the rows they were not shown — and
/// saying it by accident is worse than offering no count at all.
/// </para>
/// <para>
/// The withheld side is always a Viewer. The seeded Editors group reads past visibility by
/// design, so an Editor seeing nothing would prove nothing.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripListFacetTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;  // Editor — creates everything
    private HttpClient reader = null!; // Viewer — sees only what its audience admits
    private Guid ownerUserId;
    private Guid readerUserId;
    private long surveyTypeId;
    private long explorationTypeId;
    private long participantRoleId;
    private long leaderRoleId;

    public TripListFacetTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tf-own-{suffix}@t.local");
        readerUserId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tf-read-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            surveyTypeId = await db.TripTypes.Where(t => t.Code == "survey").Select(t => t.Id).FirstAsync();
            explorationTypeId = await db.TripTypes.Where(t => t.Code == "exploration").Select(t => t.Id).FirstAsync();
            participantRoleId = await db.TripParticipantRoles
                .Where(r => r.Code == "participant").Select(r => r.Id).FirstAsync();
            leaderRoleId = await db.TripParticipantRoles
                .Where(r => r.Code == "leader").Select(r => r.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"tf-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tf-read-{suffix}@t.local");
    }

    /// <summary>
    /// The one this whole surface is judged on. Two audiences over the same trips, and the count
    /// beside an option is checked against the rows that option actually hands the same caller.
    /// </summary>
    [Fact]
    public async Task A_facet_count_and_the_page_that_option_produces_agree_for_a_caller_who_cannot_see_everything()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Open survey one {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Open survey two {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Open survey three {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Shut survey one {marker}", surveyTypeId, "private");
        await CreateTripAsync($"Shut survey two {marker}", surveyTypeId, "private");
        await CreateTripAsync($"Open dig {marker}", explorationTypeId, "authenticated");

        // Fixture proof, in both directions: the withheld trips really are withheld from this
        // reader and really do exist, so a smaller count below is the audience walk and not a
        // fixture that never wrote them.
        var readerTotal = (await ListAsync(reader, $"search={marker}")).GetProperty("totalItems").GetInt32();
        var ownerTotal = (await ListAsync(owner, $"search={marker}")).GetProperty("totalItems").GetInt32();
        readerTotal.ShouldBe(4);
        ownerTotal.ShouldBe(6);

        var readerFacets = await FacetsAsync(reader, $"search={marker}");
        var ownerFacets = await FacetsAsync(owner, $"search={marker}");

        readerFacets.GetProperty("matching").GetInt32().ShouldBe(4);
        ownerFacets.GetProperty("matching").GetInt32().ShouldBe(6);

        // The two callers get different numbers for the same option, and both are right.
        var readerSurvey = FacetCount(readerFacets, "types", surveyTypeId.ToString());
        var ownerSurvey = FacetCount(ownerFacets, "types", surveyTypeId.ToString());
        readerSurvey.ShouldBe(3);
        ownerSurvey.ShouldBe(5);

        // …and each number is what that option really hands that caller.
        (await ListAsync(reader, $"search={marker}&types={surveyTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(readerSurvey);
        (await ListAsync(owner, $"search={marker}&types={surveyTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(ownerSurvey);

        // The audience facet is the sharpest case: the reader is offered no private option at
        // all, because for them there are no private trips to offer.
        // The word is the one the listing's parameter takes and the one every other answer
        // spells the audience with, so a panel can look its own translation up under it.
        FacetCount(readerFacets, "visibilities", "private").ShouldBe(0);
        FacetCount(ownerFacets, "visibilities", "private").ShouldBe(2);
        (await ListAsync(owner, $"search={marker}&visibilities=private"))
            .GetProperty("totalItems").GetInt32().ShouldBe(2);

        // The filtered-of-total figure has both halves about the caller, so the fraction never
        // implies the filter is hiding what access is hiding.
        readerFacets.GetProperty("overall").GetInt32().ShouldBeLessThan(ownerFacets.GetProperty("overall").GetInt32());
    }

    /// <summary>
    /// A facet's own choices are left out of its option counts, so an option says what it would
    /// leave. Every other facet still applies, which is what makes them answers to "and this one
    /// too" rather than to nothing.
    /// </summary>
    [Fact]
    public async Task An_option_says_how_many_trips_it_would_leave_while_the_other_facets_still_apply()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Survey kept {marker}", surveyTypeId, "authenticated", hadIncident: false);
        await CreateTripAsync($"Survey hurt {marker}", surveyTypeId, "authenticated", hadIncident: true);
        await CreateTripAsync($"Dig kept {marker}", explorationTypeId, "authenticated", hadIncident: false);

        var narrowed = await FacetsAsync(reader, $"search={marker}&types={surveyTypeId}");

        // The type facet is counted with its own choice dropped, so the option nobody picked is
        // still offered with the number it would leave — not with zero.
        FacetCount(narrowed, "types", explorationTypeId.ToString()).ShouldBe(1);
        FacetCount(narrowed, "types", surveyTypeId.ToString()).ShouldBe(2);

        // Every other facet is counted with the type choice still in force, so the went-wrong
        // option answers "surveys where something went wrong", not "trips where it did".
        FacetCount(narrowed, "incident", "true").ShouldBe(1);
        FacetCount(narrowed, "incident", "false").ShouldBe(1);
        narrowed.GetProperty("matching").GetInt32().ShouldBe(2);

        // And each of those is what the listing hands back when the option is added.
        (await ListAsync(reader, $"search={marker}&types={surveyTypeId}&hadIncident=true"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);
        (await ListAsync(reader, $"search={marker}&types={surveyTypeId},{explorationTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(3);
    }

    /// <summary>
    /// A roster row is one person doing one job, so a leader who also surveyed is two rows and
    /// one person who went once.
    /// </summary>
    [Fact]
    public async Task Somebody_who_did_two_jobs_on_one_trip_is_one_person_who_went_once()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var tripId = await CreateTripAsync(
            $"Two jobs {marker}", surveyTypeId, "authenticated", people: [("Ana Doi", participantRoleId)]);
        var anaId = await SoleCaverIdAsync(tripId);
        await AddRoleAsync(tripId, anaId, leaderRoleId);

        // Fixture proof: Ana really does hold two jobs on the one trip, so a count of one below
        // is the roster being reduced to people rather than the second job never being written.
        (await RosterRowCountAsync(tripId)).ShouldBe(2);

        var facets = await FacetsAsync(reader, $"search={marker}");
        FacetCount(facets, "participants", anaId.ToString()).ShouldBe(1);

        // And the number is what the person option hands back.
        (await ListAsync(reader, $"search={marker}&participantIds={anaId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// The area a trip worked in narrows the listing, and a cave may not be asked through that
    /// door: the cave filter carries a position check the area filter has no way to apply, so a
    /// cave id offered as an area answers as though nothing were linked.
    /// </summary>
    [Fact]
    public async Task An_area_narrows_the_listing_and_a_cave_offered_as_one_answers_empty()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var inArea = await CreateTripAsync($"In the massif {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Elsewhere {marker}", surveyTypeId, "authenticated");

        var areaId = await CreateGenericFeatureAsync();
        await NameFeatureAsync(inArea, areaId, "trip-work-area");

        (await ListAsync(reader, $"search={marker}&areaIds={areaId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);
        FacetCount(await FacetsAsync(reader, $"search={marker}"), "areas", areaId.ToString()).ShouldBe(1);

        // The positive half above is what makes the refusal below meaningful: the door works, and
        // is still shut to a cave. The cave is named on a trip first, so the empty answer is the
        // kind gate refusing and not a fixture that linked the cave to nothing — without that,
        // deleting the gate leaves this passing.
        var caveId = await CreateCaveAsync($"Cave {marker}");
        await NameFeatureAsync(inArea, caveId, "trip-visited");
        (await ListAsync(reader, $"search={marker}&caveId={caveId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);

        (await ListAsync(reader, $"search={marker}&areaIds={caveId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// The property this whole surface is judged on, over the shape that broke it: an area's count
    /// was taken over the trips naming it directly while selecting it also matched every trip that
    /// named something inside it, so a massif offered as one handed back three.
    /// </summary>
    [Fact]
    public async Task An_area_counts_the_trips_that_reached_it_through_what_it_contains()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var massif = await CreateGenericFeatureAsync();
        var subArea = await CreateGenericFeatureAsync(parentId: massif);

        var namedMassif = await CreateTripAsync($"On the massif {marker}", surveyTypeId, "authenticated");
        var namedSubOne = await CreateTripAsync($"In the valley one {marker}", surveyTypeId, "authenticated");
        var namedSubTwo = await CreateTripAsync($"In the valley two {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Elsewhere {marker}", surveyTypeId, "authenticated");
        await NameFeatureAsync(namedMassif, massif, "trip-work-area");
        await NameFeatureAsync(namedSubOne, subArea, "trip-work-area");
        await NameFeatureAsync(namedSubTwo, subArea, "trip-work-area");

        var facets = await FacetsAsync(reader, $"search={marker}");
        var massifCount = FacetCount(facets, "areas", massif.ToString());
        var subCount = FacetCount(facets, "areas", subArea.ToString());

        // The massif is reached by all three, the sub-area by the two that named it. Written out
        // rather than only compared, so a walk that stopped reaching would fail here and not just
        // agree with itself about nothing.
        massifCount.ShouldBe(3);
        subCount.ShouldBe(2);

        // …and each number is what that option really hands this caller.
        (await ListAsync(reader, $"search={marker}&areaIds={massif}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(massifCount);
        (await ListAsync(reader, $"search={marker}&areaIds={subArea}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(subCount);

        // The slice a grouping cuts is the same reading, so a reader who goes from the shape to
        // the rows is not shown a different number on the way.
        var groups = await GroupingAsync(reader, $"search={marker}&groupBy=area");
        GroupCount(groups, massif.ToString()).ShouldBe(massifCount);
        GroupCount(groups, subArea.ToString()).ShouldBe(subCount);
    }

    /// <summary>
    /// An area whose position is guarded is neither named, counted nor usable as a narrowing by
    /// somebody who may not see where it is: a trip carries its own exact geometry, so "these
    /// trips worked there" places the area by proximity even when the area itself reads publicly.
    /// </summary>
    [Fact]
    public async Task A_guarded_area_is_not_offered_and_narrows_nothing_for_a_caller_who_cannot_place_it()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var guarded = await CreateGenericFeatureAsync(locationProtected: true);
        var trip = await CreateTripAsync($"Guarded ground {marker}", surveyTypeId, "authenticated");
        await NameFeatureAsync(trip, guarded, "trip-work-area");

        // Fixture proof on both halves: the trip is readable by this caller, and the naming really
        // happened — the owner, who may place the area, is offered it and narrowed by it.
        (await ListAsync(reader, $"search={marker}")).GetProperty("totalItems").GetInt32().ShouldBe(1);
        FacetCount(await FacetsAsync(owner, $"search={marker}"), "areas", guarded.ToString()).ShouldBe(1);
        (await ListAsync(owner, $"search={marker}&areaIds={guarded}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);

        FacetCount(await FacetsAsync(reader, $"search={marker}"), "areas", guarded.ToString()).ShouldBe(0);
        (await ListAsync(reader, $"search={marker}&areaIds={guarded}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// The cave question cannot be asked one level down. A guarded cave sits inside a readable
    /// area, so an area filter that walked the hierarchy without the position gate would hand back
    /// exactly the trips the cave filter refuses — each carrying its own coordinates.
    /// </summary>
    [Fact]
    public async Task A_guarded_cave_inside_a_readable_area_answers_the_same_empty_through_either_door()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var area = await CreateGenericFeatureAsync();
        var guardedCave = await CreateCaveAsync($"Guarded {marker}", parentId: area, locationProtected: true);
        var trip = await CreateTripAsync($"Down the hole {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Elsewhere {marker}", surveyTypeId, "authenticated");
        await NameFeatureAsync(trip, guardedCave, "trip-visited");

        // Fixture proof: the naming happened and the cave really is inside the area — the owner,
        // who may place it, reaches the trip through both doors.
        (await ListAsync(owner, $"search={marker}&caveId={guardedCave}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);
        (await ListAsync(owner, $"search={marker}&areaIds={area}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);

        // For a caller who may not place the cave, both doors answer the same nothing.
        (await ListAsync(reader, $"search={marker}&caveId={guardedCave}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);
        (await ListAsync(reader, $"search={marker}&areaIds={area}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);

        // …and the area is not offered as a place those trips reached either, which is the same
        // sentence said by a count rather than by a page.
        FacetCount(await FacetsAsync(reader, $"search={marker}"), "areas", area.ToString()).ShouldBe(0);
    }

    /// <summary>
    /// Values inside one facet are alternatives, for the two facets that name rows rather than
    /// words. Two people chosen is the trips either of them was on, which is the reading the panel
    /// promises everywhere else — and one term the caller may not read stops the whole answer
    /// rather than quietly widening it to the others.
    /// </summary>
    [Fact]
    public async Task Two_values_chosen_in_one_open_ended_facet_are_alternatives()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var withAna = await CreateTripAsync(
            $"Ana went {marker}", surveyTypeId, "authenticated", people: [("Ana Trei", participantRoleId)]);
        var withBogdan = await CreateTripAsync(
            $"Bogdan went {marker}", surveyTypeId, "authenticated", people: [("Bogdan Trei", participantRoleId)]);
        await CreateTripAsync($"Nobody named {marker}", surveyTypeId, "authenticated");
        var anaId = await SoleCaverIdAsync(withAna);
        var bogdanId = await SoleCaverIdAsync(withBogdan);

        (await ListAsync(reader, $"search={marker}&participantIds={anaId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);
        (await ListAsync(reader, $"search={marker}&participantIds={anaId},{bogdanId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(2);

        // Two areas behave the same way, and a trip naming both is still one trip.
        var north = await CreateGenericFeatureAsync();
        var south = await CreateGenericFeatureAsync();
        await NameFeatureAsync(withAna, north, "trip-work-area");
        await NameFeatureAsync(withBogdan, south, "trip-work-area");
        await NameFeatureAsync(withBogdan, north, "trip-objective");
        (await ListAsync(reader, $"search={marker}&areaIds={north}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(2);
        (await ListAsync(reader, $"search={marker}&areaIds={north},{south}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(2);

        // A term this caller may not read stops the answer instead of leaving the others standing.
        (await ListAsync(reader, $"search={marker}&participantIds={anaId},{Guid.NewGuid()}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);
        (await ListAsync(reader, $"search={marker}&areaIds={north},{Guid.NewGuid()}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(0);
    }

    /// <summary>
    /// Naming something the caller may not read answers with an empty page and every count zero,
    /// rather than refusing — an id that answers differently from one that does not exist is an
    /// id anybody can go looking for.
    /// </summary>
    [Fact]
    public async Task A_filter_naming_what_the_caller_cannot_read_answers_empty_rather_than_refusing()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Readable {marker}", surveyTypeId, "authenticated");

        // The positive half: with nothing named, this caller does see the trip.
        (await ListAsync(reader, $"search={marker}")).GetProperty("totalItems").GetInt32().ShouldBe(1);

        foreach (var narrowing in new[] { "participantIds", "areaIds", "caveId", "expeditionId" })
        {
            var absent = Guid.NewGuid();
            var page = await ListAsync(reader, $"search={marker}&{narrowing}={absent}");
            page.GetProperty("totalItems").GetInt32().ShouldBe(0, narrowing);
            page.GetProperty("items").GetArrayLength().ShouldBe(0, narrowing);

            // The counts go quiet with the page. A panel showing numbers beside a page showing
            // nothing would say the rows exist.
            var facets = await FacetsAsync(reader, $"search={marker}&{narrowing}={absent}");
            facets.GetProperty("matching").GetInt32().ShouldBe(0, narrowing);
            facets.GetProperty("overall").GetInt32().ShouldBe(0, narrowing);
            facets.GetProperty("types").GetArrayLength().ShouldBe(0, narrowing);
        }
    }

    /// <summary>A word the listing does not know is refused by name, so a panel can say which
    /// control to put right.</summary>
    [Fact]
    public async Task An_unknown_word_is_refused_with_the_code_that_names_the_control()
    {
        await AssertRefusedAsync("states=underground", "trip_log.state_invalid");
        await AssertRefusedAsync("visibilities=semi-public", "trip_log.visibility_invalid");
        await AssertRefusedAsync("types=survey", "trip_log.type_invalid");
        await AssertRefusedAsync("sort=depth", "trip_log.sort_invalid");
        await AssertRefusedAsync("participantIds=ana", "trip_log.participant_invalid");
        await AssertRefusedAsync("areaIds=bucegi", "trip_log.area_invalid");

        // The facet counts refuse the same words the same way, or a panel would show numbers for
        // a filter the listing rejects.
        var facets = await reader.GetAsync("/api/v1/trip-logs/facets?states=underground");
        facets.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(facets)).ShouldBe("trip_log.state_invalid");

        // An empty list is no opinion, not an impossible one: clearing the last choice out of a
        // facet is the same thing as never having touched it.
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Unconstrained {marker}", surveyTypeId, "authenticated");
        (await ListAsync(reader, $"search={marker}&types=&states=&visibilities="))
            .GetProperty("totalItems").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// The order is the server's to decide, and the caller may choose it. The default is
    /// unchanged — most recent first — because a listing that quietly reordered itself would move
    /// every page under every reader who never asked.
    /// </summary>
    [Fact]
    public async Task The_caller_chooses_the_order_and_the_default_is_still_most_recent_first()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Bravo {marker}", surveyTypeId, "authenticated", tripDate: "2026-03-02");
        await CreateTripAsync($"Alpha {marker}", surveyTypeId, "authenticated", tripDate: "2026-03-01");
        await CreateTripAsync($"Charlie {marker}", surveyTypeId, "authenticated", tripDate: "2026-03-03");

        (await TitlesAsync(reader, $"search={marker}"))
            .ShouldBe([$"Charlie {marker}", $"Bravo {marker}", $"Alpha {marker}"]);
        (await TitlesAsync(reader, $"search={marker}&sort=date"))
            .ShouldBe([$"Alpha {marker}", $"Bravo {marker}", $"Charlie {marker}"]);
        (await TitlesAsync(reader, $"search={marker}&sort=title"))
            .ShouldBe([$"Alpha {marker}", $"Bravo {marker}", $"Charlie {marker}"]);
        (await TitlesAsync(reader, $"search={marker}&sort=-title"))
            .ShouldBe([$"Charlie {marker}", $"Bravo {marker}", $"Alpha {marker}"]);
    }

    /// <summary>The listing is not open to a caller with no account, counts included.</summary>
    [Fact]
    public async Task Neither_the_listing_nor_its_counts_answer_an_account_less_caller()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-logs/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/trip-logs/facets")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private static int FacetCount(JsonElement facets, string facet, string value) =>
        facets.GetProperty(facet).EnumerateArray()
            .Where(x => x.GetProperty("value").GetString() == value)
            .Select(x => x.GetProperty("count").GetInt32())
            .FirstOrDefault();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/?pageSize=200&{query}"));

    private static async Task<JsonElement> FacetsAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/facets?{query}"));

    private static async Task<JsonElement> GroupingAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/grouping?{query}"));

    private static int GroupCount(JsonElement grouping, string value) =>
        grouping.GetProperty("groups").EnumerateArray()
            .Where(x => x.GetProperty("value").GetString() == value)
            .Select(x => x.GetProperty("count").GetInt32())
            .FirstOrDefault();

    private static async Task<List<string>> TitlesAsync(HttpClient client, string query) =>
        [.. (await ListAsync(client, query)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString()!)];

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    private async Task AssertRefusedAsync(string query, string code)
    {
        var response = await reader.GetAsync($"/api/v1/trip-logs/?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        (await CodeOfAsync(response)).ShouldBe(code, query);
    }

    private async Task<Guid> CreateTripAsync(
        string title,
        long tripTypeId,
        string visibility,
        bool hadIncident = false,
        string tripDate = "2026-04-10",
        (string Name, long RoleId)[]? people = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate,
            tripTypeId,
            hadIncident,
            visibility,
            caveIds = Array.Empty<Guid>(),
            participants = (people ?? [])
                .Select(p => new { newCaverName = p.Name, roleId = p.RoleId }).ToArray(),
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCaveAsync(
        string name, Guid? parentId = null, bool locationProtected = false)
    {
        long caveTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        }

        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "public",
            locationProtected,
            parentId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>An area feature for the trip role that names one. Written straight in, because
    /// what kind it is matters here and nothing else about it does — as does where it sits, since
    /// the containment chain is what an area filter and an area count both walk.</summary>
    private async Task<Guid> CreateGenericFeatureAsync(
        Guid? parentId = null, bool locationProtected = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = await db.FeatureTypes.AsNoTracking().Select(t => t.Id).FirstAsync();
        var id = Guid.NewGuid();
        var above = parentId is null
            ? []
            : await db.Features.AsNoTracking()
                .Where(f => f.Id == parentId).Select(f => f.AncestorIds).SingleAsync();
        Guid[] ancestors = [id, .. above];
        db.Features.Add(new Feature
        {
            Id = id,
            Name = $"Massif {Guid.NewGuid():N}"[..30],
            Kind = FeatureKind.Generic,
            FeatureTypeId = typeId,
            OwnerUserId = ownerUserId,
            Visibility = Visibility.Public,
            LocationProtected = locationProtected,
            IsProtectedEffective = locationProtected,
            AncestorIds = ancestors,
        });
        foreach (var ancestorId in ancestors)
        {
            db.FeatureAncestors.Add(new FeatureAncestor { FeatureId = id, AncestorId = ancestorId });
        }

        // The containment edge, which is the one of the three the array and the closure are
        // derived from. Writing the two derived halves without it leaves a feature whose stored
        // ancestry says it has a parent and whose edges say it is a root, which the integrity
        // verifier is right to report — and because that verifier reads the whole database and
        // several classes assert it finds nothing at all, the failure lands on some unrelated
        // test, naming identifiers that belong to no test at all.
        if (parentId is { } containedBy)
        {
            db.FeatureHierarchyEdges.Add(
                new FeatureHierarchyEdge { ParentId = containedBy, ChildId = id, IsPrimary = true });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task NameFeatureAsync(Guid tripId, Guid featureId, string roleCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await TripRoleLinks.NameFeatureAsync(db, tripId, featureId, roleCode, null, default)).ShouldBeTrue();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SoleCaverIdAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripId).Select(p => p.CaverId).Distinct().SingleAsync();
    }

    private async Task AddRoleAsync(Guid tripId, Guid caverId, long roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.TripLogParticipants.Add(new TripLogParticipant { TripLogId = tripId, CaverId = caverId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    /// <summary>Roster rows, unreduced — the fixture proof a reduced count cannot give.</summary>
    private async Task<int> RosterRowCountAsync(Guid tripId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking().CountAsync(p => p.TripLogId == tripId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        factory.Dispose();
    }
}
