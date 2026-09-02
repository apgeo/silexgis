// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The path from an uploaded file to caves, entrances and features: the dry run, the resumable
/// review, the confirmation and its undo — plus the two things this feature could get wrong
/// quietly, which are creating without review where the installation forbids it, and answering
/// "there is already something within twelve metres" about a cave the caller may not locate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StagedImportTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private HttpClient admin = null!;
    private Guid editorId;
    private string tag = null!;

    public StagedImportTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async Task InitializeAsync()
    {
        tag = Guid.NewGuid().ToString("N")[..8];
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"si-editor-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"si-view-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"si-admin-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"si-editor-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"si-view-{tag}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"si-admin-{tag}@t.local");

        await SeedRulesAsync();
    }

    // ---------- the whole path ----------

    [Fact]
    public async Task A_gpx_becomes_a_cave_a_spring_and_a_sinkhole_after_review()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="45.61" lon="25.51"><name>P. Ursilor {tag}</name><ele>812</ele></wpt>
              <wpt lat="45.62" lon="25.52"><name>Izbuc Mare {tag}</name></wpt>
              <wpt lat="45.63" lon="25.53"><name>Dolina {tag}</name></wpt>
              <wpt lat="45.64" lon="25.54"><name>Parcare {tag}</name></wpt>
              <trk><name>Approach {tag}</name><trkseg>
                <trkpt lat="45.60" lon="25.50"/><trkpt lat="45.605" lon="25.505"/>
              </trkseg></trk>
            </gpx>
            """);

        var preview = await PreviewAsync(editor, geofileId);

        // The dry run, before anything exists: four points, three claimed, one track.
        preview.GetProperty("candidateCount").GetInt32().ShouldBe(4);
        preview.GetProperty("matchedCount").GetInt32().ShouldBe(3);
        preview.GetProperty("unmatchedCount").GetInt32().ShouldBe(1);
        preview.GetProperty("trackCount").GetInt32().ShouldBe(1);

        var items = preview.GetProperty("items").EnumerateArray().ToList();
        var cave = items.Single(i => i.GetProperty("sourceName").GetString()!.StartsWith("P. Ursilor"));
        cave.GetProperty("proposedKind").GetString().ShouldBe("cave");
        cave.GetProperty("proposedCaveTypeCode").GetString().ShouldBe("cave");

        // The identifying term is out of the name: "P. Ursilor" is the cave "Ursilor".
        cave.GetProperty("proposedName").GetString().ShouldBe($"Ursilor {tag}");

        var spring = items.Single(i => i.GetProperty("sourceName").GetString()!.StartsWith("Izbuc"));
        spring.GetProperty("proposedKind").GetString().ShouldBe("surfaceFeature");
        spring.GetProperty("proposedFeatureTypeCode").GetString().ShouldBe("water_flow");

        var parking = items.Single(i => i.GetProperty("sourceName").GetString()!.StartsWith("Parcare"));
        parking.GetProperty("proposedKind").ValueKind.ShouldBe(JsonValueKind.Null);

        // Nothing has been created yet — the registry is untouched until confirmation.
        (await SearchFeatureNamesAsync(editor)).ShouldBeEmpty();

        var selection = items
            .Where(i => i.GetProperty("proposedKind").ValueKind != JsonValueKind.Null)
            .Select(i => i.GetProperty("sourceId").GetInt64())
            .ToList();
        var commit = await CommitAsync(editor, geofileId, selection);

        commit.GetProperty("failures").EnumerateArray().ShouldBeEmpty();
        var batch = commit.GetProperty("batch");
        batch.GetProperty("createdCount").GetInt32().ShouldBe(3);
        batch.GetProperty("mode").GetString().ShouldBe("reviewed");

        var names = await SearchFeatureNamesAsync(editor);
        names.ShouldContain($"Ursilor {tag}");
        names.ShouldContain($"Izbuc Mare {tag}");
        names.ShouldContain($"Dolina {tag}");
        names.ShouldNotContain($"Parcare {tag}");

        // A cave imported from a waypoint is a position first: it gets that position as its
        // main entrance, so it draws on the map rather than sitting there with no geometry.
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveFeature = await db.Features.Include(f => f.Cave)
            .FirstAsync(f => f.Name == $"Ursilor {tag}" && f.Kind == FeatureKind.Cave);
        caveFeature.Geom.ShouldNotBeNull();
        caveFeature.Cave!.EntranceCount.ShouldBe(1);
        caveFeature.Cave.Altitude.ShouldBe(812);
        (await db.CaveEntrances.CountAsync(e => e.CaveFeatureId == caveFeature.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_confirmation_reverts_as_one_unit()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Undo {tag}", 45.71, 25.61));
        var preview = await PreviewAsync(editor, geofileId);
        var sourceIds = SourceIds(preview);

        var commit = await CommitAsync(editor, geofileId, sourceIds);
        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();
        (await SearchFeatureNamesAsync(editor)).ShouldContain($"Undo {tag}");

        var reverted = await editor.PostAsync($"/api/v1/import-batches/{batchId}/revert", null);
        reverted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The cave and the entrance it was given both go: the undo stamps the whole subtree.
        (await SearchFeatureNamesAsync(editor)).ShouldNotContain($"Undo {tag}");
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var rows = await db.Features.IgnoreQueryFilters()
            .Where(f => f.Name == $"Undo {tag}")
            .ToListAsync();
        rows.ShouldAllBe(f => f.DeletedAt != null);

        // Undoing twice is refused rather than silently repeated.
        (await editor.PostAsync($"/api/v1/import-batches/{batchId}/revert", null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Provenance_says_which_file_which_rule_who_and_when()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Provenance {tag}", 45.81, 25.71));
        var commit = await CommitAsync(editor, geofileId, SourceIds(await PreviewAsync(editor, geofileId)));
        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();

        var detail = await GetJsonAsync(editor, $"/api/v1/import-batches/{batchId}");
        var item = detail.GetProperty("items").EnumerateArray().Single();
        var featureId = item.GetProperty("featureId").GetGuid();

        var provenance = await GetJsonAsync(editor, $"/api/v1/features/{featureId}/import-provenance");
        provenance.GetProperty("batch").GetProperty("id").GetGuid().ShouldBe(batchId);
        provenance.GetProperty("item").GetProperty("ruleId").GetString().ShouldBe("cave-abbrev");
        provenance.GetProperty("batch").GetProperty("confirmedByUserId").GetGuid().ShouldBe(editorId);

        // The source row's own attributes are kept verbatim, which is what makes a bad
        // attribute mapping recoverable without the original file.
        provenance.GetProperty("sourceProperties").GetProperty("name").GetString()
            .ShouldBe($"P. Provenance {tag}");
    }

    // ---------- the resumable review ----------

    [Fact]
    public async Task A_review_survives_the_tab_being_closed()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Session {tag}", 45.91, 25.81));
        var sourceId = SourceIds(await PreviewAsync(editor, geofileId)).Single();

        var save = await editor.PutAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/session", new
        {
            options = DefaultOptions(),
            decisions = new Dictionary<string, object>
            {
                [sourceId.ToString()] = new { action = "skip" },
            },
        });
        save.StatusCode.ShouldBe(HttpStatusCode.OK);

        var resumed = await GetJsonAsync(editor, $"/api/v1/geofiles/{geofileId}/import/session");
        resumed.GetProperty("decisions").GetProperty(sourceId.ToString())
            .GetProperty("action").GetString().ShouldBe("skip");

        // The preview carries the saved decision back with the row it belongs to, so the table
        // comes back the way it was left rather than as a fresh proposal.
        var preview = await PreviewAsync(editor, geofileId);
        preview.GetProperty("items").EnumerateArray().Single()
            .GetProperty("decision").GetProperty("action").GetString().ShouldBe("skip");

        // Two people reviewing the same upload keep their own decisions rather than
        // overwriting each other, and neither has committed anything.
        await ShareGeofileAsync(geofileId);
        var theirs = await GetJsonAsync(viewer, $"/api/v1/geofiles/{geofileId}/import/session");
        theirs.GetProperty("decisions").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirming_clears_the_review_it_spent()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Spent {tag}", 46.01, 25.91));
        var sourceIds = SourceIds(await PreviewAsync(editor, geofileId));

        await editor.PutAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/session", new
        {
            options = DefaultOptions(),
            decisions = new Dictionary<string, object>(),
        });
        await CommitAsync(editor, geofileId, sourceIds);

        // Leaving the review would offer to create the same objects a second time.
        var session = await GetJsonAsync(editor, $"/api/v1/geofiles/{geofileId}/import/session");
        session.GetProperty("updatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---------- second entrance on an existing cave ----------

    [Fact]
    public async Task A_candidate_becomes_a_second_entrance_of_a_cave_already_in_the_registry()
    {
        var caveId = await CreateCaveWithEntranceAsync($"Existing Cave {tag}", 46.11, 26.01);

        var geofileId = await UploadGpxAsync(SimpleGpx($"Intrarea 2 {tag}", 46.1105, 26.0105));
        var preview = await PreviewAsync(editor, geofileId);
        var candidate = preview.GetProperty("items").EnumerateArray().Single();
        candidate.GetProperty("proposedKind").GetString().ShouldBe("caveEntrance");

        // Duplicate detection found the cave's own entrance and named the cave with it, which
        // is what makes "add it to that cave" a single click.
        var duplicate = candidate.GetProperty("duplicate");
        duplicate.GetProperty("caveFeatureId").GetGuid().ShouldBe(caveId);
        duplicate.GetProperty("distanceMeters").GetDouble().ShouldBeLessThan(100);

        var sourceId = candidate.GetProperty("sourceId").GetInt64();
        await CommitAsync(editor, geofileId, [sourceId], new Dictionary<string, object>
        {
            [sourceId.ToString()] = new { action = "create", kind = "caveEntrance", attachToFeatureId = caveId },
        });

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.CaveEntrances.CountAsync(e => e.CaveFeatureId == caveId)).ShouldBe(2);
        var cave = await db.Caves.FirstAsync(c => c.Id == caveId);
        cave.EntranceCount.ShouldBe(2);
    }

    // ---------- the location oracle ----------

    [Fact]
    public async Task Duplicate_detection_never_measures_to_something_the_caller_may_not_locate()
    {
        // The editor's own protected cave, at a position the viewer may read the existence of
        // but not the coordinates of.
        const double lon = 26.21;
        const double lat = 46.21;
        await CreateCaveWithEntranceAsync($"Protected Cave {tag}", lat, lon, locationProtected: true);

        // A file with a waypoint ten metres away, shared so both accounts review the same rows.
        var geofileId = await UploadGpxAsync(SimpleGpx($"Unknown {tag}", lat + 0.0001, lon));
        await ShareGeofileAsync(geofileId);

        var theirs = await PreviewAsync(viewer, geofileId);
        var candidate = theirs.GetProperty("items").EnumerateArray().Single();

        // Answering "something is 11 m away" would hand the protected entrance's position to
        // anybody who can read a waypoint near it — a search that converges in a few rounds.
        // The distance is not computed against what the caller cannot locate.
        candidate.GetProperty("duplicate").ValueKind.ShouldBe(JsonValueKind.Null);

        // The same file reviewed by somebody who may see that cave exactly does get the answer,
        // so this is a protection rule rather than duplicate detection quietly not working.
        var owners = await PreviewAsync(editor, geofileId);
        owners.GetProperty("items").EnumerateArray().Single()
            .GetProperty("duplicate").ValueKind.ShouldBe(JsonValueKind.Object);
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Creating_without_review_is_refused_until_the_installation_allows_it()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Auto {tag}", 46.31, 26.31));

        var session = await GetJsonAsync(editor, $"/api/v1/geofiles/{geofileId}/import/session");
        session.GetProperty("allowCreateWithoutReview").GetBoolean().ShouldBeFalse();

        var refused = await editor.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection = Array.Empty<long>(),
            decisions = new Dictionary<string, object>(),
            withoutReview = true,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SearchFeatureNamesAsync(editor)).ShouldNotContain($"Auto {tag}");

        // Switched on by an administrator, the same request creates what the rules claimed.
        var saved = await admin.PutAsJsonAsync("/api/v1/admin/settings/import", new
        {
            allowCreateWithoutReview = true,
            duplicateRadiusMeters = 50,
            duplicateNameSimilarity = 0.8,
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        try
        {
            var accepted = await Eventually(() => editor.PostAsJsonAsync(
                $"/api/v1/geofiles/{geofileId}/import/commit",
                new
                {
                    options = DefaultOptions(),
                    selection = Array.Empty<long>(),
                    decisions = new Dictionary<string, object>(),
                    withoutReview = true,
                }));
            var result = await RunCommitAsync(editor, accepted);
            result.GetProperty("batch").GetProperty("mode").GetString().ShouldBe("autoCreated");
            (await SearchFeatureNamesAsync(editor)).ShouldContain($"Auto {tag}");
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/v1/admin/settings/import", new
            {
                allowCreateWithoutReview = false,
                duplicateRadiusMeters = 50,
                duplicateNameSimilarity = 0.8,
            });
        }
    }

    [Fact]
    public async Task A_file_the_caller_cannot_read_has_no_review_no_preview_and_no_commit()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Private {tag}", 46.41, 26.41));

        // Private by default: the outsider is told the file does not exist, not that it is
        // forbidden — a body reference must never confirm a row the caller cannot see.
        (await viewer.GetAsync($"/api/v1/geofiles/{geofileId}/import/session"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/preview", new { options = DefaultOptions() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection = Array.Empty<long>(),
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reader_who_may_not_create_features_is_refused_the_confirmation()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Reader {tag}", 46.51, 26.51));
        await ShareGeofileAsync(geofileId);

        var sourceIds = SourceIds(await PreviewAsync(viewer, geofileId));
        sourceIds.ShouldNotBeEmpty();

        // The dry run is a reading and is allowed; creating is not.
        var refused = await viewer.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection = sourceIds,
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_batch_is_its_confirmers_own_and_an_administrators()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Batch {tag}", 46.61, 26.61));
        var commit = await CommitAsync(editor, geofileId, SourceIds(await PreviewAsync(editor, geofileId)));
        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();

        (await editor.GetAsync($"/api/v1/import-batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/v1/import-batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/import-batches/{batchId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await viewer.PostAsync($"/api/v1/import-batches/{batchId}/revert", null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- the other formats ----------

    [Fact]
    public async Task A_spreadsheet_of_positions_goes_through_the_same_review()
    {
        // Three people's habits in one file: a decimal comma, degrees-minutes-seconds, and a
        // semicolon separator because that is what a Romanian Excel writes.
        var csv = string.Join("\n",
            "name;latitude;longitude;ele",
            $"P. Csv A {tag};45,7100;25,6100;640",
            $"Izbuc Csv B {tag};45°42'36\"N;25°36'36\"E;610");

        var geofileId = await UploadAsync("positions.csv", Encoding.UTF8.GetBytes(csv), editor);
        var preview = await PreviewAsync(editor, geofileId);

        preview.GetProperty("candidateCount").GetInt32().ShouldBe(2);
        var names = preview.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("proposedName").GetString())
            .ToList();
        names.ShouldContain($"Csv A {tag}");
        names.ShouldContain($"Izbuc Csv B {tag}");

        // The columns are offered so a wrong guess can be corrected without a fresh upload.
        var columns = await GetJsonAsync(editor, $"/api/v1/geofiles/{geofileId}/columns");
        columns.GetProperty("columns").EnumerateArray().Select(c => c.GetString())
            .ShouldBe(["name", "latitude", "longitude", "ele"]);
    }

    [Fact]
    public async Task A_seconds_mark_in_a_coordinate_is_not_read_as_an_opening_quote()
    {
        // The defect this pins: 45°42'36"N carries a quote character in an unquoted field.
        // Treating it as the start of a quoted string swallowed the separator, the rest of the
        // row and the row after it — so a file of DMS coordinates read as one unusable record.
        // A quote only opens a field when it is that field's first character.
        var csv = string.Join("\n",
            "name,lat,lon,note",
            $"Dms A {tag},45°42'36\"N,25°36'36\"E,plain",
            $"\"Quoted, comma\" {tag},45.72,25.62,\"a note, with a comma\"");

        var geofileId = await UploadAsync("dms.csv", Encoding.UTF8.GetBytes(csv), editor);
        var preview = await PreviewAsync(editor, geofileId);

        preview.GetProperty("candidateCount").GetInt32().ShouldBe(2);
        var names = preview.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("sourceName").GetString())
            .ToList();
        names.ShouldContain($"Dms A {tag}");

        // …and a genuinely quoted field still keeps the separator it contains.
        names.ShouldContain($"Quoted, comma {tag}");
        preview.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("sourceName").GetString() == $"Quoted, comma {tag}")
            .GetProperty("sourceDescription").GetString().ShouldBe("a note, with a comma");
    }

    [Fact]
    public async Task A_kmz_is_recognised_by_what_is_inside_it_not_by_its_name()
    {
        var kml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
              <Placemark><name>P. Kmz {tag}</name>
                <Point><coordinates>25.81,45.81,700</coordinates></Point>
              </Placemark>
            </Document></kml>
            """;

        // Named .zip on purpose: mail servers and chat clients strip the unfamiliar extension,
        // and reading it as a shapefile would fail with a message about a missing .shp.
        var geofileId = await UploadAsync("earth.zip", Kmz(kml), editor);

        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            (await db.Geofiles.FirstAsync(g => g.Id == geofileId)).Format.ShouldBe(GeofileFormat.Kmz);
        }

        var preview = await PreviewAsync(editor, geofileId);
        preview.GetProperty("items").EnumerateArray().Single()
            .GetProperty("proposedName").GetString().ShouldBe($"Kmz {tag}");
    }

    // ---------- whole-file choices ----------

    [Fact]
    public async Task The_import_keeps_or_drops_the_altitude_as_it_is_told()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Noisy {tag}", 46.71, 26.71, elevation: 1234));
        var sourceIds = SourceIds(await PreviewAsync(editor, geofileId));

        var options = DefaultOptions();
        options["elevation"] = "discard";
        options["namePrefix"] = "Bihor";
        await CommitAsync(editor, geofileId, sourceIds, options: options);

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var cave = await db.Features.Include(f => f.Cave)
            .FirstAsync(f => f.Name == $"Bihor Noisy {tag}" && f.Kind == FeatureKind.Cave);

        // A handheld's vertical error is the worst of the three numbers it reports, so
        // discarding it is a real answer rather than a way of losing data.
        cave.Cave!.Altitude.ShouldBeNull();
        double.IsNaN(cave.Geom!.Coordinate.Z).ShouldBeTrue();
    }

    [Fact]
    public async Task One_candidate_can_answer_about_its_own_altitude()
    {
        // A survey station and a walk-past waypoint sit in the same GPX, so a file where the
        // altitude is trustworthy for some points and not others is the normal case. The file
        // is the only source either way — nothing is derived from an elevation model.
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="47.01" lon="27.01"><name>P. Surveyed {tag}</name><ele>640</ele></wpt>
              <wpt lat="47.02" lon="27.02"><name>P. Walkpast {tag}</name><ele>655</ele></wpt>
            </gpx>
            """);

        var preview = await PreviewAsync(editor, geofileId);
        var items = preview.GetProperty("items").EnumerateArray().ToList();
        var surveyed = items.Single(i => i.GetProperty("sourceName").GetString()!.Contains("Surveyed"))
            .GetProperty("sourceId").GetInt64();

        var options = DefaultOptions();
        options["elevation"] = "perCandidate";
        await CommitAsync(editor, geofileId, SourceIds(preview), new Dictionary<string, object>
        {
            [surveyed.ToString()] = new { action = "create", keepElevation = true },
        }, options);

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // Under the per-candidate policy nothing is kept until a row says so.
        (await db.Features.Include(f => f.Cave)
            .FirstAsync(f => f.Name == $"Surveyed {tag}" && f.Kind == FeatureKind.Cave))
            .Cave!.Altitude.ShouldBe(640);
        (await db.Features.Include(f => f.Cave)
            .FirstAsync(f => f.Name == $"Walkpast {tag}" && f.Kind == FeatureKind.Cave))
            .Cave!.Altitude.ShouldBeNull();
    }

    [Fact]
    public async Task A_track_is_left_alone_unless_the_import_asks_for_it()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <trk><name>P. Sweep {tag}</name><trkseg>
                <trkpt lat="46.80" lon="26.80"/><trkpt lat="46.805" lon="26.805"/>
              </trkseg></trk>
            </gpx>
            """);

        // A track named after a cave is the walk to that cave, not the cave: no term claims it.
        var ignored = await PreviewAsync(editor, geofileId);
        ignored.GetProperty("trackCount").GetInt32().ShouldBe(1);
        ignored.GetProperty("items").EnumerateArray().Single()
            .GetProperty("proposedKind").ValueKind.ShouldBe(JsonValueKind.Null);

        var options = DefaultOptions();
        options["tracks"] = "importAsLine";
        options["trackFeatureTypeCode"] = "fracture_line";
        var preview = await PreviewAsync(editor, geofileId, options);
        var candidate = preview.GetProperty("items").EnumerateArray().Single();
        candidate.GetProperty("proposedKind").GetString().ShouldBe("surfaceFeature");
        candidate.GetProperty("proposedFeatureTypeCode").GetString().ShouldBe("fracture_line");

        // A track's own shape is drawn by the file's map layer; the review table does not carry
        // tens of thousands of vertices to preview a row.
        candidate.GetProperty("geom").ValueKind.ShouldBe(JsonValueKind.Null);

        await CommitAsync(editor, geofileId, SourceIds(preview), options: options);

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var line = await db.Features.FirstAsync(f => f.Name == $"P. Sweep {tag}");
        line.Kind.ShouldBe(FeatureKind.Generic);
        line.Geom!.GeometryType.ShouldBe("MultiLineString");
    }

    [Fact]
    public async Task A_row_that_cannot_be_created_is_reported_and_the_rest_still_land()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="46.91" lon="26.91"><name>P. Fine {tag}</name></wpt>
              <wpt lat="46.92" lon="26.92"><name>P. Broken {tag}</name></wpt>
            </gpx>
            """);
        var preview = await PreviewAsync(editor, geofileId);
        var items = preview.GetProperty("items").EnumerateArray().ToList();
        var broken = items.Single(i => i.GetProperty("sourceName").GetString()!.Contains("Broken"))
            .GetProperty("sourceId").GetInt64();

        var commit = await CommitAsync(editor, geofileId, SourceIds(preview), new Dictionary<string, object>
        {
            // A feature type this installation does not have: one odd row must not cost the
            // other three hundred, and it must not be swallowed either.
            [broken.ToString()] = new { action = "create", kind = "surfaceFeature", featureTypeCode = "no_such_kind" },
        });

        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        var failure = commit.GetProperty("failures").EnumerateArray().Single();
        failure.GetProperty("sourceId").GetInt64().ShouldBe(broken);
        failure.GetProperty("code").GetString().ShouldBe("import.type_unknown");

        (await SearchFeatureNamesAsync(editor)).ShouldContain($"Fine {tag}");
    }

    // ---------- helpers ----------

    /// <summary>
    /// A rule set of this test's own, so the assertions do not move when the shipped defaults
    /// are tuned. It mirrors the shipped shape rather than replacing it: the shipped set has
    /// its own tests in the domain suite.
    /// </summary>
    private async Task SeedRulesAsync()
    {
        var response = await editor.PostAsJsonAsync("/api/v1/term-rule-sets", new
        {
            name = $"Test rules {tag}",
            description = (string?)null,
            rules = new object[]
            {
                new
                {
                    id = "cave-abbrev",
                    name = "Cave abbreviation",
                    matchMode = "prefix",
                    matchName = true,
                    terms = new Dictionary<string, string[]> { ["*"] = ["p."] },
                    target = "cave",
                    caveTypeCode = "cave",
                    strip = "leading",
                },
                new
                {
                    id = "entrance",
                    name = "Entrance",
                    matchMode = "wholeWord",
                    matchName = true,
                    terms = new Dictionary<string, string[]> { ["ro"] = ["intrarea", "intrare"] },
                    target = "caveEntrance",
                    entranceTypeCode = "natural",
                    strip = "leading",
                },
                new
                {
                    id = "spring",
                    name = "Spring",
                    matchMode = "wholeWord",
                    matchName = true,
                    terms = new Dictionary<string, string[]> { ["ro"] = ["izbuc"] },
                    target = "surfaceFeature",
                    featureTypeCode = "water_flow",
                },
                new
                {
                    id = "sinkhole",
                    name = "Sinkhole",
                    matchMode = "wholeWord",
                    matchName = true,
                    terms = new Dictionary<string, string[]> { ["ro"] = ["dolina"] },
                    target = "surfaceFeature",
                    featureTypeCode = "sinkhole",
                },
            },
            copyFromId = (Guid?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        ruleSetId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("set").GetProperty("id").GetGuid();
    }

    private Guid ruleSetId;

    private Dictionary<string, object?> DefaultOptions() => new()
    {
        ["termRuleSetId"] = ruleSetId,
        ["languages"] = Array.Empty<string>(),
        ["mapping"] = new { },
        ["elevation"] = "keep",
        ["tracks"] = "ignore",
        ["visibility"] = "private",
        ["duplicateRadiusMeters"] = 100,
        ["tagIds"] = Array.Empty<long>(),
    };

    private static string SimpleGpx(string name, double lat, double lon, int? elevation = null) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
          <wpt lat="{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
               lon="{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}">
            <name>{name}</name>{(elevation is null ? string.Empty : $"<ele>{elevation}</ele>")}
          </wpt>
        </gpx>
        """;

    private static byte[] Kmz(string kml)
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("doc.kml").Open());
            writer.Write(kml);
        }

        return buffer.ToArray();
    }

    // Uploads are always the editor's: a Viewer may not create a geofile at all, so a test
    // that needs a second reviewer shares the upload rather than making one.
    private Task<Guid> UploadGpxAsync(string gpx) =>
        UploadAsync("import.gpx", Encoding.UTF8.GetBytes(gpx), editor);

    private async Task<Guid> UploadAsync(string fileName, byte[] bytes, HttpClient client)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };

        var response = await client.PostAsync("/api/v1/geofiles", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await GetJsonAsync(client, $"/api/v1/geofiles/{id}/status");
            var value = status.GetProperty("importStatus").GetString();
            if (value is "imported" or "failed")
            {
                value.ShouldBe("imported", status.ToString());
                return id;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "import job did not finish in time");
            await Task.Delay(200);
        }
    }

    private async Task<JsonElement> PreviewAsync(
        HttpClient client, Guid geofileId, Dictionary<string, object?>? options = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/geofiles/{geofileId}/import/preview",
            new { options = options ?? DefaultOptions(), pageSize = 200 });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    [Fact]
    public async Task A_confirmation_is_queued_and_creates_nothing_until_the_job_runs()
    {
        // A name the shipped rules classify, so there is something to confirm — which term it is
        // does not matter here, only that the row has a proposed kind.
        var geofileId = await UploadGpxAsync(SimpleGpx($"Izbuc {tag}", 45.53, 25.44));
        // Only what a rule claimed. An unmatched row has nothing saying what it should become,
        // and confirming one is refused — which is a different test from this one.
        var selection = ClassifiedSourceIds(await PreviewAsync(editor, geofileId));
        selection.ShouldNotBeEmpty();

        var response = await editor.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection,
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        });

        // Accepted, not OK: the objects do not exist yet. This is the whole point of the change —
        // a few thousand rows cannot be created inside a request, and a reverse proxy cutting the
        // caller off used to roll the entire batch back and report a gateway timeout.
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, payload);
        var accepted = JsonDocument.Parse(payload).RootElement;
        accepted.GetProperty("queued").GetInt32().ShouldBe(selection.Count);

        // The address of the result is settled before the work starts, so the reviewer can be
        // sent to it rather than made to guess which batch appeared most recently.
        var batchId = accepted.GetProperty("batchId").GetGuid();
        (await editor.GetAsync($"/api/v1/import-batches/{batchId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await SearchFeatureNamesAsync(editor)).ShouldBeEmpty();

        await RunCommitAsync(editor, response);

        var batch = (await GetJsonAsync(editor, $"/api/v1/import-batches/{batchId}")).GetProperty("batch");
        batch.GetProperty("createdCount").GetInt32().ShouldBe(selection.Count);
        (await SearchFeatureNamesAsync(editor)).Count.ShouldBe(selection.Count);
    }

    [Fact]
    public async Task A_job_that_runs_twice_does_not_create_the_batch_twice()
    {
        // A name the shipped rules classify, so there is something to confirm — which term it is
        // does not matter here, only that the row has a proposed kind.
        var geofileId = await UploadGpxAsync(SimpleGpx($"Izbuc {tag}", 45.53, 25.44));
        // Only what a rule claimed. An unmatched row has nothing saying what it should become,
        // and confirming one is refused — which is a different test from this one.
        var selection = ClassifiedSourceIds(await PreviewAsync(editor, geofileId));
        selection.ShouldNotBeEmpty();

        var response = await editor.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection,
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        });
        var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var jobId = accepted.GetProperty("jobId").GetInt64();

        // The queue retries a job whose handler threw. A retry of one whose transaction had
        // already committed must not create every object a second time — which is why the batch
        // id is fixed by whoever queued it rather than invented by the run.
        for (var run = 0; run < 2; run++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId);
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.ImportCommit);
            await handler.ExecuteAsync(job, CancellationToken.None);
        }

        (await SearchFeatureNamesAsync(editor)).Count.ShouldBe(selection.Count);
    }

    private async Task<JsonElement> CommitAsync(
        HttpClient client,
        Guid geofileId,
        IReadOnlyList<long> selection,
        Dictionary<string, object>? decisions = null,
        Dictionary<string, object?>? options = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = options ?? DefaultOptions(),
            selection,
            decisions = decisions ?? [],
            withoutReview = false,
        });
        return await RunCommitAsync(client, response);
    }

    /// <summary>
    /// Runs the queued confirmation and answers with the batch it produced, shaped the way the
    /// old synchronous answer was so the assertions above it still read the same.
    /// </summary>
    /// <remarks>
    /// The job is executed here rather than waited for. The container runs a live worker, so
    /// waiting would work most of the time and race the rest — and a test that sometimes asserts
    /// against a batch that does not exist yet reports a defect in whatever it was checking.
    /// Running the handler directly is the same code on the same row, at a moment this test
    /// chooses.
    /// </remarks>
    private async Task<JsonElement> RunCommitAsync(HttpClient client, HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, payload);
        var accepted = JsonDocument.Parse(payload).RootElement;
        var jobId = accepted.GetProperty("jobId").GetInt64();
        var batchId = accepted.GetProperty("batchId").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId);
            job.Kind.ShouldBe(ProcessingJobKinds.ImportCommit);
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.ImportCommit);
            await handler.ExecuteAsync(job, CancellationToken.None);
        }

        // Shaped as the synchronous answer was — `{ batch, failures }` — so every assertion
        // written against that still says what it said. The failures now live on the batch,
        // because the confirmation no longer answers in the request that asked for it.
        var detail = await GetJsonAsync(client, $"/api/v1/import-batches/{batchId}");
        var batch = detail.GetProperty("batch");
        using var shaped = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            batch = JsonSerializer.Deserialize<JsonElement>(batch.GetRawText()),
            failures = JsonSerializer.Deserialize<JsonElement>(batch.GetProperty("failures").GetRawText()),
        }));
        return shaped.RootElement.Clone();
    }

    /// <summary>The rows a rule actually classified — the only ones a confirmation may create.</summary>
    private static List<long> ClassifiedSourceIds(JsonElement preview) =>
        [.. preview.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("proposedKind").ValueKind != JsonValueKind.Null)
            .Select(i => i.GetProperty("sourceId").GetInt64())];

    private static List<long> SourceIds(JsonElement preview) =>
        [.. preview.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sourceId").GetInt64())];

    private async Task<List<string>> SearchFeatureNamesAsync(HttpClient client)
    {
        var page = await GetJsonAsync(client, $"/api/v1/features/?search={tag}&pageSize=100");
        return [.. page.GetProperty("items").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .Where(n => n is not null)
            .Select(n => n!)];
    }

    /// <summary>
    /// Creates a cave with one entrance at the given position, owned by the editor.
    /// </summary>
    private async Task<Guid> CreateCaveWithEntranceAsync(
        string name, double lat, double lon, bool locationProtected = false)
    {
        long caveTypeId;
        long entranceTypeId;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).FirstAsync();
        }

        var created = await editor.PostAsJsonAsync("/api/v1/caves", new
        {
            name,
            caveTypeId,
            visibility = "authenticated",
            locationProtected,
            explorationStatus = "unknown",
            isShowCave = false,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var caveId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetGuid();

        var entrance = await editor.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            name = (string?)null,
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { lon, lat } },
            positionQuality = "unknown",
        });
        entrance.StatusCode.ShouldBe(HttpStatusCode.Created, await entrance.Content.ReadAsStringAsync());
        return caveId;
    }

    /// <summary>Widens a geofile so a second account can review the same upload.</summary>
    private async Task ShareGeofileAsync(Guid geofileId)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var geofile = await db.Geofiles.FirstAsync(g => g.Id == geofileId);
        geofile.Visibility = Visibility.Authenticated;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Retries while the settings cache still holds the previous value. Sections are cached per
    /// process for a short window, so a test that changes one and immediately depends on it is
    /// racing the cache rather than the application.
    /// </summary>
    private static async Task<HttpResponseMessage> Eventually(Func<Task<HttpResponseMessage>> send)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (true)
        {
            var response = await send();
            if (response.StatusCode != HttpStatusCode.Forbidden || DateTimeOffset.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(1000);
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        editor?.Dispose();
        viewer?.Dispose();
        admin?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
