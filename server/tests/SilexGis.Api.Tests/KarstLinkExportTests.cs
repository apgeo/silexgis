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
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The interchange export: what leaves, what does not, and what the file and the audit
/// trail say about the difference.
/// </summary>
/// <remarks>
/// The reader throughout is a Viewer, deliberately: the seeded Editors group reads past
/// visibility at the widest scope, so a test that only ever ran as an Editor would prove
/// nothing about the visibility filter. What a treatment has to be chosen for does not
/// depend on who is asking — that is asserted here as its own case, because it is the one
/// thing that separates this route from every other place the exact-location rule is applied.
/// </remarks>
public sealed class KarstLinkExportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Url = "/api/v1/export/caves/karstlink";
    private const string PreviewUrl = "/api/v1/export/caves/karstlink/preview";
    private const double ExactLon = 25.44721;
    private const double ExactLat = 45.53127;

    /// <summary>
    /// A second surveyed position, used only by the cave that proves a coordinate is nowhere in
    /// the bytes. It has to differ from the one the open cave sits at, or "the file does not
    /// contain this number" would fail on the cave that is entitled to publish it — and passing
    /// it for the wrong reason would be worse still.
    /// </summary>
    private const double OtherLon = 22.31468;
    private const double OtherLat = 46.71235;

    private readonly SilexGisApiFactory factory;

    private HttpClient author = null!;
    private HttpClient reader = null!;
    private HttpClient anonymous = null!;

    /// <summary>
    /// Token woven into every name this instance seeds. The suite shares one database and
    /// this export is search-scoped, so a class that did not tag its rows would export
    /// another class's caves and assert against them.
    /// </summary>
    private string tag = null!;

    public KarstLinkExportTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"kl-author-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"kl-reader-{tag}@t.local");
        author = await AuthHelper.BearerClientAsync(factory, $"kl-author-{tag}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"kl-reader-{tag}@t.local");
        anonymous = factory.CreateClient();
    }

    [Fact]
    public async Task An_anonymous_caller_gets_no_file()
    {
        var response = await anonymous.PostAsJsonAsync(Url, new { treatmentForAll = "omit" });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The three treatments, on the same protected cave, in the same request shape — so the
    /// only thing that differs between the three files is what was chosen.
    /// </summary>
    [Fact]
    public async Task Each_treatment_puts_a_different_thing_in_the_file()
    {
        var (openName, protectedName) = await SeedAsync();

        // Grid: the cave is there, on the grid, and says how far that may be from the truth.
        var grid = await ExportAsync(reader, "grid_position");
        grid.GetProperty("omittedCaveCount").GetInt32().ShouldBe(0);
        var gridNode = Node(grid, protectedName);
        gridNode.GetProperty("positionTreatment").GetString().ShouldBe("grid_position");
        gridNode.GetProperty("coordinatePrecision").GetDouble().ShouldBe(5000);

        var lon = gridNode.GetProperty("longitude").GetDouble();
        var lat = gridNode.GetProperty("latitude").GetDouble();
        lon.ShouldNotBe(ExactLon);

        // The published position is the one the application's own rule produces, asked for
        // rather than reproduced here: a rounding written in this test would agree with a
        // rounding written in the exporter and both could be wrong together.
        var snapped = LocationProtection.Snap(
            new NetTopologySuite.Geometries.Point(ExactLon, ExactLat) { SRID = 4326 }, 5000);
        lon.ShouldBe(snapped.X, 1e-9);
        lat.ShouldBe(snapped.Y, 1e-9);

        // No position: the record still asserts the cave exists and says what it is.
        var withheld = await ExportAsync(reader, "no_position");
        withheld.GetProperty("omittedCaveCount").GetInt32().ShouldBe(0);
        var withheldNode = Node(withheld, protectedName);
        withheldNode.GetProperty("positionTreatment").GetString().ShouldBe("no_position");
        withheldNode.TryGetProperty("latitude", out _).ShouldBeFalse();
        withheldNode.TryGetProperty("longitude", out _).ShouldBeFalse();
        withheldNode.GetProperty("name").GetString().ShouldBe(protectedName);

        // Omit: the cave is gone and the file says one is.
        var omitted = await ExportAsync(reader, "omit");
        Names(omitted).ShouldNotContain(protectedName);
        omitted.GetProperty("omittedCaveCount").GetInt32().ShouldBe(1);
        omitted.GetProperty("provenance").GetString()!.ShouldContain("1 further cave(s)");

        // In all three the unprotected cave leaves at its surveyed position, untouched: the
        // treatment is about protection and not about exporting.
        foreach (var document in new[] { grid, withheld, omitted })
        {
            var open = Node(document, openName);
            open.GetProperty("longitude").GetDouble().ShouldBe(ExactLon, 1e-9);
            open.TryGetProperty("positionTreatment", out _).ShouldBeFalse();
        }
    }

    /// <summary>
    /// A cave the caller may not read is not in the file under any treatment — and is not
    /// counted among the omitted either, because that count is a statement about what this
    /// caller chose to withhold and not a census of what exists.
    /// </summary>
    [Fact]
    public async Task A_cave_the_caller_may_not_read_is_in_the_file_under_no_treatment()
    {
        var (openName, _) = await SeedAsync();
        var privateName = $"KL Private Cave {tag}";
        var privateCave = await CreateCaveAsync(privateName, locationProtected: false, visibility: "private");
        await CreateEntranceAsync(privateCave);

        foreach (var treatment in new[] { "grid_position", "no_position", "omit" })
        {
            var document = await ExportAsync(reader, treatment);
            var names = Names(document);

            // The positive leg in the same test: the fixture really did produce a file with
            // caves in it, so the absence below is the rule working and not an empty answer.
            names.ShouldContain(openName);
            names.ShouldNotContain(privateName);

            // Not even by identifier: the file must not disclose that the cave is there.
            document.GetRawText().ShouldNotContain(privateCave.ToString());
            document.GetProperty("omittedCaveCount").GetInt32()
                .ShouldBe(treatment == "omit" ? 1 : 0, "a cave nobody may read was counted as withheld");
        }

        // And the author, who may read it, does get it — so "private" is a grant this reader
        // lacks rather than a cave that was never created.
        var authorsDocument = await ExportAsync(author, "grid_position");
        Names(authorsDocument).ShouldContain(privateName);
    }

    [Fact]
    public async Task A_protected_cave_with_nothing_chosen_for_it_refuses_the_whole_export()
    {
        var (_, _) = await SeedAsync();

        var response = await reader.PostAsJsonAsync(Url, new { search = tag });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("export.treatment_missing");
        problem.GetProperty("caveIds").EnumerateArray().Count().ShouldBe(1);
    }

    /// <summary>
    /// The one request the whole design exists to refuse. There is no code for the surveyed
    /// position, so asking for it is indistinguishable from asking for nonsense — which is
    /// the point: the two cannot drift apart because they are one code path.
    /// </summary>
    [Theory]
    [InlineData("exact")]
    [InlineData("exact_position")]
    [InlineData("surveyed")]
    [InlineData("")]
    public async Task A_request_naming_the_exact_position_is_refused(string treatment)
    {
        await SeedAsync();

        var response = await reader.PostAsJsonAsync(Url, new { search = tag, treatmentForAll = treatment });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("export.treatment_unknown");
    }

    [Fact]
    public async Task A_treatment_named_for_one_cave_wins_over_the_answer_for_all()
    {
        var (openName, protectedName) = await SeedAsync();
        var protectedId = await CaveIdAsync(protectedName);

        var response = await reader.PostAsJsonAsync(Url, new
        {
            search = tag,
            treatmentForAll = "grid_position",
            treatments = new Dictionary<string, string> { [protectedId.ToString()] = "omit" },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var document = await ReadAsync(response);
        Names(document).ShouldNotContain(protectedName);
        Names(document).ShouldContain(openName);
        document.GetProperty("omittedCaveCount").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// A stored answer stands in for a decision the exporter already made, and is consulted
    /// only where the request says nothing.
    /// </summary>
    [Fact]
    public async Task A_stored_answer_is_used_when_the_request_names_none()
    {
        var (_, protectedName) = await SeedAsync();

        var response = await reader.PostAsJsonAsync(Url, new { search = tag, defaultTreatment = "no_position" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var document = await ReadAsync(response);
        Node(document, protectedName).GetProperty("positionTreatment").GetString().ShouldBe("no_position");
    }

    [Fact]
    public async Task The_audit_entry_records_which_caves_got_which_treatment()
    {
        var (_, protectedName) = await SeedAsync();
        var protectedId = await CaveIdAsync(protectedName);

        var document = await ExportAsync(reader, "grid_position");
        var exportId = document.GetProperty("@id").GetString()!["urn:uuid:".Length..];

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var entry = await db.AuditEntries
            .AsNoTracking()
            .SingleAsync(e => e.Action == AuditActions.Exported && e.EntityId == exportId);

        entry.EntityType.ShouldBe("CaveExport");
        entry.UserId.ShouldNotBeNull();

        var changes = JsonDocument.Parse(entry.Changes!).RootElement;
        changes.GetProperty("format").GetString().ShouldBe("karstlink-jsonld");
        changes.GetProperty("caveCount").GetInt32().ShouldBe(2);
        changes.GetProperty("omittedCaveCount").GetInt32().ShouldBe(0);

        // The treatment is recorded against the cave it was applied to — "who exported which
        // caves how" is the question asked afterwards, and the file is long gone by then.
        var treated = changes.GetProperty("protectedPositionTreatments").GetProperty("grid_position")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        treated.ShouldBe([protectedId.ToString()]);
    }

    /// <summary>
    /// The chooser is told how big the decision is, and told it by the server.
    ///
    /// Read twice by two callers with very different rights over exactly the same two caves,
    /// and the answer is the same both times, because the number counts caves this
    /// installation protects rather than caves this caller may not place. The author owns
    /// both and sees both exactly on every other screen in the application; a count that
    /// followed the caller's rights would tell them there is nothing to decide, and the
    /// surveyed coordinate of a protected cave would leave in a file that never mentions it
    /// was protected.
    /// </summary>
    [Fact]
    public async Task The_preview_counts_the_protected_caves_whoever_is_asking()
    {
        await SeedAsync();

        var forReader = await PreviewAsync(reader);
        forReader.GetProperty("caveCount").GetInt32().ShouldBe(2);
        forReader.GetProperty("protectedCaveCount").GetInt32().ShouldBe(1);
        forReader.GetProperty("exceedsLimit").GetBoolean().ShouldBeFalse();

        var forAuthor = await PreviewAsync(author);
        forAuthor.GetProperty("caveCount").GetInt32().ShouldBe(2);
        forAuthor.GetProperty("protectedCaveCount").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// The cave's own author — who created it, owns it, and is shown its surveyed position
    /// everywhere else — is asked the question like anybody else, and is refused when they
    /// do not answer it.
    /// </summary>
    /// <remarks>
    /// This is the case the whole route exists for. Everywhere else in the application "may
    /// this account see the exact position" is asked once and answered once; here the answer
    /// is written into bytes that outlive the account, so being allowed to look is not the
    /// same as being allowed to hand it on. Whichever of the three is then chosen, the
    /// surveyed coordinate is not in the file and the file says what was done instead.
    /// </remarks>
    [Fact]
    public async Task The_cave_s_own_author_cannot_export_a_protected_position_either()
    {
        var (openName, protectedName) = await SeedAsync();

        var refused = await author.PostAsJsonAsync(Url, new { search = tag });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("export.treatment_missing");

        foreach (var treatment in new[] { "grid_position", "no_position" })
        {
            var node = Node(await ExportAsync(author, treatment), protectedName);
            node.GetProperty("positionTreatment").GetString().ShouldBe(treatment);

            var longitude = node.TryGetProperty("longitude", out var value) ? value.GetDouble() : (double?)null;
            longitude.ShouldNotBe(ExactLon);
        }

        var omitted = await ExportAsync(author, "omit");
        Names(omitted).ShouldNotContain(protectedName);
        omitted.GetProperty("omittedCaveCount").GetInt32().ShouldBe(1);

        // And the whole document, not only the node the assertions above looked at. A second
        // protected cave at a position of its own, because the open cave in this fixture sits
        // at the shared one and is entitled to publish it — a search for that number would come
        // back positive for the right reason and prove nothing.
        var otherName = $"KL Elsewhere Cave {tag}";
        await CreateEntranceAsync(
            await CreateCaveAsync(otherName, locationProtected: true), OtherLon, OtherLat);

        var elsewhere = OtherLon.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var treatment in new[] { "grid_position", "no_position", "omit" })
        {
            var document = await ExportAsync(author, treatment);

            // The positive half: the file really is about these caves, so the absence below is
            // the rule working rather than an empty answer.
            Names(document).ShouldContain(openName);
            document.GetRawText().ShouldNotContain(elsewhere);
        }
    }

    /// <summary>
    /// A cave matched by a toponym rather than by its name is in the count and in the file.
    /// </summary>
    /// <remarks>
    /// The screen this export is started from searches names and toponyms together and hands
    /// its text straight across. An export matching only the name would drop caves the person
    /// could see on that screen, in a file whose header states that nothing was left out — a
    /// document of true statements adding up to a false one, which is the failure this format
    /// is written to make impossible.
    /// </remarks>
    [Fact]
    public async Task A_cave_found_by_its_toponym_on_the_list_is_in_the_file()
    {
        var toponym = $"KL Toponym {tag}";
        var caveName = $"KL Named Otherwise {tag}";
        var caveId = await CreateCaveAsync(caveName, locationProtected: false);
        await CreateEntranceAsync(caveId);

        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var cave = await db.Caves.SingleAsync(c => c.Id == caveId);
            cave.OtherToponyms = toponym;
            await db.SaveChangesAsync();
        }

        var preview = await reader.PostAsJsonAsync(PreviewUrl, new { search = toponym });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        (await ReadAsync(preview)).GetProperty("caveCount").GetInt32().ShouldBe(1);

        var response = await reader.PostAsJsonAsync(
            Url, new { search = toponym, treatmentForAll = "grid_position" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Names(await ReadAsync(response)).ShouldContain(caveName);
    }

    [Fact]
    public async Task An_anonymous_caller_is_told_no_counts()
    {
        var response = await anonymous.PostAsJsonAsync(PreviewUrl, new { });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A cave the caller may not read is not counted, and is therefore not something the
    /// chooser ever asks about. The preview narrows by exactly the filter the export narrows
    /// by, or the number in front of the person would be about a different set of caves from
    /// the one going into the file.
    /// </summary>
    [Fact]
    public async Task The_preview_does_not_count_a_cave_the_caller_may_not_read()
    {
        await SeedAsync();
        var hiddenName = $"KL Hidden Cave {tag}";
        await CreateEntranceAsync(await CreateCaveAsync(hiddenName, locationProtected: false, visibility: "private"));

        // The author can read their own private cave, so the same request counts three for them
        // and two for the reader — the positive half of the same assertion.
        (await PreviewAsync(author)).GetProperty("caveCount").GetInt32().ShouldBe(3);
        (await PreviewAsync(reader)).GetProperty("caveCount").GetInt32().ShouldBe(2);
    }

    /// <summary>
    /// A cave carries its identity in another register, and a protected one does not.
    /// </summary>
    /// <remarks>
    /// The link is what makes two files about the same caves joinable at all, and it is also a
    /// second door onto a position: the register at the other end publishes coordinates, so a
    /// link on a cave whose position was withheld here would hand that position over by
    /// reference while the file still said it had been protected. The withholding is the point
    /// of the test; the cave that does carry a link is what proves the fixture works.
    /// </remarks>
    [Fact]
    public async Task An_identity_in_another_register_travels_only_for_a_cave_nobody_protected()
    {
        var (openName, protectedName) = await SeedAsync();

        foreach (var name in new[] { openName, protectedName })
        {
            var id = await CaveIdAsync(name);
            var response = await author.PutAsJsonAsync(
                $"/api/v1/caves/{id}/external-ids/grottocenter", new { value = $"reg-{name.GetHashCode()}" });
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        foreach (var treatment in new[] { "grid_position", "no_position" })
        {
            var document = await ExportAsync(reader, treatment);
            Node(document, openName).TryGetProperty("sameAs", out var open).ShouldBeTrue();
            open.GetString().ShouldNotBeNullOrWhiteSpace();

            Node(document, protectedName).TryGetProperty("sameAs", out _)
                .ShouldBeFalse("a protected cave was linked to a register that publishes its position");
        }
    }

    // ---- fixture ----

    /// <summary>Seeds one cave anybody signed in may place, and one this installation protects.</summary>
    private async Task<(string OpenName, string ProtectedName)> SeedAsync()
    {
        var openName = $"KL Open Cave {tag}";
        var protectedName = $"KL Protected Cave {tag}";

        await CreateEntranceAsync(await CreateCaveAsync(openName, locationProtected: false));
        await CreateEntranceAsync(await CreateCaveAsync(protectedName, locationProtected: true));
        return (openName, protectedName);
    }

    private async Task<JsonElement> PreviewAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(PreviewUrl, new { search = tag });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    private async Task<JsonElement> ExportAsync(HttpClient client, string treatmentForAll)
    {
        var response = await client.PostAsJsonAsync(Url, new { search = tag, treatmentForAll });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/ld+json");
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static List<string?> Names(JsonElement document) =>
        document.GetProperty("@graph").EnumerateArray()
            .Select(n => n.TryGetProperty("name", out var name) ? name.GetString() : null)
            .ToList();

    private static JsonElement Node(JsonElement document, string name) =>
        document.GetProperty("@graph").EnumerateArray()
            .Single(n => n.TryGetProperty("name", out var value) && value.GetString() == name);

    private async Task<Guid> CaveIdAsync(string name)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.Features.AsNoTracking().Where(f => f.Name == name).Select(f => f.Id).SingleAsync();
    }

    private async Task<long> CaveTypeIdAsync()
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        return await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync();
    }

    private async Task<Guid> CreateCaveAsync(
        string name, bool locationProtected, string visibility = "authenticated")
    {
        var response = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId = await CaveTypeIdAsync(),
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
            surveyedLength = 1234.5,
            depth = 88.5,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task CreateEntranceAsync(Guid caveId, double? lon = null, double? lat = null)
    {
        long entranceTypeId;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync();
        }

        var response = await author.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon ?? ExactLon, lat ?? ExactLat } },
            positionQuality = "Gps",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
