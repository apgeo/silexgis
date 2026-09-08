// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The registry answered as a dataset, over real HTTP.
///
/// <para>
/// Every request here names a region minted for this class alone. The registry statistics count
/// every cave in the installation that the caller may read, and this assembly runs its classes
/// against one shared database — so an unscoped assertion would be a claim about every other
/// class's fixtures as well, and would pass or fail depending on which of them ran first.
/// </para>
/// <para>
/// The withheld caller is always a genuine <c>Viewer</c>, or a caller carrying an explicit deny.
/// The seeded Editors group holds Read over every content domain at the widest scope there is, so
/// an Editor who "cannot see" something proves nothing whatever. And every refusal below is paired
/// with the same request being answered for somebody entitled to it, over the same fixture, so a
/// zero is a withholding and not an empty seed.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegistryStatisticsTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;
    private HttpClient viewer = null!;
    private HttpClient anonymous = null!;
    private Guid viewerId;
    private long caveTypeId;
    private long karstAreaTypeId;
    private string suffix = null!;

    public RegistryStatisticsTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString, configureServices: JobWorkers.RemoveFrom);

    public async Task InitializeAsync()
    {
        suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"reg-own-{suffix}@t.local");
        viewerId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Viewer, $"reg-view-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes
                .Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"reg-own-{suffix}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"reg-view-{suffix}@t.local");
        anonymous = factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        viewer?.Dispose();
        anonymous?.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task A_thin_sector_is_joined_to_its_neighbour_and_an_empty_one_is_left_standing()
    {
        // Five short caves, three around eight hundred and fifty metres, and two near a thousand.
        // Over ten sectors the last one holds two caves, which is under the floor, and the eight
        // sectors between the two groups hold nothing at all.
        var region = await SeedAsync("SH", [10m, 20m, 30m, 40m, 50m, 850m, 860m, 870m, 995m, 1000m]);

        var body = await ReadJsonAsync(
            viewer, $"distribution?measure=surveyedLength&bins=10&region={region}");

        body.GetProperty("measuredCount").GetInt32().ShouldBe(10);
        var bins = body.GetProperty("bins").EnumerateArray().ToList();

        // Nothing was suppressed: the sectors still cover the range end to end with no gap in it,
        // and every cave is still inside one of them. A hole in an even axis is a statement that
        // something rare sits exactly there.
        bins.Sum(b => b.GetProperty("count").GetInt32()).ShouldBe(10);
        for (var i = 1; i < bins.Count; i++)
        {
            bins[i].GetProperty("lowerBound").GetDouble()
                .ShouldBe(bins[i - 1].GetProperty("upperBound").GetDouble(), 1e-9);
        }

        // Every published sector holds either nothing or at least the floor. A sector holding one
        // or two caves on an axis of lengths describes them closely enough to name them.
        foreach (var bin in bins)
        {
            var count = bin.GetProperty("count").GetInt32();
            (count == 0 || count >= 3).ShouldBeTrue($"a sector of {count} caves was published.");
        }

        // The thin one was joined rather than dropped, and says so.
        bins.ShouldContain(b => b.GetProperty("merged").GetBoolean());

        // And the empty stretch between the two groups survived: it describes nothing and
        // identifies nobody, and it is the shape the histogram was drawn for.
        bins.ShouldContain(b => b.GetProperty("count").GetInt32() == 0);
    }

    [Fact]
    public async Task The_saved_file_states_the_same_figures_the_screen_does_for_the_same_caller()
    {
        var region = await SeedAsync("EX", [10m, 20m, 30m, 40m, 50m, 850m, 860m, 870m, 995m, 1000m]);
        var query = $"distribution?measure=surveyedLength&bins=10&region={region}";

        var onScreen = await ReadJsonAsync(viewer, query);
        var sheet = await SheetAsync(viewer, query);

        Figure(sheet, "Caves in scope").ShouldBe(onScreen.GetProperty("caveCount").GetInt32());
        Figure(sheet, "Caves recording it").ShouldBe(onScreen.GetProperty("measuredCount").GetInt32());
        Figure(sheet, "Smallest recorded").ShouldBe(onScreen.GetProperty("minimum").GetDouble(), 1e-6);
        Figure(sheet, "Largest recorded").ShouldBe(onScreen.GetProperty("maximum").GetDouble(), 1e-6);

        // The histogram itself, sector by sector. A file that agreed on the totals and disagreed on
        // the sectors would be the more dangerous of the two disagreements: a finer axis in a file
        // than on the screen is the screen's refusal handed over anyway.
        var onPaper = BinRows(sheet);
        var expected = onScreen.GetProperty("bins").EnumerateArray().ToList();
        onPaper.Count.ShouldBe(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            onPaper[i].Lower.ShouldBe(expected[i].GetProperty("lowerBound").GetDouble(), 1e-6);
            onPaper[i].Upper.ShouldBe(expected[i].GetProperty("upperBound").GetDouble(), 1e-6);
            onPaper[i].Count.ShouldBe(expected[i].GetProperty("count").GetInt32());
        }
    }

    [Fact]
    public async Task A_cave_the_caller_may_not_read_is_counted_for_neither_of_them()
    {
        var region = await SeedAsync("SE", [100m, 200m, 300m]);
        var hidden = await CreateCaveAsync(region, 400m, visibility: "private");

        // The fixture proves itself first: the owner really does have four caves here, so the
        // viewer's three are a withholding and not an empty region.
        var seenByOwner = await ReadJsonAsync(
            owner, $"distribution?measure=surveyedLength&region={region}");
        seenByOwner.GetProperty("measuredCount").GetInt32().ShouldBe(4);
        seenByOwner.GetProperty("maximum").GetDouble().ShouldBe(400, 1e-6);

        var seenByViewer = await ReadJsonAsync(
            viewer, $"distribution?measure=surveyedLength&region={region}");
        seenByViewer.GetProperty("caveCount").GetInt32().ShouldBe(3);
        seenByViewer.GetProperty("measuredCount").GetInt32().ShouldBe(3);

        // Not merely absent from the count: absent from the range as well, which is where a filter
        // applied after the arithmetic would have left it.
        seenByViewer.GetProperty("maximum").GetDouble().ShouldBe(300, 1e-6);

        hidden.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_cave_the_caller_may_read_and_may_not_place_is_in_the_totals_and_under_no_region()
    {
        var region = await SeedAsync("PL", [100m, 200m]);
        var guarded = await CreateCaveAsync(region, 300m, locationProtected: true);

        // The half that has to be true first: the viewer can read this cave. Without it the test
        // below would be measuring readability and not placement.
        var caveResponse = await viewer.GetAsync($"/api/v1/caves/{guarded}");
        caveResponse.StatusCode.ShouldBe(
            HttpStatusCode.OK, await caveResponse.Content.ReadAsStringAsync());
        JsonDocument.Parse(await caveResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("locationProtected").GetBoolean().ShouldBeTrue();

        // Counted in the totals. Leaving it out would make every total one smaller for each cave
        // whose position is closed, and the difference against the list the caller can see is the
        // disclosure.
        var distribution = await ReadJsonAsync(
            viewer, $"distribution?measure=surveyedLength&region={region}");
        distribution.GetProperty("caveCount").GetInt32().ShouldBe(3);
        distribution.GetProperty("maximum").GetDouble().ShouldBe(300, 1e-6);

        // And standing under no place, because a count under a named region including it would say
        // where it is.
        var breakdown = await ReadJsonAsync(viewer, $"regions?region={region}");
        breakdown.GetProperty("caveCount").GetInt32().ShouldBe(3);
        var rows = breakdown.GetProperty("regions").EnumerateArray().ToList();
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("region").GetString().ShouldBe(region);
        rows[0].GetProperty("caveCount").GetInt32().ShouldBe(2);

        // Both halves of the rule, over one fixture: the owner may place it, so for them the row
        // and the total agree. A count that was simply wrong would be wrong for everybody.
        var ownerBreakdown = await ReadJsonAsync(owner, $"regions?region={region}");
        ownerBreakdown.GetProperty("regions").EnumerateArray()
            .Single().GetProperty("caveCount").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task The_breakdown_is_ordered_by_the_region_and_never_by_how_many_stand_under_it()
    {
        // Three regions whose alphabetical order matches neither their order by size nor its
        // reverse. Two would not do: with a small AA and a large ZZ, alphabetical and
        // ascending-by-count give the same sequence, so the test would pass over an ordering by a
        // withheld quantity in one of the two directions — and an ordering over withheld
        // quantities publishes their relative sizes whichever way it runs.
        //
        // Alphabetical:      AA(3), MM(1), ZZ(2)
        // Ascending count:   MM(1), ZZ(2), AA(3)
        // Descending count:  AA(3), ZZ(2), MM(1)
        var first = await SeedAsync("AA", [10m, 20m, 30m]);
        var middle = await SeedAsync("MM", [10m]);
        var last = await SeedAsync("ZZ", [10m, 20m]);

        var body = await ReadJsonAsync(viewer, "regions");
        var names = body.GetProperty("regions").EnumerateArray()
            .Select(r => r.GetProperty("region").GetString()).ToList();

        var firstAt = names.IndexOf(first);
        var middleAt = names.IndexOf(middle);
        var lastAt = names.IndexOf(last);
        firstAt.ShouldBeGreaterThanOrEqualTo(0);
        middleAt.ShouldBeGreaterThanOrEqualTo(0);
        lastAt.ShouldBeGreaterThanOrEqualTo(0);
        firstAt.ShouldBeLessThan(middleAt);
        middleAt.ShouldBeLessThan(lastAt);
    }

    [Fact]
    public async Task Two_measures_that_move_together_exactly_are_reported_as_doing_so()
    {
        // Depth as the square of length, so on logarithmic axes the points lie on one line of slope
        // two — a figure that can be checked by hand rather than against the code that produced it.
        var region = Unique("CO");
        foreach (var (length, depth) in new[]
                 {
                     (10m, 100m), (20m, 400m), (40m, 1600m), (80m, 6400m),
                 })
        {
            await CreateCaveAsync(region, length, depth: depth);
        }

        var body = await ReadJsonAsync(
            viewer, $"correlation?x=surveyedLength&y=depth&region={region}");

        body.GetProperty("count").GetInt32().ShouldBe(4);
        body.GetProperty("slope").GetDouble().ShouldBe(2, 1e-9);
        body.GetProperty("rSquared").GetDouble().ShouldBe(1, 1e-9);
        body.GetProperty("logarithmic").GetBoolean().ShouldBeTrue();
        body.GetProperty("basis").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_request_the_bounds_refuse_is_refused_on_the_screen_and_on_the_file_alike()
    {
        var region = await SeedAsync("VA", [10m, 20m, 30m]);

        // One sector is not a histogram; a floor of one is the rule that keeps a sector from
        // describing a single cave, asked to stand down; and a descending list of quantiles would
        // publish a median before its own first quartile.
        foreach (var query in new[]
                 {
                     $"distribution?measure=surveyedLength&bins=1&region={region}",
                     $"distribution?measure=surveyedLength&bins=1000&region={region}",
                     $"distribution?measure=surveyedLength&minimumBinCaveCount=1&region={region}",
                     $"distribution?measure=surveyedLength&percentiles=0.9,0.1&region={region}",
                     $"distribution?measure=surveyedLength&percentiles=1.5&region={region}",

                     // A measure this registry does not publish, and no measure at all. Altitude
                     // is deliberately not among the names: a height above sea level is a
                     // coordinate, and a distribution of coordinates asked one range at a time is
                     // a way of placing a cave whose position is closed to the caller.
                     $"distribution?measure=altitude&region={region}",
                     $"distribution?region={region}",
                 })
        {
            foreach (var route in new[] { query, Exported(query) })
            {
                var response = await viewer.GetAsync($"/api/v1/stats/registry/{route}");
                var payload = await response.Content.ReadAsStringAsync();
                response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);
                JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()
                    .ShouldBe("validation.failed", payload);
            }
        }

        // The same request with the bounds respected is answered, so what is refused above is the
        // rule and not the route.
        (await viewer.GetAsync(
                $"/api/v1/stats/registry/distribution?measure=surveyedLength&bins=4&region={region}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A measure correlated with itself fits a perfect line through every cave and says nothing,
        // and two spellings of one measure are still one measure.
        (await viewer.GetAsync("/api/v1/stats/registry/correlation?x=depth&y=Depth"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // And a name the registry does not publish is refused rather than answered as whichever
        // measure happened to be first.
        (await viewer.GetAsync("/api/v1/stats/registry/correlation?x=altitude&y=depth"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Nothing_here_is_answered_to_a_caller_who_is_not_signed_in()
    {
        foreach (var route in new[]
                 {
                     "distribution?measure=surveyedLength",
                     "distribution/export?measure=surveyedLength",
                     "correlation?x=surveyedLength&y=depth",
                     "correlation/export?x=surveyedLength&y=depth",
                     "regions",
                     "regions/export",
                 })
        {
            (await anonymous.GetAsync($"/api/v1/stats/registry/{route}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);
        }
    }

    [Fact]
    public async Task An_area_the_caller_may_not_read_is_answered_as_one_that_is_not_there()
    {
        var areaId = await CreateKarstAreaAsync();
        var query = $"distribution?measure=surveyedLength&areaId={areaId}";

        // The fixture proves itself: before the deny, this viewer is answered about this area.
        (await viewer.GetAsync($"/api/v1/stats/registry/{query}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The unreadable state, built explicitly. It has to be an object-scoped deny rather than a
        // private visibility, because readability cascades down the containment chain and a
        // Viewer is otherwise shown anything marked as readable by every signed-in account.
        await DenyReadAsync(owner, areaId, viewerId);

        foreach (var route in new[] { query, Exported(query) })
        {
            var response = await viewer.GetAsync($"/api/v1/stats/registry/{route}");
            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, payload);
            JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()
                .ShouldBe("feature.not_found", payload);
        }

        // And the owner is still answered, so the refusal is the missing right and not a broken
        // route. An area that does not exist is refused in the same words, deliberately: a refusal
        // that told the two apart would report which areas are protected.
        (await owner.GetAsync($"/api/v1/stats/registry/{query}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync(
                $"/api/v1/stats/registry/distribution?measure=surveyedLength&areaId={Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_saved_file_says_what_it_was_counted_over()
    {
        var region = await SeedAsync("BA", [10m, 20m, 30m]);

        var response = await viewer.GetAsync(
            $"/api/v1/stats/registry/distribution/export?measure=surveyedLength&region={region}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName
            .ShouldNotBeNull().ShouldContain("registry-statistics-distribution");

        var text = string.Join(
            '\n', Rows(await response.Content.ReadAsByteArrayAsync()).SelectMany(r => r));
        text.ShouldContain("Computed over the caves you may read");
    }

    [Fact]
    public async Task The_saved_fit_states_the_same_figures_the_screen_does_for_the_same_caller()
    {
        // Depth as the square of length again, so the fit is a figure that can be checked by hand:
        // on logarithmic axes the points lie on one line of slope two.
        var region = Unique("CX");
        foreach (var (length, depth) in new[]
                 {
                     (10m, 100m), (20m, 400m), (40m, 1600m), (80m, 6400m),
                 })
        {
            await CreateCaveAsync(region, length, depth: depth);
        }

        var query = $"correlation?x=surveyedLength&y=depth&region={region}";
        var onScreen = await ReadJsonAsync(viewer, query);
        var sheet = await SheetAsync(viewer, query);

        Stated(sheet, "Caves recording both").ShouldBe(onScreen.GetProperty("count").GetInt32());
        Stated(sheet, "Slope").ShouldBe(onScreen.GetProperty("slope").GetDouble(), 1e-9);
        Stated(sheet, "Intercept").ShouldBe(onScreen.GetProperty("intercept").GetDouble(), 1e-9);
        Stated(sheet, "Variation accounted for")
            .ShouldBe(onScreen.GetProperty("rSquared").GetDouble(), 1e-9);
        Stated(sheet, "Correlation")
            .ShouldBe(onScreen.GetProperty("correlation").GetDouble(), 1e-9);

        // The axes the fit was taken on, which is the difference between a slope of two and a
        // number with no meaning, and the basis it was computed under.
        Text(sheet).ShouldContain("logarithmic");
        Text(sheet).ShouldContain(onScreen.GetProperty("basis").GetString()!);
    }

    [Fact]
    public async Task A_fit_with_too_few_pairs_states_nothing_rather_than_a_figure_of_its_own()
    {
        // One cave records both measures, which is the ordinary state of a thinly-measured
        // registry rather than an edge case. A line through one point has no slope, and the file
        // has to leave the cell empty: a zero written where there is no figure is a figure.
        var region = Unique("CT");
        await CreateCaveAsync(region, 10m, depth: 100m);

        var query = $"correlation?x=surveyedLength&y=depth&region={region}";
        var onScreen = await ReadJsonAsync(viewer, query);
        onScreen.GetProperty("count").GetInt32().ShouldBe(1);
        foreach (var figure in new[] { "slope", "intercept", "rSquared", "correlation" })
        {
            onScreen.GetProperty(figure).ValueKind.ShouldBe(JsonValueKind.Null, figure);
        }

        var sheet = await SheetAsync(viewer, query);
        Stated(sheet, "Caves recording both").ShouldBe(1);
        foreach (var label in new[]
                 {
                     "Slope", "Intercept", "Variation accounted for", "Correlation",
                 })
        {
            string.IsNullOrEmpty(Cell(sheet, label)).ShouldBeTrue(
                $"the file stated {label} where the screen stated nothing.");
        }
    }

    [Fact]
    public async Task The_saved_breakdown_states_the_same_rows_the_screen_does_for_the_same_caller()
    {
        var region = await SeedAsync("RX", [10m, 20m, 30m]);
        await CreateCaveAsync(region, 40m, locationProtected: true);

        var onScreen = await ReadJsonAsync(viewer, $"regions?region={region}");
        var sheet = await SheetAsync(viewer, $"regions?region={region}");

        // The total first. It is the figure a reader adds the rows up against, and it is the one
        // the caves they may read but may not place are counted in.
        Stated(sheet, "Caves in scope").ShouldBe(onScreen.GetProperty("caveCount").GetInt32());
        onScreen.GetProperty("caveCount").GetInt32().ShouldBe(4);

        var expected = onScreen.GetProperty("regions").EnumerateArray()
            .Select(r => (
                Name: r.GetProperty("region").GetString() ?? "(no region recorded)",
                Count: (double)r.GetProperty("caveCount").GetInt32()))
            .ToList();
        expected.ShouldContain(row => row.Name == region && row.Count == 3);

        // Row for row and in the same order. A file naming a region the screen did not, or
        // counting one cave more under it, is the screen's answer given anyway.
        RegionRows(sheet).ShouldBe(expected);
        Text(sheet).ShouldContain(onScreen.GetProperty("basis").GetString()!);
    }

    [Fact]
    public async Task A_breakdown_narrowed_to_an_area_says_that_its_total_is_narrowed_too()
    {
        var areaId = await CreateKarstAreaAsync();

        // Unnarrowed, the total counts every cave in scope the caller may read — including one
        // whose position is closed to them, which then stands under no region — and the answer
        // says exactly that.
        var whole = await ReadJsonAsync(viewer, "regions");
        whole.GetProperty("basis").GetString().ShouldNotBeNull()
            .ShouldContain("is counted in the registry's totals");

        // Narrowed to an area, the same cave is out of the total as well, because a count inside a
        // named area would say it is there. A reader told otherwise would add the rows up, find
        // them reconciled against the total, and read that as proof nothing was withheld.
        var inArea = await ReadJsonAsync(viewer, $"regions?areaId={areaId}");
        var basis = inArea.GetProperty("basis").GetString().ShouldNotBeNull();
        basis.ShouldContain("absent from these figures altogether");
        basis.ShouldNotBe(whole.GetProperty("basis").GetString());

        // And the file says it in the same words the screen did.
        Text(await SheetAsync(viewer, $"regions?areaId={areaId}")).ShouldContain(basis);
    }

    private static string Exported(string query)
    {
        var split = query.IndexOf('?', StringComparison.Ordinal);
        return string.Concat(query[..split], "/export", query[split..]);
    }

    private string Unique(string prefix) => $"{prefix}-{suffix}";

    /// <summary>A region of this class's own, holding one cave per length given.</summary>
    private async Task<string> SeedAsync(string prefix, IReadOnlyList<decimal> lengths)
    {
        var region = Unique(prefix);
        foreach (var length in lengths)
        {
            await CreateCaveAsync(region, length);
        }

        return region;
    }

    private async Task<Guid> CreateCaveAsync(
        string region,
        decimal surveyedLength,
        decimal? depth = null,
        string visibility = "authenticated",
        bool locationProtected = false)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"Reg {Guid.NewGuid():N}"[..30],
            caveTypeId,
            region,
            surveyedLength,
            depth,
            visibility,
            locationProtected,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateKarstAreaAsync()
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"Reg area {Guid.NewGuid():N}"[..30],
            featureTypeId = karstAreaTypeId,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>An object-scoped deny of Read for one user, which overrides every cascade above it.</summary>
    private static async Task DenyReadAsync(HttpClient client, Guid featureId, Guid subjectId)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = new[]
            {
                new
                {
                    subjectKind = "user",
                    subjectId,
                    effect = "deny",
                    actions = "Read",
                    scopeKind = "object",
                },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync($"/api/v1/stats/registry/{route}");
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> SheetAsync(
        HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/stats/registry/{Exported(query)}");
        var payload = await response.Content.ReadAsByteArrayAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return Rows(payload);
    }

    /// <summary>The sheet as text, a number written the way the invariant culture writes it.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> Rows(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var workbook = new XSSFWorkbook(stream);
        var sheet = workbook.GetSheetAt(0);
        var rows = new List<IReadOnlyList<string>>();
        for (var r = 0; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                rows.Add([]);
                continue;
            }

            var cells = new List<string>();
            for (var c = 0; c < row.LastCellNum; c++)
            {
                var cell = row.GetCell(c);
                cells.Add(cell?.CellType switch
                {
                    CellType.Numeric => cell.NumericCellValue.ToString(
                        "R", CultureInfo.InvariantCulture),
                    CellType.String => cell.StringCellValue,
                    _ => string.Empty,
                });
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>The whole sheet as one block of text, for the sentences rather than the figures.</summary>
    private static string Text(IReadOnlyList<IReadOnlyList<string>> sheet) =>
        string.Join('\n', sheet.SelectMany(r => r));

    /// <summary>
    /// The cell beside a label, as it was written — empty or absent where the answer stated no
    /// figure, which is a different thing from a figure of nought and has to stay different.
    /// </summary>
    private static string? Cell(IReadOnlyList<IReadOnlyList<string>> sheet, string label)
    {
        var row = sheet.Single(r => r.Count > 0 && r[0] == label);
        return row.Count > 1 ? row[1] : null;
    }

    /// <summary>The figure beside a label, which the caller has asserted is there.</summary>
    private static double Stated(IReadOnlyList<IReadOnlyList<string>> sheet, string label)
    {
        var written = Cell(sheet, label).ShouldNotBeNull($"the file stated no {label}.");
        written.ShouldNotBeEmpty($"the file stated no {label}.");
        return double.Parse(written, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The breakdown's rows: everything under the pair of column titles that still reads as a name
    /// beside a number, in the order the file wrote them.
    /// </summary>
    private static IReadOnlyList<(string Name, double Count)> RegionRows(
        IReadOnlyList<IReadOnlyList<string>> sheet)
    {
        var rows = new List<(string, double)>();
        var started = false;
        foreach (var row in sheet)
        {
            if (!started)
            {
                started = row.Count > 1 && row[0] == "Region" && row[1] == "Caves";
                continue;
            }

            if (row.Count < 2
                || !double.TryParse(
                    row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var count))
            {
                break;
            }

            rows.Add((row[0], count));
        }

        return rows;
    }

    private static double Figure(IReadOnlyList<IReadOnlyList<string>> sheet, string label) =>
        double.Parse(
            sheet.Single(r => r.Count > 1 && r[0] == label)[1], CultureInfo.InvariantCulture);

    /// <summary>The histogram rows: three numbers where the first column is itself a number.</summary>
    private static IReadOnlyList<(double Lower, double Upper, int Count)> BinRows(
        IReadOnlyList<IReadOnlyList<string>> sheet) =>
        [.. sheet
            .Where(r => r.Count >= 3
                && double.TryParse(r[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                && double.TryParse(r[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                && double.TryParse(r[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(r => (
                double.Parse(r[0], CultureInfo.InvariantCulture),
                double.Parse(r[1], CultureInfo.InvariantCulture),
                (int)double.Parse(r[2], CultureInfo.InvariantCulture)))];
}
