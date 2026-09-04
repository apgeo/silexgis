// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.XSSF.Extractor;
using NPOI.XSSF.UserModel;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The trip listing sliced, and the same listing exported.
/// <para>
/// The property asserted throughout is the one the whole surface is judged on: a slice's count
/// and the rows that slice produces agree, <em>for a caller who cannot see everything</em>. A
/// slice saying twelve over a filter that yields four tells the reader they are being shown less
/// than they may see, which is a sentence about the rows they were not shown.
/// </para>
/// <para>
/// The withheld side is always a Viewer. The seeded Editors group reads past visibility by
/// design, so an Editor seeing nothing would prove nothing.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TripListGroupingTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;  // Editor — creates everything
    private HttpClient reader = null!; // Viewer — sees only what its audience admits
    private long surveyTypeId;
    private long explorationTypeId;
    private long participantRoleId;
    private long leaderRoleId;

    public TripListGroupingTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tg-own-{suffix}@t.local");
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tg-read-{suffix}@t.local");

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

        owner = await AuthHelper.BearerClientAsync(factory, $"tg-own-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tg-read-{suffix}@t.local");
    }

    /// <summary>
    /// The one this surface is judged on. Two audiences over the same trips, and a slice's count
    /// checked against the rows that slice hands the same caller.
    /// </summary>
    [Fact]
    public async Task A_slice_count_and_the_rows_that_slice_produces_agree_for_a_caller_who_cannot_see_everything()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Open survey one {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Open survey two {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Shut survey {marker}", surveyTypeId, "private");
        await CreateTripAsync($"Open dig {marker}", explorationTypeId, "authenticated");

        // Fixture proof, in both directions: the withheld trip really is withheld from this
        // reader and really does exist, so a smaller slice below is the audience walk and not a
        // fixture that never wrote it.
        (await ListAsync(reader, $"search={marker}")).GetProperty("totalItems").GetInt32().ShouldBe(3);
        (await ListAsync(owner, $"search={marker}")).GetProperty("totalItems").GetInt32().ShouldBe(4);

        var readerGroups = await GroupingAsync(reader, $"search={marker}&groupBy=type");
        var ownerGroups = await GroupingAsync(owner, $"search={marker}&groupBy=type");

        readerGroups.GetProperty("matching").GetInt32().ShouldBe(3);
        ownerGroups.GetProperty("matching").GetInt32().ShouldBe(4);

        // The same slice, two callers, two numbers — and both are right.
        var readerSurvey = GroupCount(readerGroups, surveyTypeId.ToString());
        var ownerSurvey = GroupCount(ownerGroups, surveyTypeId.ToString());
        readerSurvey.ShouldBe(2);
        ownerSurvey.ShouldBe(3);

        // …and each number is what that slice really hands that caller, asked as a filter.
        (await ListAsync(reader, $"search={marker}&types={surveyTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(readerSurvey);
        (await ListAsync(owner, $"search={marker}&types={surveyTypeId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(ownerSurvey);

        // One value per trip on this dimension, so the slices add up and the answer says as much.
        readerGroups.GetProperty("overlapping").GetBoolean().ShouldBeFalse();
        SliceTotal(readerGroups).ShouldBe(3);

        // Every slice carries the days it spans and the purposes it was for.
        var survey = SliceOf(readerGroups, surveyTypeId.ToString());
        survey.GetProperty("firstDay").GetString().ShouldNotBeNull();
        survey.GetProperty("lastDay").GetString().ShouldNotBeNull();
        survey.GetProperty("topTypes").EnumerateArray().First()
            .GetProperty("value").GetString().ShouldBe(surveyTypeId.ToString());
    }

    /// <summary>
    /// A trip counts into every person it holds, so the slices add up to more than the trips —
    /// and a person who did two jobs on one trip is one person who went once.
    /// </summary>
    [Fact]
    public async Task A_trip_counts_into_every_person_it_holds_and_the_answer_says_so()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var both = await CreateTripAsync(
            $"Together {marker}", surveyTypeId, "authenticated",
            people: [("Ana Unu", participantRoleId), ("Bogdan Doi", participantRoleId)]);
        await CreateTripAsync(
            $"Alone {marker}", surveyTypeId, "authenticated", people: [("Ana Unu", participantRoleId)]);

        var anaId = await CaverIdAsync(both, "Ana Unu");
        await AddRoleAsync(both, anaId, leaderRoleId);

        // Fixture proof: Ana really does hold two jobs on that trip, so a count of two below is
        // the roster being reduced to people rather than the second job never being written.
        (await RosterRowCountAsync(both)).ShouldBe(3);

        var groups = await GroupingAsync(reader, $"search={marker}&groupBy=participant");
        groups.GetProperty("matching").GetInt32().ShouldBe(2);

        // Two trips, three person-slices: Ana on both, Bogdan on one. That is the right answer to
        // "how many trips did each caver do", and the flag is what stops it reading as an error.
        groups.GetProperty("overlapping").GetBoolean().ShouldBeTrue();
        SliceTotal(groups).ShouldBe(3);
        GroupCount(groups, anaId.ToString()).ShouldBe(2);

        // And the person slice is what the person filter hands back.
        (await ListAsync(reader, $"search={marker}&participantIds={anaId}"))
            .GetProperty("totalItems").GetInt32().ShouldBe(2);

        // The name travels with the slice, because the client holds no roster to look it up in.
        SliceOf(groups, anaId.ToString()).GetProperty("label").GetString().ShouldBe("Ana Unu");
    }

    /// <summary>
    /// The second level slices within the first, and asking for the same dimension twice is
    /// refused rather than answered with one child per parent.
    /// </summary>
    [Fact]
    public async Task A_second_level_slices_within_the_first_and_the_same_dimension_twice_is_refused()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Survey old {marker}", surveyTypeId, "authenticated", tripDate: "2024-05-01");
        await CreateTripAsync($"Survey new {marker}", surveyTypeId, "authenticated", tripDate: "2026-05-01");
        await CreateTripAsync($"Dig new {marker}", explorationTypeId, "authenticated", tripDate: "2026-05-02");

        var groups = await GroupingAsync(reader, $"search={marker}&groupBy=type&thenBy=year");
        var survey = SliceOf(groups, surveyTypeId.ToString());
        survey.GetProperty("count").GetInt32().ShouldBe(2);

        var years = survey.GetProperty("groups").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString()).OrderBy(x => x).ToList();
        years.ShouldBe(["2024", "2026"]);

        // Nothing to slice by is the panel's first choice, not a refusal.
        var none = await GroupingAsync(reader, $"search={marker}");
        none.GetProperty("groups").GetArrayLength().ShouldBe(0);

        var refused = await reader.GetAsync($"/api/v1/trip-logs/grouping?search={marker}&groupBy=type&thenBy=type");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("trip_log.group_invalid");

        var unknown = await reader.GetAsync($"/api/v1/trip-logs/grouping?groupBy=weather");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(unknown)).ShouldBe("trip_log.group_invalid");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-logs/grouping?groupBy=type")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The export is the filter and not the page, and it holds exactly what its caller may read.
    /// </summary>
    [Fact]
    public async Task The_export_holds_the_filtered_set_and_nothing_the_caller_may_not_read()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateTripAsync($"Open survey {marker}", surveyTypeId, "authenticated");
        await CreateTripAsync($"Shut survey {marker}", surveyTypeId, "private");
        await CreateTripAsync($"Open dig {marker}", explorationTypeId, "authenticated");

        // Fixture proof: the withheld trip exists and its title is in the owner's file, so its
        // absence from the reader's is the audience walk rather than a fixture that never wrote
        // it — and the positive half is what makes the absence mean anything.
        var ownerText = await SheetTextAsync(owner, $"search={marker}");
        ownerText.ShouldContain($"Shut survey {marker}");
        ownerText.ShouldContain($"Open survey {marker}");

        var readerText = await SheetTextAsync(reader, $"search={marker}");
        readerText.ShouldContain($"Open survey {marker}");
        readerText.ShouldNotContain($"Shut survey {marker}");

        // The file says whose answer it is, because a saved file outlives the screen that
        // produced it and two people's files for one filter differ legitimately.
        readerText.ShouldContain("Trips you may read");

        // The narrowing travels into the file, and the page it was taken from does not: the
        // export is of the filter, not of whichever rows were on screen.
        var narrowed = await SheetTextAsync(reader, $"search={marker}&types={explorationTypeId}");
        narrowed.ShouldContain($"Open dig {marker}");
        narrowed.ShouldNotContain($"Open survey {marker}");

        var response = await reader.GetAsync($"/api/v1/trip-logs/export?search={marker}");
        response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull();
        response.Content.Headers.ContentDisposition.FileName!.ShouldContain("trips-");

        // A word the listing refuses is refused here too, or a file would exist for a filter the
        // page rejects.
        var refused = await reader.GetAsync("/api/v1/trip-logs/export?states=underground");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeOfAsync(refused)).ShouldBe("trip_log.state_invalid");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/trip-logs/export")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private static JsonElement SliceOf(JsonElement grouping, string value) =>
        grouping.GetProperty("groups").EnumerateArray()
            .First(x => x.GetProperty("value").GetString() == value);

    private static int GroupCount(JsonElement grouping, string value) =>
        grouping.GetProperty("groups").EnumerateArray()
            .Where(x => x.GetProperty("value").GetString() == value)
            .Select(x => x.GetProperty("count").GetInt32())
            .FirstOrDefault();

    private static int SliceTotal(JsonElement grouping) =>
        grouping.GetProperty("groups").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32());

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/?pageSize=200&{query}"));

    private static async Task<JsonElement> GroupingAsync(HttpClient client, string query) =>
        await ReadAsync(await client.GetAsync($"/api/v1/trip-logs/grouping?{query}"));

    private static async Task<string> SheetTextAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/trip-logs/export?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XSSFWorkbook(stream);
        return new XSSFExcelExtractor(workbook).Text;
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString();

    private async Task<Guid> CreateTripAsync(
        string title,
        long tripTypeId,
        string visibility,
        string tripDate = "2026-04-10",
        (string Name, long RoleId)[]? people = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", new
        {
            title,
            tripDate,
            tripTypeId,
            visibility,
            caveIds = Array.Empty<Guid>(),
            participants = (people ?? [])
                .Select(p => new { newCaverName = p.Name, roleId = p.RoleId }).ToArray(),
        });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CaverIdAsync(Guid tripId, string fullName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripId)
            .Join(db.Cavers.AsNoTracking(), p => p.CaverId, c => c.Id, (p, c) => new { c.Id, c.FullName })
            .Where(x => x.FullName == fullName)
            .Select(x => x.Id)
            .Distinct()
            .SingleAsync();
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
