// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
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
/// The first half of importing a club's trip spreadsheet: the review that is kept, the header
/// that can be re-pointed, and the reading of the sheet that happens again every time somebody
/// asks — plus the three things this half could get wrong quietly. Offering a row that cannot be
/// recorded; forgetting on the third page what somebody decided on the first; and letting an
/// account that may not record trips at all read somebody's sheet back.
///
/// <para>
/// Every sheet in here is invented: four placeholder massifs, four placeholder caves and two
/// placeholder people, written to exercise the reading rather than to resemble anybody's records.
/// </para>
/// </summary>
public sealed class TripImportSessionTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient editor = null!;
    private HttpClient viewer = null!;
    private Guid editorId;
    private string tag = null!;

    /// <summary>
    /// Four rows: two that can be recorded, one with no start date and one with no title. The
    /// second row settles the day/month order on its own — a seventeenth is no month.
    /// </summary>
    private const string Sheet =
        "Nr crt.,Data inceput,Data sfarsit,Titlu,Tara,Masiv/zona,Subzona,Pesteri,Propus de,Participanti,Detalii,Tip,Erori\r\n"
        + "1,05/03/2024,,Prima tura,Romania,Masivul Unu,Valea A,Pestera Unu,Ana P.,\"Ana P.; Bogdan Q.\",Nimic special,explorare,\r\n"
        + "2,17/04/2024,18/04/2024,A doua tura,Romania,Masivul Doi,Valea B,\"Pestera Doi; Pestera Trei\",Bogdan Q.,Bogdan Q.,Doua zile,cartare,\r\n"
        + "3,,,Fara data,Romania,Masivul Unu,,Pestera Unu,Ana P.,Ana P.,Lipseste data,explorare,data lipsa\r\n"
        + "4,22/05/2024,,,Romania,Masivul Trei,,Pestera Patru,Ana P.,Ana P.,Fara titlu,turism,titlu lipsa\r\n";

    public TripImportSessionTests(PostgresFixture postgres)
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
        editorId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"ti-editor-{tag}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"ti-viewer-{tag}@t.local");
        editor = await AuthHelper.BearerClientAsync(factory, $"ti-editor-{tag}@t.local");
        viewer = await AuthHelper.BearerClientAsync(factory, $"ti-viewer-{tag}@t.local");
    }

    // ---------- reading the sheet ----------

    [Fact]
    public async Task A_row_the_reading_refused_is_a_problem_rather_than_something_to_select()
    {
        var fileId = await UploadAsync("centralizator.csv", Sheet);

        var preview = await PreviewAsync(fileId);

        preview.GetProperty("rowCount").GetInt32().ShouldBe(4);
        preview.GetProperty("readableRowCount").GetInt32().ShouldBe(2);
        preview.GetProperty("failedRowCount").GetInt32().ShouldBe(2);
        preview.GetProperty("truncated").GetBoolean().ShouldBeFalse();

        // The two rows that can be recorded are the two that are offered, and both of them are
        // selectable. The two that cannot are in neither list.
        Lines(preview, "filteredLines").ShouldBe([2, 3]);
        Lines(preview, "selectableLines").ShouldBe([2, 3]);
        Items(preview).Select(i => i.GetProperty("line").GetInt32()).ShouldBe([2, 3]);

        // They are not silently gone either: each is named among the file's problems, on the
        // physical line somebody can open in their editor.
        var problems = preview.GetProperty("problems").EnumerateArray().ToList();
        problems.Select(p => p.GetProperty("line").GetInt32()).ShouldContain(4);
        problems.Select(p => p.GetProperty("line").GetInt32()).ShouldContain(5);
        problems
            .Where(p => p.GetProperty("severity").GetString() == "error")
            .Select(p => p.GetProperty("code").GetString())
            .ShouldAllBe(code => code == "requiredFieldEmpty");

        // A row above twelve settles the day/month order for the whole file, so the first row's
        // ambiguous date is read the same way as the row that proved it.
        preview.GetProperty("dateOrder").GetString().ShouldBe("dayFirst");
        preview.GetProperty("dateOrderSource").GetString().ShouldBe("file");
        Items(preview)[0].GetProperty("startDate").GetString().ShouldBe("2024-03-05");
        Items(preview)[1].GetProperty("endDate").GetString().ShouldBe("2024-04-18");

        // Nothing about the sheet has been resolved or created: a preview is a reading.
        // Counted against the account that did the reading rather than against the whole table:
        // one database is shared by every test in this suite, so a trip some other test recorded
        // says nothing about whether this preview wrote.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripLogs.AsNoTracking().CountAsync(t => t.OwnerUserId == editorId)).ShouldBe(0);
    }

    [Fact]
    public async Task The_sheet_is_read_again_under_whatever_options_the_request_carries()
    {
        // The same content twice over, once comma-separated and once semicolon-separated. The
        // reading is not stored anywhere, so which one is readable is decided by the options in
        // front of it rather than by what a first parse happened to produce.
        var fileId = await UploadAsync(
            "semicolons.csv",
            "Nr crt.;Data inceput;Titlu;Participanti\r\n1;17/04/2024;Prima tura;Ana P.\r\n");

        var asCommas = await PreviewAsync(fileId);
        asCommas.GetProperty("readableRowCount").GetInt32().ShouldBe(0);
        asCommas.GetProperty("header").EnumerateArray().Count().ShouldBe(1);

        var asSemicolons = await PreviewAsync(fileId, Options(delimiter: ";"));
        asSemicolons.GetProperty("readableRowCount").GetInt32().ShouldBe(1);
        Items(asSemicolons)[0].GetProperty("title").GetString().ShouldBe("Prima tura");
    }

    [Fact]
    public async Task The_header_answers_even_when_the_reading_of_the_rows_did_not()
    {
        // A sheet with a header and nothing under it: exactly when somebody needs to see which
        // column was taken for what, so that they can re-point one.
        var fileId = await UploadAsync("header-only.csv", "Nr crt.,Data inceput,Titlu,Necunoscut\r\n");

        var columns = await JsonAsync(editor, $"/api/v1/trip-imports/{fileId}/columns");

        columns.GetProperty("header").EnumerateArray().Select(h => h.GetString())
            .ShouldBe(["Nr crt.", "Data inceput", "Titlu", "Necunoscut"]);
        columns.GetProperty("resolvedColumns").GetProperty("title").GetString().ShouldBe("Titlu");
        columns.GetProperty("unmappedColumns").EnumerateArray().Select(h => h.GetString())
            .ShouldBe(["Necunoscut"]);
    }

    // ---------- the review that is kept ----------

    [Fact]
    public async Task A_review_nobody_has_begun_is_not_a_row_and_a_review_somebody_saved_comes_back()
    {
        var fileId = await UploadAsync("centralizator.csv", Sheet);

        var fresh = await JsonAsync(editor, $"/api/v1/trip-imports/{fileId}/session");
        fresh.GetProperty("updatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        fresh.GetProperty("options").GetProperty("delimiter").GetString().ShouldBe(",");
        fresh.GetProperty("options").GetProperty("createMissingCaves").GetBoolean().ShouldBeFalse();
        fresh.GetProperty("options").GetProperty("createMissingAreas").GetBoolean().ShouldBeFalse();
        fresh.GetProperty("options").GetProperty("createMissingCavers").GetBoolean().ShouldBeFalse();
        fresh.GetProperty("decisions").EnumerateObject().ShouldBeEmpty();

        await SessionCountShouldBe(0);

        var setAside = new Dictionary<string, object> { ["3"] = new { action = "skip" } };
        var saved = await SaveSessionAsync(fileId, Options(dateOrder: "monthFirst"), setAside);
        saved.GetProperty("updatedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var resumed = await JsonAsync(editor, $"/api/v1/trip-imports/{fileId}/session");
        resumed.GetProperty("options").GetProperty("dateOrder").GetString().ShouldBe("monthFirst");
        resumed.GetProperty("decisions").GetProperty("3").GetProperty("action").GetString().ShouldBe("skip");

        // Saving twice is one review, not two: the row is keyed by the pair, in the database.
        await SaveSessionAsync(fileId, Options(), setAside);
        await SessionCountShouldBe(1);
    }

    [Fact]
    public async Task A_row_set_aside_on_one_page_is_still_set_aside_on_another()
    {
        var fileId = await UploadAsync("long.csv", Rows(30));

        // Line 2 is the first data row, so it can only be seen on the first page.
        await SaveSessionAsync(fileId, Options(), new Dictionary<string, object> { ["2"] = new { action = "skip" } });

        var lastPage = await PreviewAsync(fileId, Options(), page: 3, pageSize: 10);

        Items(lastPage).Select(i => i.GetProperty("line").GetInt32()).ShouldBe([22, 23, 24, 25, 26, 27, 28, 29, 30, 31]);
        lastPage.GetProperty("skippedRowCount").GetInt32().ShouldBe(1);

        // The row set aside is still one of the file's rows, and still not one that "select all"
        // would take — answered here rather than by a browser that is three pages away from it.
        Lines(lastPage, "filteredLines").ShouldContain(2);
        Lines(lastPage, "selectableLines").ShouldNotContain(2);
        Lines(lastPage, "selectableLines").Count.ShouldBe(29);
    }

    [Fact]
    public async Task Only_the_rows_the_search_matches_are_offered()
    {
        var fileId = await UploadAsync("centralizator.csv", Sheet);

        var found = await PreviewAsync(fileId, Options(), search: "masivul doi");

        Lines(found, "filteredLines").ShouldBe([3]);
        found.GetProperty("totalItems").GetInt32().ShouldBe(1);

        // The whole-file counts are about the file, not about the filter — a reviewer narrowing
        // the table has not made the other rows stop existing.
        found.GetProperty("readableRowCount").GetInt32().ShouldBe(2);
        found.GetProperty("failedRowCount").GetInt32().ShouldBe(2);
    }

    // ---------- who may ask ----------

    [Fact]
    public async Task An_account_that_may_not_record_trips_may_not_review_a_sheet_of_them()
    {
        var fileId = await UploadAsync("centralizator.csv", Sheet);

        // The seeded viewer holds no create right in any content domain, so there is genuinely
        // nothing for this account to import — which is what makes the refusal mean something.
        var refusedSession = await viewer.GetAsync($"/api/v1/trip-imports/{fileId}/session");
        refusedSession.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        CodeOf(await refusedSession.Content.ReadAsStringAsync()).ShouldBe("access.create_forbidden");

        var refusedPreview = await viewer.PostAsync(
            $"/api/v1/trip-imports/{fileId}/preview", Body(new { options = Options() }));
        refusedPreview.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        CodeOf(await refusedPreview.Content.ReadAsStringAsync()).ShouldBe("access.create_forbidden");

        var refusedSave = await viewer.PutAsync(
            $"/api/v1/trip-imports/{fileId}/session",
            Body(new { options = Options(), decisions = new Dictionary<string, object>() }));
        refusedSave.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The same sheet, asked for by an account that may record trips, answers.
        var allowed = await editor.GetAsync($"/api/v1/trip-imports/{fileId}/session");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_sheet_that_is_not_there_is_not_described()
    {
        var missing = await editor.GetAsync($"/api/v1/trip-imports/{Guid.NewGuid()}/session");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        CodeOf(await missing.Content.ReadAsStringAsync()).ShouldBe("file.not_found");

        var fileId = await UploadAsync("centralizator.csv", Sheet);
        var found = await editor.GetAsync($"/api/v1/trip-imports/{fileId}/session");
        found.StatusCode.ShouldBe(HttpStatusCode.OK, await found.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_separator_that_is_not_one_character_is_refused_before_anything_is_read()
    {
        var fileId = await UploadAsync("centralizator.csv", Sheet);

        var refused = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/preview",
            Body(new { options = Options(delimiter: ";;") }));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        CodeOf(await refused.Content.ReadAsStringAsync()).ShouldBe("validation.failed");

        var accepted = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/preview", Body(new { options = Options(delimiter: ";") }));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
    }

    // ---------- helpers ----------

    /// <summary>A sheet of the given length, every row readable and every line number known.</summary>
    private static string Rows(int count)
    {
        var text = new StringBuilder("Nr crt.,Data inceput,Titlu,Masiv/zona,Participanti\r\n");
        for (var i = 1; i <= count; i++)
        {
            text.Append($"{i},17/04/2024,Tura {i},Masivul Unu,Ana P.\r\n");
        }

        return text.ToString();
    }

    private static object Options(string delimiter = ",", string dateOrder = "dayFirst") => new
    {
        delimiter,
        multiValueSeparators = ";,",
        slashSeparatedFields = Array.Empty<string>(),
        dateOrder,
        columns = new Dictionary<string, string>(),
        visibility = "private",
        cavingGroupId = (Guid?)null,
        createMissingCaves = false,
        createMissingAreas = false,
        createMissingCavers = false,
        createMissingTripTypes = false,

    };

    private async Task<Guid> UploadAsync(string fileName, string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new("text/csv");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };
        var response = await editor.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> PreviewAsync(
        Guid fileId, object? options = null, int? page = null, int? pageSize = null, string? search = null)
    {
        var response = await editor.PostAsync(
            $"/api/v1/trip-imports/{fileId}/preview",
            Body(new { options = options ?? Options(), page, pageSize, search }));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task<JsonElement> SaveSessionAsync(Guid fileId, object options, object decisions)
    {
        var response = await editor.PutAsync(
            $"/api/v1/trip-imports/{fileId}/session", Body(new { options, decisions }));
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private async Task SessionCountShouldBe(int expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.TripImportSessions.AsNoTracking().CountAsync(s => s.UserId == editorId)).ShouldBe(expected);
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static List<JsonElement> Items(JsonElement preview) =>
        [.. preview.GetProperty("items").EnumerateArray()];

    private static List<int> Lines(JsonElement preview, string property) =>
        [.. preview.GetProperty(property).EnumerateArray().Select(e => e.GetInt32())];

    private static string? CodeOf(string problemBody) =>
        JsonDocument.Parse(problemBody).RootElement.GetProperty("code").GetString();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
