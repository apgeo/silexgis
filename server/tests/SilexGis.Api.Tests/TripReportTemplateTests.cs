// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// A club's own layout for its trip write-ups: the system hands one out, somebody edits it in a
/// text editor and uploads it back, and says which one write-ups should use.
///
/// The rule the whole feature turns on is that a layout chooses what is written and never widens
/// what may be written. Every name a layout may use is answered out of the reading of the trip
/// its producer already has, so a layout naming a part of the record this reader is not given
/// produces a document without that line — not an error, and not the line.
/// </summary>
public sealed class TripReportTemplateTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;
    private readonly List<Guid> stored = [];

    private HttpClient owner = null!;   // Editor — owns the trip, may change it
    private HttpClient reader = null!;  // Viewer — may read the trip, may not change it
    private HttpClient admin = null!;   // holds the vocabulary rights a layout is written under

    public TripReportTemplateTests(PostgresFixture postgres)
    {
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-triptpl-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"tpl-own-{suffix}@t.local");
        owner = await AuthHelper.BearerClientAsync(factory, $"tpl-own-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"tpl-read-{suffix}@t.local");
        reader = await AuthHelper.BearerClientAsync(factory, $"tpl-read-{suffix}@t.local");

        await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"tpl-adm-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"tpl-adm-{suffix}@t.local");
    }

    /// <summary>
    /// The layout the system hands out comes back as a file, is accepted when it is uploaded
    /// again unchanged, and produces the document it describes.
    /// </summary>
    /// <remarks>
    /// The round trip is the feature: a starting point nobody can upload back is not a starting
    /// point, and it is the copy that carries the documentation of the language, so it is also
    /// the only reference the person editing it has.
    /// </remarks>
    [Fact]
    public async Task The_layout_the_system_hands_out_can_be_uploaded_back_and_a_write_up_built_in_it()
    {
        using var handed = await admin.GetAsync("/api/v1/trip-report-templates/default");
        handed.StatusCode.ShouldBe(HttpStatusCode.OK, await handed.Content.ReadAsStringAsync());
        handed.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        handed.Content.Headers.ContentDisposition!.FileName
            .ShouldNotBeNull().ShouldContain("trip-report-template");

        var body = new UTF8Encoding(false).GetString(await handed.Content.ReadAsByteArrayAsync())
            .TrimStart('﻿');
        body.ShouldContain("{title}");

        var templateId = await StoreAsync(admin, "Handed back", body, isDefault: false);
        var tripId = await CreateTripAsync(body => body["description"] = "Two hours of survey.");

        var text = await DocumentTextAsync(owner, tripId, templateId);
        text.ShouldContain("Two hours of survey.");
        text.ShouldContain("Account");

        // Nobody was on this trip, so the layout's roster produced nothing — and its heading went
        // with it. A layout asks for a part before it can know whether the trip has one, so an
        // empty part is ordinary; a bare heading over nothing is a statement the record did not
        // make.
        text.ShouldNotContain("Who was there");
    }

    /// <summary>
    /// A layout that cannot be read is refused where it is written, under a code that does not
    /// move, and says which line is wrong.
    /// </summary>
    /// <remarks>
    /// Refusing at upload is the whole point: the alternative is a club secretary finding out on
    /// the evening the bulletin is due, from a failure to produce a document, that a line they
    /// typed six weeks ago was never going to work.
    /// </remarks>
    [Fact]
    public async Task A_layout_that_cannot_be_read_is_refused_when_it_is_uploaded()
    {
        using var refused = await admin.PostAsJsonAsync("/api/v1/trip-report-templates/", new
        {
            name = "Broken",
            body = "title: {title}\nphotograph: all of them\nfield: Where = {gps_position}",
            isDefault = false,
            kind = "trip",
        });
        var payload = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, payload);

        var problem = JsonDocument.Parse(payload).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("trip_report_template.invalid");
        var detail = problem.GetProperty("detail").GetString().ShouldNotBeNull();
        detail.ShouldContain("Line 2");
        detail.ShouldContain("Line 3");
        detail.ShouldContain("gps_position");

        // The same file with those two lines corrected is stored, which is what makes the
        // refusal above about the lines rather than about uploading at all.
        await StoreAsync(admin, "Fixed", "title: {title}\nphotographs\nfield: Where = {location}", false);
    }

    /// <summary>
    /// A layout naming the account of what went wrong produces, for a reader who may not change
    /// the trip, a document without that line — and never the account.
    /// </summary>
    /// <remarks>
    /// This is the sentence the template store had to be built around. A layout is written once,
    /// by an administrator, and then every reader's copy is produced from it; if naming a field
    /// in a layout were a way of reaching past what a reader was given, the layout would be a
    /// hole in the narrowest audience in the application, opened by somebody who never intended
    /// one. Both halves are over one fixture: the writer's copy carries the account, so the
    /// reader's silence is a withholding rather than a layout that quietly prints nothing.
    /// </remarks>
    [Fact]
    public async Task A_layout_naming_what_went_wrong_gives_a_reader_a_document_without_it()
    {
        const string Account = "Ilie was hit by a rock below the second pitch; we came out early.";
        const string Label = "What the club must know";

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var typeId = await CreateTripTypeAsync(suffix);
        var templateId = await StoreAsync(
            admin,
            "Bulletin",
            $"title: {{title}}\nfield: Incident = {{incident}}\nfield: {Label} = {{safety.incident_account}}",
            isDefault: false);

        var tripId = await CreateTripAsync(body =>
        {
            body["tripTypeId"] = typeId;
            body["hadIncident"] = true;
            body["safety"] = JsonSerializer.SerializeToElement(new { incident_account = Account });
        });

        var writersCopy = await DocumentTextAsync(owner, tripId, templateId);
        writersCopy.ShouldContain(Label);
        writersCopy.ShouldContain(Account);

        var readersCopy = await DocumentTextAsync(reader, tripId, templateId);
        readersCopy.ShouldNotContain(Account);
        readersCopy.ShouldNotContain("second pitch");
        // Not the label either: a labelled blank on a circulated document is a statement of its
        // own, and on this field it is the wrong one.
        readersCopy.ShouldNotContain(Label);
        // The document was still produced, and still says what every reader is told.
        readersCopy.ShouldContain("Incident");
    }

    /// <summary>
    /// Which kind of thing a layout writes up is asked for on every save, never assumed.
    /// </summary>
    /// <remarks>
    /// A save is a full replace, so a kind that fell back to a default would be stamped over the
    /// kind the stored layout already had — retyping a camp layout as a trip layout, and, if it
    /// was the chosen one, taking the club's chosen trip layout with it. Nothing in the answer
    /// would say so, and camp write-ups would quietly go back to the shipped layout. The positive
    /// half is over the same fixture: the same body with the kind named is stored.
    /// </remarks>
    [Fact]
    public async Task A_layout_saved_without_saying_what_it_writes_up_is_refused()
    {
        const string Body = "title: {title}\nheading: Ordinary";

        using var refused = await admin.PostAsJsonAsync(
            "/api/v1/trip-report-templates/", new { name = "Kindless", body = Body, isDefault = false });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());

        var stored = await StoreAsync(admin, "Kinded", Body, isDefault: false);

        // And a rewrite of a stored layout is refused the same way, which is the case that
        // silently retyped one: the layout is still there afterwards, still of its own kind.
        using var rewritten = await admin.PutAsJsonAsync(
            $"/api/v1/trip-report-templates/{stored}",
            new { name = "Kindless again", body = Body, isDefault = false });
        rewritten.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await rewritten.Content.ReadAsStringAsync());

        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-report-templates/");
        var row = listed.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == stored);
        row.GetProperty("name").GetString().ShouldBe("Kinded");
        row.GetProperty("kind").GetString().ShouldBe("trip");
    }

    /// <summary>
    /// The layout a write-up gets when nobody names one is the one chosen for the installation,
    /// and only one layout may be that.
    /// </summary>
    [Fact]
    public async Task The_chosen_layout_is_what_a_write_up_asked_for_without_one_is_built_in()
    {
        var first = await StoreAsync(admin, "First", "title: {title}\nheading: Ordinary", isDefault: true);
        var second = await StoreAsync(admin, "Second", "title: {title}\nheading: Chosen\ntext: {dates}", true);
        var tripId = await CreateTripAsync(_ => { });

        var text = await DocumentTextAsync(owner, tripId, templateId: null);
        text.ShouldContain("Chosen");
        text.ShouldNotContain("Ordinary");

        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/trip-report-templates/");
        listed.EnumerateArray()
            .Where(x => x.GetProperty("isDefault").GetBoolean())
            .Select(x => x.GetProperty("id").GetGuid())
            .ShouldBe([second]);
        first.ShouldNotBe(second);
    }

    /// <summary>
    /// A write-up asked for in a layout that is not there is refused rather than quietly built in
    /// another one.
    /// </summary>
    [Fact]
    public async Task A_write_up_asked_for_in_a_layout_that_is_not_there_is_refused()
    {
        var tripId = await CreateTripAsync(_ => { });

        using var response = await owner.GetAsync(
            $"/api/v1/trip-logs/{tripId}/report?templateId={Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("code").GetString()
            .ShouldBe("trip_report_template.not_found");
    }

    /// <summary>
    /// Writing a layout is an administrator's act; reading the list is not, because anybody
    /// producing a write-up has to be able to choose between them.
    /// </summary>
    [Fact]
    public async Task A_layout_is_written_by_whoever_may_edit_the_installations_vocabularies()
    {
        using var refused = await reader.PostAsJsonAsync("/api/v1/trip-report-templates/", new
        {
            name = "Not mine",
            body = "title: {title}",
            isDefault = false,
            kind = "trip",
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());

        var stored = await StoreAsync(admin, "Theirs", "title: {title}", isDefault: false);

        using var listed = await reader.GetAsync("/api/v1/trip-report-templates/");
        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Select(x => x.GetProperty("id").GetGuid())
            .ShouldContain(stored);

        using var anonymous = factory.CreateClient();
        using var unsigned = await anonymous.GetAsync("/api/v1/trip-report-templates/");
        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> StoreAsync(HttpClient client, string name, string body, bool isDefault)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/trip-report-templates/", new { name, body, isDefault, kind = "trip" });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        var id = JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
        stored.Add(id);
        return id;
    }

    private static async Task<string> DocumentTextAsync(HttpClient client, Guid tripId, Guid? templateId)
    {
        var url = templateId is { } id
            ? $"/api/v1/trip-logs/{tripId}/report?templateId={id}"
            : $"/api/v1/trip-logs/{tripId}/report";
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var document = WordprocessingDocument.Open(bytes, false);

        // A file a word processor refuses to open is not a write-up, whatever it says inside.
        var faults = new OpenXmlValidator().Validate(document).ToList();
        faults.ShouldBeEmpty(string.Join("; ", faults.Select(f => f.Description)));

        return document.MainDocumentPart!.Document!.InnerText;
    }

    private async Task<Guid> CreateTripAsync(Action<Dictionary<string, object?>> shape)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = $"Layout trip {Guid.NewGuid():N}"[..30],
            ["tripDate"] = "2026-07-01",
            ["participants"] = Array.Empty<object>(),
            // Readable by every account here, so the Viewer genuinely reads the trip and
            // genuinely cannot change it — the only state a withholding rule can be proved on.
            ["visibility"] = "authenticated",
        };
        shape(body);

        using var response = await owner.PostAsJsonAsync("/api/v1/trip-logs/", body);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A club purpose whose safety section asks what happened.</summary>
    private async Task<long> CreateTripTypeAsync(string suffix)
    {
        using var response = await admin.PostAsJsonAsync("/api/v1/trip-types", new
        {
            code = $"layout_{suffix}",
            name = $"Layout {suffix}",
            description = (string?)null,
            sortOrder = 0,
            fieldDataSchema = (string?)null,
            logisticsSchema = (string?)null,
            safetySchema =
                """{"type":"object","properties":{"incident_account":{"type":"string","title":"What happened"}}}""",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetInt64();
    }

    /// <summary>
    /// Every layout this suite wrote is taken away again.
    /// </summary>
    /// <remarks>
    /// Which layout is used when nobody names one is a fact about the whole installation, not
    /// about a trip, so a suite that left one behind would change what every write-up produced
    /// afterwards looks like — including in other suites, which share this database.
    /// </remarks>
    public async Task DisposeAsync()
    {
        foreach (var id in stored)
        {
            using var response = await admin.DeleteAsync($"/api/v1/trip-report-templates/{id}");
        }
    }

    public void Dispose()
    {
        owner?.Dispose();
        reader?.Dispose();
        admin?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }
}
