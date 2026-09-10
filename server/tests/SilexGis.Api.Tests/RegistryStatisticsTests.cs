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
public sealed class RegistryStatisticsTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
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

    [Fact]
    public async Task A_grouping_says_how_many_caves_it_left_out_and_which_measure_left_them_out()
    {
        // Twelve caves record a length; only nine of them also record a depth. That is the shape a
        // real register has — most caves have no survey — and it is the whole reason this answer
        // carries a population account: grouping on both measures describes nine caves, and every
        // figure it produces is internally consistent and about a different set than "the caves in
        // this region".
        var region = Unique("CLPOP");
        for (var i = 0; i < 12; i++)
        {
            await CreateCaveAsync(region, 100m + (i * 50m), depth: i < 9 ? 10m + (i * 3m) : null);
        }

        var onLengthAlone = await ReadJsonAsync(
            viewer, $"clustering?measures=surveyedLength&clusters=2&region={region}");
        var onBoth = await ReadJsonAsync(
            viewer, $"clustering?measures=surveyedLength,depth&clusters=2&region={region}");

        // The same scope, two column choices, two different populations — and the caller can see
        // both before reading either grouping, which is the point of letting them choose.
        onLengthAlone.GetProperty("population").GetProperty("considered").GetInt32().ShouldBe(12);
        onLengthAlone.GetProperty("population").GetProperty("eligible").GetInt32().ShouldBe(12);
        onLengthAlone.GetProperty("population").GetProperty("excluded").GetInt32().ShouldBe(0);

        var population = onBoth.GetProperty("population");
        population.GetProperty("considered").GetInt32().ShouldBe(12);
        population.GetProperty("eligible").GetInt32().ShouldBe(9);

        // Published rather than left to be subtracted: a figure a reader has to compute is one a
        // reader skips.
        population.GetProperty("excluded").GetInt32().ShouldBe(3);

        var measures = population.GetProperty("measures").EnumerateArray().ToList();
        measures.Count.ShouldBe(2);

        var length = measures.Single(m => m.GetProperty("measure").GetString() == "surveyedLength");
        length.GetProperty("recorded").GetInt32().ShouldBe(12);
        length.GetProperty("missing").GetInt32().ShouldBe(0);
        length.GetProperty("soleReason").GetInt32().ShouldBe(0);

        // The figure that answers "what did asking for this column cost me". Three caves are
        // outside this grouping and would have been inside it had depth not been named.
        var depth = measures.Single(m => m.GetProperty("measure").GetString() == "depth");
        depth.GetProperty("recorded").GetInt32().ShouldBe(9);
        depth.GetProperty("missing").GetInt32().ShouldBe(3);
        depth.GetProperty("soleReason").GetInt32().ShouldBe(3);

        // And the measures the distances were actually taken over are named in the answer, in the
        // order they were asked for, so the clusters cannot be read as being about anything else.
        onBoth.GetProperty("measures").EnumerateArray().Select(m => m.GetString())
            .ShouldBe(["surveyedLength", "depth"]);

        // What each column was divided by, which is how a reader can see that the standardisation
        // happened at all — without it the metre-scaled column would decide every group by itself.
        var scaling = onBoth.GetProperty("scaling").EnumerateArray().ToList();
        scaling.Count.ShouldBe(2);
        foreach (var column in scaling)
        {
            column.GetProperty("standardDeviation").GetDouble().ShouldBeGreaterThan(0d);
        }
    }

    [Fact]
    public async Task A_grouping_publishes_the_spread_that_can_contradict_it()
    {
        // A grouping always returns the number of groups it was asked for, on any data whatever.
        // These twelve caves lie on one even ramp with no groups in them, so the answer has to
        // carry the figure that says so — a within-group spread of the same order as the distance
        // between groups is the reading "there is no structure here", and it is the only thing in
        // the answer able to contradict the fact that three groups came back.
        var region = await SeedAsync("CLSEP", [.. Enumerable.Range(0, 12).Select(i => 100m + (i * 100m))]);

        var body = await ReadJsonAsync(
            viewer, $"clustering?measures=surveyedLength&clusters=3&region={region}");

        body.GetProperty("clusters").GetArrayLength().ShouldBe(3);

        var separation = body.GetProperty("separation");
        separation.GetProperty("meanWithinDistance").GetDouble().ShouldBeGreaterThan(0d);
        separation.GetProperty("meanBetweenDistance").GetDouble().ShouldBeGreaterThan(0d);
        separation.GetProperty("ratio").GetDouble().ShouldBeGreaterThan(0d);
    }

    [Fact]
    public async Task A_group_too_small_to_describe_a_population_publishes_its_count_and_nothing_else()
    {
        // Ten caves of much the same length and two lone giants. Asked for three groups, the two
        // giants each end up alone — and a mean over one cave is that cave's reading with an
        // arithmetic step in front of it. The count stands, because a thin group is itself a
        // finding and removing it would stop the counts adding up to the eligible total; the
        // measurements do not.
        var lengths = new List<decimal>();
        for (var i = 0; i < 10; i++)
        {
            lengths.Add(1000m + (i * 10m));
        }

        lengths.Add(50_000m);
        lengths.Add(100_000m);
        var region = await SeedAsync("CLTHIN", lengths);

        var body = await ReadJsonAsync(
            viewer, $"clustering?measures=surveyedLength&clusters=3&region={region}");

        var floor = body.GetProperty("minimumPublishableClusterSize").GetInt32();
        floor.ShouldBeGreaterThan(1);

        var clusters = body.GetProperty("clusters").EnumerateArray().ToList();
        clusters.Count.ShouldBe(3);
        clusters.Sum(c => c.GetProperty("count").GetInt32()).ShouldBe(12);

        // At least one group is under the floor here by construction; if the arrangement ever
        // stopped producing one, this test would stop testing anything, so it is asserted.
        clusters.Any(c => c.GetProperty("count").GetInt32() < floor).ShouldBeTrue();

        foreach (var cluster in clusters)
        {
            var count = cluster.GetProperty("count").GetInt32();
            var published = cluster.GetProperty("centre").ValueKind is not JsonValueKind.Null;
            published.ShouldBe(
                count >= floor,
                $"a group of {count} caves published its centre against a floor of {floor}.");

            // The scaled middle and the group's own width are withheld under the same rule, or
            // a reader could recover the first from either of them.
            (cluster.GetProperty("scaledCentre").ValueKind is not JsonValueKind.Null)
                .ShouldBe(count >= floor);
            (cluster.GetProperty("meanDistanceToCentre").ValueKind is not JsonValueKind.Null)
                .ShouldBe(count >= floor);
        }
    }

    [Fact]
    public async Task A_cave_the_caller_may_not_read_is_not_in_the_grouping_at_all()
    {
        // Twelve caves; the viewer is denied Read on three of them. The grouping the viewer gets
        // must have considered nine, not twelve with three quietly dropped afterwards: a population
        // account computed over rows the caller may not read would state the registry's size to
        // somebody who is not entitled to know it. Nine is also above the floor a grouping is
        // attempted over, so the viewer really does get groups and assignments to check — over
        // seven the answer would be empty and the assignment check below would pass on nothing.
        var region = Unique("CLACL");
        var denied = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var id = await CreateCaveAsync(region, 100m + (i * 25m), depth: 5m + i);
            if (i < 3)
            {
                denied.Add(id);
            }
        }

        foreach (var id in denied)
        {
            await DenyReadAsync(owner, id, viewerId);
        }

        var seen = await ReadJsonAsync(
            viewer, $"clustering?measures=surveyedLength,depth&clusters=2&region={region}");
        var whole = await ReadJsonAsync(
            owner, $"clustering?measures=surveyedLength,depth&clusters=2&region={region}");

        // Paired against the same fixture read by somebody entitled to it, so seven is a
        // withholding rather than an empty seed.
        whole.GetProperty("population").GetProperty("considered").GetInt32().ShouldBe(12);
        seen.GetProperty("population").GetProperty("considered").GetInt32().ShouldBe(9);
        seen.GetProperty("population").GetProperty("eligible").GetInt32().ShouldBe(9);

        // And no assignment names a cave the viewer may not read — asserted against a list that is
        // not empty, or the intersection below would be vacuous.
        var named = seen.GetProperty("assignments").EnumerateArray()
            .Select(a => a.GetProperty("caveId").GetGuid()).ToList();
        named.Count.ShouldBe(9);
        named.Intersect(denied).ShouldBeEmpty();

        // Both answers say in words what they were computed over, because two callers get
        // different groupings of one registry and both are right.
        seen.GetProperty("basis").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_population_too_small_to_group_is_answered_with_the_account_of_why()
    {
        // Four caves, which is under the floor a grouping is attempted over. The answer is not an
        // error and not an empty object: it is the population account with no groups in it, so a
        // reader can see how far short the registry fell rather than being told the request was
        // bad.
        var region = await SeedAsync("CLFEW", [100m, 200m, 300m, 400m]);

        var body = await ReadJsonAsync(
            viewer, $"clustering?measures=surveyedLength&clusters=3&region={region}");

        body.GetProperty("clusters").GetArrayLength().ShouldBe(0);
        body.GetProperty("assignments").GetArrayLength().ShouldBe(0);
        body.GetProperty("separation").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("population").GetProperty("considered").GetInt32().ShouldBe(4);
        body.GetProperty("population").GetProperty("eligible").GetInt32().ShouldBe(4);
        body.GetProperty("minimumEligibleCount").GetInt32().ShouldBeGreaterThan(4);
    }

    [Fact]
    public async Task A_grouping_is_not_answered_to_a_caller_who_is_not_signed_in()
    {
        var region = await SeedAsync("CLANON", [100m, 200m, 300m]);

        var response = await anonymous.GetAsync(
            $"/api/v1/stats/registry/clustering?measures=surveyedLength&region={region}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("measures=altitude")]
    [InlineData("measures=2")]
    [InlineData("measures=surveyedLength,surveyedLength")]
    [InlineData("measures=")]
    [InlineData("measures=surveyedLength&clusters=1")]
    [InlineData("measures=surveyedLength&clusters=99")]
    public async Task A_grouping_asked_for_in_terms_the_registry_does_not_offer_is_refused(string query)
    {
        // Altitude is not a measure here at all — a height above sea level is a coordinate — and a
        // measure named by its underlying number is not a name. A measure named twice would be
        // counted twice in every distance, which is a weighting nobody asked for and which the
        // answer could not show. One group is the population and ninety-nine of them is a list of
        // caves.
        var response = await viewer.GetAsync($"/api/v1/stats/registry/clustering?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_grouping_scoped_to_an_area_the_caller_may_not_read_is_answered_as_absent()
    {
        var areaId = await CreateKarstAreaAsync();
        await DenyReadAsync(owner, areaId, viewerId);

        var response = await viewer.GetAsync(
            $"/api/v1/stats/registry/clustering?measures=surveyedLength&areaId={areaId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("feature.not_found");
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
