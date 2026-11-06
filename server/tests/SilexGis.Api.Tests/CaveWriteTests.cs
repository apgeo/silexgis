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
/// The cave register's write path: a catalogue id that names nothing is a coded refusal on
/// create and on update, and an update that does not carry the property document leaves the
/// stored one as it was instead of emptying it.
/// </summary>
public sealed class CaveWriteTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private HttpClient editor = null!;
    private string marker = null!;
    private long caveTypeId;

    public CaveWriteTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"cavew-edi-{marker}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"cavew-edi-{marker}@t.local");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
    }

    [Fact]
    public async Task A_cave_type_or_rock_type_that_does_not_exist_is_refused_with_a_code_on_create_and_update()
    {
        var unknownCaveType = await UnusedIdAsync(db => db.CaveTypes.Select(t => t.Id));
        var unknownRockType = await UnusedIdAsync(db => db.RockTypes.Select(t => t.Id));

        var badType = await editor.PostAsJsonAsync("/api/v1/caves", Body($"Typeless {marker}", unknownCaveType));
        badType.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await badType.Content.ReadAsStringAsync());
        (await CodeOf(badType)).ShouldBe("cave.type_unknown");

        var badRock = await editor.PostAsJsonAsync("/api/v1/caves",
            Body($"Rockless {marker}", caveTypeId, rockTypeId: unknownRockType));
        badRock.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await badRock.Content.ReadAsStringAsync());
        (await CodeOf(badRock)).ShouldBe("cave.rock_type_unknown");

        // The same cave with ids that exist is created, and its update meets the same rule.
        var created = await editor.PostAsJsonAsync("/api/v1/caves", Body($"Typed {marker}", caveTypeId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var updated = await editor.PutWithIfMatchAsync($"/api/v1/caves/{id}", Body($"Typed {marker}", unknownCaveType));
        updated.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await updated.Content.ReadAsStringAsync());
        (await CodeOf(updated)).ShouldBe("cave.type_unknown");
        var rockUpdate = await editor.PutWithIfMatchAsync($"/api/v1/caves/{id}",
            Body($"Typed {marker}", caveTypeId, rockTypeId: unknownRockType));
        rockUpdate.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await rockUpdate.Content.ReadAsStringAsync());
        (await CodeOf(rockUpdate)).ShouldBe("cave.rock_type_unknown");

        (await editor.PutWithIfMatchAsync($"/api/v1/caves/{id}", Body($"Typed again {marker}", caveTypeId)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_update_without_a_property_document_keeps_the_stored_one()
    {
        var created = await editor.PostAsJsonAsync("/api/v1/caves",
            Body($"Noted {marker}", caveTypeId, properties: new { clubNote = "kept" }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var omitted = await editor.PutWithIfMatchAsync($"/api/v1/caves/{id}", Body($"Noted renamed {marker}", caveTypeId));
        omitted.StatusCode.ShouldBe(HttpStatusCode.OK, await omitted.Content.ReadAsStringAsync());
        var answered = await omitted.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("name").GetString().ShouldBe($"Noted renamed {marker}");
        answered.GetProperty("properties").GetProperty("clubNote").GetString().ShouldBe("kept");

        // A document that is sent still replaces the stored one.
        var replaced = await editor.PutWithIfMatchAsync($"/api/v1/caves/{id}",
            Body($"Noted renamed {marker}", caveTypeId, properties: new { other = 1 }));
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
        var stored = (await editor.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{id}")).GetProperty("properties");
        stored.TryGetProperty("clubNote", out _).ShouldBeFalse();
        stored.GetProperty("other").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// Every register field travels from the write to the read and back, each with a value no
    /// other field holds, so that two fields swapped on the way — two depths, two lengths — show.
    /// The update replaces every value, read back from the cave and from its row in the list.
    /// </summary>
    [Fact]
    public async Task Every_register_field_is_stored_and_read_back_as_written_on_create_and_update()
    {
        long otherCaveTypeId, rockTypeId, otherRockTypeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var caveTypes = await db.CaveTypes.OrderBy(t => t.Id).Select(t => t.Id).Take(2).ToListAsync();
            otherCaveTypeId = caveTypes[^1];
            var rockTypes = await db.RockTypes.OrderBy(t => t.Id).Select(t => t.Id).Take(2).ToListAsync();
            rockTypeId = rockTypes[0];
            otherRockTypeId = rockTypes[^1];
        }

        Dictionary<string, object?> Fields(string tag, long typeId, long rockId, decimal start, string status, bool show) => new()
        {
            ["name"] = $"Register {tag} {marker}",
            ["otherToponyms"] = $"Toponyms {tag}",
            ["identificationCode"] = $"ID-{tag}-{marker}",
            ["caveTypeId"] = typeId,
            ["description"] = $"Description {tag}",
            ["website"] = $"https://example.org/{tag}",
            ["region"] = $"Region {tag}",
            ["hydrographicBasin"] = $"Basin {tag}",
            ["valley"] = $"Valley {tag}",
            ["tributaryRiver"] = $"River {tag}",
            ["closestAddress"] = $"Address {tag}",
            ["landRegistryNumber"] = $"LR-{tag}",
            ["locationNotes"] = $"Notes {tag}",
            ["rockTypeId"] = rockId,
            ["rockAge"] = $"Age {tag}",
            ["surveyedLength"] = start + 1.01m,
            ["estimatedLength"] = start + 2.02m,
            ["realExtension"] = start + 3.03m,
            ["projectedExtension"] = start + 4.04m,
            ["depth"] = start + 5.05m,
            ["positiveDepth"] = start + 6.06m,
            ["negativeDepth"] = start + 7.07m,
            ["potentialDepth"] = start + 8.08m,
            ["altitude"] = start + 9.09m,
            ["volume"] = start + 10.10m,
            ["area"] = start + 11.11m,
            ["ramificationIndex"] = 1.234m + (start / 1000m),
            ["caveAge"] = (int)start + 13,
            ["explorationStatus"] = status,
            ["protectionClass"] = $"Class {tag}",
            ["isShowCave"] = show,
            ["showCaveLength"] = start + 14.14m,
            ["discoveryDate"] = $"19{tag.Length}5",
            ["discoverer"] = $"Discoverer {tag}",
        };

        static void ShouldHold(JsonElement read, Dictionary<string, object?> written)
        {
            foreach (var (field, value) in written)
            {
                var actual = read.GetProperty(field);
                switch (value)
                {
                    case string text:
                        actual.GetString().ShouldBe(text, field);
                        break;
                    case decimal number:
                        actual.GetDecimal().ShouldBe(number, field);
                        break;
                    case long id:
                        actual.GetInt64().ShouldBe(id, field);
                        break;
                    case int whole:
                        actual.GetInt32().ShouldBe(whole, field);
                        break;
                    case bool flag:
                        actual.GetBoolean().ShouldBe(flag, field);
                        break;
                }
            }
        }

        object WriteBody(Dictionary<string, object?> fields) =>
            new Dictionary<string, object?>(fields)
            {
                ["visibility"] = "authenticated",
                ["locationProtected"] = false,
            };

        var first = Fields("first", caveTypeId, rockTypeId, 100m, "ongoing", false);
        var created = await editor.PostAsJsonAsync("/api/v1/caves", WriteBody(first));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        ShouldHold(await editor.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{id}"), first);

        var second = Fields("second", otherCaveTypeId, otherRockTypeId, 200m, "finished", true);
        var updated = await editor.PutWithIfMatchAsync($"/api/v1/caves/{id}", WriteBody(second));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        ShouldHold(await updated.Content.ReadFromJsonAsync<JsonElement>(), second);
        ShouldHold(await editor.GetFromJsonAsync<JsonElement>($"/api/v1/caves/{id}"), second);

        var listed = (await editor.GetFromJsonAsync<JsonElement>($"/api/v1/caves?search={marker}&pageSize=100"))
            .GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        ShouldHold(listed, new Dictionary<string, object?>
        {
            ["name"] = second["name"],
            ["identificationCode"] = second["identificationCode"],
            ["caveTypeId"] = second["caveTypeId"],
            ["region"] = second["region"],
            ["surveyedLength"] = second["surveyedLength"],
            ["depth"] = second["depth"],
            ["explorationStatus"] = second["explorationStatus"],
        });
    }

    public static TheoryData<string, object?> InvalidCaveFields => new()
    {
        { "name", "" },
        { "name", new string('n', 256) },
        { "otherToponyms", new string('o', 251) },
        { "identificationCode", new string('i', 51) },
        { "caveTypeId", 0 },
        { "website", new string('w', 256) },
        { "region", new string('r', 101) },
        { "hydrographicBasin", new string('h', 101) },
        { "valley", new string('v', 101) },
        { "tributaryRiver", new string('t', 101) },
        { "closestAddress", new string('c', 201) },
        { "landRegistryNumber", new string('l', 51) },
        { "rockAge", new string('a', 51) },
        { "protectionClass", new string('p', 51) },
        { "discoveryDate", new string('d', 51) },
        { "discoverer", new string('x', 256) },
        { "surveyedLength", -1 },
        { "estimatedLength", -1 },
        { "depth", -1 },
        { "volume", -1 },
        { "area", -1 },
        { "visibility", 99 },
        { "explorationStatus", 99 },
        { "properties", "not an object" },
    };

    [Theory]
    [MemberData(nameof(InvalidCaveFields))]
    public async Task A_cave_body_outside_the_rules_is_refused_with_a_validation_code(string field, object? value)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Valid {marker}",
            ["caveTypeId"] = caveTypeId,
            ["visibility"] = "authenticated",
            ["locationProtected"] = false,
            ["explorationStatus"] = "unknown",
            ["isShowCave"] = false,
        };
        body[field] = value;

        var response = await editor.PostAsJsonAsync("/api/v1/caves", body);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await CodeOf(response)).ShouldBe("validation.failed");
    }

    private object Body(string name, long typeId, long? rockTypeId = null, object? properties = null) => new
    {
        name,
        caveTypeId = typeId,
        rockTypeId,
        visibility = "authenticated",
        locationProtected = false,
        explorationStatus = "unknown",
        isShowCave = false,
        properties,
    };

    private async Task<long> UnusedIdAsync(Func<SilexGisDbContext, IQueryable<long>> ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return (await ids(db).OrderByDescending(x => x).FirstOrDefaultAsync()) + 1000;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        editor?.Dispose();
        factory.Dispose();
    }
}
