// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Document kinds and the typed metadata they describe: the same versioned-schema-over-jsonb
/// mechanism the feature-kind registry uses, applied to documents. The load-bearing property
/// is what happens when a schema is tightened — documents written under the looser one stay
/// valid, because they are re-checked against the version they were actually written under.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentMetadataTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;
    private readonly string filesRoot;

    private HttpClient owner = null!;     // Editor: uploads, and therefore may write its documents
    private HttpClient outsider = null!;  // Viewer with no grant on anything
    private HttpClient admin = null!;     // Full administrator: may edit taxonomies

    public DocumentMetadataTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        filesRoot = Path.Combine(Path.GetTempPath(), $"silexgis-test-docmeta-{Guid.NewGuid():N}");
        factory = new SilexGisApiFactory(postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        });
    }

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"dm-own-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"dm-out-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, $"dm-adm-{suffix}@t.local");

        owner = await AuthHelper.BearerClientAsync(factory, $"dm-own-{suffix}@t.local");
        outsider = await AuthHelper.BearerClientAsync(factory, $"dm-out-{suffix}@t.local");
        admin = await AuthHelper.BearerClientAsync(factory, $"dm-adm-{suffix}@t.local");
    }

    [Fact]
    public async Task Seeded_document_kinds_publish_their_schemas_and_a_version_to_stamp()
    {
        var types = await owner.GetFromJsonAsync<JsonElement>("/api/v1/document-types");

        var codes = types.EnumerateArray().Select(t => t.GetProperty("code").GetString()).ToList();
        codes.ShouldContain("survey_report");
        codes.ShouldContain("trip_report");

        var surveyReport = types.EnumerateArray().Single(t => t.GetProperty("code").GetString() == "survey_report");
        surveyReport.GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);
        surveyReport.GetProperty("metadataSchema").GetString()!.ShouldContain("surveyed_length_m");

        // Every published schema is recorded, so a document's version stamp resolves to text.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var typeId = surveyReport.GetProperty("id").GetInt64();
        (await db.DocumentTypeSchemas.AsNoTracking()
            .AnyAsync(s => s.DocumentTypeId == typeId && s.Version == 1)).ShouldBeTrue();
    }

    [Fact]
    public async Task Typed_metadata_is_accepted_when_it_fits_the_kind_and_refused_when_it_does_not()
    {
        var documentId = await UploadDocumentAsync("survey.png");
        var typeId = await TypeIdAsync("survey_report");

        var accepted = await PutAsync(owner, documentId, new
        {
            title = "Ridicare topografică",
            documentTypeId = typeId,
            metadata = new { cave_name = "Peștera Urșilor", surveyed_length_m = 1520.5, grade = "5" },
        });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        var stored = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()).RootElement;
        stored.GetProperty("documentTypeCode").GetString().ShouldBe("survey_report");
        stored.GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);
        stored.GetProperty("metadata").GetProperty("cave_name").GetString().ShouldBe("Peștera Urșilor");

        // A value the schema forbids is refused with a code, and the stored row is untouched.
        var refused = await PutAsync(owner, documentId, new
        {
            title = "Ridicare topografică",
            documentTypeId = typeId,
            metadata = new { surveyed_length_m = -12 },
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(refused)).ShouldBe("document.metadata_invalid");

        var unchanged = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        unchanged.GetProperty("metadata").GetProperty("surveyed_length_m").GetDouble().ShouldBe(1520.5);
    }

    [Fact]
    public async Task An_unknown_kind_is_refused_and_no_kind_at_all_clears_the_stamp()
    {
        var documentId = await UploadDocumentAsync("notes.png");
        var typeId = await TypeIdAsync("trip_report");

        var unknown = await PutAsync(owner, documentId, new { title = "Tură", documentTypeId = 987654321L });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(unknown)).ShouldBe("document.type_unknown");

        var typed = await PutAsync(owner, documentId, new
        {
            title = "Tură",
            documentTypeId = typeId,
            metadata = new { participants = 4 },
        });
        typed.StatusCode.ShouldBe(HttpStatusCode.OK, await typed.Content.ReadAsStringAsync());
        JsonDocument.Parse(await typed.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);

        // Dropping the kind drops the stamp with it: a version number that describes nothing
        // is worse than none, because it would resolve to a schema the row never met.
        var untyped = await PutAsync(owner, documentId, new { title = "Tură" });
        untyped.StatusCode.ShouldBe(HttpStatusCode.OK, await untyped.Content.ReadAsStringAsync());
        var payload = JsonDocument.Parse(await untyped.Content.ReadAsStringAsync()).RootElement;
        payload.GetProperty("documentTypeId").ValueKind.ShouldBe(JsonValueKind.Null);
        payload.GetProperty("metadataSchemaVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        // The metadata itself survives the kind being removed — it was never the kind's data.
        payload.GetProperty("metadata").GetProperty("participants").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task Tightening_a_kinds_schema_leaves_documents_written_under_the_old_one_valid()
    {
        var typeId = await CreateTypeAsync(
            "expedition_log",
            """
            {"type":"object","properties":{
              "leader":{"type":"string","title":"Leader"}
            }}
            """);

        var documentId = await UploadDocumentAsync("log.png");
        var written = await PutAsync(owner, documentId, new
        {
            title = "Jurnal de expediție",
            documentTypeId = typeId,
            metadata = new { leader = "A. Popescu" },
        });
        written.StatusCode.ShouldBe(HttpStatusCode.OK, await written.Content.ReadAsStringAsync());
        JsonDocument.Parse(await written.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);

        // The kind's schema is tightened: from now on a permit number is required.
        var bumped = await admin.PutAsJsonAsync($"/api/v1/document-types/{typeId}", new
        {
            code = "expedition_log",
            name = "Expedition log",
            description = (string?)null,
            sortOrder = 0,
            metadataSchema =
                """
                {"type":"object","required":["permit_number"],"properties":{
                  "leader":{"type":"string","title":"Leader"},
                  "permit_number":{"type":"string","title":"Permit number"}
                }}
                """,
        });
        bumped.StatusCode.ShouldBe(HttpStatusCode.OK, await bumped.Content.ReadAsStringAsync());
        JsonDocument.Parse(await bumped.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(2);

        // The document written under version 1 is still served, still carries its metadata,
        // and still reports the version it was validated against.
        var afterBump = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        afterBump.GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);
        afterBump.GetProperty("metadata").GetProperty("leader").GetString().ShouldBe("A. Popescu");

        // And it can still be edited. Its metadata is re-checked against version 1 — the text
        // of which the schema history still holds — so a title correction is not blocked by a
        // requirement that arrived after the document was written.
        var titleOnly = await PutAsync(owner, documentId, new
        {
            title = "Jurnal de expediție, corectat",
            documentTypeId = typeId,
        });
        titleOnly.StatusCode.ShouldBe(HttpStatusCode.OK, await titleOnly.Content.ReadAsStringAsync());
        var corrected = JsonDocument.Parse(await titleOnly.Content.ReadAsStringAsync()).RootElement;
        corrected.GetProperty("title").GetString().ShouldBe("Jurnal de expediție, corectat");
        corrected.GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);

        // Rewriting the metadata does face the new schema — otherwise tightening one would
        // never take effect on anything that already exists.
        var stale = await PutAsync(owner, documentId, new
        {
            title = "Jurnal de expediție, corectat",
            documentTypeId = typeId,
            metadata = new { leader = "A. Popescu" },
        });
        stale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(stale)).ShouldBe("document.metadata_invalid");

        var conforming = await PutAsync(owner, documentId, new
        {
            title = "Jurnal de expediție, corectat",
            documentTypeId = typeId,
            metadata = new { leader = "A. Popescu", permit_number = "APM-2026-14" },
        });
        conforming.StatusCode.ShouldBe(HttpStatusCode.OK, await conforming.Content.ReadAsStringAsync());
        JsonDocument.Parse(await conforming.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Giving_a_kind_its_first_schema_leaves_the_documents_already_filed_under_it_editable()
    {
        // Most shipped kinds have no schema at all, so their documents carry free-form
        // metadata and no version stamp — there was nothing to stamp. The kind acquiring a
        // schema later must not decide, retroactively, that those documents are invalid.
        var seeded = await owner.GetFromJsonAsync<JsonElement>("/api/v1/document-types");
        seeded.EnumerateArray().Single(t => t.GetProperty("code").GetString() == "photo")
            .GetProperty("metadataSchema").ValueKind.ShouldBe(JsonValueKind.Null);

        // Same shape, on a kind of this test's own, so tightening it later disturbs nothing else.
        var typeId = await CreateTypeAsync("field_photo", schema: null);
        var documentId = await UploadDocumentAsync("entrance.png");

        var filed = await PutAsync(owner, documentId, new
        {
            title = "Intrarea principală",
            documentTypeId = typeId,
            metadata = new { photographer = "A. Popescu" },
        });
        filed.StatusCode.ShouldBe(HttpStatusCode.OK, await filed.Content.ReadAsStringAsync());
        JsonDocument.Parse(await filed.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").ValueKind.ShouldBe(JsonValueKind.Null);

        // The administrator now gives the kind a schema that the document above cannot meet.
        var schemaAdded = await admin.PutAsJsonAsync($"/api/v1/document-types/{typeId}", new
        {
            code = "field_photo",
            name = "field_photo",
            description = (string?)null,
            sortOrder = 900,
            metadataSchema =
                """
                {"type":"object","required":["caption"],"properties":{
                  "caption":{"type":"string","title":"Caption"}
                }}
                """,
        });
        schemaAdded.StatusCode.ShouldBe(HttpStatusCode.OK, await schemaAdded.Content.ReadAsStringAsync());

        // A correction that touches only the title leaves the metadata alone, so nothing is
        // measured and the edit goes through.
        var titleOnly = await PutAsync(owner, documentId, new
        {
            title = "Intrarea principală, 2026",
            documentTypeId = typeId,
        });
        titleOnly.StatusCode.ShouldBe(HttpStatusCode.OK, await titleOnly.Content.ReadAsStringAsync());
        var corrected = JsonDocument.Parse(await titleOnly.Content.ReadAsStringAsync()).RootElement;
        corrected.GetProperty("title").GetString().ShouldBe("Intrarea principală, 2026");
        corrected.GetProperty("metadata").GetProperty("photographer").GetString().ShouldBe("A. Popescu");
        corrected.GetProperty("metadataSchemaVersion").ValueKind.ShouldBe(JsonValueKind.Null);

        // The twin that proves the exemption is only for metadata nobody touched: rewriting it
        // does face the new schema, so the kind's requirement is real from now on.
        var rewritten = await PutAsync(owner, documentId, new
        {
            title = "Intrarea principală, 2026",
            documentTypeId = typeId,
            metadata = new { photographer = "A. Popescu" },
        });
        rewritten.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(rewritten)).ShouldBe("document.metadata_invalid");

        var conforming = await PutAsync(owner, documentId, new
        {
            title = "Intrarea principală, 2026",
            documentTypeId = typeId,
            metadata = new { photographer = "A. Popescu", caption = "Intrarea de sus" },
        });
        conforming.StatusCode.ShouldBe(HttpStatusCode.OK, await conforming.Content.ReadAsStringAsync());
        JsonDocument.Parse(await conforming.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Reformatting_a_schema_is_not_a_new_version()
    {
        var typeId = await CreateTypeAsync(
            "field_note",
            """{"type":"object","properties":{"page":{"type":"integer"},"author":{"type":"string"}}}""");

        // Same schema, different spelling: reordered keys and generous whitespace.
        var response = await admin.PutAsJsonAsync($"/api/v1/document-types/{typeId}", new
        {
            code = "field_note",
            name = "Field note",
            description = (string?)null,
            sortOrder = 0,
            metadataSchema =
                """
                {
                  "properties": { "author": { "type": "string" }, "page": { "type": "integer" } },
                  "type": "object"
                }
                """,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("metadataSchemaVersion").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Editing_a_document_kind_needs_taxonomy_rights()
    {
        // The unreadable state, constructed explicitly: an Editor holds broad rights over
        // content, and none over the taxonomies — a metadata schema decides what every
        // document of that kind may say, so it is administration rather than content.
        var refused = await owner.PostAsJsonAsync("/api/v1/document-types", new
        {
            code = "club_bulletin",
            name = "Club bulletin",
            description = (string?)null,
            sortOrder = 500,
            metadataSchema = (string?)null,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The positive twin in the same test: an administrator does hold it, so the refusal
        // above is about the caller's rights and not about the request being malformed.
        var allowed = await admin.PostAsJsonAsync("/api/v1/document-types", new
        {
            code = "club_bulletin",
            name = "Club bulletin",
            description = (string?)null,
            sortOrder = 500,
            metadataSchema = (string?)null,
        });
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());

        var created = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()).RootElement;
        var id = created.GetProperty("id").GetInt64();
        var update = await owner.PutAsJsonAsync($"/api/v1/document-types/{id}", new
        {
            code = "club_bulletin",
            name = "Club bulletin, renamed",
            description = (string?)null,
            sortOrder = 500,
            metadataSchema = (string?)null,
        });
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_metadata_schema_that_is_not_a_schema_is_refused()
    {
        var response = await admin.PostAsJsonAsync("/api/v1/document-types", new
        {
            code = "broken_kind",
            name = "Broken kind",
            description = (string?)null,
            sortOrder = 600,
            metadataSchema = "{ this is not json",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCodeAsync(response)).ShouldBe("document_type.schema_invalid");
    }

    [Fact]
    public async Task A_kind_code_is_used_once()
    {
        var response = await admin.PostAsJsonAsync("/api/v1/document-types", new
        {
            code = "survey_report",
            name = "Duplicate",
            description = (string?)null,
            sortOrder = 700,
            metadataSchema = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("document_type.code_taken");
    }

    [Fact]
    public async Task A_kind_code_has_to_look_like_a_code()
    {
        var response = await admin.PostAsJsonAsync("/api/v1/document-types", new
        {
            code = "Not A Code!",
            name = "Bad code",
            description = (string?)null,
            sortOrder = 800,
            metadataSchema = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_document_is_not_disclosed_to_a_caller_who_cannot_reach_its_content()
    {
        // The unreadable state, built explicitly: an uploaded document attached to nothing,
        // and a Viewer who holds no grant that could reach it. Nothing about it is disclosed
        // — not its metadata, and not that it exists.
        var documentId = await UploadDocumentAsync("private.png");

        var denied = await outsider.GetAsync($"/api/v1/documents/{documentId}");
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(denied)).ShouldBe("document.not_found");

        var deniedWrite = await PutAsync(outsider, documentId, new { title = "taken over" });
        deniedWrite.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The positive twin: the uploader reads and writes it, so the refusals above are the
        // access rule and not a document that was never there.
        var allowed = await owner.GetAsync($"/api/v1/documents/{documentId}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var allowedWrite = await PutAsync(owner, documentId, new { title = "Fotografie de intrare" });
        allowedWrite.StatusCode.ShouldBe(HttpStatusCode.OK, await allowedWrite.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_callers_reach_neither_documents_nor_their_kinds()
    {
        var documentId = await UploadDocumentAsync("anon.png");
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/api/v1/documents/{documentId}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/document-types")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_document_must_keep_a_title()
    {
        var documentId = await UploadDocumentAsync("titled.png");

        var response = await PutAsync(owner, documentId, new { title = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Metadata_that_is_not_an_object_is_refused_before_any_schema_is_consulted()
    {
        var documentId = await UploadDocumentAsync("shape.png");

        var response = await owner.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}",
            new { title = "Shape", documentTypeId = (long?)null, metadata = new[] { 1, 2, 3 } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        owner?.Dispose();
        outsider?.Dispose();
        admin?.Dispose();
        factory.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    private async Task<long> TypeIdAsync(string code)
    {
        var types = await owner.GetFromJsonAsync<JsonElement>("/api/v1/document-types");
        return types.EnumerateArray().Single(t => t.GetProperty("code").GetString() == code)
            .GetProperty("id").GetInt64();
    }

    private async Task<long> CreateTypeAsync(string code, string? schema)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/document-types", new
        {
            code,
            name = code,
            description = (string?)null,
            sortOrder = 900,
            metadataSchema = schema,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetInt64();
    }

    private async Task<Guid> UploadDocumentAsync(string fileName)
    {
        using var image = new MagickImage(MagickColors.SlateGray, 32, 32);
        var content = new ByteArrayContent(image.ToByteArray(MagickFormat.Png));
        content.Headers.ContentType = new("image/png");
        using var form = new MultipartFormDataContent { { content, "file", fileName } };

        // These fixtures upload byte-identical content more than once, which the store now
        // warns about. Saying yes up front is what a person would do; deduplication is
        // asserted in its own suite rather than incidentally here.
        var response = await owner.PostAsync("/api/v1/files/?allowDuplicate=true", form);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("documentId").GetGuid();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid documentId, object body) =>
        client.PutAsJsonAsync($"/api/v1/documents/{documentId}", body);

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
