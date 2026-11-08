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
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The path from an uploaded file to caves, entrances and features: the dry run, the resumable
/// review, the confirmation and its undo — plus the two things this feature could get wrong
/// quietly, which are creating without review where the installation forbids it, and answering
/// "there is already something within twelve metres" about a cave the caller may not locate.
/// </summary>
public sealed class StagedImportTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private HttpClient admin = null!;
    private Guid editorId;
    private string tag = null!;

    private readonly string connectionString;

    public StagedImportTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-files-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async ValueTask InitializeAsync()
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

    /// <summary>
    /// An undo is its confirmer's: another editor who knows the batch's id is told it is not
    /// there. A file import is undone whole, never a few lines at a time. And two undos sent at
    /// once take it back once — one is done, the other is told it already was.
    /// </summary>
    [Fact]
    public async Task An_undo_is_the_confirmers_own_whole_and_done_once()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Once {tag}", 45.72, 25.62));
        var commit = await CommitAsync(editor, geofileId, SourceIds(await PreviewAsync(editor, geofileId)));
        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();
        var revertUrl = $"/api/v1/import-batches/{batchId}/revert";

        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"si-editor2-{tag}@t.local");
        using var secondEditor = await AuthHelper.BearerClientAsync(factory, $"si-editor2-{tag}@t.local");
        var theirs = await secondEditor.PostAsync(revertUrl, null);
        theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound, await theirs.Content.ReadAsStringAsync());
        ProblemCode(await theirs.Content.ReadAsStringAsync()).ShouldBe("import_batch.not_found");
        (await SearchFeatureNamesAsync(editor)).ShouldContain($"Once {tag}");

        var partial = await editor.PostAsJsonAsync(revertUrl, new { itemIds = new long[] { 1 } });
        partial.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await partial.Content.ReadAsStringAsync());
        ProblemCode(await partial.Content.ReadAsStringAsync()).ShouldBe("import_batch.partial_undo_unsupported");
        (await SearchFeatureNamesAsync(editor)).ShouldContain($"Once {tag}");

        var both = await Task.WhenAll(editor.PostAsync(revertUrl, null), editor.PostAsync(revertUrl, null));
        var bodies = await Task.WhenAll(both.Select(r => r.Content.ReadAsStringAsync()));
        both.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1, string.Join(" | ", bodies));
        var refused = both.Select((r, i) => (r, body: bodies[i])).Single(x => x.r.StatusCode != HttpStatusCode.OK);
        refused.r.StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused.body);
        ProblemCode(refused.body).ShouldBe("import_batch.already_reverted");
        (await SearchFeatureNamesAsync(editor)).ShouldNotContain($"Once {tag}");

        // Done once: the history records the cave going one time, not once per request.
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveKey = (await db.Features.IgnoreQueryFilters()
            .SingleAsync(f => f.Kind == FeatureKind.Cave && f.Name == $"Once {tag}")).Id.ToString();
        (await db.AuditEntries.AsNoTracking().CountAsync(a =>
                a.EntityId == caveKey && a.Action == AuditActions.Deleted
                && a.Source == ChangeSource.Undo && a.ImportBatchId == batchId))
            .ShouldBe(1);
    }

    /// <summary>
    /// A line of a vector import reports no trip title, whatever the source rows happen to be
    /// called. The line keeps its source object's own attributes verbatim, and a file whose
    /// author wrote a "title" on every object owns that word for something of their own — a
    /// photograph's caption, a map sheet, a survey name. Reading it back as the title of a trip
    /// would invent a trip that was never recorded, and would hand the reader a name that the
    /// same answer withholds two fields earlier when the object is not theirs to see.
    /// </summary>
    [Fact]
    public async Task A_vector_import_line_reports_no_trip_title_even_when_the_source_row_has_one()
    {
        var geojson = $$"""
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "name": "P. Titled {{tag}}", "title": "Nu este o tura {{tag}}" },
                  "geometry": { "type": "Point", "coordinates": [25.51, 45.61] }
                }
              ]
            }
            """;
        var geofileId = await UploadAsync("titled.geojson", Encoding.UTF8.GetBytes(geojson), editor);

        var commit = await CommitAsync(editor, geofileId, SourceIds(await PreviewAsync(editor, geofileId)));
        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();

        var detail = await GetJsonAsync(editor, $"/api/v1/import-batches/{batchId}");
        var item = detail.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("tripLogId").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("tripTitle").ValueKind.ShouldBe(JsonValueKind.Null);

        // And the attribute really is there to be read, so the assertion above is about the
        // answer rather than about a file that never carried the word.
        var provenance = await GetJsonAsync(
            editor, $"/api/v1/features/{item.GetProperty("featureId").GetGuid()}/import-provenance");
        provenance.GetProperty("sourceProperties").GetProperty("title").GetString()
            .ShouldBe($"Nu este o tura {tag}");
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

    /// <summary>
    /// A waypoint stored as a one-part multi-point — which is how several writers save a single
    /// point — is measured against the registry like any other point. Otherwise re-importing a
    /// layer of caves already in the registry shows no duplicate hint, and confirming it creates
    /// every cave a second time.
    /// </summary>
    [Fact]
    public async Task A_waypoint_stored_as_a_one_part_multipoint_is_still_found_as_a_duplicate()
    {
        var caveId = await CreateCaveWithEntranceAsync($"Known Cave {tag}", 46.37, 26.37);

        var geojson = $$"""
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": { "name": "P. Again {{tag}}" },
                  "geometry": { "type": "MultiPoint", "coordinates": [[26.3701, 46.3701]] }
                }
              ]
            }
            """;
        var geofileId = await UploadAsync("again.geojson", Encoding.UTF8.GetBytes(geojson), editor);

        // The precondition: the row really is stored as a multi-point, or this proves nothing.
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var stored = await db.GeofileFeatures.AsNoTracking().Where(f => f.GeofileId == geofileId)
                .Select(f => f.Geom).SingleAsync();
            stored.ShouldBeOfType<NetTopologySuite.Geometries.MultiPoint>();
        }

        var candidate = (await PreviewAsync(editor, geofileId)).GetProperty("items").EnumerateArray().Single();
        var duplicate = candidate.GetProperty("duplicate");
        duplicate.ValueKind.ShouldBe(JsonValueKind.Object);
        duplicate.GetProperty("caveFeatureId").GetGuid().ShouldBe(caveId);
        candidate.GetProperty("geom").ValueKind.ShouldBe(JsonValueKind.Object);
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
        ProblemCode(await refused.Content.ReadAsStringAsync()).ShouldBe("import.review_required");
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

        // A body the route would otherwise accept, so the answer is about the file and not
        // about the request: a reviewed confirmation names at least one row.
        (await viewer.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection = new long[] { 1 },
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

    [Fact]
    public async Task An_entrance_with_no_cave_named_keeps_the_entrance_type_it_was_given()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"Intrarea Adit {tag}", 46.41, 26.41));
        var preview = await PreviewAsync(editor, geofileId);
        var sourceId = SourceIds(preview).Single();

        // No cave chosen, so the entrance becomes the first entrance of a cave made around it —
        // and the type the reviewer saw must be the type it gets, not the installation's default.
        var commit = await CommitAsync(editor, geofileId, [sourceId], new Dictionary<string, object>
        {
            [sourceId.ToString()] = new { action = "create", kind = "caveEntrance", entranceTypeCode = "artificial" },
        });
        commit.GetProperty("failures").EnumerateArray().ShouldBeEmpty();

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var caveId = await db.Features.Where(f => f.Kind == FeatureKind.Cave && f.Name == $"Adit {tag}")
            .Select(f => f.Id).SingleAsync();
        var typeCode = await db.CaveEntrances.Where(e => e.CaveFeatureId == caveId && e.IsMain)
            .Join(db.EntranceTypes, e => e.EntranceTypeId, t => t.Id, (_, t) => t.Code)
            .SingleAsync();
        typeCode.ShouldBe("artificial");
    }

    [Fact]
    public async Task A_cave_row_that_fails_after_its_cave_was_begun_leaves_no_cave_behind()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="46.51" lon="26.51"><name>P. Halfmade {tag}</name></wpt>
              <wpt lat="46.52" lon="26.52"><name>Izbuc Whole {tag}</name></wpt>
            </gpx>
            """);
        var preview = await PreviewAsync(editor, geofileId);
        var cave = preview.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("sourceName").GetString()!.Contains("Halfmade"))
            .GetProperty("sourceId").GetInt64();

        // A cave becomes a cave plus its first entrance, whose type is the installation's
        // default. With that type missing the row has to fail — and fail whole, not leave the
        // cave it had begun sitting in the registry on no batch line.
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.EntranceTypes.Where(t => t.Code == "natural")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Code, $"natural-away-{tag}"));
        }

        JsonElement commit;
        try
        {
            commit = await CommitAsync(editor, geofileId, SourceIds(preview));
        }
        finally
        {
            using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            await db.EntranceTypes.Where(t => t.Code == $"natural-away-{tag}")
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Code, "natural"));
        }

        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        var failure = commit.GetProperty("failures").EnumerateArray().Single();
        failure.GetProperty("sourceId").GetInt64().ShouldBe(cave);
        failure.GetProperty("code").GetString().ShouldBe("import.type_unknown");

        using var check = factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await checkDb.Features.IgnoreQueryFilters().CountAsync(f => f.Name == $"Halfmade {tag}")).ShouldBe(0);
        (await checkDb.Features.CountAsync(f => f.Name == $"Izbuc Whole {tag}")).ShouldBe(1);
    }

    [Fact]
    public async Task A_name_longer_than_the_registry_keeps_fails_its_own_row_only()
    {
        var longName = new string('x', 300);
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="46.61" lon="26.61"><name>P. {longName}</name></wpt>
              <wpt lat="46.62" lon="26.62"><name>P. Short {tag}</name></wpt>
            </gpx>
            """);
        var preview = await PreviewAsync(editor, geofileId);
        var tooLong = preview.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("sourceName").GetString()!.Contains(longName))
            .GetProperty("sourceId").GetInt64();

        var commit = await CommitAsync(editor, geofileId, SourceIds(preview));

        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        var failure = commit.GetProperty("failures").EnumerateArray().Single();
        failure.GetProperty("sourceId").GetInt64().ShouldBe(tooLong);
        failure.GetProperty("code").GetString().ShouldBe("import.value_too_long");
        (await SearchFeatureNamesAsync(editor)).ShouldContain($"Short {tag}");
    }

    [Fact]
    public async Task A_row_chosen_twice_is_created_once()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Twice {tag}", 46.71, 26.71));
        var sourceId = SourceIds(await PreviewAsync(editor, geofileId)).Single();

        var commit = await CommitAsync(editor, geofileId, [sourceId, sourceId]);

        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        commit.GetProperty("batch").GetProperty("skippedCount").GetInt32().ShouldBe(0);
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Features.CountAsync(f => f.Kind == FeatureKind.Cave && f.Name == $"Twice {tag}")).ShouldBe(1);
    }

    [Fact]
    public async Task A_reviewed_confirmation_with_no_rows_chosen_fails_validation()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. None {tag}", 46.72, 26.72));

        var response = await editor.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection = Array.Empty<long>(),
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.ImportCommit && j.RequestedBy == editorId))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Recognising_a_row_as_something_that_is_not_there_fails_that_row_only()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="46.81" lon="26.81"><name>P. Real {tag}</name></wpt>
              <wpt lat="46.82" lon="26.82"><name>P. Ghost {tag}</name></wpt>
              <wpt lat="46.83" lon="26.83"><name>P. Nameless {tag}</name></wpt>
            </gpx>
            """);
        var preview = await PreviewAsync(editor, geofileId);
        long IdOf(string part) => preview.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("sourceName").GetString()!.Contains(part))
            .GetProperty("sourceId").GetInt64();
        var ghost = IdOf("Ghost");
        var nameless = IdOf("Nameless");

        // One row recognised as an object that does not exist (removed since the review was
        // saved, or never there), one recognised as nothing at all: each is that row's failure,
        // and the confirmation still creates the row that was fine.
        var commit = await CommitAsync(editor, geofileId, SourceIds(preview), new Dictionary<string, object>
        {
            [ghost.ToString()] = new { action = "attach", attachToFeatureId = Guid.NewGuid() },
            [nameless.ToString()] = new { action = "attach" },
        });

        commit.GetProperty("batch").GetProperty("createdCount").GetInt32().ShouldBe(1);
        var failures = commit.GetProperty("failures").EnumerateArray()
            .ToDictionary(f => f.GetProperty("sourceId").GetInt64(), f => f.GetProperty("code").GetString());
        failures.Count.ShouldBe(2);
        failures[ghost].ShouldBe("import.attach_target_not_found");
        failures[nameless].ShouldBe("import.attach_target_missing");
        (await SearchFeatureNamesAsync(editor)).ShouldContain($"Real {tag}");
    }

    /// <summary>
    /// A row pointed at an object the confirmer may not read — as a second entrance of a cave, or
    /// recognised as something already in the registry — fails exactly as if the object were not
    /// there, and nothing the confirmer is shown names it. The same two rows pointed at a cave
    /// the confirmer may read but not change are refused as forbidden, and recognised, in that
    /// order, so the refusal is a visibility rule rather than every attach failing.
    /// </summary>
    [Fact]
    public async Task A_row_pointed_at_a_cave_the_confirmer_may_not_read_is_answered_as_not_there()
    {
        var hiddenName = $"Ascunsa {tag}";
        var hiddenCave = await CreateCaveWithEntranceAsync(hiddenName, 47.01, 27.01, visibility: "private");
        var readableCave = await CreateCaveWithEntranceAsync($"Vizibila {tag}", 47.02, 27.02);

        // A signed-in account that may create features and change only its own: it reads the
        // editor's cave open to every account, and not the editor's private one.
        var confirmerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"si-ctb-{tag}@t.local");
        using var confirmer = await AuthHelper.BearerClientAsync(factory, $"si-ctb-{tag}@t.local");
        await GrantFeaturesAsync(confirmerId, AccessAction.Create, AccessScopeKind.All);
        await GrantFeaturesAsync(confirmerId, AccessAction.Read | AccessAction.Write, AccessScopeKind.Own);

        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="47.011" lon="27.011"><name>Intrarea Hidden {tag}</name></wpt>
              <wpt lat="47.021" lon="27.021"><name>Intrarea Readable {tag}</name></wpt>
              <wpt lat="47.012" lon="27.012"><name>Seen Hidden {tag}</name></wpt>
              <wpt lat="47.022" lon="27.022"><name>Seen Readable {tag}</name></wpt>
            </gpx>
            """);
        await ShareGeofileAsync(geofileId);
        var preview = await PreviewAsync(confirmer, geofileId);
        long IdOf(string part) => preview.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("sourceName").GetString()!.Contains(part))
            .GetProperty("sourceId").GetInt64();
        var entranceHidden = IdOf("Intrarea Hidden");
        var entranceReadable = IdOf("Intrarea Readable");
        var seenHidden = IdOf("Seen Hidden");
        var seenReadable = IdOf("Seen Readable");

        var commit = await CommitAsync(confirmer, geofileId, SourceIds(preview), new Dictionary<string, object>
        {
            [entranceHidden.ToString()] = new { action = "create", kind = "caveEntrance", attachToFeatureId = hiddenCave },
            [entranceReadable.ToString()] = new { action = "create", kind = "caveEntrance", attachToFeatureId = readableCave },
            [seenHidden.ToString()] = new { action = "attach", attachToFeatureId = hiddenCave },
            [seenReadable.ToString()] = new { action = "attach", attachToFeatureId = readableCave },
        });

        var failures = commit.GetProperty("failures").EnumerateArray()
            .ToDictionary(f => f.GetProperty("sourceId").GetInt64(), f => f.GetProperty("code").GetString());
        failures.Count.ShouldBe(3);
        failures[entranceHidden].ShouldBe("import.cave_not_found");
        failures[entranceReadable].ShouldBe("import.cave_forbidden");
        failures[seenHidden].ShouldBe("import.attach_target_not_found");
        commit.GetProperty("batch").GetProperty("attachedCount").GetInt32().ShouldBe(1);
        commit.GetRawText().ShouldNotContain(hiddenName);
        commit.GetRawText().ShouldNotContain($"Vizibila {tag}");

        var batchId = commit.GetProperty("batch").GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var lines = await db.ImportBatchItems.AsNoTracking().Where(i => i.ImportBatchId == batchId).ToListAsync();
        lines.ShouldHaveSingleItem().AttachedToFeatureId.ShouldBe(readableCave);
        (await db.CaveEntrances.CountAsync(e => e.CaveFeatureId == hiddenCave)).ShouldBe(1);
        (await db.CaveEntrances.CountAsync(e => e.CaveFeatureId == readableCave)).ShouldBe(1);
    }

    private async Task GrantFeaturesAsync(Guid userId, AccessAction actions, AccessScopeKind scopeKind)
    {
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        db.AccessEntries.Add(new AccessEntry
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Domain = AccessDomain.Features,
            Actions = actions,
            Effect = AccessEffect.Allow,
            ScopeKind = scopeKind,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Two_first_saves_of_one_review_at_once_both_succeed()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Tabs {tag}", 46.91, 26.91));

        // The review saves itself as somebody works, and a second tab on the same upload does
        // too: several first saves arriving together all find no review yet. Each is answered,
        // and one review is left.
        var saves = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            editor.PutAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/session", new
            {
                options = DefaultOptions(),
                decisions = new Dictionary<string, object>(),
            })));

        saves.Select(r => r.StatusCode).ShouldAllBe(c => c == HttpStatusCode.OK);
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.GeofileImportSessions.CountAsync(s => s.GeofileId == geofileId && s.UserId == editorId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_queued_confirmation_whose_rule_set_is_gone_fails_rather_than_running_without_it()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"P. Ruleless {tag}", 46.92, 26.92));
        var sourceId = SourceIds(await PreviewAsync(editor, geofileId)).Single();

        // Queued as the route queues it, naming a rule set that is no longer there by the time
        // the job runs. Run without it, the row's kind is the reviewer's, so it would land —
        // with the name its rule would have stripped, and nothing saying the rules were missing.
        var batchId = Guid.CreateVersion7();
        var payload = new ImportCommitPayload(
            geofileId,
            batchId,
            editorId,
            Guid.NewGuid(),
            ImportJson.Serialize(new ImportOptions { Visibility = Visibility.Private }),
            ImportJson.Serialize(new Dictionary<long, ImportDecision>
            {
                [sourceId] = new() { Action = ImportDecisionAction.Create, Kind = ImportTargetKind.Cave },
            }),
            [sourceId],
            ImportBatchMode.Reviewed);
        long jobId;
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = new ProcessingJob
            {
                Kind = ProcessingJobKinds.ImportCommit,
                RequestedBy = editorId,
                Payload = ImportJson.Serialize(payload),
            };
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        // Whoever carries it — this test or the host's own worker — the job ends failed.
        Exception? thrown = null;
        try
        {
            await QueuedJob.RunAsync(factory.Services, jobId);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        thrown.ShouldNotBeNull();

        using var check = factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var ran = await checkDb.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        ran.Status.ShouldBe(ProcessingJobStatus.Failed);
        (await checkDb.ImportBatches.AnyAsync(b => b.Id == batchId)).ShouldBeFalse();
        (await checkDb.Features.CountAsync(f => f.Name != null && f.Name.Contains($"Ruleless {tag}"))).ShouldBe(0);
    }

    [Fact]
    public async Task Creating_without_review_refuses_a_file_larger_than_one_scan_reads()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="47.01" lon="27.01"><name>P. First {tag}</name></wpt>
              <wpt lat="47.02" lon="27.02"><name>P. Second {tag}</name></wpt>
            </gpx>
            """);

        // The same installation, read through a host whose scan stops after one row.
        using var narrow = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Import:MaxScanRows"] = "1",
        }, JobWorkers.RemoveFrom);
        var narrowEditor = await AuthHelper.BearerClientAsync(narrow, $"si-editor-{tag}@t.local");
        var narrowAdmin = await AuthHelper.BearerClientAsync(narrow, $"si-admin-{tag}@t.local");
        (await narrowAdmin.PutAsJsonAsync("/api/v1/admin/settings/import", new
        {
            allowCreateWithoutReview = true,
            duplicateRadiusMeters = 50,
            duplicateNameSimilarity = 0.8,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        try
        {
            var refused = await narrowEditor.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
            {
                options = DefaultOptions(),
                selection = Array.Empty<long>(),
                decisions = new Dictionary<string, object>(),
                withoutReview = true,
            });

            // Created from the first row alone, the batch would report success and the second
            // row would exist nowhere.
            var body = await refused.Content.ReadAsStringAsync();
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
            JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()
                .ShouldBe("import.too_large_without_review");
            (await SearchFeatureNamesAsync(editor)).ShouldNotContain($"First {tag}");
        }
        finally
        {
            await narrowAdmin.PutAsJsonAsync("/api/v1/admin/settings/import", new
            {
                allowCreateWithoutReview = false,
                duplicateRadiusMeters = 50,
                duplicateNameSimilarity = 0.8,
            });
        }
    }

    /// <summary>
    /// The two ceilings of a reviewed import, each answered for what it is: a file with more rows
    /// than a review reads says so and how far it read, and a confirmation of more rows than one
    /// creates is refused with its code before anything is queued.
    /// </summary>
    [Fact]
    public async Task A_review_says_where_it_stopped_and_a_confirmation_past_its_ceiling_is_refused()
    {
        var geofileId = await UploadGpxAsync($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="47.11" lon="27.11"><name>P. Uno {tag}</name></wpt>
              <wpt lat="47.12" lon="27.12"><name>P. Due {tag}</name></wpt>
            </gpx>
            """);
        var sourceIds = SourceIds(await PreviewAsync(editor, geofileId));
        sourceIds.Count.ShouldBe(2);

        using var narrow = new SilexGisApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Import:MaxScanRows"] = "1",
            ["Import:MaxCommitItems"] = "1",
        }, JobWorkers.RemoveFrom);
        using var narrowEditor = await AuthHelper.BearerClientAsync(narrow, $"si-editor-{tag}@t.local");

        var preview = await PreviewAsync(narrowEditor, geofileId);
        preview.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        preview.GetProperty("scannedRowCount").GetInt32().ShouldBe(1);
        preview.GetProperty("fileRowCount").GetInt32().ShouldBe(2);

        var refused = await narrowEditor.PostAsJsonAsync($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = DefaultOptions(),
            selection = sourceIds,
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        });
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        ProblemCode(body).ShouldBe("import.selection_too_large");

        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ProcessingJobs.CountAsync(j => j.Kind == ProcessingJobKinds.ImportCommit && j.RequestedBy == editorId))
            .ShouldBe(0);
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

        // Claimed before anything is asserted: this host keeps a live worker, and one that took
        // the job during the checks below would make "nothing yet" fail with no code change.
        var jobId = accepted.GetProperty("jobId").GetInt64();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var claimed = await db.ProcessingJobs
                .Where(j => j.Id == jobId && j.Status == ProcessingJobStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ProcessingJobStatus.Running)
                    .SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddHours(1)));
            claimed.ShouldBe(1, "the worker took the job before this test could claim it");
        }

        // The address of the result is settled before the work starts, so the reviewer can be
        // sent to it rather than made to guess which batch appeared most recently.
        var batchId = accepted.GetProperty("batchId").GetGuid();
        (await editor.GetAsync($"/api/v1/import-batches/{batchId}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await SearchFeatureNamesAsync(editor)).ShouldBeEmpty();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId);
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.ImportCommit);
            await handler.ExecuteAsync(job, CancellationToken.None);
            job.Status = ProcessingJobStatus.Succeeded;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.LeaseExpiresAt = null;
            await db.SaveChangesAsync();
        }

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
        //
        // The first of the two runs is the queue's own, and the second waits for it to be over
        // rather than racing it: running the handler by hand while a worker is still inside the
        // same job is not a retry, it is two writers, and it fails on the batch's primary key for
        // a reason that has nothing to do with what is asserted here.
        var first = await QueuedJob.RunAsync(factory.Services, jobId);
        first.Kind.ShouldBe(ProcessingJobKinds.ImportCommit);
        (await SearchFeatureNamesAsync(editor)).Count.ShouldBe(selection.Count);

        // The second run: the retry, by hand, of a job that has already been carried through.
        await QueuedJob.RunAgainAsync(factory.Services, jobId);

        (await SearchFeatureNamesAsync(editor)).Count.ShouldBe(selection.Count);
    }

    /// <summary>
    /// Two runners inside one confirmation at the same moment create its objects once between
    /// them, and neither is told the confirmation failed.
    /// </summary>
    /// <remarks>
    /// Not a hypothetical. The queue has no owner column: the pass that returns interrupted work
    /// to the queue matches on status and lane alone, so a second process starting up puts back a
    /// job the first is currently inside, and both then run the same handler. The handler's own
    /// "already done" read cannot stop that on its own — it is a read taken outside the
    /// transaction that creates the batch, so both runners pass it while neither can see the
    /// other's uncommitted row, and they meet on the batch's primary key instead. Whoever loses
    /// used to have the failure recorded against the job, which told the person who confirmed the
    /// import that it had failed while its objects sat there, created.
    /// <para>
    /// Driven by hand and concurrently rather than through the queue, so it does not depend on
    /// winning a race: the row is claimed first, which is what keeps this host's own worker out of
    /// it and leaves exactly the two runners this test is about.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Two_runners_inside_one_confirmation_create_its_objects_once()
    {
        var geofileId = await UploadGpxAsync(SimpleGpx($"Izbuc {tag}", 45.53, 25.44));
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
        var batchId = accepted.GetProperty("batchId").GetGuid();

        // Claimed, so the only two runners are the ones started below.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var claimed = await db.ProcessingJobs
                .Where(j => j.Id == jobId && j.Status == ProcessingJobStatus.Queued)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ProcessingJobStatus.Running));
            claimed.ShouldBe(1, "the worker took the job before this test could claim it");
        }

        await Task.WhenAll(RunOnceAsync(), RunOnceAsync());

        // One batch, holding every object exactly once.
        var batch = (await GetJsonAsync(editor, $"/api/v1/import-batches/{batchId}")).GetProperty("batch");
        batch.GetProperty("createdCount").GetInt32().ShouldBe(selection.Count);
        (await SearchFeatureNamesAsync(editor)).Count.ShouldBe(selection.Count);

        async Task RunOnceAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            var job = await db.ProcessingJobs.SingleAsync(j => j.Id == jobId);
            var handler = scope.ServiceProvider.GetServices<IProcessingJobHandler>()
                .Single(h => h.Kind == ProcessingJobKinds.ImportCommit);
            await handler.ExecuteAsync(job, CancellationToken.None);
        }
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
    /// The job is carried through here rather than merely waited for, so that it runs at a moment
    /// this test chooses. This host keeps a live worker — every upload above depends on one — so
    /// the row is claimed first and the handler is run only if the claim succeeded: the worker's
    /// poll is entitled to the same row, and two executions of one confirmation both pass the
    /// handler's "already done" read and then collide on the batch's primary key.
    /// </remarks>
    private async Task<JsonElement> RunCommitAsync(HttpClient client, HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, payload);
        var accepted = JsonDocument.Parse(payload).RootElement;
        var jobId = accepted.GetProperty("jobId").GetInt64();
        var batchId = accepted.GetProperty("batchId").GetGuid();

        var job = await QueuedJob.RunAsync(factory.Services, jobId);
        job.Kind.ShouldBe(ProcessingJobKinds.ImportCommit);

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
        string name, double lat, double lon, bool locationProtected = false, string visibility = "authenticated")
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
            visibility,
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
    /// racing the cache rather than the application. Only the refusal that stale value gives is
    /// retried: any other answer, a different 403 included, is returned at once so that a real
    /// permission fault is reported as itself rather than as a timeout.
    /// </summary>
    private static async Task<HttpResponseMessage> Eventually(Func<Task<HttpResponseMessage>> send)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (true)
        {
            var response = await send();
            if (response.StatusCode != HttpStatusCode.Forbidden
                || ProblemCode(await response.Content.ReadAsStringAsync()) != "import.review_required"
                || DateTimeOffset.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(1000);
        }
    }

    private static string? ProblemCode(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

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
