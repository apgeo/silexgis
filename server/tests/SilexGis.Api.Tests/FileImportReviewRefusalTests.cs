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
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// An account under review cannot land a file import unreviewed. Holding a file import for a
/// reviewer is not built, so every importer's confirmation is refused for such an account — a
/// vector file, photographs, a trip spreadsheet, a SpeleoLoc archive and a kept recording, and a
/// catalogue import — before anything is written; once the mark is cleared the same requests are
/// answered as they always were.
/// </summary>
public sealed class FileImportReviewRefusalTests : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Code = "import.review_unavailable";

    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private HttpClient importer = null!;
    private HttpClient admin = null!;
    private Guid importerId;
    private string marker = null!;

    public FileImportReviewRefusalTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-importrefusal-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
        });
    }

    public async ValueTask InitializeAsync()
    {
        marker = Guid.NewGuid().ToString("N")[..8];
        importerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fir-imp-{marker}@t.local");
        importer = await AuthHelper.BearerClientAsync(factory, $"fir-imp-{marker}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"fir-adm-{marker}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"fir-adm-{marker}@t.local");
    }

    [Fact]
    public async Task Every_file_import_of_an_account_under_review_is_refused_before_anything_is_written()
    {
        var geofileId = await UploadGpxAsync();
        await MarkAsync(true);

        foreach (var (route, body) in Commits(geofileId))
        {
            var response = await importer.PostAsJsonAsync(route, body);
            var raw = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{route}: {raw}");
            CodeOf(raw).ShouldBe(Code, route);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.ImportBatches.AsNoTracking().CountAsync(b => b.ConfirmedByUserId == importerId)).ShouldBe(0);
        (await db.Features.IgnoreQueryFilters().CountAsync(f => f.OwnerUserId == importerId)).ShouldBe(0);

        // The mark is what refuses: cleared, the same requests reach the importers again, which
        // answer them in their own words.
        await MarkAsync(false);
        foreach (var (route, body) in Commits(geofileId))
        {
            var response = await importer.PostAsJsonAsync(route, body);
            var raw = await response.Content.ReadAsStringAsync();
            (raw.Length == 0 ? null : CodeOrNull(raw)).ShouldNotBe(Code, route);
        }
    }

    [Fact]
    public async Task The_refusal_needs_a_signed_in_caller_and_says_nothing_to_anybody_else()
    {
        await MarkAsync(true);
        using var anonymous = factory.CreateClient();
        foreach (var (route, body) in Commits(Guid.NewGuid()))
        {
            (await anonymous.PostAsJsonAsync(route, body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route);
        }

        // Another account, not under review, is not answered with the refusal.
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"fir-other-{marker}@t.local");
        using var other = await AuthHelper.BearerClientAsync(factory, $"fir-other-{marker}@t.local");
        foreach (var (route, body) in Commits(Guid.NewGuid()))
        {
            var raw = await (await other.PostAsJsonAsync(route, body)).Content.ReadAsStringAsync();
            (raw.Length == 0 ? null : CodeOrNull(raw)).ShouldNotBe(Code, route);
        }
    }

    private static IEnumerable<(string Route, object Body)> Commits(Guid geofileId)
    {
        var file = Guid.NewGuid();
        yield return ($"/api/v1/geofiles/{geofileId}/import/commit", new
        {
            options = new Dictionary<string, object?>
            {
                ["termRuleSetId"] = null,
                ["languages"] = Array.Empty<string>(),
                ["mapping"] = new { },
                ["elevation"] = "keep",
                ["tracks"] = "ignore",
                ["visibility"] = "private",
                ["duplicateRadiusMeters"] = 100,
                ["tagIds"] = Array.Empty<long>(),
            },
            selection = new long[] { 1 },
            decisions = new Dictionary<string, object>(),
            withoutReview = false,
        });
        yield return ("/api/v1/photo-import/commit", new
        {
            options = new Dictionary<string, object?>
            {
                ["defaultKind"] = "caveEntrance",
                ["defaultCaveTypeCode"] = "cave",
                ["defaultEntranceTypeCode"] = "natural",
                ["clusterRadiusMeters"] = 25,
                ["proximityRadiusMeters"] = 80,
                ["visibility"] = "private",
                ["elevation"] = "keep",
                ["locationProtected"] = false,
                ["tagIds"] = Array.Empty<long>(),
            },
            fileIds = new[] { file },
            selection = new[] { file },
            decisions = new Dictionary<string, object>(),
        });
        yield return ($"/api/v1/trip-imports/{file}/commit", new
        {
            options = new
            {
                delimiter = ",",
                multiValueSeparators = ";,",
                slashSeparatedFields = Array.Empty<string>(),
                dateOrder = "dayFirst",
                columns = new Dictionary<string, string>(),
                visibility = "private",
                cavingGroupId = (Guid?)null,
                createMissingCaves = false,
                createMissingAreas = false,
                createMissingCavers = false,
                createMissingTripTypes = false,
            },
            lines = new[] { 2 },
        });
        var speleoloc = new
        {
            options = new
            {
                tripUuid = Guid.NewGuid().ToString(),
                tripLogId = (Guid?)null,
                createTrip = true,
                surveyModelId = Guid.NewGuid(),
                cavers = new Dictionary<string, Guid>(),
                cavingGroupId = (Guid?)null,
                visibility = "private",
                candidateCount = 5,
            },
            pointIds = new[] { Guid.NewGuid().ToString() },
        };
        yield return ($"/api/v1/speleoloc-imports/{file}/commit", speleoloc);
        yield return ($"/api/v1/speleoloc-imports/recordings/{Guid.NewGuid()}/commit", speleoloc);
        yield return ("/api/v1/catalogue/speologie/import", new
        {
            selection = new[] { 1 },
            decisions = (object?)null,
            visibility = "private",
            cavingGroupId = (Guid?)null,
            locationProtected = false,
            parentId = (Guid?)null,
        });
    }

    private async Task<Guid> UploadGpxAsync()
    {
        var gpx = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="45.5" lon="22.5"><name>Held {marker}</name></wpt>
            </gpx>
            """;
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(gpx));
        content.Headers.ContentType = new("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "file", "held.gpx" } };
        var response = await importer.PostAsync("/api/v1/geofiles", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await importer.GetFromJsonAsync<JsonElement>($"/api/v1/geofiles/{id}/status");
            var value = status.GetProperty("importStatus").GetString();
            if (value is "imported" or "failed")
            {
                value.ShouldBe("imported", status.ToString());
                return id;
            }

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "the file was not read in time");
            await Task.Delay(200);
        }
    }

    private async Task MarkAsync(bool needsReview)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/users/{importerId}/upload-review", new { uploadsNeedReview = needsReview });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static string? CodeOf(string raw) => JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString();

    private static string? CodeOrNull(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var code)
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        importer?.Dispose();
        admin?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
